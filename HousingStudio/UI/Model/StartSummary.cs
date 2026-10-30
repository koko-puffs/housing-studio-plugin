using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>
/// What Start does with the pieces shown, for the Ready view's "What Start does" panel and its time
/// estimate: the pieces to move, how many come from the storeroom and your bags, how many get dyed,
/// and what's left as it is. Pure, so it's tested.
/// </summary>
public sealed record StartSummary(IReadOnlyList<PlanEntry> Moves, int Storeroom, int Bags, int Dyes, int InPlace, int Missing, int Total)
{
    public int Places => Storeroom + Bags;

    /// <param name="dyeing">Settings → "Dye pieces after placing".</param>
    public static StartSummary Of(IReadOnlyCollection<PlanEntry> entries, bool dyeing)
    {
        var moves = Matcher.WorkOrder(entries.Where(e => e.State == PieceState.Move)).ToList();
        var dyes = dyeing ? DyeCounts.ToApply(entries) : 0;
        return new StartSummary(
            moves,
            entries.Count(e => e.State == PieceState.FromStoreroom),
            entries.Count(e => e.State == PieceState.FromInventory),
            dyes,
            entries.Count(e => e.State == PieceState.InPlace),
            entries.Count(e => e.State == PieceState.Missing),
            entries.Count);
    }

    /// <summary>About how long Start takes at a pace, with the runner's own guesses per piece (<see cref="RunEstimate"/>).</summary>
    public TimeSpan Estimate(IReadOnlyCollection<PlanEntry> entries, int intervalMs) =>
        RunEstimate.TimeLeft(RunSequence.Build(entries), intervalMs, dyesStillToPlan: Dyes > 0, dyesExpected: Dyes);
}
