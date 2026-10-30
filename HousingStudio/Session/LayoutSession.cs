using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// The layout you loaded, and how it matches what you have where you are now: re-read from the
/// game every second or so (and right before every piece is placed).
/// </summary>
public sealed class LayoutSession
{
    private readonly GameData data;
    private readonly FurnitureReader furniture;
    private readonly PlotTable plots;
    private readonly ActivityLog log;

    public LayoutSession(GameData data, FurnitureReader furniture, PlotTable plots, ActivityLog log)
    {
        this.data = data;
        this.furniture = furniture;
        this.plots = plots;
        this.log = log;
    }

    public Layout? Layout { get; private set; }
    public string? FilePath { get; private set; }
    public string? LoadError { get; private set; }

    /// <summary>The build's name, else the file's.</summary>
    public string Title => Layout?.BuildName ?? (FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : "No layout");

    public HousingContext Context { get; private set; } = HousingContext.None;

    /// <summary>The match for the side you're on (inside, or your garden), or null when there's nothing to match.</summary>
    public Plan? Plan { get; private set; }

    public Side? PlanSide { get; private set; }

    /// <summary>Placed pieces by furniture slot, as of the last read.</summary>
    public Dictionary<int, LivePiece> Live { get; } = new();

    public bool StoreroomLoaded { get; private set; }

    /// <summary>Why a side can't be matched or placed (wrong size of house, garden unreadable…).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>A garden's plot (where you are), for its markers and transforms.</summary>
    public PlotSpot? Plot { get; private set; }

    public DateTime ReadAt { get; private set; }

