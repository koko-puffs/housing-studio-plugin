using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using HousingStudio.Core;

namespace HousingStudio.Game;

/// <summary>
/// Reads what a home is put up with, apart from its furniture: inside, each floor's walls, floor and
/// light, and the house's own walls, window and door (its "Main" floor, as MakePlace names it);
/// outside, the plot's roof, walls, window, door, decorations, placard and fence with their dyes.
/// The outside can only be read in the ward, so the last one seen of each plot is kept, for a home
/// saved from inside.
/// </summary>
public sealed unsafe class FixtureReader
{
    private static readonly string[] Floors = { "Ground Floor", "Upper Floor", "Basement", "Main" };
    private static readonly string[] InteriorParts = { "Wall", "Window", "Door", "Floor", "Light" };

    // OutdoorPlotLayoutData's parts, in order, as the site's build/model.ts names them.
    private static readonly string[] ExteriorParts = { "Roof", "Walls", "Window", "Door", "Roof Decoration", "Wall Decoration", "Placard", "Fence" };

    private readonly GameData data;
    private readonly Dictionary<PlotKey, List<SavedFixture>> seenOutside = new();
    private DateTime nextLook;

    /// <summary>A plot: its world, the ward's territory, the ward (0-based) and the plot (0-based).</summary>
    private readonly record struct PlotKey(uint World, uint Territory, int Ward, int Plot);

    /// <summary>
    /// The world you're on: the same read in the ward and in a house (the game's own house ids
    /// carry a world too, but nothing says the ward's is filled in).
    /// </summary>
    private static uint World() => Services.PlayerState.IsLoaded ? Services.PlayerState.CurrentWorld.RowId : 0;

    public FixtureReader(GameData data) => this.data = data;

    /// <summary>
    /// Inside a home: its fixtures, floor by floor ("Main" for the house's own walls, window and
    /// door). Empty when it can't be read. <paramref name="houseSize"/> leaves out the floors the
    /// home hasn't got.
    /// </summary>
    public List<SavedFixture> ReadInterior(string? houseSize)
    {
        var list = new List<SavedFixture>();
        var area = IndoorArea();
        if (area == null) return list;
        for (var floor = 0; floor < Floors.Length; floor++)
        {
            // A cottage has no upper floor; an apartment has one room.
            if (floor == 1 && houseSize is "Small" or "Apartment") continue;
            if (floor == 2 && houseSize == "Apartment") continue;
            var parts = floor switch
            {
                0 => &area->Floor0,
                1 => &area->Floor1,
                2 => &area->Floor2,
                _ => &area->Exterior,
            };
            var keys = (int*)parts;
            for (var part = 0; part < InteriorParts.Length; part++)
            {
                var key = keys[part];
                if (key <= 0) continue;
                var item = data.FixtureItem((uint)key);
                if (item == 0) item = data.UnitedOf((uint)key);
                if (item == 0) continue;
                list.Add(new SavedFixture(Floors[floor], InteriorParts[part], item, data.AnyItemName(item)));
            }
        }
        return list;
    }

