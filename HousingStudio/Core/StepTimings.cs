using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>
/// How long a run's steps and pieces took, for the "Timing" summary at the end of a run, so the next
/// round of feedback shows where the time goes. Pure, so it's tested.
/// </summary>
public sealed class StepTimings
{
    private readonly Dictionary<string, List<double>> steps = new();
    private readonly Dictionary<RunPhase, List<double>> pieces = new();

    public void AddStep(string key, TimeSpan took)
    {
        if (!steps.TryGetValue(key, out var list)) steps[key] = list = new List<double>();
        list.Add(took.TotalMilliseconds);
    }

    public void AddPiece(RunPhase phase, TimeSpan took)
    {
        if (!pieces.TryGetValue(phase, out var list)) pieces[phase] = list = new List<double>();
        list.Add(took.TotalMilliseconds);
    }

    public void Clear()
    {
        steps.Clear();
        pieces.Clear();
    }

    /// <summary>Average time per piece in a phase, or null when none was timed.</summary>
    public TimeSpan? PerPiece(RunPhase phase) =>
        pieces.TryGetValue(phase, out var l) && l.Count > 0 ? TimeSpan.FromMilliseconds(l.Average()) : null;

    /// <summary>Steps by the total time they took, most first: (key, count, average ms, longest ms).</summary>
    public List<(string Key, int Count, double AverageMs, double LongestMs)> Slowest(int top = 8) =>
        steps.Select(kv => (kv.Key, kv.Value.Count, kv.Value.Average(), kv.Value.Max()))
            .OrderByDescending(x => x.Item3 * x.Item2)
            .Take(top)
            .ToList();

    /// <summary>The summary as lines for the log.</summary>
    public List<string> Summary()
    {
        var lines = new List<string>();
        var all = pieces.Values.SelectMany(v => v).ToList();
        if (all.Count > 0) lines.Add($"Timing: {all.Count} pieces, {all.Average() / 1000:0.0} s each on average (the pause between pieces not included).");
        foreach (var (phase, l) in pieces.OrderBy(kv => kv.Key))
            lines.Add($"  {phase}: {l.Count} pieces, {l.Average() / 1000:0.0} s each (longest {l.Max() / 1000:0.0} s).");
        var slow = Slowest();
        if (slow.Count > 0)
        {
            lines.Add("  Where the time went (step: times run, average, longest):");
            foreach (var (key, count, avg, max) in slow) lines.Add($"    {key}: {count}×, {avg:0} ms, {max:0} ms");
        }
        return lines;
    }
}
