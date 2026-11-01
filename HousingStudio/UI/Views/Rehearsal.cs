using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// A rehearsal: Ready's Preview offers Mark the spots (the preview as it was) and Rehearse the run
/// (every step a real start does, nothing committed), following Ready's choices as the brass button
/// would: Get first opens the errands to rehearse, else the placing is rehearsed (Keep, Clear out or
/// Swap), with the fixtures after it when they're set after placing. While it runs, an outlined
/// "Rehearsing" pill; after it, "Rehearsal done" (or stopped) with one sentence of the outcome, what
/// didn't work (first, when something didn't), what it would have done or, stopped before it would
/// have done anything, how far it got, the folded log, and Done with Copy report (no Put them back,
/// Swap back or Try again: nothing changed).
/// </summary>
public sealed partial class MainWindow
{
    private static readonly RehearsalAct[] RehearsalActs = Enum.GetValues<RehearsalAct>();

    private const string PreviewMenu = "##preview-menu";

    /// <summary>The errands' screen was opened from Rehearse the run: its button rehearses them.</summary>
    private bool errandsRehearse;

    /// <summary>Ready's Preview menu: Mark the spots, Rehearse the run.</summary>
    private void DrawPreviewMenu(GoPlan g, List<PlanEntry> entries, string? markBlocked, string? rehearseBlocked)
    {
        if (!Kit.BeginMenu(PreviewMenu)) return;
        if (PreviewMenuRow("##preview-mark", Icon.Eye, RehearsalWords.Mark, RehearsalWords.MarkNote, markBlocked,
                "Walks through every piece, marking each spot in your home, and moves nothing."))
        {
            ImGui.CloseCurrentPopup();
            StartRun(true, entries, g.Clear);
        }
        if (PreviewMenuRow("##preview-rehearse", Icon.Play, RehearsalWords.Rehearse, RehearsalWords.RehearseNote, rehearseBlocked,
                RehearsalWords.RehearseTip(g.Label, g.Getting)))
        {
            ImGui.CloseCurrentPopup();
            Rehearse(g, entries);
        }
        Kit.EndMenu();
    }