    /// <summary>Loads a file; false (with <see cref="LoadError"/> set) when it isn't a layout.</summary>
    public bool Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new LayoutFormatException("That file isn't there any more.");
            if (info.Length > LayoutReader.MaxBytes) throw new LayoutFormatException("That file is too big to be a layout.");
            var layout = LayoutReader.Parse(File.ReadAllText(path));
            Layout = layout;
            FilePath = path;
            LoadError = null;
            Plan = null;
            PlanSide = null;
            log.Info($"Loaded “{Title}”: {layout.Interior.Count} pieces inside, {layout.Garden.Count} in the garden.");
            return true;
        }
        catch (LayoutFormatException ex)
        {
            LoadError = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadError = $"Couldn't read that file: {ex.Message}";
        }
        log.Warn($"Couldn't load {Path.GetFileName(path)}: {LoadError}");
        return false;
    }

    public void Unload()
    {
        Layout = null;
        FilePath = null;
        Plan = null;
        PlanSide = null;
        LoadError = null;
    }

    /// <summary>Reads where you are and, when a layout is loaded, matches it again. Framework thread only.</summary>
    public void Refresh(HousingContext context)
    {
        Context = context;
        ReadAt = DateTime.UtcNow;
        Warnings.Clear();
        Live.Clear();
        Plot = null;
        var side = context.Side;
        if (Layout == null || side == null)
        {
            Plan = null;
            PlanSide = null;
            return;
        }

        List<LivePiece>? live;
        if (side == Side.Interior)
        {
            if (Layout.HouseSize != null && context.HouseSize != null && !Districts.SameSize(Layout.HouseSize, context.HouseSize))
                Warnings.Add($"This design is for {Home(Layout.HouseSize)}, and you're in {Home(context.HouseSize)}. Its pieces wouldn't line up with these walls, so placing is off here.");
            live = furniture.ReadInterior();
        }
        else
        {
            if (context.District == null || context.Plot == null || !plots.TryGet(context.District, context.Plot.Value, out var spot))
            {
                Warnings.Add("Couldn't tell which plot you're on. Stand on your plot and open Housing → Outdoor Furnishings.");
                Plan = null;
                PlanSide = side;
                return;
            }
            Plot = spot;
            if (Layout.HouseSize != null && !Layout.IsApartment && !Districts.SameSize(Layout.HouseSize, context.HouseSize))
                Warnings.Add($"This garden was made for a {Layout.HouseSize.ToLowerInvariant()} plot, and yours is {context.HouseSize?.ToLowerInvariant() ?? "another size"}; pieces near the edges may not fit.");
            if (Layout.District != null && Layout.District != context.District)
                Warnings.Add($"This garden was made in {Districts.Display(Layout.District)}; the ground in {Districts.Display(context.District)} may sit at other heights.");
            live = furniture.ReadGarden(out var why);
            if (live == null && why != null) Warnings.Add(why);
        }

        foreach (var p in live ?? new List<LivePiece>()) Live[p.Key] = p;
        var stock = InventoryReader.Read(data, side.Value, out var storeroomLoaded);
        StoreroomLoaded = storeroomLoaded;

        var targets = Targets(side.Value);
        var placed = Live.Values.Select(p => new PlacedPiece(p.Key, p.ItemId, p.Position, p.Yaw, p.Stain)).ToList();
        Plan = Matcher.Build(targets, placed, stock);
        PlanSide = side;
    }

    /// <summary>The layout's pieces for a side, where they go in this house or on this plot.</summary>
    public List<Target> Targets(Side side)
    {
        var list = new List<Target>();
        if (Layout == null) return list;
        if (side == Side.Interior)
        {
            var size = Context.HouseSize ?? Layout.HouseSize;
            foreach (var p in Layout.Interior)
                list.Add(new Target(p.Index, p.ItemId, p.Position, p.Yaw, data.StainOf(p.DyeHex), Floors.Of(p.Position.Y, size),
                    p.ParentIndex != null || data.GoesOnSomethingElse(p.ItemId)));
            return list;
        }

        if (Plot is not { } plot) return list;
        var designPlot = DesignPlot(plot);
        foreach (var p in Layout.Garden)
        {
            var local = p.Position;
            var yaw = p.Yaw;
            if (Layout.GardenInWardCoordinates && designPlot is { } dp)
                (local, yaw) = Transforms.WardToPlot(p.Position, p.Yaw, dp);
            var (world, worldYaw) = Transforms.PlotToWard(local, yaw, plot);
            list.Add(new Target(p.Index, p.ItemId, world, worldYaw, data.StainOf(p.DyeHex), Floor.Garden,
                p.ParentIndex != null || data.GoesOnSomethingElse(p.ItemId)));
        }
        return list;
    }

    /// <summary>For a garden saved in the ward's coordinates: the plot it was laid out on (the nearest to its middle).</summary>
    private PlotSpot? DesignPlot(PlotSpot fallback)
    {
        if (Layout == null || Layout.Garden.Count == 0) return null;
        var c = new Vector3(Layout.Garden.Average(g => g.Position.X), 0, Layout.Garden.Average(g => g.Position.Z));
        var district = Layout.District ?? Context.District;
        if (district == null) return fallback;
        PlotSpot? best = null;
        var bestDist = float.MaxValue;
        for (var n = 1; n <= 60; n++)
        {
            if (!plots.TryGet(district, n, out var spot)) continue;
            var d = Vector2.Distance(new Vector2(spot.X, spot.Z), new Vector2(c.X, c.Z));
            if (d < bestDist)
            {
                bestDist = d;
                best = spot;
            }
        }
        return bestDist < 40 ? best : fallback;
    }

    /// <summary>
    /// The dyes this design still needs beyond what's in your bags (each dyeing uses one): for pieces
    /// missing, coming from your bags, or in another dye now. Counts only a Stain's own dye items (the
    /// game's dye window may also offer a general dye such as Standard Spectrum Dye). Framework thread.
    /// </summary>
    public List<(byte Stain, string Name, int Count)> MissingDyes()
    {
        if (Plan == null) return new List<(byte, string, int)>();
        var owned = Plan.Entries.Select(e => e.Target.Stain).Where(s => s != 0).Distinct().ToDictionary(s => s, s => HousingActions.DyesOwned(data, s));
        return DyePlanner.ToBuy(Plan, owned).Select(b => (b.Stain, data.DyeName(b.Stain), b.Count)).ToList();
    }

    /// <summary>"a medium house", "an apartment".</summary>
    private static string Home(string size) => size == "Apartment" ? "an apartment" : $"a {size.ToLowerInvariant()} house";

    /// <summary>Can this side be placed (a layout, a plan, and nothing that rules it out)?</summary>
    public bool PlacingBlocked(out string why)
    {
        why = string.Empty;
        if (Layout == null) { why = "Load a layout first."; return true; }
        if (Context.Side == null) { why = "Go to your house (inside) or your plot (for the garden)."; return true; }
        if (!Context.HasPermission) { why = "You can only place furniture in a home you're allowed to decorate."; return true; }
        if (Context.Side == Side.Interior && Layout.HouseSize != null && Context.HouseSize != null && !Districts.SameSize(Layout.HouseSize, Context.HouseSize))
        {
            why = "This design is for another size of home.";
            return true;
        }
        if (Plan == null) { why = Warnings.FirstOrDefault() ?? "Nothing to match here yet."; return true; }
        return false;
    }
}
