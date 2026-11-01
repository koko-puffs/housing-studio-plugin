using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using HousingStudio.Core;

namespace HousingStudio.Game;

public enum PlaceKind
{
    /// <summary>Not at a home (or not logged in).</summary>
    Elsewhere,

    /// <summary>Inside a house, apartment or private chambers.</summary>
    Interior,

    /// <summary>Out in a residential ward (your garden is on a plot there).</summary>
    Ward,
}

/// <summary>Where you are and what the game lets you do there, read once a frame.</summary>
public sealed record HousingContext(
    PlaceKind Kind,
    uint TerritoryId,
    string TerritoryName,
    ulong HouseId,
    bool HasPermission,
    string? HouseSize,
    string? District,
    int? Plot,
    EditorMode Mode,
    bool EditorOpen,
    bool Busy)
{
    public static readonly HousingContext None = new(PlaceKind.Elsewhere, 0, string.Empty, 0, false, null, null, null, EditorMode.None, false, false);

    public Side? Side => Kind switch
    {
        PlaceKind.Interior => Core.Side.Interior,
        PlaceKind.Ward => Core.Side.Garden,
        _ => null,
    };

    private static (uint Territory, string? Size, string? District) interior;

    /// <summary>An interior's size and district, from its territory's name (kept: it changes only with the zone).</summary>
    private static (string? Size, string? District) InteriorOf(uint territory, string name)
    {
        if (interior.Territory == territory) return (interior.Size, interior.District);
        string? district = name.Length >= 2
            ? name[..2] switch
            {
                "s1" => "mist",
                "f1" => "lavender-beds",
                "w1" => "goblet",
                "e1" => "shirogane",
                "r1" => "empyreum",
                _ => null,
            }
            : null;
        interior = (territory, Districts.HouseSizeOfInterior(name), district);
        return (interior.Size, interior.District);
    }

    private static (ulong House, string? District) houseDistrict;

    /// <summary>The district of the house you're in, from its id's ward territory, else the game's original house territory (kept per house).</summary>
    private static string? HouseDistrict(FFXIVClientStructs.FFXIV.Client.Game.HouseId id)
    {
        if (houseDistrict.House == id.Id && id.Id != 0) return houseDistrict.District;
        string? district = null;
        try
        {
            var ward = (uint)((id.Id >> 32) & 0xFFFF);
            district = Districts.OfTerritoryName(GameData.TerritoryName(ward))
                       ?? Districts.OfTerritoryName(GameData.TerritoryName(HousingManager.GetOriginalHouseTerritoryTypeId()));
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't read the house's district");
        }
        houseDistrict = (id.Id, district);
        return district;
    }

    public static unsafe HousingContext Read(PlotTable plots)
    {
        if (!Services.ClientState.IsLoggedIn) return None;
        var territory = Services.ClientState.TerritoryType;
        var hm = HousingManager.Instance();
        if (hm == null) return None;

        var busy = Services.Condition[ConditionFlag.InCombat] || Services.Condition[ConditionFlag.BetweenAreas]
                   || Services.Condition[ConditionFlag.BetweenAreas51] || Services.Condition[ConditionFlag.WatchingCutscene]
                   || Services.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Services.Condition[ConditionFlag.LoggingOut];
        var name = GameData.TerritoryName(territory);
        // The editor's state is read only in a home (its offset isn't in ClientStructs: nothing is
        // read through it elsewhere), once a frame.
        var inHome = hm->IndoorTerritory != null || hm->OutdoorTerritory != null;
        var mode = inHome ? HousingEditor.Mode : EditorMode.None;
        var open = mode != EditorMode.None;

        if (hm->IndoorTerritory != null)
        {
            var (size, district) = InteriorOf(territory, name);
            // A remodelled interior's name says no district ("h2i2": 0.8.1's Mist house read none, so its
            // district's Housing Merchant was out of reach and nothing was planned from vendors): the
            // house's own id has its ward's territory ("s1h1"), and the game its original one.
            district ??= HouseDistrict(hm->IndoorTerritory->HouseId);
            return new HousingContext(PlaceKind.Interior, territory, name, hm->IndoorTerritory->HouseId.Id, hm->HasHousePermissions(),
                size, district, null, mode, open, busy);
        }

        if (hm->OutdoorTerritory != null)
        {
            var ot = hm->OutdoorTerritory;
            var district = plots.DistrictOfWard(name);
            // The plot whose furnishings you're editing, else the one you're standing on.
            int? plot = ot->EditingFurnishingsOfPlot >= 0 ? ot->EditingFurnishingsOfPlot + 1
                : ot->HouseUnit.IsApartment ? null : ot->HouseUnit.PlotIndex + 1;
            string? size = null;
            if (district != null && plot != null && plots.TryGet(district, plot.Value, out var spot)) size = Districts.HouseSizeOfPlot(spot.Size);
            return new HousingContext(PlaceKind.Ward, territory, name, ot->HouseId.Id, hm->HasHousePermissions(), size, district, plot, mode, open, busy);
        }

        return None with { TerritoryId = territory, TerritoryName = name, Busy = busy };
    }
}
