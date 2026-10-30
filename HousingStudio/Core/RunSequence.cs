using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

public enum JobKind
{
    /// <summary>Already placed: picked up and set at its spot.</summary>
    Move,

    /// <summary>In the storeroom: picked from the furnishing list, placed, set at its spot.</summary>
    PlaceFromStoreroom,

    /// <summary>In your bags: picked from the item's menu, placed, set at its spot.</summary>
    PlaceFromBags,

    /// <summary>Placed, in the wrong dye: dyed in the furnishing dye mode.</summary>
    Dye,
}

public enum RunPhase
{
    Move,
    Place,
    Dye,
}

/// <summary>One piece to work on in a run.</summary>
public sealed class RunJob
{
    public required JobKind Kind { get; init; }
    public required Target Target { get; init; }

    /// <summary>The matched entry (for moves and placing), or null for a dye job.</summary>
    public PlanEntry? Entry { get; init; }

    /// <summary>The placed piece's furniture slot: known for moves and dyes, found once a new piece lands.</summary>
    public int? Key { get; set; }

    /// <summary>For a dye job: the dye it has now (0 undyed).</summary>
    public byte FromStain { get; init; }

    public JobOutcome Outcome { get; set; } = JobOutcome.Pending;
    public string? Note { get; set; }

    /// <summary>Why it didn't work, when it didn't (for the problems list).</summary>
    public ProblemKind Problem { get; set; }

    /// <summary>When work on it started and ended (for the time left).</summary>
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public RunPhase Phase => Kind switch
    {
        JobKind.Move => RunPhase.Move,
        JobKind.Dye => RunPhase.Dye,
        _ => RunPhase.Place,
    };
}

/// <summary>Why a piece didn't work, for the problems list.</summary>
public enum ProblemKind
{
    None,

    /// <summary>Not where the plan said: not in the storeroom list, not in the bags, no longer placed.</summary>
    NotFound,

    /// <summary>Placed or moved, but the game didn't put it at its spot (or didn't place it at all).</summary>
    DidNotLand,

    /// <summary>The dye isn't in your bags (or the game wouldn't use it).</summary>
    DyeMissing,

    /// <summary>The dye was chosen but the piece didn't change.</summary>
    DyeFailed,

    /// <summary>The game hasn't loaded the piece (too far away).</summary>
    NotLoaded,

    /// <summary>Skipped by you.</summary>
    Skipped,

    Other,
}

public enum JobOutcome
{
    Pending,
    Working,
    Done,
    Skipped,
    Failed,
}

/// <summary>
/// The order a run works in: everything that stands on the floor first (pieces already placed are
/// moved, then pieces from the bags and storeroom are put in), then the things that sit on them or
/// hang on walls in the same way, and dyes last (they're planned when the run gets there, from what
/// is placed by then). Pure, so it's tested without the game.
/// </summary>
public static class RunSequence
{
    public static List<RunJob> Build(IEnumerable<PlanEntry> entries)
    {
        var ordered = Matcher.WorkOrder(entries).ToList();
        var jobs = new List<RunJob>();
        foreach (var late in new[] { false, true })
        {
            foreach (var e in ordered.Where(e => e.Target.Late == late && e.State == PieceState.Move))
                jobs.Add(new RunJob { Kind = JobKind.Move, Target = e.Target, Entry = e, Key = e.Placed?.Key });
            foreach (var e in ordered.Where(e => e.Target.Late == late && e.State is PieceState.FromStoreroom or PieceState.FromInventory))
                jobs.Add(new RunJob
                {
                    Kind = e.State == PieceState.FromStoreroom ? JobKind.PlaceFromStoreroom : JobKind.PlaceFromBags,
                    Target = e.Target,
                    Entry = e,
                });
        }
        return jobs;
    }
}

/// <summary>A placed piece and the dye the design wants on it.</summary>
/// <param name="Key">The placed piece's furniture slot.</param>
/// <param name="Has">Its dye now (0 undyed).</param>
public sealed record DyeNeed(int Key, Target Target, byte Has)
{
    public byte Want => Target.Stain;
}

public sealed class DyePlan
{
    /// <summary>Pieces to dye, in order, each with a dye you have.</summary>
    public List<RunJob> Jobs { get; } = new();

    /// <summary>Dyes you're short of: (dye, pieces that need it, how many you have).</summary>
    public List<(byte Stain, int Needed, int Owned)> Short { get; } = new();

