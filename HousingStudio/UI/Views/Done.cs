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
/// Done (the 0.6 flow, docs/mockups/flow.html): a run finished or stopped. The mark, "<name> is in" and
/// one line of what happened (with the errands' trips and gil when the run came from them); the tally;
/// what needs you; one undo card (Swap back after a swap, else Put them back); a card for the fixtures
/// left (Set them); and Try again / Carry on / Done. A finished preview offers to start placing as
/// Ready's choice has it (Keep, Clear out or Swap).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>One row of "Needs you".</summary>
    /// <param name="NeedsHome">Its action shows a spot in your home (disabled away from it).</param>
    private sealed record Issue(Icon Icon, string Name, string Reason, string Action, Action OnAction, string? Tip = null, Icon? ActionIcon = null,
        bool NeedsHome = false);

    private readonly record struct DoneLineKey(bool Preview, bool Stopped, bool ByYou, string? Why, int Figure, int Pieces, int Work, bool Dyes);

    private (DoneLineKey Key, string Line) doneLine = (default, string.Empty);

    /// <summary>Done's one line under the title, made again only when what it says changes.</summary>
    private string DoneLine(bool preview, bool stopped)
    {
        var key = new DoneLineKey(preview, stopped, vm.StoppedByYou, runner.StopReason, vm.Figure, vm.RunPieces,
            preview ? runner.Count(RunPhase.Move) - runner.ClearOutCount + runner.Count(RunPhase.Place) : 0, config.DyeAfterPlacing);
        if (doneLine.Line.Length > 0 && doneLine.Key == key) return doneLine.Line;
        string line;
        if (preview)
            line = $"Every spot was marked and nothing was moved. Start works on {Words.Count(key.Work, "piece")}{(config.DyeAfterPlacing ? ", then the dyes" : string.Empty)}.";
        else if (stopped)
            line = vm.StoppedByYou ? "You stopped it" : runner.StopReason ?? "It stopped";
        else
            line = DoneWords.Line(vm.Figure, vm.RunPieces);
        doneLine = (key, line);
        return line;
    }

    private readonly Run[] doneLineRun = new Run[1];

    private void DrawDone()
    {
        // After a rehearsal: what it would have done and what didn't work (no undo, no retry: nothing changed).
        if (runner.Rehearsal)
        {
            DrawRehearsalResult(errands: false);
            return;
        }
        var preview = runner.DryRun;
        var stopped = runner.State == RunState.Stopped;
        var problems = runner.Problems;
        var issues = preview ? NoIssues : Issues(problems);
        var needYou = preview ? 0 : problems.Count + vm.RunMissing.Count;

        // Header, centred: the mark, the title, one line of what happened.
        Frame.BeginHeader(24);
        var at = Kit.At;
        var w = Kit.Width;
        var (markFill, markEdge, markInk, markIcon) = stopped
            ? vm.StoppedByYou ? (Palette.LinePicked, Palette.LineStrong, Palette.Ash, Icon.Square)
            : (Palette.DangerWash, Palette.WithAlpha(Palette.Danger, 0.4f), Palette.Danger, Icon.Alert)
            : (Palette.SuccessWash, Palette.WithAlpha(Palette.Success, 0.4f), Palette.Success, Icon.Check);
        var d = S(52);
        var c = new Vector2(at.X + w / 2, at.Y + d / 2);
        var dl = Kit.Dl;
        dl.AddCircleFilled(c, d / 2, Palette.U32(markFill), 48);
        dl.AddCircle(c, d / 2 - 0.5f, Palette.U32(markEdge), 48, 1f);
        Icons.Draw(dl, markIcon, c - new Vector2(S(12)), S(24), markInk, markIcon == Icon.Check ? 2f : 1.8f);
        var y = at.Y + d + S(12);
        float titleH;
        if (stopped) titleH = CenteredTitle(y, $"Stopped at {vm.Figure} of {vm.RunPieces}", null, at.X, w);
        else if (preview) titleH = CenteredTitle(y, "Preview", "done", at.X, w);
        else titleH = CenteredTitle(y, $"{vm.RunTitle} is", "in", at.X, w);
        y += titleH + S(4);
        doneLineRun[0] = new Run(DoneLine(preview, stopped), Face.Sans, 13, Palette.Dim);
        y += Txt.Flow(new Vector2(at.X, y), w, doneLineRun, 19.5f, out _, center: true);
        Kit.Reserve(at, w, y - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##done-body", "done-foot", 20, 22);
        DrawFixtureRunPanel();
        DrawTally(preview);
        DrawPutAwayLine(preview);
        if (issues.Count > 0)
        {
            Kit.Gap(18);
            Kit.Section("Needs you", needYou.ToString());
            Kit.Gap(10);
            Kit.BeginPanel("##issues", 0, 0, 0);
            for (var i = 0; i < issues.Count; i++) IssueRow(i, issues[i]);
            Kit.EndPanel();
        }
        // One undo: Swap back after a swap, else Put them back. A swap's backup is from its first run:
        // Try again and Carry on keep it, so it may be older.
        if (!preview && runner.LastBackupPath != null && vm.BackupAt is { } savedAt
            && (runner.KeptAs != null || savedAt >= vm.RunStartedAt.AddMinutes(-1)))
        {
            Kit.Gap(14);
            if (runner.KeptAs != null) DrawSwapBack(savedAt);
            else DrawPutBack(savedAt);
        }
        if (!preview && session.Layout is { } layout && !Plugin.FixtureRun.Active) DrawFixturesCard(layout);
        Frame.EndBody();

        DrawDoneFooter(preview, stopped, problems);
    }

    private static readonly List<Issue> NoIssues = new();

    /// <summary>A serif title centred in the box (cut to fit, as <see cref="Title"/> does). Returns its line's height.</summary>
    private static float CenteredTitle(float y, string plain, string? italic, float left, float width, int px = 32)
    {
        var italicW = italic != null ? Txt.Measure(" " + italic, Face.SerifItalic, px).X : 0;
        var shown = Txt.Fit(plain, Face.Serif, px, MathF.Max(S(40), width - italicW));
        var tw = MathF.Min(width, Txt.Measure(shown, Face.Serif, px).X + italicW);
        return Title(new Vector2(left + (width - tw) / 2, y), plain, italic, width, px);
    }

    private readonly (int Value, string Label)[] tally = new (int, string)[3];

    /// <summary>The tally: moved, placed, dyed, as stat cells (serif 32).</summary>
    private void DrawTally(bool preview)
    {
        // Pieces put away count with the moves in the runner; they're told apart under the tally.
        tally[0] = (runner.Count(RunPhase.Move, JobOutcome.Done) - runner.PutAwayDone, preview ? "to move" : "moved");
        tally[1] = (runner.Count(RunPhase.Place, JobOutcome.Done), preview ? "to place" : "placed");
        tally[2] = (runner.Count(RunPhase.Dye, JobOutcome.Done), preview ? "to dye" : "dyed");
        Kit.StatCells(tally, 32);
    }

    private (object? Problems, int Count, object? Missing, DateTime Run, List<Issue> Issues) issuesCache = (null, -1, null, DateTime.MinValue, new List<Issue>());

    /// <summary>
    /// The problems as rows: each piece with its reason and Show where; the dyes you're short of in
    /// one row with Copy list; the pieces you don't have in one row with Copy list.
    /// </summary>
    private List<Issue> Issues(IReadOnlyList<RunProblem> problems)
    {
        // Made again only when the run, its problems or the pieces you don't have change, not every frame.
        var c = issuesCache;
        if (ReferenceEquals(c.Problems, problems) && c.Count == problems.Count && ReferenceEquals(c.Missing, vm.RunMissing) && c.Run == vm.RunStartedAt)
            return c.Issues;
        var issues = new List<Issue>();
        foreach (var p in problems.Where(p => p.Kind != ProblemKind.DyeMissing))
        {
            var target = p.Job.Target;
            issues.Add(new Issue(p.Kind is ProblemKind.DyeFailed ? Icon.Flask : Icon.AlertTriangle, p.Name, FirstSentence(p.Reason), "Show",
                () => ShowWhere(new[] { target }), p.Reason, Icon.MapPin, true));
        }

        var undyed = problems.Where(p => p.Kind == ProblemKind.DyeMissing).ToList();
        if (undyed.Count > 0)
        {
            var dyes = undyed.GroupBy(p => p.Job.Target.Stain).Select(g => (Name: data.DyeName(g.Key), Count: g.Count())).ToList();
            issues.Add(new Issue(Icon.Flask, undyed.Count == 1 ? "1 left undyed" : $"{undyed.Count} left undyed",
                $"You need {Words.List(dyes.Select(d => $"{d.Name} ×{d.Count}"))}", "Copy", () =>
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
        issuesCache = (problems, problems.Count, vm.RunMissing, vm.RunStartedAt, issues);
        return issues;
    }

    private static string FirstSentence(string text)
    {
        var dot = text.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? text[..(dot + 1)] : text;
    }

    /// <summary>
    /// A row of "Needs you" (10px 14px inside): the 34px thumb with its icon in danger, the name and
    /// one line of why (the whole reason as its tooltip), and its action as a quiet small button.
    /// </summary>
    private void IssueRow(int index, Issue issue)
    {
        var at = Kit.At;
        var w = Kit.Width;
        if (index > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);
        var bw = Kit.ButtonWidth(issue.Action, BtnSize.Tiny, issue.ActionIcon, Btn.Quiet);
        var bh = Kit.ButtonHeight(BtnSize.Tiny);
        var x = at.X + S(14) + S(34) + S(12);
        var tw = at.X + w - S(14) - bw - S(12) - x;
        var nameH = Txt.Natural(Face.SansMedium, 14);
        var reasonH = S(18);
        var wordsH = nameH + S(1) + reasonH;
        var h = S(10) * 2 + MathF.Max(S(34), wordsH);
        var mid = at.Y + h / 2;
        var dl = Kit.Dl;
        Kit.IconThumb(new Vector2(at.X + S(14), mid - S(17)), issue.Icon, Palette.Danger);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(issue.Name, Face.SansMedium, 14, tw), Face.SansMedium, 14, Palette.Ivory);
        var reason = Txt.Fit(issue.Reason, Face.Sans, 12, tw);
        Txt.Line(dl, new Vector2(x, top + nameH + S(1)), reason, Face.Sans, 12, Palette.Dim, 18);
        var tip = issue.Tip ?? issue.Reason;
        if ((tip != issue.Reason || !ReferenceEquals(reason, issue.Reason)) && !Kit.Blocked && ImGui.IsWindowHovered()
            && ImGui.IsMouseHoveringRect(new Vector2(x, top), new Vector2(x + tw, top + wordsH)))
            Kit.Tooltip(tip);
        var cantShow = issue.NeedsHome && session.Context.Side == null;
        if (Kit.Button($"##issue{index}", new Vector2(at.X + w - S(14) - bw, mid - bh / 2), issue.Action, Btn.Quiet, BtnSize.Tiny, issue.ActionIcon, 0, cantShow,
                cantShow ? "Go to your home to see where it goes" : null))
            issue.OnAction();
        Kit.Reserve(at, w, h);
    }

    private (DateTime At, int Pieces, string Line) putBackLine = (DateTime.MinValue, -1, string.Empty);

    /// <summary>The undo card after a run: "Not what you wanted?", the backup it saved, and Put them back (after asking).</summary>
    private void DrawPutBack(DateTime savedAt)
    {
        if (putBackLine.At != savedAt || putBackLine.Pieces != vm.LiveAtStart)
            putBackLine = (savedAt, vm.LiveAtStart, $"A backup of the {Words.Count(vm.LiveAtStart, "piece")} you had, saved at {Clock(savedAt)}");
        var cannot = runner.CanRestore();
        if (!Kit.ActionCard("##putback", Icon.Undo, null, "Not what you wanted?", putBackLine.Line, "Put them back", null, false, cannot != null,
                cannot ?? "Moves what you had back where it was, and puts away what was placed since (asks first)."))
            return;
        var pieces = Words.Count(vm.LiveAtStart, "piece");
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

    private (Layout? Layout, int Left, bool Enabled, string Title, string Line) fixturesCard = (null, -1, false, string.Empty, string.Empty);

    /// <summary>
    /// The fixtures left as a card (step 3 of the flow): "8 fixtures to set", what they are, and Set them
    /// with the fixtures run switched on (asks first, as Ready's does); else where to set them.
    /// </summary>
    private void DrawFixturesCard(Layout layout)
    {
        var all = FixtureCount(layout);
        if (all == 0) return;
        var left = all - FixturesDone(layout);
        if (left <= 0) return;
        var run = Plugin.FixtureRun;
        var f = fixturesCard;
        if (!ReferenceEquals(f.Layout, layout) || f.Left != left || f.Enabled != run.Enabled)
        {
            var menus = layout.InteriorFixtures.Count > 0 && layout.ExteriorFixtures.Count > 0 ? "Remodel Interior and Remodel Exterior"
                : layout.ExteriorFixtures.Count > 0 ? "Remodel Exterior" : "Remodel Interior";
            var what = FixtureSummary(layout);
            f = fixturesCard = (layout, left, run.Enabled, $"{Words.Count(left, "fixture")} to set",
                run.Enabled ? what : $"{what}. Set them from Housing → {menus}.");
        }
        Kit.Gap(14);
        if (!run.Enabled)
        {
            Kit.ActionCard("##fixtures-card", Icon.Brick, null, f.Title, f.Line, null);
            return;
        }
        var toSet = 0;
        foreach (var t in FixturePlan())
            if (t.State == FixtureState.ToSet) toSet++;
        var blocked = run.CanStart() ?? (toSet == 0 ? "Nothing here to set from your bags" : null);
        if (Kit.ActionCard("##fixtures-card", Icon.Brick, null, f.Title, f.Line, "Set them", null, false, blocked != null, blocked ?? FixtureBagWords()))
            AskSetFixtures(toSet);
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
            // Ready's brass button, as it stands: Get first (the errands' estimate), Keep, Clear out (asks
            // first) or Swap (asks first, keeps what's here), with " · fixtures" when they follow.
            var g = ReadyGo();
            var blocked = g.Getting ? null : runner.CanStart(false);
            var tip = g.Getting ? "Opens what the errands would get, the trips and the gil, before anything is bought."
                : g.NotGetting;
            if (Kit.Button("##start-real", at, g.Label, Btn.Primary, BtnSize.Lg, Icon.Play, primaryW, blocked != null, blocked ?? tip))
                Go(g, Filtered().ToList());
        }
        else if (stopped)
        {
            // A stopped putting-back: Carry on (the design alone) would leave what it was putting away out.
            var blocked = runner.WasPuttingBack ? "This was putting things back: start Put them back (or Swap back) again to finish it." : runner.CanStart(false);
            if (Kit.Button("##carry-on", at, "Carry on", Btn.Primary, BtnSize.Lg, Icon.Play, primaryW, blocked != null, blocked ?? "Starts again with what's still to do."))
            {
                // It clears out again only when the run stopped was clearing out (what you confirmed then).
                StartRun(false, null, runner.ClearOutCount > 0 ? ChosenClearOut() : null, vm.Dismiss);
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