    /// <summary>Inside a home: which plot it stands on (1-60), or null (an apartment, or not inside).</summary>
    public static int? PlotOfInterior()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IndoorTerritory == null) return null;
        var id = hm->IndoorTerritory->HouseId;
        if (id.IsApartment) return null;
        return id.PlotIndex + 1;
    }

    /// <summary>
    /// The outside of a plot (1-60) in the ward you're in, or null when it can't be read (not in a
    /// ward, or no house on it). A themed house (all its parts one item) is written as its walls.
    /// </summary>
    public List<SavedFixture>? ReadExterior(int plot)
    {
        var area = OutdoorArea();
        if (area == null || plot < 1 || plot > area->Plots.Length) return null;
        var fixtures = area->Plots[plot - 1].Fixture;
        var count = Math.Min(fixtures.Length, ExteriorParts.Length);
        if (count == 0 || fixtures[0].FixtureId == 0) return null;

        string? Dye(byte stain) => stain == 0 ? null : data.DyeRgb(stain) is { } rgb ? Dyes.HexOfRgb(rgb) : null;

        var list = new List<SavedFixture>();
        // A themed house shows as its roof's (or walls') key belonging to it, or as the theme's own key.
        uint united = 0;
        for (var i = 0; i < Math.Min(2, count) && united == 0; i++)
        {
            var key = fixtures[i].FixtureId;
            united = data.UnitedOf(key);
            if (united == 0 && data.IsUnited(data.FixtureItem(key))) united = data.FixtureItem(key);
        }
        if (united != 0)
        {
            // The theme's dye is its walls' (or its roof's).
            var stain = count > 1 && fixtures[1].StainId != 0 ? fixtures[1].StainId : fixtures[0].StainId;
            list.Add(new SavedFixture("Exterior", "Walls", united, data.AnyItemName(united), Dye(stain)));
        }

        for (var i = 0; i < count; i++)
        {
            var key = fixtures[i].FixtureId;
            if (key == 0) continue;
            // The themed house's own parts are in it already.
            if (united != 0 && (data.IsPartOf(united, key) || data.UnitedOf(key) == united || data.FixtureItem(key) == united)) continue;
            var item = data.FixtureItem(key);
            if (item == 0) continue;
            list.Add(new SavedFixture("Exterior", ExteriorParts[i], item, data.AnyItemName(item), Dye(fixtures[i].StainId)));
        }
        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// The outside of the home you're in or at, as read now (in the ward) or when you were last by it
    /// (inside: read from the ward on your way in). Empty when it hasn't been seen.
    /// </summary>
    public List<SavedFixture> ExteriorOfHome(HousingContext ctx)
    {
        if (Key(ctx) is not { } key) return new List<SavedFixture>();
        if (ctx.Kind == PlaceKind.Ward && ReadExterior(key.Plot + 1) is { } now)
        {
            seenOutside[key] = now;
            return now;
        }
        return seenOutside.TryGetValue(key, out var seen) ? new List<SavedFixture>(seen) : new List<SavedFixture>();
    }

    /// <summary>
    /// In a ward, now and then: keeps the outside of every plot with a house, so a home saved from
    /// inside has its roof, walls and fence too. Cheap (60 plots × 8 parts); framework thread only.
    /// </summary>
    public void Look(HousingContext ctx)
    {
        if (ctx.Kind != PlaceKind.Ward || DateTime.UtcNow < nextLook || !data.FixturesReady) return;
        nextLook = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        try
        {
            LookAround(ctx);
        }
        catch (Exception ex)
        {
            // Only for saving later: never worth stopping a run over.
            Services.Log.Warning(ex, "Couldn't read the ward's houses");
            nextLook = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        }
    }

    private void LookAround(HousingContext ctx)
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->OutdoorTerritory == null) return;
        var ward = hm->GetCurrentWard();
        if (ward < 0) return;
        var area = OutdoorArea();
        if (area == null) return;
        var world = World();
        for (var plot = 1; plot <= area->Plots.Length; plot++)
            if (ReadExterior(plot) is { } parts) seenOutside[new PlotKey(world, ctx.TerritoryId, ward, plot - 1)] = parts;
    }

    /// <summary>Which plot the home is (its ward's territory, ward, plot), inside or out; null for an apartment.</summary>
    private static PlotKey? Key(HousingContext ctx)
    {
        var hm = HousingManager.Instance();
        if (hm == null) return null;
        if (ctx.Kind == PlaceKind.Interior && hm->IndoorTerritory != null)
        {
            var id = hm->IndoorTerritory->HouseId;
            if (id.IsApartment || hm->GetCurrentWard() is var ward && ward < 0) return null;
            // The ward the house is in (you're in its interior's territory now).
            return new PlotKey(World(), HousingManager.GetOriginalHouseTerritoryTypeId(), ward, id.PlotIndex);
        }
        if (ctx.Kind == PlaceKind.Ward && hm->OutdoorTerritory != null && ctx.Plot is { } plot && hm->GetCurrentWard() is var w && w >= 0)
            return new PlotKey(World(), ctx.TerritoryId, w, plot - 1);
        return null;
    }

    private static IndoorAreaLayoutData* IndoorArea()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IndoorTerritory == null) return null;
        var world = LayoutWorld.Instance();
        if (world != null && world->ActiveLayout != null && world->ActiveLayout->IndoorAreaData != null)
            return world->ActiveLayout->IndoorAreaData;
        return &hm->IndoorTerritory->IndoorAreaLayoutData;
    }

    private static OutdoorAreaLayoutData* OutdoorArea()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->OutdoorTerritory == null) return null;
        var world = LayoutWorld.Instance();
        if (world == null || world->ActiveLayout == null) return null;
        return world->ActiveLayout->OutdoorAreaData;
    }
}
