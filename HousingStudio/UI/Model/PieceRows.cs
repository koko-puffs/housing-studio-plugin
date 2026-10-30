using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>The Pieces view's filter chips.</summary>
public enum PieceFilter
{
    ToDo,
    InPlace,
    Missing,
    All,
}

/// <summary>One row of the Pieces list: the same item in the same dye and state, stacked ("Hingan Bookshelf ×9").</summary>
/// <param name="State">Null when there's nothing to match against yet (not in a home).</param>
public sealed record PieceRow(uint ItemId, byte Stain, PieceState? State, Floor Floor, IReadOnlyList<PlanEntry> Entries)
{
    public int Count => Entries.Count;
}

/// <summary>A floor's rows, with how many pieces they hold.</summary>
public sealed record FloorGroup(Floor Floor, int Count, IReadOnlyList<PieceRow> Rows);

/// <summary>The four filter chips' counts.</summary>
public readonly record struct FilterCounts(int ToDo, int InPlace, int Missing, int All)
{
    public int Of(PieceFilter f) => f switch
    {
        PieceFilter.ToDo => ToDo,
        PieceFilter.InPlace => InPlace,
        PieceFilter.Missing => Missing,
        _ => All,
    };
}

/// <summary>Builds the Pieces list: counts per chip, floors in the house's order, stacked rows in the run's order. Pure, so it's tested.</summary>
public static class PieceRows
{
    /// <summary>The order floors are listed in: the ground floor first, as you walk in.</summary>
    public static readonly Floor[] FloorOrder = { Floor.Ground, Floor.Upper, Floor.Basement, Floor.Garden };

    public static bool IsToDo(PieceState s) => s is PieceState.Move or PieceState.FromStoreroom or PieceState.FromInventory;

    public static bool Matches(PieceState? state, PieceFilter filter) => filter switch
    {
        PieceFilter.All => true,
        _ when state == null => false,
        PieceFilter.ToDo => IsToDo(state.Value),
        PieceFilter.InPlace => state == PieceState.InPlace,
        _ => state == PieceState.Missing,
    };

    public static FilterCounts Counts(IReadOnlyCollection<PlanEntry> entries, bool statesKnown = true)
    {
        if (!statesKnown) return new FilterCounts(0, 0, 0, entries.Count);
        return new FilterCounts(
            entries.Count(e => IsToDo(e.State)),
            entries.Count(e => e.State == PieceState.InPlace),
            entries.Count(e => e.State == PieceState.Missing),
            entries.Count);
    }

    /// <summary>The run's order: what's moved first, then placed from the storeroom, then your bags; then missing; what's in place last.</summary>
    public static int StateOrder(PieceState? s) => s switch
    {
        PieceState.Move => 0,
        PieceState.FromStoreroom => 1,
        PieceState.FromInventory => 2,
        PieceState.Missing => 3,
        PieceState.InPlace => 4,
        _ => 5,
    };

    /// <param name="statesKnown">False when the design isn't matched yet: every row then has a null state.</param>
    public static List<FloorGroup> Build(IEnumerable<PlanEntry> entries, PieceFilter filter, Func<uint, string> name, string search, bool statesKnown = true)
    {
        var query = search.Trim();
        var shown = entries
            .Where(e => !statesKnown || Matches(e.State, filter))
            .Where(e => query.Length == 0 || name(e.Target.ItemId).Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var groups = new List<FloorGroup>();
        foreach (var floor in FloorOrder)
        {
            var onFloor = shown.Where(e => e.Target.Floor == floor).ToList();
            if (onFloor.Count == 0) continue;
            var rows = onFloor
                .GroupBy(e => (e.Target.ItemId, e.Target.Stain, State: statesKnown ? e.State : (PieceState?)null))
                .Select(g => new PieceRow(g.Key.ItemId, g.Key.Stain, g.Key.State, floor, g.ToList()))
                .OrderBy(r => StateOrder(r.State))
                .ThenBy(r => name(r.ItemId), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(r => r.Count)
                .ThenBy(r => r.Stain)
                .ToList();
            groups.Add(new FloorGroup(floor, onFloor.Count, rows));
        }
        return groups;
    }
}
