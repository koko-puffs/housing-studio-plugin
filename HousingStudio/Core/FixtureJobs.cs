using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>Where a design's fixture stands against the home.</summary>
public enum FixtureState
{
    /// <summary>The home has it already (and its dye, outside).</summary>
    Set,

    /// <summary>To set: its item is in your bags, and the plugin knows its slot.</summary>
    ToSet,

    /// <summary>Its item isn't in your bags (or not enough of them for every floor that has it).</summary>
    NotOwned,

    /// <summary>One the plugin doesn't set (the house's window and door, a themed outside, a dye): you set it.</summary>
    ByHand,
}

/// <summary>A fixture of the design, and what setting it takes.</summary>
/// <param name="Slot">Its slot in the fixtures window's edit container (<see cref="FixtureJobs.InteriorSlot"/>), when it's to set.</param>
public sealed record FixtureTask(LayoutFixture Fixture, FixtureState State, string Why, int? Slot);

/// <summary>A fixture the home has now, as the game's tables read it.</summary>
public sealed record CurrentFixture(string Level, string Type, uint ItemId, string? DyeHex = null);

/// <summary>
/// Setting a design's fixtures (Settings → Experimental): which of them the home has, which can be
/// set from your bags, and where each goes in the game's fixtures window. The window keeps what it
/// shows in an edit container (InventoryType HousingInteriorAppearanceEdit, 2012, or the outside's
/// 2011); the order of its slots is a guess (the game's own order of the parts, floor by floor),
/// which <see cref="LayoutAgrees"/> checks against what's set before anything is put in it. Pure,
/// so it's tested.
/// </summary>
public static class FixtureJobs
{
    /// <summary>The floors, in the order the game keeps them (IndoorAreaLayoutData's Floor0, Floor1, Floor2).</summary>
    public static readonly string[] InteriorLevels = { "Ground Floor", "Upper Floor", "Basement" };

    /// <summary>A floor's parts, in the game's order (IndoorFloorLayoutData's Part0 to Part4).</summary>
    public static readonly string[] InteriorParts = { "Wall", "Window", "Door", "Floor", "Light" };

    /// <summary>The parts of a floor the fixtures window sets (a floor's window and door are the house's).</summary>
    public static readonly string[] InteriorSettable = { "Wall", "Floor", "Light" };

    /// <summary>The outside's parts, in the game's order (OutdoorPlotLayoutData's Fixture[0..7]).</summary>
    public static readonly string[] ExteriorParts = { "Roof", "Walls", "Window", "Door", "Roof Decoration", "Wall Decoration", "Placard", "Fence" };

    /// <summary>
    /// "Windows", "roof decoration" and "RoofDecoration" are one part: compared without case, spaces or
    /// a plural s, with the game's longer words for a floor's parts ("Flooring", "Interior Wall",
    /// "Lighting") taken as the short ones.
    /// </summary>
    public static string Norm(string s)
    {
        var t = new string(s.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray()).ToLowerInvariant();
        if (t.Length > 1 && t.EndsWith('s')) t = t[..^1];
        return t switch
        {
            "flooring" => "floor",
            "interiorwall" => "wall",
            "lighting" or "ceilinglight" => "light",
            _ => t,
        };
    }

    /// <summary>
    /// A floor's wall, flooring or light: its slot in the edit container, the floors one after another
    /// (five parts each), or null for what the window doesn't set. A guess, checked in the game.
    /// </summary>
    public static int? InteriorSlot(string level, string type)
    {
        var floor = Array.FindIndex(InteriorLevels, l => Norm(l) == Norm(level));
        var part = Array.FindIndex(InteriorParts, p => Norm(p) == Norm(type));
        if (floor < 0 || part < 0 || !InteriorSettable.Any(p => Norm(p) == Norm(type))) return null;
        return floor * InteriorParts.Length + part;
    }

    /// <summary>An outside part's slot in the edit container (the parts' own order), or null. A guess, checked in the game.</summary>
    public static int? ExteriorSlot(string type)
    {
        var i = Array.FindIndex(ExteriorParts, p => Norm(p) == Norm(type));
        return i < 0 ? null : i;
    }

    /// <summary>The slot of a fixture on a side (inside: its floor and part; outside: its part).</summary>
    public static int? SlotOf(Side side, string level, string type) => side == Side.Interior ? InteriorSlot(level, type) : ExteriorSlot(type);

