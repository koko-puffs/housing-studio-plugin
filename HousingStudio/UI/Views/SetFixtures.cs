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
/// Ready's "Set them for me" (Settings → Experimental): under the fixtures to set yourself, what the
/// plugin can set from your bags here, the button, and while it runs the step it's on with Continue
/// (it goes step by step) and Stop.
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

    private void DrawSetFixtures()
    {
        var run = Plugin.FixtureRun;
        if (!run.Enabled && !run.Active) return;
        Kit.PushBox(Kit.Left + S(20), Kit.Width - S(40));
        Kit.Gap(4);
        if (run.Active)
        {
            fixturesWereActive = true;
            DrawFixturesRunning(run);
        }
        else
        {
            if (fixturesWereActive)
            {
                // Seen once it ends: read again, so what was set shows as set.
                fixturesWereActive = false;
                fixturePlan.Until = DateTime.MinValue;
            }
            DrawFixturesIdle(run);
        }
        Kit.Gap(10);
        Kit.PopBox();
    }

    private void DrawFixturesIdle(FixtureRunner run)
    {
        var tasks = FixturePlan();
        var toSet = tasks.Count(t => t.State == FixtureState.ToSet);
        var notOwned = tasks.Count(t => t.State == FixtureState.NotOwned);
        var byHand = tasks.Count(t => t.State == FixtureState.ByHand);
        var parts = new List<string>();
        if (session.Context.Side == null) parts.Add("Go to your home to see which the plugin can set");
        else
        {
            parts.Add(toSet == 0 ? "None to set from your bags here" : $"{Words.Count(toSet, "fixture")} to set from your bags");
            if (notOwned > 0) parts.Add($"{notOwned} not in your bags");
            if (byHand > 0) parts.Add($"{byHand} yours to set");
        }
        var line = string.Join(" · ", parts);
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
