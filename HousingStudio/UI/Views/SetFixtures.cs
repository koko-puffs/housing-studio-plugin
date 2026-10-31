using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Setting the fixtures for you (Settings → Beta): in Ready's Finish stage, "Set them for me
/// after placing" (it starts by itself when a placing run started from Ready finishes) and, under Tick
/// off, what the plugin can set from your bags here with "Set them for me"; while it runs, the step
/// it's on with Continue (it goes step by step) and Stop, in Ready's Finish stage and on Done.
/// </summary>
public sealed partial class MainWindow
{
    private (DateTime Until, string? Design, List<FixtureTask> Tasks) fixturePlan = (DateTime.MinValue, null, new List<FixtureTask>());

    /// <summary>The fixtures' plan for where you are, read again every second (it reads the game's tables and your bags).</summary>
    private List<FixtureTask> FixturePlan()
    {
        var now = DateTime.UtcNow;
        if (now < fixturePlan.Until && fixturePlan.Design == session.FilePath) return fixturePlan.Tasks;
        fixturePlan = (now + TimeSpan.FromSeconds(1), session.FilePath, Plugin.FixtureRun.Plan());
        return fixturePlan.Tasks;
    }

    private bool fixturesWereActive;

    /// <summary>
    /// While the fixtures are being set: the brass panel with the step, Continue and Stop, with the
    /// gaps around it (Ready's Finish stage, and the top of Done). Returns false (and draws nothing)
    /// when they aren't.
    /// </summary>
    private bool DrawFixtureRunPanel(float gapBefore = 0, float gapAfter = 18)
    {
        var run = Plugin.FixtureRun;
        if (!run.Active)
        {
            if (fixturesWereActive)
            {
                // Seen once it ends: read again, so what was set shows as set.
                fixturesWereActive = false;
                fixturePlan.Until = DateTime.MinValue;
                finishLine.Until = DateTime.MinValue;
            }
            return false;
        }
        fixturesWereActive = true;
        if (gapBefore > 0) Kit.Gap(gapBefore);
        DrawFixturesRunning(run);
        if (gapAfter > 0) Kit.Gap(gapAfter);
        return true;
    }

    /// <summary>Under Tick off: what the plugin can set from your bags here, and Set them for me (when it's switched on).</summary>
    private void DrawSetFixtures()
    {
        var run = Plugin.FixtureRun;
        if (!run.Enabled || run.Active) return;
        DrawFixturesIdle(run);
        Kit.Gap(10);
    }

    private (List<FixtureTask>? Tasks, string Words) bagWords = (null, string.Empty);

    /// <summary>"4 fixtures to set from your bags · 2 not in your bags · 1 yours to set", for where you are (made once per read of the fixtures).</summary>
    private string FixtureBagWords()
    {
        if (session.Context.Side == null) return "Go to your home to see which the plugin can set";
        var tasks = FixturePlan();
        if (ReferenceEquals(bagWords.Tasks, tasks)) return bagWords.Words;
        var toSet = tasks.Count(t => t.State == FixtureState.ToSet);
        var notOwned = tasks.Count(t => t.State == FixtureState.NotOwned);
        var byHand = tasks.Count(t => t.State == FixtureState.ByHand);
        var parts = new List<string> { toSet == 0 ? "None to set from your bags here" : $"{Words.Count(toSet, "fixture")} to set from your bags" };
        if (notOwned > 0) parts.Add($"{notOwned} not in your bags");
        if (byHand > 0) parts.Add($"{byHand} yours to set");
        bagWords = (tasks, string.Join(" · ", parts));
        return bagWords.Words;
    }

    // ---------------------------------------------------------------------------------------------
    // After placing: a placing run started from Ready (or the errands' placing) that finishes starts
    // setting the fixtures, when "Set them for me after placing" is on.

    private readonly AfterPlacing afterPlacing = new();

    /// <summary>
    /// Call when a placing run has started on Ready's behalf (its brass button, a swap, Done's start
    /// after a preview): when that very run finishes, the fixtures are set if "Set them for me after
    /// placing" is on. Framework thread, from <see cref="StartRun"/>'s onStarted (the run is going).
    /// </summary>
    private void ArmFixturesAfterPlacing()
    {
        afterPlacing.ArmRun(runner.RunNumber, DesignId);
        afterPlacingRehearse = runner.Rehearsal;
    }

    /// <summary>The run (or errands) armed for is a rehearsal: the fixtures after it are rehearsed too.</summary>
    private bool afterPlacingRehearse;

    /// <summary>Call once the errands are sent out: when they finish, and every placing run they started finished, the fixtures follow.</summary>
    private void ArmFixturesAfterErrands()
    {
        afterPlacing.ArmErrands(DesignId);
        afterPlacingRehearse = Errands.Rehearsal;
    }

    private DateTime afterPlacingFailedAt;

    /// <summary>
    /// Every framework tick (Plugin's update), whether the window is open or not: follows the armed
    /// run (<see cref="AfterPlacing"/>) and, when it's due, starts setting the fixtures for the design
    /// it was armed for, opening the window (its Continue and Stop are there).
    /// </summary>
    internal void Tick()
    {
        try
        {
            FixturesAfterPlacing();
        }
        catch (Exception ex)
        {
            afterPlacing.Disarm();
            if (DateTime.UtcNow - afterPlacingFailedAt > TimeSpan.FromSeconds(30)) Services.Log.Error(ex, "Housing Studio couldn't follow the run for the fixtures");
            afterPlacingFailedAt = DateTime.UtcNow;
        }
    }

