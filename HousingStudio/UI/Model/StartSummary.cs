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
    /// <param name="undye">Dyes are taken off pieces the design has undyed (Terebinth is known, 0.9.12).</param>
    public static StartSummary Of(IReadOnlyCollection<PlanEntry> entries, bool dyeing, bool undye = false)
    {
        var moves = Matcher.WorkOrder(entries.Where(e => e.State == PieceState.Move)).ToList();
        var dyes = dyeing ? DyeCounts.ToApply(entries, undye) : 0;
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
    /// <param name="putAways">Pieces Clear out or Swap puts away first (1.0.2: left out, so "about N min" came out far too low).</param>
    public TimeSpan Estimate(IReadOnlyCollection<PlanEntry> entries, Pace pace, int putAways = 0)
    {
        var profile = Pacing.Profile(pace);
        var time = RunEstimate.TimeLeft(RunSequence.Build(entries), profile, dyesStillToPlan: Dyes > 0, dyesExpected: Dyes);
        if (putAways <= 0) return time;
        // As a move: picked, then put away (Quick's 1.5 s, longer or shorter with the pace's waits), and the gap.
        var each = profile.WaitScale * 1.5 + System.Math.Max(Pacing.MinimumMs, profile.PieceGapMs) / 1000.0;
        return time + System.TimeSpan.FromSeconds(putAways * each);
    }
}
