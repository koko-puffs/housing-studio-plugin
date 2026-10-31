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
/// Setting the fixtures for you (Settings → Experimental): in Ready's Finish stage, "Set them for me
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

    /// <summary>"4 fixtures to set from your bags · 2 not in your bags · 1 yours to set", for where you are.</summary>
    private string FixtureBagWords()
    {
        if (session.Context.Side == null) return "Go to your home to see which the plugin can set";
        var tasks = FixturePlan();
        var toSet = tasks.Count(t => t.State == FixtureState.ToSet);
        var notOwned = tasks.Count(t => t.State == FixtureState.NotOwned);
        var byHand = tasks.Count(t => t.State == FixtureState.ByHand);
        var parts = new List<string> { toSet == 0 ? "None to set from your bags here" : $"{Words.Count(toSet, "fixture")} to set from your bags" };
        if (notOwned > 0) parts.Add($"{notOwned} not in your bags");
        if (byHand > 0) parts.Add($"{byHand} yours to set");
        return string.Join(" · ", parts);
    }

    // ---------------------------------------------------------------------------------------------
    // After placing: a placing run started from Ready (or the errands' placing) that finishes starts
    // setting the fixtures, when "Set them for me after placing" is on.

    private enum AfterPlacing
    {
        Off,

        /// <summary>Waiting for the placing run to start (the errands go first).</summary>
        Armed,

        /// <summary>The placing run is going.</summary>
        Placing,

        /// <summary>It finished: the fixtures start once nothing else runs.</summary>
        Due,
    }

    private AfterPlacing afterPlacing;

    /// <summary>
    /// Call when a placing run is started on Ready's behalf (its brass button, Carry on, a swap; the
    /// errands once they're sent out, as their placing comes after): when that run finishes, the
    /// fixtures are set if "Set them for me after placing" is on. Once per run.
    /// </summary>
    private void ArmFixturesAfterPlacing() => afterPlacing = runner.Active ? AfterPlacing.Placing : AfterPlacing.Armed;

    /// <summary>Every frame: follows the armed run, and starts setting the fixtures once it has finished.</summary>
    private void FixturesAfterPlacing()
    {
        switch (afterPlacing)
        {
            case AfterPlacing.Armed:
                if (runner.Active) afterPlacing = AfterPlacing.Placing;
                else if (!Errands.Active) afterPlacing = AfterPlacing.Off;
                return;
            case AfterPlacing.Placing:
                if (runner.Active) return;
                afterPlacing = runner.State == RunState.Finished && !runner.DryRun ? AfterPlacing.Due : AfterPlacing.Off;
                return;
            case AfterPlacing.Due:
                if (runner.Active || Errands.Active) return;
                afterPlacing = AfterPlacing.Off;
                var run = Plugin.FixtureRun;
                if (!config.FixturesAfterPlacing || !run.Enabled || run.Active || session.Layout is not { } layout) return;
                if (FixturesDone(layout) >= FixtureCount(layout)) return;
                OnFramework(() =>
                {
                    run.Start();
                    Notify(run.Active ? "Setting the fixtures…" : run.StopReason ?? "Couldn't start setting the fixtures");
                });
                return;
        }
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
        Kit.Reserve(at, w, h);
    }

    private void DrawFixturesRunning(FixtureRunner run)
    {
        Kit.BeginPanel("##fixtures-run", 14, 16, 14, Palette.BrassWash, Palette.Brass);
        var at = Kit.At;
        var w = Kit.Width;
        var eyebrow = run.AwaitingContinue ? "SETTING FIXTURES · NEXT" : run.WaitingForYou ? "SETTING FIXTURES · WAITING FOR YOU" : "SETTING FIXTURES";
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