    private void FixturesAfterPlacing()
    {
        if (!afterPlacing.Armed) return;
        var e = Plugin.Errands;
        var run = Plugin.FixtureRun;
        var seen = new AfterPlacingSeen(runner.Active, runner.RunNumber, runner.State == RunState.Finished && !runner.DryRun, e.Active,
            e.State == RunState.Finished, e.ResultWaiting, run.Active, DesignId);
        if (!afterPlacing.Step(seen)) return;
        if (!config.FixturesAfterPlacing || !run.Enabled || session.Layout is not { } layout) return;
        var all = FixtureCount(layout);
        if (all == 0 || FixtureTicks.CountDone(config.FixturesDone, DesignKey, Fixtures(layout).Select(FixtureKey)) >= all) return;
        if (afterPlacingRehearse) run.Rehearse();
        else run.Start();
        Notify(run.Active ? afterPlacingRehearse ? "Rehearsing the fixtures…" : "Setting the fixtures…" : run.StopReason ?? "Couldn't start setting the fixtures");
        if (run.Active) IsOpen = true;
    }

    private void DrawFixturesIdle(FixtureRunner run)
    {
        var tasks = FixturePlan();
        var toSet = tasks.Count(t => t.State == FixtureState.ToSet);
        var line = FixtureBagWords();
        if (run.Result != null || run.StopReason != null)
            line += $". Last time: {(run.State == RunState.Stopped ? $"stopped ({run.StopReason}) " : string.Empty)}{run.Result ?? run.StopReason}";

        const string label = "Set them for me";
        var blocked = run.CanStart() ?? (toSet == 0 ? "Nothing here to set from your bags" : null);
        var at = Kit.At;
        var w = Kit.Width;
        var bw = Kit.ButtonWidth(label, BtnSize.Sm, Icon.Play);
        var tw = w - bw - S(14);
        var noteH = Txt.Flow(Vector2.Zero, tw, new[] { Run.Note(line) }, 19.5f, out _, draw: false);
        var h = MathF.Max(Kit.ButtonHeight(BtnSize.Sm), noteH);
        Txt.Flow(new Vector2(at.X, at.Y + (h - noteH) / 2), tw, new[] { Run.Note(line) }, 19.5f, out _);
        if (Kit.Button("##set-fixtures", new Vector2(at.X + w - bw, at.Y + (h - Kit.ButtonHeight(BtnSize.Sm)) / 2), label, Btn.Glass, BtnSize.Sm, Icon.Play, 0,
                blocked != null, blocked ?? "Experimental: opens the game's fixtures window and puts each one in from your bags, step by step."))
            AskSetFixtures(toSet);
        Kit.Reserve(at, w, h);
    }

    /// <summary>Asks before setting the fixtures (Ready's Set them for me, Done's Set them), then starts it.</summary>
    private void AskSetFixtures(int toSet)
    {
        Ask("Set the fixtures?",
            $"This opens the game's {(session.Context.Side == Side.Garden ? "Exterior" : "Interior")} Fixtures window and puts the {Words.Count(toSet, "fixture")} in from your bags. "
            + "It's experimental: it holds before each action until you press Continue, asks you where it can't find its way, and you press the window's apply yourself. "
            + "Setting a fixture uses its item, as it does by hand.",
            "Set them", "Not now", () => OnFramework(() =>
            {
                Plugin.FixtureRun.Start();
                if (!Plugin.FixtureRun.Active) Notify(Plugin.FixtureRun.StopReason ?? "Couldn't start");
            }));
    }

    private void DrawFixturesRunning(FixtureRunner run)
    {
        Kit.BeginPanel("##fixtures-run", 14, 16, 14, Palette.BrassWash, Palette.Brass);
        var at = Kit.At;
        var w = Kit.Width;
        var what = run.Rehearsal ? "REHEARSING FIXTURES" : "SETTING FIXTURES";
        var eyebrow = run.AwaitingContinue ? $"{what} · NEXT" : run.WaitingForYou ? $"{what} · WAITING FOR YOU" : what;
        var eh = Kit.Eyebrow(at, eyebrow, Palette.Brass, w);
        Kit.Reserve(at, w, eh);
        Kit.Gap(6);
        Txt.Para(new[] { new Run(run.StepLabel ?? "Starting…", Face.Sans, 14, Palette.Ivory) });
        Kit.Gap(10);
        var row = Kit.At;
        var x = row.X;
        if (run.AwaitingContinue)
        {
            const string go = "Continue";
            if (Kit.Button("##fixtures-continue", row, go, Btn.Primary, BtnSize.Sm, Icon.Play)) OnFramework(Plugin.FixtureRun.Continue);
            x += Kit.ButtonWidth(go, BtnSize.Sm, Icon.Play, Btn.Primary) + S(8);
        }
        if (Kit.Button("##fixtures-stop", new Vector2(x, row.Y), "Stop", Btn.Glass, BtnSize.Sm, Icon.Square))
            OnFramework(() => Plugin.FixtureRun.Stop("Stopped by you."));
        Kit.Reserve(row, w, Kit.ButtonHeight(BtnSize.Sm));
        Kit.EndPanel();
    }
}
