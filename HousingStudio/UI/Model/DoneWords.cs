using System;
using System.Collections.Generic;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>
/// Done's one line of what happened: "44 of 46 placed · 20 dyed · 2 need you", with Ready's to-do
/// numbers (<see cref="RunFigure"/>). (What the errands got is said on their own summary, which comes
/// in Done's place after them.) Pure, so it's tested.
/// </summary>
public static class DoneWords
{
    /// <summary>"445 of 447 pieces"; "All 447 pieces" when every one is where the design has it.</summary>
    public static string Pieces(int figure, int total) =>
        total > 1 && figure >= total ? $"All {total} pieces" : $"{figure} of {Words.Count(total, "piece")}";

    /// <summary>The line before 0.9.0 (the design's pieces in place of all of them).</summary>
    public static string Line(int figure, int total) => Pieces(figure, total);

    /// <summary>
    /// "44 of 46 placed · 20 dyed · 2 need you": the moves and placements of the run's to-do, the dyes,
    /// and what needs you (the section below says what). "All 46 placed" when every one was;
    /// "18 of 20 dyed" for a run that only dyed; what was put away when that was all it did.
    /// </summary>
    public static string Line(RunFigure f, int needYou)
    {
        var parts = new List<string>(3);
        if (f.ToDo > 0) parts.Add(f.Done >= f.ToDo && f.ToDo > 1 ? $"All {f.ToDo} placed" : $"{f.Done} of {f.ToDo} placed");
        if (f.Dyes > 0)
            parts.Add(f.ToDo > 0 ? $"{f.Dyed} dyed" : f.Dyed >= f.Dyes && f.Dyes > 1 ? $"All {f.Dyes} dyed" : $"{f.Dyed} of {f.Dyes} dyed");
        if (f.ToDo == 0 && f.Dyes == 0 && f.PutAway > 0) parts.Add($"{f.PutAwayDone} of {f.PutAway} put away");
        if (needYou > 0) parts.Add(needYou == 1 ? "1 needs you" : $"{needYou} need you");
        return parts.Count == 0 ? "Nothing was left to do" : string.Join(" · ", parts);
    }
}

/// <summary>
/// What a placing run has done of what Ready called "to do" (its moves and placements), and its dyes
/// and put-aways apart: the placing screen's big figure, Done's line and its "Stopped at 18 of 46".
/// A piece that only needs its dye isn't to do (Ready doesn't count it there): it's one of the dyes,
/// done once it's dyed. Pure, so it's tested.
/// </summary>
/// <param name="Done">Of the moves and placements, those done.</param>
/// <param name="ToDo">The run's moves and placements.</param>
/// <param name="Dyed">Of the dyes, those done.</param>
/// <param name="Dyes">The dyes: planned, or expected while the dye phase isn't planned yet.</param>
/// <param name="PutAwayDone">Of the pieces to put away (clear out, putting back), those done.</param>
/// <param name="PutAway">The pieces to put away.</param>
public readonly record struct RunFigure(int Done, int ToDo, int Dyed, int Dyes, int PutAwayDone, int PutAway)
{
    /// <summary>
    /// The run's counts from its pieces. While a clear out goes first the design's pieces aren't
    /// planned yet (the run has only its put-aways), so its to-do is the one it started with.
    /// </summary>
    /// <param name="toDoAtStart">The pieces it was given to move or place (Ready's "to do").</param>
    /// <param name="dyesStillExpected">The dyes expected while the dye phase isn't planned (0 once it is).</param>
    public static RunFigure From(IReadOnlyList<RunJob> jobs, bool clearingOutFirst, int toDoAtStart, int dyesStillExpected)
    {
        int done = 0, planned = 0, dyed = 0, dyes = 0, stored = 0, stores = 0;
        for (var i = 0; i < jobs.Count; i++)
        {
            var j = jobs[i];
            var ok = j.Outcome == JobOutcome.Done;
            switch (j.Kind)
            {
                case JobKind.Store:
                    stores++;
                    if (ok) stored++;
                    break;
                case JobKind.Dye:
                    dyes++;
                    if (ok) dyed++;
                    break;
                default:
                    planned++;
                    if (ok) done++;
                    break;
            }
        }
        var toDo = clearingOutFirst ? Math.Max(toDoAtStart, planned) : planned;
        return new RunFigure(done, toDo, dyed, dyes + Math.Max(0, dyesStillExpected), stored, stores);
    }

    /// <summary>The big number: the to-do done; the dyes done when there's nothing else; the put-aways when that's all.</summary>
    public int Figure => ToDo > 0 ? Done : Dyes > 0 ? Dyed : PutAwayDone;

    /// <summary>What <see cref="Figure"/> is out of.</summary>
    public int Total => ToDo > 0 ? ToDo : Dyes > 0 ? Dyes : PutAway;

    /// <summary>The bar: everything the run does (moves, placements, dyes, put-aways), so it's full only once the dyes are done too.</summary>
    public float Bar
    {
        get
        {
            var all = ToDo + Dyes + PutAway;
            return all <= 0 ? 0 : Math.Clamp((Done + Dyed + PutAwayDone) / (float)all, 0f, 1f);
        }
    }
}
