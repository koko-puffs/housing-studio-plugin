using System;
using System.Collections.Generic;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>A row of "How far it got": a piece or a stop, a line under it, and whether it worked (else it's where it stopped).</summary>
public sealed record HowFarRow(string Name, string Note, bool Worked);

/// <summary>
/// "How far it got" after a rehearsal stopped before it would have done anything: the count for its
/// label ("0 of 46 pieces", "0 of 10 stops"), the rows (what worked last, where it stopped), and where
/// it stopped in words for the line under the title ("at the first stop", "at piece 3 of 46").
/// </summary>
public sealed record HowFar(string Count, IReadOnlyList<HowFarRow> Rows, string? At);

/// <summary>
/// A rehearsal's words (Ready's Preview menu, Running, Done, the errands): a run that does every step
/// a real start does and commits nothing. Pure, so it's tested.
/// </summary>
public static class RehearsalWords
{
    public const string Mark = "Mark the spots";
    public const string MarkNote = "Today's preview: lights each spot, moves nothing";
    public const string Rehearse = "Rehearse the run";
    public const string RehearseNote = "Does every step, commits nothing";

    /// <summary>The eyebrow while it runs (the errands' screen; the placing screen says "Rehearsal · Solution9Loft", <see cref="PlacingWords.Eyebrow"/>).</summary>
    public const string Eyebrow = "Rehearsal · nothing is committed";

    /// <summary>The teleports are real: the only gil a rehearsal spends.</summary>
    public const string Teleports = "Teleports are real: their fees are the only gil a rehearsal spends.";

    /// <summary>The errands' brass button in a rehearsal.</summary>
    public const string ErrandsButton = "Rehearse these errands";

    /// <summary>Under the result's Done and Copy report: where Done goes.</summary>
    public const string DoneHint = "Done goes back to Ready, where you can rehearse again or start for real.";

    /// <summary>The pill: "Rehearsing · 4 minutes left", "Rehearsing" when the time isn't known.</summary>
    public static string Pill(string? left) => string.IsNullOrEmpty(left) ? "Rehearsing" : $"Rehearsing · {left} left";

    /// <summary>The menu row's tip for Rehearse, saying what it follows (Ready's choices, as the brass button would).</summary>
    public static string RehearseTip(string goLabel, bool getting) =>
        getting
            ? $"Does what \"{goLabel}\" would, step by step: the trips (asking for each purchase and cancelling), then placing. Nothing is bought or placed; teleports are real."
            : $"Does what \"{goLabel}\" would, step by step: each piece held at its spot and let go, each dye picked and closed. Nothing is placed, moved, stored or dyed.";

    /// <summary>Done's title: ("Rehearsal", "done") or ("Rehearsal", "stopped").</summary>
    public static (string Plain, string Italic) Title(bool stopped) => ("Rehearsal", stopped ? "stopped" : "done");

    /// <summary>The line under the title before 0.9.0 (see <see cref="Outcome"/>).</summary>
    public static string Line(int problems, bool stopped, string? why)
    {
        var head = problems switch
        {
            0 => "Every step worked",
            1 => "1 step didn't work",
            _ => $"{problems} steps didn't work",
        };
        if (stopped && !string.IsNullOrEmpty(why)) return $"{head}. It stopped: {why}";
        return $"{head}, and nothing was committed.";
    }

    /// <summary>The figure's words while it runs before 0.9.0 (see <see cref="PlacingWords.Figure"/>).</summary>
    public static string Figure(int pieces) => $"of {pieces} pieces rehearsed";

    /// <summary>What a rehearsal didn't do, said once: "Nothing was committed." (placing); the errands' "Nothing was bought or taken.", with "or placed" once it rehearsed placing too.</summary>
    public static string Nothing(bool errands, bool placingToo) =>
        !errands ? "Nothing was committed." : placingToo ? "Nothing was bought, taken or placed." : "Nothing was bought or taken.";

    /// <summary>
    /// The result's one sentence under the title: who stopped it, where, and that nothing was committed
    /// ("Stopped by you at the first stop. Nothing was bought or taken."); when it finished, what didn't
    /// work ("Every step worked. Nothing was committed.").
    /// </summary>
    /// <param name="at">Where it stopped, with its preposition ("at piece 3 of 46"), or null.</param>
    /// <param name="nothing">What it didn't do (<see cref="Nothing"/>).</param>
    public static string Outcome(bool stopped, bool byYou, string? why, string? at, int problems, string nothing)
    {
        if (!stopped)
            return problems switch
            {
                0 => $"Every step worked. {nothing}",
                1 => $"1 step didn't work. {nothing}",
                _ => $"{problems} steps didn't work. {nothing}",
            };
        var where = string.IsNullOrEmpty(at) ? string.Empty : " " + at;
        var reason = why?.Trim().TrimEnd('.').Trim();
        if (byYou || string.IsNullOrEmpty(reason)) return $"Stopped{(byYou ? " by you" : string.Empty)}{where}. {nothing}";
        return $"Stopped{where}: {reason}. {nothing}";
    }

    /// <summary>The folded log's title: "What it did · 13 lines".</summary>
    public static string LogTitle(int lines) => $"What it did · {Words.Count(lines, "line")}";

    /// <summary>What a run's piece was being done, in a few words.</summary>
    public static string Doing(JobKind kind) => kind switch
    {
        JobKind.Move => "Moving it to its spot",
        JobKind.PlaceFromStoreroom => "Placing it from the storeroom",
        JobKind.PlaceFromBags => "Placing it from your bags",
        JobKind.Dye => "Dyeing it",
        _ => "Putting it away",
    };

    /// <summary>
    /// How far a placing rehearsal got: the last piece that worked (and how many did), and the piece it
    /// stopped at (the one it was working on, or the next one when it stopped between pieces).
    /// </summary>
    /// <param name="jobs">The run's pieces in its order; <c>StoppedHere</c> marks the one stopped part way.</param>
    public static HowFar Pieces(IReadOnlyList<(string Name, JobKind Kind, JobOutcome Outcome, bool StoppedHere)> jobs)
    {
        var total = jobs.Count;
        if (total == 0) return new HowFar("no pieces", Array.Empty<HowFarRow>(), null);
        var done = 0;
        var lastDone = -1;
        var at = -1;
        for (var i = 0; i < total; i++)
        {
            if (jobs[i].Outcome == JobOutcome.Done)
            {
                done++;
                lastDone = i;
            }
            if (at < 0 && jobs[i].StoppedHere) at = i;
        }
        if (at < 0)
        {
            for (var i = 0; i < total; i++)
            {
                if (jobs[i].Outcome is JobOutcome.Pending or JobOutcome.Working)
                {
                    at = i;
                    break;
                }
            }
        }
        var rows = new List<HowFarRow>(2);
        if (lastDone >= 0)
        {
            var last = jobs[lastDone];
            rows.Add(new HowFarRow(last.Name, done == 1 ? Doing(last.Kind) : $"{Doing(last.Kind)} · the last of {done} that worked", true));
        }
        if (at >= 0)
        {
            var stop = jobs[at];
            rows.Add(new HowFarRow(stop.Name, $"{Doing(stop.Kind)} · {(stop.StoppedHere ? "stopped part way" : "not started")}", false));
        }
        var where = at < 0 ? "after the last piece" : at == 0 ? "at the first piece" : $"at piece {at + 1} of {total}";
        return new HowFar($"{done} of {Words.Count(total, "piece")}", rows, where);
    }

    /// <summary>
    /// How far the errands' rehearsal got: the stops in order (each with how many things it gets
    /// there), passed while what it would get (<paramref name="got"/>) covers them; the last one passed,
    /// or its teleports when none was, and the stop it stopped at.
    /// </summary>
    public static HowFar Stops(IReadOnlyList<(string Name, int Things)> stops, int got, int teleports)
    {
        var total = stops.Count;
        var passed = 0;
        var sum = 0;
        while (passed < total && got > 0 && sum + stops[passed].Things <= got)
        {
            sum += stops[passed].Things;
            passed++;
        }
        var rows = new List<HowFarRow>(2);
        if (passed == 0 && teleports > 0)
            rows.Add(new HowFarRow("Setting out", teleports == 1 ? "1 teleport made" : $"{teleports} teleports made", true));
        if (passed > 0)
        {
            var last = stops[passed - 1];
            rows.Add(new HowFarRow(last.Name, passed == 1 ? Words.Count(last.Things, "thing") : $"{Words.Count(last.Things, "thing")} · the last of {passed} stops", true));
        }
        if (passed < total)
        {
            var stop = stops[passed];
            rows.Add(new HowFarRow(stop.Name, $"{(got > sum ? "Part way" : "Nothing asked for yet")} · {Words.Count(stop.Things, "thing")}", false));
        }
        var where = total == 0 ? null : passed >= total ? "after the last stop" : passed == 0 ? "at the first stop" : $"at stop {passed + 1} of {total}";
        return new HowFar($"{passed} of {Words.Count(total, "stop")}", rows, where);
    }

    /// <summary>An errand stop's name: "Housing Merchant · The Lavender Beds", "Market board · Gilgamesh", "Alys · your retainer".</summary>
    public static string StopName(ErrandStop stop)
    {
        switch (stop.Kind)
        {
            case StopKind.Retainer:
                return string.IsNullOrEmpty(stop.Retainer?.Name) ? "A retainer" : $"{stop.Retainer!.Name} · your retainer";
            case StopKind.Market:
                return string.IsNullOrEmpty(stop.World) ? "Market board" : $"Market board · {stop.World}";
            default:
                var v = stop.Vendor;
                return v == null ? "A vendor" : v.PlaceName.Length > 0 ? $"{v.NpcName} · {v.PlaceName}" : v.NpcName;
        }
    }
}