    /// <summary>A row of the Preview menu: its icon, words and note; dim (with why as its tip) when it can't go now.</summary>
    private static bool PreviewMenuRow(string id, Icon icon, string title, string note, string? blocked, string tip)
    {
        var pos = ImGui.GetCursorScreenPos();
        var w = MathF.Max(S(280), Txt.Measure(note, Face.Sans, 12).X + S(52));
        var h = S(52);
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h)) && blocked == null;
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
        {
            if (blocked == null)
            {
                dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusSm));
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            Kit.Tooltip(blocked ?? tip);
        }
        var ink = blocked != null ? Palette.Dim : hovered ? Palette.Ivory : Palette.Text;
        Icons.Draw(dl, icon, new Vector2(pos.X + S(12), pos.Y + (h - S(16)) / 2), S(16), blocked != null ? Palette.Dim : Palette.Brass);
        var x = pos.X + S(12 + 16 + 12);
        Txt.Line(dl, new Vector2(x, pos.Y + S(8)), title, Face.SansMedium, 13, ink, 18);
        Txt.Line(dl, new Vector2(x, pos.Y + S(8 + 18)), note, Face.Sans, 12, Palette.Dim, 18);
        return clicked;
    }

    /// <summary>
    /// Rehearse the run, as the brass button would go: Get first opens the errands (their button
    /// rehearses them); else the placing rehearsal starts at once (nothing to confirm: nothing is
    /// committed), with the fixtures after it when they're set after placing.
    /// </summary>
    private void Rehearse(GoPlan g, List<PlanEntry> entries)
    {
        if (g.Getting)
        {
            errandsRehearse = true;
            errandsOpen = true;
            return;
        }
        var keep = g.Mode == ReadyMode.Swap && g.Clear != null ? KeepAsName() : null;
        var title = session.Title;
        // Missing pieces, and the errands not rehearsed: the report says why (as the line under the button).
        var notGetting = ShopPlan().IsEmpty ? null
            : g.NotGetting != null ? $"Getting wasn't rehearsed: {g.NotGetting}"
            : !config.GetFirst ? "Getting wasn't rehearsed: \"Get them for me first\" is off."
            : null;
        vm.Starting(entries);
        OnFramework(() =>
        {
            Rehearsals.Report.Begin(title, DateTime.Now);
            if (notGetting != null) Rehearsals.Report.Note(notGetting);
            runner.Rehearse(entries, g.Clear, keep);
            if (runner.Active)
            {
                ArmFixturesAfterPlacing();
                return;
            }
            vm.NotStarted();
            if (runner.StopReason != null) Notify(runner.StopReason);
        });
    }

    /// <summary>The "Rehearsing" pill: as the status pill, but a brass outline, not filled. Returns its width.</summary>
    private static float RehearsalPill(Vector2 pos, string text, float maxWidth = 0)
    {
        var full = Kit.StatusPillWidth(text);
        if (maxWidth > 0 && full > maxWidth) text = Txt.Fit(text, Face.Sans, 12, maxWidth - (full - Txt.Measure(text, Face.Sans, 12).X));
        var w = Kit.StatusPillWidth(text);
        var h = S(28);
        var dl = Kit.Dl;
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Brass), h / 2, ImDrawFlags.None, 1f);
        dl.AddCircle(pos + new Vector2(S(12) + S(3.5f), h / 2), S(3.5f), Palette.U32(Palette.Brass), 16, 1.2f);
        Txt.Line(dl, new Vector2(pos.X + S(12 + 7 + 8), pos.Y), text, Face.Sans, 12, Palette.BrassSoft, 28);
        return w;
    }

    // ---------------------------------------------------------------------------------------------
    // After it: Done's place (placing) and the errands' summary's.

    private readonly Run[] rehearsalLine = new Run[1];

    /// <summary>
    /// "Rehearsal done" (or stopped): the mark, one sentence of the outcome (who stopped it, where, and
    /// that nothing was committed, or bought or taken); what didn't work, each with its message, first
    /// when there's any; what it would have done (the report's counts, the purchase questions, the
    /// teleports), or how far it got when it stopped before it would have done anything; the log,
    /// folded with its line count; Done in brass and Copy report, with where Done goes under them.
    /// </summary>
    private void DrawRehearsalResult(bool errands)
    {
        var e = Errands;
        var stopped = errands ? e.State == RunState.Stopped : runner.State == RunState.Stopped;
        var why = errands ? e.StopReason : runner.StopReason;
        var byYou = why?.StartsWith("Stopped by you", StringComparison.Ordinal) == true;
        var report = Rehearsals.Report;
        var issues = report.Issues;

        // 20 on every screen (0.9.3): the round buttons at the top sit at one height from screen to screen.
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var (fill, ink, mark) = stopped && byYou ? (Palette.LinePicked, Palette.Ash, Icon.Square)
            : issues.Count > 0 || stopped ? (Palette.BrassWash, Palette.Brass, Icon.Alert)
            : (Palette.SuccessWash, Palette.Success, Icon.Check);
        var d = S(52);
        Kit.Round(new Vector2(at.X + (w - d) / 2, at.Y), d, mark, S(24), fill, ink);
        var y = at.Y + d + S(12);
        var (plain, italic) = RehearsalWords.Title(stopped);
        y += CenteredTitle(y, plain, italic, at.X, w) + S(4);
        var (outcome, far) = RehearsalOutcome(errands, stopped, why, byYou, report);
        rehearsalLine[0] = new Run(outcome, Face.Sans, 13, Palette.Dim);
        y += Txt.Flow(new Vector2(at.X, y), w, rehearsalLine, 19.5f, out _, center: true);
        Kit.Reserve(at, w, y - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##rehearsal-done-body", "rehearsal-done-foot", 20, 22);
        if (!errands) DrawFixtureRunPanel();
        // What didn't work first, when something didn't (0.9.0: an empty "0 · Every step worked" is left out;
        // the line under the title says it).
        if (issues.Count > 0)
        {
            Kit.Section("What didn't work", issues.Count.ToString());
            Kit.Gap(10);
            Kit.BeginPanel("##rehearsal-issues", 0, 0, 0);
            for (var i = 0; i < issues.Count; i++) RehearsalIssueRow(i, issues[i]);
            Kit.EndPanel();
            Kit.Gap(18);
        }
        // Stopped before it would have done anything: how far it got, in place of "Nothing it would have done".
        if (stopped && far != null && NothingCounted(report)) DrawHowFar(far, report);
        else DrawWouldHaveDone(report);
        Kit.Gap(14);
        // The log from the rehearsal's start, folded with its line count: after a stop it's for sending.
        DrawLogPanel("##rehearsal-log", ref rehearsalLogOpen, true, null, report.StartedAt == default ? (DateTime?)null : report.StartedAt);
        Frame.EndBody();

        // Done in brass on the left (every screen's main step), Copy report to its right, and where Done goes.
        Frame.BeginFooter("rehearsal-done-foot", 18, 22);
        var row = Kit.At;
        var footW = Kit.Width;
        const string copy = "Copy report";
        const string done = "Done";
        var cw = Kit.ButtonWidth(copy, BtnSize.Lg, Icon.Clipboard, Btn.Glass);
        if (Kit.Button("##rehearsal-done", row, done, Btn.Primary, BtnSize.Lg, Icon.Check, footW - cw - S(10)))
        {
            if (errands)
            {
                e.Dismiss();
                errandsOpen = false;
                errandsRehearse = false;
            }
            vm.Dismiss();
        }
        if (Kit.Button("##rehearsal-copy", new Vector2(row.X + footW - cw, row.Y), copy, Btn.Glass, BtnSize.Lg, Icon.Clipboard, 0, false,
                "Copies what it would have done, what didn't work, and the whole log, for a report"))
        {
            ImGui.SetClipboardText(report.Text(log.AsText()));
            Notify("Report copied");
        }
        Kit.Reserve(row, footW, S(46));
        Kit.Gap(12);
        Txt.Note(RehearsalWords.DoneHint, null, true);
        Frame.EndFooter();
    }

    /// <summary>The result's log starts folded (it's for sending, not reading).</summary>
    private bool rehearsalLogOpen;

    private (int Version, bool Errands, bool Stopped, string? Why, int Jobs, string Line, HowFar? Far) rehearsalResult =
        (-1, false, false, null, -1, string.Empty, null);

    /// <summary>
    /// The line under the title (who stopped it, where, and that nothing was committed) and, after a
    /// stop, how far it got: the pieces walked (placing) or the stops (the errands). Made again only
    /// when the report, the run or why it stopped changes.
    /// </summary>
    private (string Line, HowFar? Far) RehearsalOutcome(bool errands, bool stopped, string? why, bool byYou, RehearsalReport report)
    {
        var e = Errands;
        var jobs = errands ? e.GotCount : runner.Jobs.Count;
        var r = rehearsalResult;
        if (r.Line.Length > 0 && r.Version == report.Version && r.Errands == errands && r.Stopped == stopped && r.Why == why && r.Jobs == jobs)
            return (r.Line, r.Far);
        HowFar? far = null;
        if (stopped)
        {
            if (errands)
            {
                var stops = new List<(string Name, int Things)>();
                foreach (var trip in e.Plan.Trips)
                    foreach (var stop in trip.Stops)
                        stops.Add((RehearsalWords.StopName(stop), stop.Count));
                far = RehearsalWords.Stops(stops, e.GotCount, report.Teleports);
            }
            else
            {
                var pieces = new List<(string Name, JobKind Kind, JobOutcome Outcome, bool StoppedHere)>(runner.Jobs.Count);
                foreach (var j in runner.Jobs)
                    pieces.Add((data.ItemName(j.Target.ItemId), j.Kind, j.Outcome, j.Outcome == JobOutcome.Skipped && j.Note == "Stopped."));
                far = RehearsalWords.Pieces(pieces);
            }
        }
        // The errands rehearse placing after their last trip: "or placed" once they did (or finished).
        var placingToo = !stopped || report.Count(RehearsalAct.Place) + report.Count(RehearsalAct.Move) + report.Count(RehearsalAct.Store) + report.Count(RehearsalAct.Dye) > 0;
        var line = RehearsalWords.Outcome(stopped, byYou, why, far?.At, report.Issues.Count, RehearsalWords.Nothing(errands, placingToo));
        rehearsalResult = (report.Version, errands, stopped, why, jobs, line, far);
        return (line, far);
    }

    /// <summary>The report counted nothing it would have done (it can still have issues).</summary>
    private static bool NothingCounted(RehearsalReport report)
    {
        foreach (var act in RehearsalActs)
            if (report.Count(act) > 0) return false;
        return true;
    }

    /// <summary>"What it would have done": the report's counts, the purchase questions, the teleports and what it left out.</summary>
    private void DrawWouldHaveDone(RehearsalReport report)
    {
        Kit.BeginPanel("##rehearsal-counts", 14, 16, 14);
        var top = Kit.At;
        var eh = Kit.Eyebrow(top, "What it would have done", null, Kit.Width);
        Kit.Reserve(top, Kit.Width, eh);
        Kit.Gap(6);
        Txt.Para(new[] { new Run(report.CountsLine(), Face.Sans, 15, Palette.Ivory) }, 15 * 1.4f);
        if (report.QuestionsLine() is { } q)
        {
            Kit.Gap(4);
            Txt.Note(q);
        }
        if (report.Teleports > 0)
        {
            Kit.Gap(4);
            Txt.Note($"{(report.Teleports == 1 ? "1 teleport was" : $"{report.Teleports} teleports were")} made: their fees are the only gil spent.");
        }
        foreach (var n in report.Notes)
        {
            Kit.Gap(4);
            Txt.Note(n);
        }
        Kit.EndPanel();
    }

    /// <summary>
    /// "How far it got" (a rehearsal stopped before it would have done anything): the count on the
    /// right ("0 of 10 stops"), then the last step that worked and where it stopped, each with a line
    /// and how it ended; what it left out under them.
    /// </summary>
    private void DrawHowFar(HowFar far, RehearsalReport report)
    {
        Kit.BeginPanel("##rehearsal-far", 14, 16, 14);
        var top = Kit.At;
        var w = Kit.Width;
        var countW = Txt.Measure(far.Count, Face.Mono, 12).X;
        var eh = Kit.Eyebrow(top, "How far it got", null, MathF.Max(S(40), w - countW - S(12)));
        Txt.Line(Kit.Dl, new Vector2(top.X + w - countW, top.Y), far.Count, Face.Mono, 12, Palette.Brass, eh / S(1));
        Kit.Reserve(top, w, eh);
        if (far.Rows.Count == 0)
        {
            Kit.Gap(8);
            Txt.Note("It stopped before its first step.");
        }
        for (var i = 0; i < far.Rows.Count; i++)
        {
            Kit.Gap(10);
            HowFarRowDraw(far.Rows[i]);
        }
        foreach (var n in report.Notes)
        {
            Kit.Gap(6);
            Txt.Note(n);
        }
        Kit.EndPanel();
    }

    /// <summary>A row of "How far it got": a round mark (a tick when it worked), the name and its line, and "worked" or "stopped".</summary>
    private void HowFarRowDraw(HowFarRow row)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var d = S(28);
        var nameH = Txt.Natural(Face.SansMedium, 14);
        var noteH = S(18);
        var wordsH = nameH + S(1) + noteH;
        var h = MathF.Max(d, wordsH);
        var end = row.Worked ? "worked" : "stopped";
        var endW = Txt.Measure(end, Face.Mono, 11).X;
        var x = at.X + d + S(12);
        var tw = at.X + w - endW - S(12) - x;
        var (fill, ink, icon) = row.Worked ? (Palette.SuccessWash, Palette.Success, Icon.Check) : (Palette.LinePicked, Palette.Dim, Icon.Square);
        Kit.Round(new Vector2(at.X, at.Y + (h - d) / 2), d, icon, S(14), fill, ink);
        var top = at.Y + (h - wordsH) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(row.Name, Face.SansMedium, 14, tw), Face.SansMedium, 14, row.Worked ? Palette.Ivory : Palette.Ash);
        Txt.Line(dl, new Vector2(x, top + nameH + S(1)), Txt.Fit(row.Note, Face.Sans, 12, tw), Face.Sans, 12, Palette.Dim, 18);
        Txt.Line(dl, new Vector2(at.X + w - endW, top), end, Face.Mono, 11, row.Worked ? Palette.Success : Palette.Dim, nameH / S(1));
        Kit.Reserve(at, w, h);
    }

    /// <summary>A row of what didn't work: where (a piece, a step), the message, and what kind (offscreen rows only take their room).</summary>
    private void RehearsalIssueRow(int index, RehearsalIssue issue)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var x = at.X + S(14) + S(16) + S(12);
        var tw = at.X + w - S(14) - x;
        var whatRuns = new[] { new Run(issue.What, Face.Sans, 12, Palette.Dim) };
        var nameH = Txt.Natural(Face.SansMedium, 14);
        var whatH = Txt.Flow(Vector2.Zero, tw, whatRuns, 18, out _, draw: false);
        var h = S(10) * 2 + nameH + S(2) + whatH;
        var clipTop = ImGui.GetWindowPos().Y;
        if (at.Y > clipTop + ImGui.GetWindowHeight() || at.Y + h < clipTop)
        {
            Kit.Reserve(at, w, h);
            return;
        }
        if (index > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);
        var dl = Kit.Dl;
        var color = issue.Kind is RehearsalTrouble.WouldStop or RehearsalTrouble.NoMatch ? Palette.Brass : Palette.Danger;
        Icons.Draw(dl, issue.Kind == RehearsalTrouble.WouldStop ? Icon.Info : Icon.AlertTriangle, new Vector2(at.X + S(14), at.Y + S(10) + (nameH - S(16)) / 2), S(16), color);
        var kind = RehearsalReport.KindWord(issue.Kind);
        var kw = Txt.Measure(kind, Face.Mono, 11).X;
        Txt.Line(dl, new Vector2(x, at.Y + S(10)), Txt.Fit(issue.Where, Face.SansMedium, 14, tw - kw - S(10)), Face.SansMedium, 14, Palette.Ivory);
        Txt.Line(dl, new Vector2(at.X + w - S(14) - kw, at.Y + S(10)), kind, Face.Mono, 11, Palette.Dim, nameH / S(1));
        Txt.Flow(new Vector2(x, at.Y + S(10) + nameH + S(2)), tw, whatRuns, 18, out _);
        Kit.Reserve(at, w, h);
    }
}
