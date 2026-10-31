using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Automation;
using HousingStudio.Core;

namespace HousingStudio.Session;

/// <summary>
/// The fixtures rehearsed (Ready's Rehearse the run with "Set them for me after placing", after the
/// placing rehearsal): the same steps as setting them, each fixture put in its slot of the window,
/// and then the window closed (its close button) instead of applied, which leaves the fixtures as they
/// were and the fixture items back in your bags (as the window's own close does; not seen in the
/// game: it waits for Continue, as every fixtures step does). No ticks are changed.
/// </summary>
public sealed unsafe partial class FixtureRunner
{
    private bool rehearseNext;

    /// <summary>The run going (or the last) is a rehearsal: nothing is applied.</summary>
    public bool Rehearsal { get; private set; }

    private Func<bool>? rehearsingNow;

    /// <summary>For the guard (<see cref="Rehearsals"/>): this runner is rehearsing now.</summary>
    private Func<bool> rehearsing => rehearsingNow ??= () => Active && Rehearsal;

    /// <summary>What's set when the rehearsal started, and how many of each fixture item your bags held.</summary>
    private List<CurrentFixture>? setBefore;

    private Dictionary<uint, int> ownedBefore = new();
    private int putIn;

    /// <summary>Rehearses setting the fixtures: every step, the window closed instead of applied.</summary>
    public void Rehearse()
    {
        rehearseNext = true;
        try
        {
            Start();
        }
        finally
        {
            rehearseNext = false;
        }
    }

    private void RehearsalStarted(List<FixtureTask> tasks)
    {
        setBefore = Current(side, session.Context);
        ownedBefore = toSet.Select(t => t.Fixture.ItemId).Distinct().ToDictionary(i => i, Owned);
        putIn = 0;
        log.Info("A rehearsal: each fixture is put in its slot, then the window is closed without applying (nothing is set).");
    }

    /// <summary>Instead of the apply: says what it would do, closes the window, and checks nothing was set and the items came back.</summary>
    private List<Step> RehearseApply()
    {
        var apply = side == Side.Interior ? FixtureTable.InteriorApply : FixtureTable.ExteriorApply;
        var asked = false;
        return new List<Step>
        {
            new Step
            {
                Label = "Would apply (not in a rehearsal)",
                Run = () =>
                {
                    putIn = toSet.Count(t => !dropped.Contains(t) && InSlot(t));
                    Rehearsals.Report.Would(RehearsalAct.Fixture, putIn);
                    log.Info($"  Would apply{(apply != null ? $": {apply.Describe()}" : " (you press it)")}, then answer yes to \"{FixtureTable.ApplyWords[0]}?\", setting {putIn} of {toSet.Count}. Not in a rehearsal.");
                    foreach (var t in toSet.Where(t => !dropped.Contains(t) && !InSlot(t)))
                        Rehearsals.Report.Issue($"The {Describe(t)}", $"{data.AnyItemName(t.Fixture.ItemId)} wasn't in its slot when the apply would come.", RehearsalTrouble.Failed);
                    return StepOutcome.Done;
                },
            },
            Step.Act($"Close the {MenuName} window without applying (its close button): nothing is set", () =>
            {
                watchWindow = false;
                GameUi.Close(Window);
                return StepOutcome.Done;
            }),
            Step.Wait("The window closes (or the game asks something)", () => !GameUi.IsOpen(Window) || GameUi.YesNoText() != null, WindowWait, StepStatus.Done),
            new Step
            {
                Label = "Did it close?",
                Run = () =>
                {
                    if (GameUi.YesNoText() is { } question)
                    {
                        asked = true;
                        log.Info($"  The game asks: \"{question}\": you answer (a rehearsal never says yes).");
                        Rehearsals.Report.Issue($"The {MenuName} window", $"Closing it without applying asked \"{question}\": left for you to answer.", RehearsalTrouble.NoMatch);
                        queue.InsertNext(new[] { YouWaitFor($"You: the game asks \"{question}\": answer it (a rehearsal never says yes; nothing should be set)", () => GameUi.YesNoText() == null && !GameUi.IsOpen(Window), "The question wasn't answered.") });
                    }
                    else if (GameUi.IsOpen(Window))
                    {
                        Rehearsals.Report.Issue($"The {MenuName} window", "It didn't close with its close button: you close it.", RehearsalTrouble.NoMatch);
                        queue.InsertNext(new[] { YouWaitFor($"You: close the {MenuName} window without applying", () => !GameUi.IsOpen(Window), "The window wasn't closed.") });
                    }
                    return StepOutcome.Done;
                },
            },
            Step.Wait("The fixture items come back to your bags", () => ownedBefore.All(kv => Owned(kv.Key) >= kv.Value), TimeSpan.FromSeconds(3), StepStatus.Done),
            new Step
            {
                Label = "Check nothing was set",
                Run = () =>
                {
                    var now = Current(side, session.Context);
                    foreach (var t in toSet.Where(t => IsSet(t, now) && !IsSet(t, setBefore)))
                        Rehearsals.Report.Issue($"The {Describe(t)}", $"{data.AnyItemName(t.Fixture.ItemId)} is set now{(asked ? " (after the question was answered)" : string.Empty)}: closing the window applied it.", RehearsalTrouble.Failed);
                    foreach (var (item, had) in ownedBefore)
                    {
                        var n = Owned(item);
                        if (n < had) Rehearsals.Report.Issue("Your bags", $"{data.AnyItemName(item)}: {n} in your bags, {had} before (still in the window's container?).", RehearsalTrouble.Failed);
                    }
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>On a stop: the window is closed without applying (nothing set; the items go back).</summary>
    private Step CloseWindowUnapplied() => new()
    {
        Label = "Close the fixtures window without applying",
        Run = () =>
        {
            watchWindow = false;
            if (GameUi.IsOpen(Window))
            {
                GameUi.Close(Window);
                log.Info($"  The {MenuName} window is closed without applying (a rehearsal sets nothing).");
            }
            return StepOutcome.Done;
        },
    };

    private string RehearsalSummary() => $"Rehearsal: {putIn} of {toSet.Count} put in their slots, then the window closed without applying (nothing set).";
}