    /// <summary>Pieces that are dyed but the design has them undyed (removing a dye isn't done automatically).</summary>
    public List<DyeNeed> Undye { get; } = new();

    /// <summary>Pieces skipped for want of their dye.</summary>
    public List<DyeNeed> Skipped { get; } = new();
}

public static class DyePlanner
{
    /// <summary>
    /// Which placed pieces to dye with the dyes you have (each dyeing uses one dye from your bags);
    /// the rest are listed as short. Order: floor by floor, low to high, as placing.
    /// </summary>
    public static DyePlan Plan(IEnumerable<DyeNeed> needs, IReadOnlyDictionary<byte, int> owned)
    {
        var plan = new DyePlan();
        var left = owned.ToDictionary(kv => kv.Key, kv => kv.Value);
        var wanted = new Dictionary<byte, int>();
        foreach (var n in needs.OrderBy(n => n.Target.Floor).ThenBy(n => MathF.Round(n.Target.Position.Y, 2)).ThenBy(n => n.Target.Key))
        {
            if (n.Want == n.Has) continue;
            if (n.Want == 0)
            {
                plan.Undye.Add(n);
                continue;
            }
            wanted[n.Want] = wanted.GetValueOrDefault(n.Want) + 1;
            if (left.GetValueOrDefault(n.Want) > 0)
            {
                left[n.Want]--;
                plan.Jobs.Add(new RunJob { Kind = JobKind.Dye, Target = n.Target, Key = n.Key, FromStain = n.Has });
            }
            else
            {
                plan.Skipped.Add(n);
            }
        }
        foreach (var (stain, needed) in wanted.OrderBy(kv => kv.Key))
        {
            var have = owned.GetValueOrDefault(stain);
            if (have < needed) plan.Short.Add((stain, needed, have));
        }
        return plan;
    }

    /// <summary>
    /// Dyes to buy for the whole design: every piece that will need dyeing (missing ones, ones from
    /// your bags, wrongly dyed ones) less the dyes you have.
    /// </summary>
    public static List<(byte Stain, int Count)> ToBuy(Plan plan, IReadOnlyDictionary<byte, int> owned)
    {
        var needed = plan.Entries
            .Where(e => e.Target.Stain != 0 && (e.State is PieceState.Missing or PieceState.FromInventory || e.CurrentStain != e.Target.Stain))
            .GroupBy(e => e.Target.Stain)
            .Select(g => (Stain: g.Key, Count: g.Count() - owned.GetValueOrDefault(g.Key)))
            .Where(x => x.Count > 0)
            .OrderBy(x => x.Stain)
            .ToList();
        return needed;
    }
}

/// <summary>How long a run has left, from what it has measured so far. Pure, so it's tested.</summary>
public static class RunEstimate
{
    /// <summary>
    /// The pieces left times how long a piece has taken (the same phase's average when there is
    /// one, else the run's, else a guess from the pace), plus the pause between pieces.
    /// </summary>
    public static TimeSpan TimeLeft(IReadOnlyList<RunJob> jobs, int intervalMs, bool dyesStillToPlan = false, int dyesExpected = 0)
    {
        var finished = jobs.Where(j => j.StartedAt != null && j.EndedAt != null && j.Outcome is JobOutcome.Done).ToList();
        double Average(IEnumerable<RunJob> js)
        {
            var list = js.Select(j => (j.EndedAt!.Value - j.StartedAt!.Value).TotalSeconds).ToList();
            return list.Count == 0 ? double.NaN : list.Average();
        }
        var overall = Average(finished);
        var pause = Math.Max(Pacing.MinimumMs, intervalMs) / 1000.0;
        double PerPiece(RunPhase phase)
        {
            var a = Average(finished.Where(j => j.Phase == phase));
            if (!double.IsNaN(a)) return a;
            if (!double.IsNaN(overall)) return overall;
            return phase switch
            {
                RunPhase.Move => 1.5,
                RunPhase.Place => 4,
                _ => 5,
            };
        }
        var seconds = 0.0;
        foreach (var j in jobs.Where(j => j.Outcome is JobOutcome.Pending or JobOutcome.Working))
            seconds += PerPiece(j.Phase) + pause;
        if (dyesStillToPlan) seconds += dyesExpected * (PerPiece(RunPhase.Dye) + pause);
        return TimeSpan.FromSeconds(Math.Round(seconds));
    }
}