    /// <summary>
    /// The design's fixtures for a side, each with what it takes: set already; to set (from your
    /// bags: each copy used once, so the same flooring on three floors needs three); not in your bags;
    /// or for you to set (a floor's window or door, the house's own, a themed outside, a dye).
    /// </summary>
    /// <param name="owned">How many of an item are in your bags.</param>
    /// <param name="isThemed">Whether an item is a whole themed outside (HousingUnitedExterior).</param>
    public static List<FixtureTask> Plan(IEnumerable<LayoutFixture> design, IEnumerable<CurrentFixture> current, Side side, Func<uint, int> owned, Func<uint, bool> isThemed)
    {
        var now = current.ToList();
        var left = new Dictionary<uint, int>();
        var tasks = new List<FixtureTask>();
        foreach (var f in design)
        {
            if (side == Side.Interior ? f.Level == "Exterior" : f.Level != "Exterior") continue;
            var has = now.FirstOrDefault(c => Norm(c.Level) == Norm(f.Level) && Norm(c.Type) == Norm(f.Type));
            var dyeOk = f.DyeHex == null || string.Equals(has?.DyeHex, f.DyeHex, StringComparison.OrdinalIgnoreCase);
            if (has != null && has.ItemId == f.ItemId)
            {
                tasks.Add(dyeOk
                    ? new FixtureTask(f, FixtureState.Set, "Set already.", null)
                    : new FixtureTask(f, FixtureState.ByHand, "Set already; its dye is yours to apply.", null));
                continue;
            }
            if (side == Side.Garden && isThemed(f.ItemId))
            {
                tasks.Add(new FixtureTask(f, FixtureState.ByHand, "A themed outside sets every part at once: yours to set.", null));
                continue;
            }
            var slot = SlotOf(side, f.Level, f.Type);
            if (slot == null)
            {
                tasks.Add(new FixtureTask(f, FixtureState.ByHand, side == Side.Interior ? "The house's window and door are set from outside: yours to set." : "Yours to set.", null));
                continue;
            }
            if (!left.TryGetValue(f.ItemId, out var n)) n = owned(f.ItemId);
            if (n <= 0)
            {
                left[f.ItemId] = 0;
                tasks.Add(new FixtureTask(f, FixtureState.NotOwned, "Not in your bags.", slot));
                continue;
            }
            left[f.ItemId] = n - 1;
            tasks.Add(new FixtureTask(f, FixtureState.ToSet, f.DyeHex != null && side == Side.Garden ? "To set; its dye is yours to apply after." : "To set.", slot));
        }
        return tasks;
    }

    /// <summary>
    /// Does the edit container hold what's set now, slot by slot, as the guessed order puts it? Only
    /// then are pieces put in its slots. Needs at least one part to compare (a home with nothing but
    /// the default fixtures can't tell), and every part set to be where the order says.
    /// </summary>
    /// <param name="container">The container's items by slot (empty slots left out).</param>
    public static bool LayoutAgrees(IReadOnlyDictionary<int, uint> container, IEnumerable<CurrentFixture> current, Side side, out string why)
    {
        var compared = 0;
        foreach (var c in current)
        {
            var slot = side == Side.Interior
                ? Array.FindIndex(InteriorLevels, l => Norm(l) == Norm(c.Level)) is var floor and >= 0 && Array.FindIndex(InteriorParts, p => Norm(p) == Norm(c.Type)) is var part and >= 0
                    ? floor * InteriorParts.Length + part : (int?)null
                : ExteriorSlot(c.Type);
            if (slot == null) continue;
            if (!container.TryGetValue(slot.Value, out var item))
            {
                why = $"The {c.Level.ToLowerInvariant()}'s {c.Type.ToLowerInvariant()} is set, but slot {slot} of the window's container is empty.";
                return false;
            }
            if (item != c.ItemId)
            {
                why = $"Slot {slot} of the window's container holds item {item}, not the {c.Level.ToLowerInvariant()}'s {c.Type.ToLowerInvariant()} (item {c.ItemId}).";
                return false;
            }
            compared++;
        }
        why = compared == 0 ? "Nothing set here to compare the window's slots with." : string.Empty;
        return compared > 0;
    }

    /// <summary>
    /// The Housing menu's entry with these words: the same words, else one starting with them, else
    /// one holding them (the menu's "Interior Fixtures" for "Fixtures"). -1 when none.
    /// </summary>
    public static int MenuEntry(IReadOnlyList<string> entries, IReadOnlyList<string> words)
    {
        var wanted = words.Where(w => w.Trim().Length > 0).Select(w => w.Trim()).ToList();
        for (var pass = 0; pass < 3; pass++)
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i].Trim();
                if (wanted.Any(w => pass switch
                    {
                        0 => e.Equals(w, StringComparison.OrdinalIgnoreCase),
                        1 => e.StartsWith(w, StringComparison.OrdinalIgnoreCase),
                        _ => e.Contains(w, StringComparison.OrdinalIgnoreCase),
                    }))
                    return i;
            }
        return -1;
    }

    /// <summary>
    /// Are the Housing menu's entries read in their places? Its first two are Indoor Furnishings
    /// ([i:0]) and Furnishing Color ([i:1]), as recorded: only then is an entry's place taken for its
    /// click. (English words; another client's menu is chosen by you.)
    /// </summary>
    public static bool MenuInPlace(IReadOnlyList<string> entries) =>
        entries.Count >= 2
        && entries[0].Contains("Furnishing", StringComparison.OrdinalIgnoreCase)
        && entries[1].Contains("Furnishing", StringComparison.OrdinalIgnoreCase)
        && entries[1].Contains("Colo", StringComparison.OrdinalIgnoreCase);
}
