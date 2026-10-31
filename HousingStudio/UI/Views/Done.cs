using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Done (mockup 4): a run finished or stopped. The mark and the sentence, the tally, what needs you,
/// putting it back, and Try again / Carry on / Done. A finished preview offers Start placing.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>One row of "Needs you".</summary>
    private sealed record Issue(Icon Icon, string Name, string Reason, string Action, Action OnAction, string? Tip = null);

    private void DrawDone()
    {
        var preview = runner.DryRun;
        var stopped = runner.State == RunState.Stopped;
        var problems = runner.Problems;
        var issues = preview ? new List<Issue>() : Issues(problems);
        var needYou = preview ? 0 : problems.Count + vm.RunMissing.Count;

        // Header: the mark, the title, the sentence.
        Frame.BeginHeader(28);
        var at = Kit.At;
        var w = Kit.Width;
        var (markFill, markInk, markIcon) = stopped
            ? vm.StoppedByYou ? (Palette.LinePicked, Palette.Ash, Icon.Square) : (Palette.DangerWash, Palette.Danger, Icon.Alert)
            : (Palette.SuccessWash, Palette.Success, Icon.Check);
        Kit.Round(at, S(48), markIcon, S(24), markFill, markInk);
        var y = at.Y + S(48) + S(10);
        var name = vm.RunTitle;
        float titleH;
        if (stopped) titleH = Title(new Vector2(at.X, y), $"Stopped at {vm.Figure} of {vm.RunPieces}", null, w);
        else if (preview) titleH = Title(new Vector2(at.X, y), "Preview", "done", w);
        else titleH = Title(new Vector2(at.X, y), $"{name} is", "in", w);
        y += titleH + S(10);
        string sentence;
        if (preview)
            sentence = $"Every spot was marked and nothing was moved. Start works on {Words.Count(runner.Count(RunPhase.Move) + runner.Count(RunPhase.Place), "piece")}{(config.DyeAfterPlacing ? ", then the dyes" : string.Empty)}.";
        else if (stopped)
            sentence = (vm.StoppedByYou ? "You stopped it." : runner.StopReason ?? "It stopped.") + (needYou > 0 ? $" {needYou} {(needYou == 1 ? "needs" : "need")} you." : string.Empty);
        else if (vm.Figure >= vm.RunPieces && needYou == 0)
            sentence = $"All {Words.Count(vm.RunPieces, "piece")} are where the design has them.";
        else
            sentence = $"{vm.Figure} of {vm.RunPieces} pieces are where the design has them.{(needYou > 0 ? $" {needYou} {(needYou == 1 ? "needs" : "need")} you." : string.Empty)}";
        var sh = Txt.Flow(new Vector2(at.X, y), w, new[] { new Run(sentence, Face.Sans, 14, Palette.Ash) }, 21, out _);
        y += sh;
        Kit.Reserve(at, w, y - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##done", "done-foot", 22, 22);
        DrawTally(preview);
        if (issues.Count > 0)
        {
            Kit.Gap(18);
            Kit.EyebrowRow("Needs you");
            Kit.Gap(10);
            Kit.BeginPanel("##issues", 0, 0, 0);
            for (var i = 0; i < issues.Count; i++) IssueRow(i, issues[i]);
            Kit.EndPanel();
        }
        if (!preview && runner.LastBackupPath != null && vm.BackupAt is { } savedAt && savedAt >= vm.RunStartedAt.AddMinutes(-1))
        {
            Kit.Gap(18);
            DrawPutBack(savedAt);
        }

        var layout = session.Layout;
        if (layout != null && !preview)
        {
            var all = Fixtures(layout).ToList();
            var left = all.Count - FixturesDone(layout);
            if (left > 0)
            {
                Frame.BeginBottom("done-bottom");
                var menus = layout.InteriorFixtures.Count > 0 && layout.ExteriorFixtures.Count > 0 ? "Interior Fixtures and Exterior Fixtures"
                    : layout.ExteriorFixtures.Count > 0 ? "Exterior Fixtures" : "Interior Fixtures";
                Txt.Note($"Fixtures aren't set by the plugin: {left} still to do from Housing → {menus}.", center: true);
                Frame.EndBottom();
            }
        }
        Frame.EndBody();

        DrawDoneFooter(preview, stopped, problems);
    }

    /// <summary>The tally: moved, placed, dyed, in three cells.</summary>
    private void DrawTally(bool preview)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var gap = S(10);
        var cw = (w - gap * 2) / 3;
        var noteH = S(19.5f);
        var h = S(14) * 2 + S(28) + S(4) + noteH;
        var cells = new[]
        {
            (runner.Count(RunPhase.Move, JobOutcome.Done), preview ? "to move" : "moved"),
            (runner.Count(RunPhase.Place, JobOutcome.Done), preview ? "to place" : "placed"),
            (runner.Count(RunPhase.Dye, JobOutcome.Done), preview ? "to dye" : "dyed"),
        };
        var dl = Kit.Dl;
        for (var i = 0; i < 3; i++)
        {
            var p = new Vector2(at.X + i * (cw + gap), at.Y);
            dl.AddRect(p + new Vector2(0.5f), p + new Vector2(cw, h) - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusCard), ImDrawFlags.None, 1f);
            Txt.Line(dl, p + new Vector2(S(16), S(14)), cells[i].Item1.ToString(), Face.Serif, 28, Palette.Ivory, 28);
            Txt.Line(dl, p + new Vector2(S(16), S(14) + S(28) + S(4)), cells[i].Item2, Face.Sans, 13, Palette.Ash, 19.5f);
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>
    /// The problems as rows: each piece with its reason and Show where; the dyes you're short of in
    /// one row with Copy list; the pieces you don't have in one row with Copy list.
    /// </summary>
    private List<Issue> Issues(IReadOnlyList<RunProblem> problems)
    {
        var issues = new List<Issue>();
        foreach (var p in problems.Where(p => p.Kind != ProblemKind.DyeMissing))
        {
            var target = p.Job.Target;
            issues.Add(new Issue(p.Kind is ProblemKind.DyeFailed ? Icon.Droplet : Icon.Alert, p.Name, FirstSentence(p.Reason), "Show where",
                () => ShowWhere(new[] { target }), p.Reason));
        }

        var undyed = problems.Where(p => p.Kind == ProblemKind.DyeMissing).ToList();
        if (undyed.Count > 0)
        {
            var dyes = undyed.GroupBy(p => p.Job.Target.Stain).Select(g => (Name: data.DyeName(g.Key), Count: g.Count())).ToList();
            issues.Add(new Issue(Icon.Droplet, undyed.Count == 1 ? "1 piece left undyed" : $"{undyed.Count} pieces left undyed",
                $"You need {Words.List(dyes.Select(d => $"{d.Name} ×{d.Count}"))}", "Copy list", () =>
                {
                    ImGui.SetClipboardText(DyeList(dyes));
                    Notify("Dye list copied");
                }));
        }

        if (vm.RunMissing.Count > 0)
        {
            var names = vm.RunMissing.GroupBy(e => e.Target.ItemId).OrderByDescending(g => g.Count())
                .Select(g => g.Count() > 1 ? $"{data.ItemName(g.Key)} ×{g.Count()}" : data.ItemName(g.Key)).ToList();
            issues.Add(new Issue(Icon.Package, vm.RunMissing.Count == 1 ? "1 piece you don't have" : $"{vm.RunMissing.Count} pieces you don't have",
                Words.Some(names), "Shopping list", () => OpenShopping(Sub.None)));
        }
        return issues;
    }

    private static string FirstSentence(string text)
    {
        var dot = text.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? text[..(dot + 1)] : text;
    }

    /// <summary>A row of "Needs you": the danger icon, the name and reason, and the action (14px 20px inside).</summary>
    private void IssueRow(int index, Issue issue)
    {
        var at = Kit.At;
        var w = Kit.Width;
        if (index > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);
        var bw = Kit.ButtonWidth(issue.Action, BtnSize.Sm);
        var x = at.X + S(20) + S(16) + S(14);
        var tw = at.X + w - S(20) - bw - S(14) - x;
        var nameH = Txt.Natural(Face.SansMedium, 14);
        var reason = new[] { Run.Note(issue.Reason) };
        var reasonH = Txt.Flow(Vector2.Zero, tw, reason, 19.5f, out _, draw: false);
        var wordsH = nameH + S(2) + reasonH;
        var h = S(14) * 2 + MathF.Max(S(34), wordsH);
        var mid = at.Y + h / 2;
        var dl = Kit.Dl;
        Icons.Draw(dl, issue.Icon, new Vector2(at.X + S(20), mid - S(8)), S(16), Palette.Danger);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(issue.Name, Face.SansMedium, 14, tw), Face.SansMedium, 14, Palette.Ivory);
        Txt.Flow(new Vector2(x, top + nameH + S(2)), tw, reason, 19.5f, out _);
        if (issue.Tip != null && issue.Tip != issue.Reason && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + tw, top + wordsH)))
            Kit.Tooltip(issue.Tip);
        var cantShow = issue.Action == "Show where" && session.Context.Side == null;
        if (Kit.Button($"##issue{index}", new Vector2(at.X + w - S(20) - bw, mid - S(17)), issue.Action, Btn.Glass, BtnSize.Sm, null, 0, cantShow,
                cantShow ? "Go to your home to see where it goes" : null))
            issue.OnAction();
        Kit.Reserve(at, w, h);
    }

    /// <summary>"Not what you wanted?": the backup the run saved, and Put them back (after asking).</summary>
    private void DrawPutBack(DateTime savedAt)
    {
        Kit.BeginPanel("##putback", 16, 20, 16);
        var at = Kit.At;
        var w = Kit.Width;
        const string label = "Put them back";
        var bw = Kit.ButtonWidth(label, BtnSize.Sm, null, Btn.Quiet);
        var x = at.X + S(16) + S(14);
        var tw = at.X + w - bw - S(14) - x;
        var titleH = Txt.Natural(Face.SansMedium, 14);
        var pieces = Words.Count(vm.LiveAtStart, "piece");
        var note = new[] { Run.Note($"A backup of the {pieces} you had was saved at {Clock(savedAt)}.") };
        var noteH = Txt.Flow(Vector2.Zero, tw, note, 19.5f, out _, draw: false);
        var wordsH = titleH + S(2) + noteH;
        var h = MathF.Max(S(34), wordsH);
        var mid = at.Y + h / 2;
        var dl = Kit.Dl;
        Icons.Draw(dl, Icon.Refresh, new Vector2(at.X, mid - S(8)), S(16), Palette.Dim);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(x, top), "Not what you wanted?", Face.SansMedium, 14, Palette.Ivory);
        Txt.Flow(new Vector2(x, top + titleH + S(2)), tw, note, 19.5f, out _);
        var cannot = runner.CanRestore();
        if (Kit.Button("##putback-btn", new Vector2(at.X + w - bw, mid - S(17)), label, Btn.Quiet, BtnSize.Sm, disabled: cannot != null, tip: cannot))
        {
            Ask("Put them back?",
                $"This loads the backup saved at {Clock(savedAt)} as the design and starts a run that moves the {pieces} you had back to where they were, " +
                "then puts the pieces placed since (not in the backup) away in the storeroom.",
                "Put them back", "Keep it as it is", () =>
                {
                    OnFramework(() =>
                    {
                        if (runner.RestoreBackup(out var why))
                        {
                            vm.Dismiss();
                            session.Refresh(Game.HousingContext.Read(Plugin.Plots));
                            Notify("Putting them back…");
                        }
                        else Notify(why);
                    });
                });
        }
        Kit.Reserve(at, w, h);
        Kit.EndPanel();
    }

    private void DrawDoneFooter(bool preview, bool stopped, IReadOnlyList<RunProblem> problems)
    {
        Frame.BeginFooter("done-foot", 18, 22);
        var at = Kit.At;
        var w = Kit.Width;
        const string done = "Done";
        var dw = Kit.ButtonWidth(done, BtnSize.Lg);
        var primaryW = w - dw - S(10);
        var retry = problems.Count;
        if (preview)
        {
            var blocked = runner.CanStart(false);
            if (Kit.Button("##start-real", at, "Start placing", Btn.Primary, BtnSize.Lg, Icon.Play, primaryW, blocked != null, blocked))
            {
                StartRun(false, onStarted: vm.Dismiss);
            }
        }
        else if (stopped)
        {
            var blocked = runner.CanStart(false);
            if (Kit.Button("##carry-on", at, "Carry on", Btn.Primary, BtnSize.Lg, Icon.Play, primaryW, blocked != null, blocked ?? "Starts again with what's still to do."))
            {
                StartRun(false, onStarted: vm.Dismiss);
            }
        }
        else if (retry > 0)
        {
            var blocked = runner.CanStart(false);
            if (Kit.Button("##retry", at, retry == 1 ? "Try it again" : $"Try the {retry} again", Btn.Primary, BtnSize.Lg, Icon.Refresh, primaryW, blocked != null, blocked))
            {
                var wanted = problems.Select(p => (p.Job.Target.Key, p.Job.Target.ItemId)).ToHashSet();
                vm.Starting(session.Plan?.Entries.Where(e => wanted.Contains((e.Target.Key, e.Target.ItemId))) ?? Enumerable.Empty<PlanEntry>());
                OnFramework(() =>
                {
                    runner.RetryProblems();
                    if (runner.Active)
                    {
                        vm.Dismiss();
                        return;
                    }
                    vm.NotStarted();
                    Notify(runner.StopReason ?? "Nothing to try again");
                });
            }
        }
        else
        {
            // Nothing needs you: Done is the one thing to do.
            if (Kit.Button("##done-only", at, done, Btn.Primary, BtnSize.Lg, null, w)) vm.Dismiss();
            Kit.Reserve(at, w, S(46));
            Frame.EndFooter();
            return;
        }
        if (Kit.Button("##done", new Vector2(at.X + w - dw, at.Y), done, Btn.Glass, BtnSize.Lg)) vm.Dismiss();
        Kit.Reserve(at, w, S(46));
        Frame.EndFooter();
    }
}
