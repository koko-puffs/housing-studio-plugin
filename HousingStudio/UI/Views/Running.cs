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
/// Running (mockup 3): a run or a preview going, paused, or waiting for you. The figure and bar, the
/// three phases, the piece in hand, the step-by-step callout, the log, and Pause / Stop.
/// </summary>
public sealed partial class MainWindow
{
    private bool logOpen = true;

    /// <summary>The run (its log's start) whose earlier lines are shown: "Earlier lines" is on for that run only.</summary>
    private DateTime? logEarlierFor;

    /// <summary>A little before the start given (a run logs its first line in the frame before the window sees it going).</summary>
    private static readonly TimeSpan LogStartGrace = TimeSpan.FromSeconds(2);

    private (int Count, string Line) skippedLine = (-1, string.Empty);
    private (RunKind Kind, string Short, string Line) runEyebrow = (RunKind.Placing, string.Empty, string.Empty);
    private (RunFigure Figure, RunKind Kind, bool Dyeing, string Big, string Text) figureWords = (default, RunKind.Placing, false, string.Empty, string.Empty);

    /// <summary>"Placing · Solution9Loft" (the short name: the whole one was cut off), made again only when it changes.</summary>
    private string RunEyebrow(RunKind kind)
    {
        var shortTitle = vm.RunShortTitle;
        if (runEyebrow.Line.Length == 0 || runEyebrow.Kind != kind || !ReferenceEquals(runEyebrow.Short, shortTitle))
            runEyebrow = (kind, shortTitle, PlacingWords.Eyebrow(kind, shortTitle));
        return runEyebrow.Line;
    }

    private void DrawRunning()
    {
        var preview = runner.DryRun;
        var rehearsal = runner.Rehearsal;
        var waiting = runner.AwaitingContinue || runner.WaitingForYou;

        Frame.BeginHeader(24);
        var at = Kit.At;
        var eyebrowH = Kit.Eyebrow(at, RunEyebrow(preview ? RunKind.Preview : rehearsal ? RunKind.Rehearsal : RunKind.Placing), null, Kit.Width);
        var left = Words.About(runner.TimeLeft);
        var (pill, tone) = waiting ? ("Waiting for you", Tone.Waiting)
            : runner.State == RunState.Paused ? ("Paused", Tone.Neutral)
            : preview ? ($"Previewing · nothing is moved · {left} left", Tone.Live)
            : ($"Running · {left} left", Tone.Live);
        // A rehearsal's pill is a brass outline (it changes nothing), while it isn't waiting or paused.
        if (rehearsal && tone == Tone.Live) RehearsalPill(at + new Vector2(0, eyebrowH + S(8)), RehearsalWords.Pill(left), Kit.Width);
        else Kit.StatusPill(at + new Vector2(0, eyebrowH + S(8)), pill, tone, tone == Tone.Live, Kit.Width);
        Kit.Reserve(at, Kit.Width, eyebrowH + S(8) + S(28));
        Frame.EndHeader();

        Frame.BeginBody("##running", "running-foot", 22, 22);
        // Continue first, at the top (0.8.0): below the piece in hand it moved with every step's words.
        if (waiting)
        {
            DrawCallout();
            Kit.Gap(18);
        }
        DrawFigure(preview);
        Kit.Gap(18);
        DrawPhases();
        if (runner.Current is { } job)
        {
            Kit.Gap(18);
            DrawCurrent(job, preview);
        }
        // Pieces that didn't work (skipped, or failed and passed over): where they go, under the piece in hand.
        var problems = runner.Failed + runner.Skipped;
        if (problems > 0)
        {
            Kit.Gap(10);
            if (skippedLine.Count != problems) skippedLine = (problems, PlacingWords.Skipped(problems));
            Txt.Note(skippedLine.Line, null, true);
        }
        Kit.Gap(18);
        // The log from this run's start; the lines before it behind "Earlier lines".
        DrawLog("running-bottom", vm.RunStartedAt);

        Frame.BeginBottom("running-bottom");
        var stay = session.Context.Side == Side.Garden
            ? "Stay on your plot and leave the furnishing editor open. You can move the camera."
            : "Stay in the house and leave the furnishing editor open. You can move the camera.";
        Txt.Note(preview ? "Nothing is moved: each spot is marked in your home in turn. You can move the camera."
            : rehearsal ? $"Nothing is committed: each piece is held at its spot and let go, each dye picked and closed. {stay}"
            : stay, center: true);
        Frame.EndBottom();
        Frame.EndBody();

        DrawRunningFooter();
    }

    /// <summary>
    /// "18 of 46 to do · then 20 dyes": the 56px serif figure on the words' baseline, and the 8px bar.
    /// The figure counts Ready's to-do (moves and placements), the dyes beside it (<see cref="RunFigure"/>);
    /// the bar is everything the run does, so it's full only once the dyes are done too.
    /// </summary>
    private void DrawFigure(bool preview)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var counts = vm.RunCounts;
        var kind = preview ? RunKind.Preview : runner.Rehearsal ? RunKind.Rehearsal : RunKind.Placing;
        var dyeing = runner.Phase == RunPhase.Dye;
        var fw0 = figureWords;
        if (fw0.Big.Length == 0 || fw0.Figure != counts || fw0.Kind != kind || fw0.Dyeing != dyeing)
            fw0 = figureWords = (counts, kind, dyeing, counts.Figure.ToString(), PlacingWords.Figure(counts, kind, dyeing));
        var figure = fw0.Big;
        var words = fw0.Text;
        var dl = Kit.Dl;
        // line-height 1 for the figure; the words share its baseline (align-items: baseline).
        var bigBox = S(56);
        var bigTop = at.Y + (bigBox - Txt.Natural(Face.Serif, 56)) / 2;
        var baseline = bigTop + Txt.Ascent(Face.Serif, 56);
        var fw = Txt.Draw(dl, new Vector2(at.X, bigTop), figure, Face.Serif, 56, Palette.Ivory);
        Txt.Draw(dl, new Vector2(at.X + fw + S(14), baseline - Txt.Ascent(Face.Sans, 15)), words, Face.Sans, 15, Palette.Ash);
        Kit.Reserve(at, w, bigBox);
        Kit.Gap(14);
        var bar = Kit.At;
        Kit.Progress(bar, w, counts.Bar);
        Kit.Reserve(bar, w, S(8));
    }

    /// <summary>The three phases in a grid: done (a green tick), now (brass edge and wash), next (dim).</summary>
    private void DrawPhases()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var gap = S(10);
        var cw = (w - gap * 2) / 3;
        var nameH = Txt.Natural(Face.Sans, 13);
        var figH = Txt.Natural(Face.Mono, 13);
        var h = S(14) * 2 + MathF.Max(S(16), nameH) + S(6) + figH;
        var current = runner.Phase;
        var phases = new[] { (RunPhase.Move, "Move", Icon.Move), (RunPhase.Place, "Place", Icon.Package), (RunPhase.Dye, "Dye", Icon.Droplet) };
        var dl = Kit.Dl;
        for (var i = 0; i < 3; i++)
        {
            var (phase, name, icon) = phases[i];
            var (done, problems, total) = runner.PhaseProgress(phase);
            var unplanned = phase == RunPhase.Dye && !runner.DyesPlanned;
            var now = current == phase;
            var before = current != null && phase < current.Value;
            var finished = !now && !unplanned && (before || (total > 0 && done + problems >= total));
            var p = new Vector2(at.X + i * (cw + gap), at.Y);
            var max = p + new Vector2(cw, h);
            var r = S(Palette.RadiusCard);
            if (now) dl.AddRectFilled(p, max, Palette.U32(Palette.BrassNow), r);
            dl.AddRect(p + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(now ? Palette.BrassEdge : Palette.Line), r, ImDrawFlags.None, 1f);
            var ink = finished ? Palette.Success : now ? Palette.BrassSoft : Palette.Ash;
            var y = p.Y + S(14);
            var lineH = MathF.Max(S(16), nameH);
            Icons.Draw(dl, finished ? Icon.Check : icon, new Vector2(p.X + S(16), y + (lineH - S(16)) / 2), S(16), ink);
            Txt.Line(dl, new Vector2(p.X + S(16 + 16 + 8), y + (lineH - nameH) / 2), name, Face.Sans, 13, ink);
            y += lineH + S(6);
            string figure;
            var dim = false;
            if (unplanned)
            {
                figure = config.DyeAfterPlacing ? "next" : "off";
                dim = true;
            }
            else if (total == 0)
            {
                figure = finished || before ? "none" : "next";
                dim = true;
            }
            else if (!now && !finished)
            {
                figure = "next";
                dim = true;
            }
            else
            {
                figure = $"{done} of {total}";
            }
            Txt.Line(dl, new Vector2(p.X + S(16), y), Txt.Fit(figure, Face.Mono, 13, cw - S(32)), Face.Mono, 13, dim ? Palette.Dim : Palette.Ivory);
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>
    /// The piece in hand: its icon, name, dye (the stain's own colour) and floor, and the step in words;
    /// a quiet Skip on the right (0.9.0: skipping a stuck piece was only in the step-by-step callout).
    /// A preview has no Skip: it only marks the spot.
    /// </summary>
    private void DrawCurrent(RunJob job, bool preview = false)
    {
        Kit.BeginPanel("##current", 18, 20, 18);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        const string skipCard = "Skip";
        var skipW = preview ? 0 : Kit.ButtonWidth(skipCard, BtnSize.Sm, null, Btn.Quiet);
        var x = at.X + S(52) + S(16);
        var tw = at.X + w - x - (preview ? 0 : skipW + S(12));
        var nameH = Txt.Natural(Face.SansMedium, 15);
        var note = new System.Collections.Generic.List<Run>();
        if (job.Target.Stain != 0)
        {
            note.Add(Run.Dot(data.DyeRgb(job.Target.Stain)));
            note.Add(Run.Note($"{data.DyeName(job.Target.Stain)} · "));
        }
        note.Add(Run.Note(Floors.Name(job.Target.Floor)));
        var step = vm.StepText;
        var stepRuns = step != null ? new[] { Run.Note(step, Palette.Text) } : Array.Empty<Run>();
        var noteH = Txt.Flow(Vector2.Zero, tw, note, 19.5f, out _, draw: false);
        var stepH = step != null ? Txt.Flow(Vector2.Zero, tw, stepRuns, 19.5f, out _, draw: false) : 0;
        var wordsH = nameH + S(4) + noteH + (step != null ? S(4) + stepH : 0);
        var h = MathF.Max(S(52), wordsH);
        Kit.Thumb(new Vector2(at.X, at.Y + (h - S(52)) / 2), S(52), S(Palette.RadiusMd), data.ItemIcon(job.Target.ItemId), S(22));
        var y = at.Y + (h - wordsH) / 2;
        Txt.Line(dl, new Vector2(x, y), Txt.Fit(data.ItemName(job.Target.ItemId), Face.SansMedium, 15, tw), Face.SansMedium, 15, Palette.Ivory);
        y += nameH + S(4);
        Txt.Flow(new Vector2(x, y), tw, note, 19.5f, out _);
        y += noteH + S(4);
        if (step != null) Txt.Flow(new Vector2(x, y), tw, stepRuns, 19.5f, out _);
        if (!preview)
        {
            var skipH = Kit.ButtonHeight(BtnSize.Sm);
            if (Kit.Button("##skip-card", new Vector2(at.X + w - skipW, at.Y + (h - skipH) / 2), skipCard, Btn.Quiet, BtnSize.Sm, null, 0, false,
                    "Leaves this piece for now (it's listed at the end) and goes on to the next."))
                OnFramework(runner.SkipCurrent);
        }
        Kit.Reserve(at, w, h);
        Kit.EndPanel();
    }

    /// <summary>
    /// Step by step (no mockup frame): a brass callout saying what the next click does, with Continue
    /// as the screen's one brass button. Waiting for you uses the same callout without Continue.
    /// </summary>
    private void DrawCallout()
    {
        var step = runner.AwaitingContinue;
        var name = runner.Current is { } j ? data.ItemName(j.Target.ItemId) : null;
        Kit.BeginPanel("##callout", 16, 20, 16, Palette.Over(Palette.BrassNow, Palette.Ink), Palette.BrassEdge);
        Kit.EyebrowRow(step ? "Step by step · next" : "Waiting for you");
        Kit.Gap(6);
        var line = StepWords.Friendly(runner.StepLabel, name) ?? (step ? "The next click" : "Something only you can do");
        Txt.Para(new[] { new Run(line, Face.Sans, 15, Palette.Ivory) }, 15 * 1.4f);
        Kit.Gap(4);
        if (step)
        {
            // The click itself, window and values, as the log has it.
            Txt.Para(new[] { new Run(runner.StepLabel ?? string.Empty, Face.Mono, 12, Palette.Dim) }, 12 * 1.6f);
            Kit.Gap(4);
            Txt.Para(new[] { Run.Note("Nothing is clicked until you continue. "), Run.Note("/hstudio continue", Palette.Dim), Run.Note(" works too.") });
            if (runner.HoldingALetGo)
            {
                // The rehearsal's let-go isn't seen working yet: it waits here until you trust it.
                Kit.Gap(6);
                Txt.Note("This click hasn't been seen working yet: watch that the editor lets go of the piece without setting it down (its old spot is written back first, so it stays where it was either way). Send the log if it doesn't.");
                Kit.Gap(8);
                var trustAt = Kit.At;
                if (Kit.Button("##trust-letgo", trustAt, "Trust it from now on", Btn.Quiet, BtnSize.Sm, Icon.Check, 0, false,
                        "Lets the rehearsal let go of pieces without waiting for Continue each time."))
                {
                    config.RehearsalTrustLetGo = true;
                    config.Save();
                    OnFramework(runner.Continue);
                }
                Kit.Reserve(trustAt, Kit.Width, Kit.ButtonHeight(BtnSize.Sm));
            }
        }
        else
        {
            Txt.Note("It carries on by itself once that's done.");
        }
        Kit.Gap(14);
        var row = Kit.At;
        var w = Kit.Width;
        const string cont = "Continue";
        const string skip = "Skip this piece";
        var cw = step ? Kit.ButtonWidth(cont, BtnSize.Md, Icon.Play, Btn.Primary) : 0;
        var sw = Kit.ButtonWidth(skip, BtnSize.Md, null, Btn.Quiet);
        if (step && Kit.Button("##continue", new Vector2(row.X + w - cw, row.Y), cont, Btn.Primary, BtnSize.Md, Icon.Play))
            OnFramework(runner.Continue);
        if (runner.Current != null && Kit.Button("##skip", new Vector2(row.X + w - cw - (step ? S(8) : 0) - sw, row.Y), skip, Btn.Quiet, BtnSize.Md, null, 0, false,
                "Leaves this piece for now (it's listed at the end) and goes on to the next."))
            OnFramework(runner.SkipCurrent);
        Kit.Reserve(row, w, S(40));
        Kit.EndPanel();
    }

    /// <summary>
    /// "What it's doing": a disclosure with Copy, and the log's last lines: as many as fit down to the
    /// window's bottom (at least three; <paramref name="bottomId"/> is the part kept under it), so a
    /// bigger window shows more. The newest in full colour, warnings and errors in theirs, the rest dim.
    /// With <paramref name="since"/> (a run's start), the lines begin there (0.9.0: they began with
    /// design loads from earlier); "Earlier lines" beside Copy shows the rest. Copy copies everything.
    /// </summary>
    private void DrawLog(string? bottomId = null, DateTime? since = null) =>
        DrawLogPanel("##log", ref logOpen, false, bottomId, since);

    private (int Lines, string Title) logTitle = (-1, string.Empty);

    /// <summary>
    /// The log's panel (<see cref="DrawLog"/>) under its own id and open state; <paramref name="counted"/>
    /// titles it with its line count, as a rehearsal's result does ("What it did · 13 lines").
    /// </summary>
    private void DrawLogPanel(string id, ref bool open, bool counted, string? bottomId, DateTime? since)
    {
        var all = log.Lines;
        var first = since is { } start ? LogFirstAt(all, start - LogStartGrace) : 0;
        var earlier = first > 0 && logEarlierFor == since;
        var from = earlier ? 0 : first;
        var title = "What it's doing";
        if (counted)
        {
            if (logTitle.Lines != all.Count - from) logTitle = (all.Count - from, RehearsalWords.LogTitle(all.Count - from));
            title = logTitle.Title;
        }

        Kit.BeginPanel(id, 0, 0, 0);
        const string copy = "Copy";
        const string before = "Earlier lines";
        var copyW = Kit.ButtonWidth(copy, BtnSize.Tiny, null, Btn.Quiet);
        // "Earlier lines" only when there are some: a toggle (picked while they're shown).
        var beforeStyle = earlier ? Btn.Picked : Btn.Quiet;
        var beforeW = first > 0 ? Kit.ButtonWidth(before, BtnSize.Tiny, null, beforeStyle) : 0;
        var gap = first > 0 ? S(6) : 0;
        Disclosure(id + "-row", ref open, title, null, 14, beforeW + gap + copyW, S(30), (p, _) =>
        {
            if (first > 0 && Kit.Button(id + "-earlier", p, before, beforeStyle, BtnSize.Tiny, null, 0, false,
                    earlier ? "Shows the log from this run's start again" : "Shows the log's lines from before this run too"))
                logEarlierFor = earlier ? null : since;
            if (Kit.Button(id + "-copy", new Vector2(p.X + beforeW + gap, p.Y), copy, Btn.Quiet, BtnSize.Tiny, null, 0, false, "Copies the whole log, for a report"))
            {
                ImGui.SetClipboardText(log.AsText());
                Notify("Log copied");
            }
        }, true);
        if (open)
        {
            var lineBox = S(12 * 1.6f);
            var step = lineBox + S(2);
            // The panel's own padding below the lines (16) is in the room too.
            var fits = (int)MathF.Floor((Frame.RoomLeft(bottomId) - S(16) + S(2)) / step);
            var count = Math.Clamp(fits, 3, 200);
            var at = Kit.At;
            var x = at.X + S(20);
            var w = Kit.Width - S(40);
            var y = at.Y;
            var dl = Kit.Dl;
            var lines = LogRows(count, x + w, from);
            if (lines.Count == 0)
            {
                Txt.Line(dl, new Vector2(x, y), "Nothing yet.", Face.Sans, 12, Palette.Dim, 12 * 1.6f);
                y += lineBox;
            }
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var tw = Txt.Line(dl, new Vector2(x, y), l.Time, Face.Mono, 12, Palette.Dim, 12 * 1.6f);
                var color = l.Kind switch
                {
                    LogKind.Error => Palette.Danger,
                    LogKind.Warn => Palette.BrassSoft,
                    _ => i < lines.Count - 1 ? Palette.Dim : Palette.Text,
                };
                var tx = x + tw + S(12);
                Txt.Line(dl, new Vector2(tx, y), l.Fit, Face.Sans, 12, color, 12 * 1.6f);
                // A line cut short: the whole of it on hover.
                if (l.Fit != l.Text && !Kit.Blocked && ImGui.IsWindowHovered()
                    && ImGui.IsMouseHoveringRect(new Vector2(tx, y), new Vector2(x + w, y + lineBox)))
                    Kit.Tooltip(l.Text);
                y += i < lines.Count - 1 ? step : lineBox;
            }
            Kit.Reserve(at, Kit.Width, y - at.Y + S(16));
        }
        Kit.EndPanel();
    }

    /// <summary>The first of the log's lines at or after <paramref name="at"/> (they're in time order), found by halving.</summary>
    private static int LogFirstAt(IReadOnlyList<LogLine> lines, DateTime at)
    {
        var lo = 0;
        var hi = lines.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].At < at) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private readonly record struct LogRow(string Time, string Text, string Fit, LogKind Kind);

    private (int Count, LogLine? Last, int Shown, float Right, int From, List<LogRow> Rows) logRows = (0, null, 0, 0, 0, new List<LogRow>());

    /// <summary>
    /// The log's last <paramref name="count"/> lines from line <paramref name="from"/> on, worded and cut
    /// to fit, made again only when a line comes, the room changes or the window's width does (it was
    /// all done again every frame).
    /// </summary>
    private List<LogRow> LogRows(int count, float right, int from = 0)
    {
        var all = log.Lines;
        var last = all.Count > 0 ? all[^1] : null;
        var c = logRows;
        if (c.Count == all.Count && ReferenceEquals(c.Last, last) && c.Shown == count && c.From == from && Math.Abs(c.Right - right) < 0.5f) return c.Rows;
        var start = Math.Max(Math.Max(0, from), all.Count - count);
        var rows = new List<LogRow>(Math.Max(0, Math.Min(count, all.Count - start)));
        var timeW = Txt.Measure("00:00:00", Face.Mono, 12).X;
        for (var i = start; i < all.Count; i++)
        {
            var l = all[i];
            var text = LogWords(l.Text);
            var tx = right - Kit.Width + S(40) + timeW + S(12);
            rows.Add(new LogRow(l.At.ToString("HH:mm:ss"), text, Txt.Fit(text, Face.Sans, 12, right - tx), l.Kind));
        }
        logRows = (all.Count, last, count, right, from, rows);
        return rows;
    }

    /// <summary>A log line without the step markers it's indented with ("  → ", "  … ", "  ✕ ").</summary>
    private static string LogWords(string text) => text.TrimStart().TrimStart('→', '…', '✕', '?').TrimStart();

    private void DrawRunningFooter()
    {
        Frame.BeginFooter("running-foot", 18, 22);
        var at = Kit.At;
        var w = Kit.Width;
        const string stop = "Stop";
        var sw = Kit.ButtonWidth(stop, BtnSize.Lg, Icon.Square, Btn.Danger);
        var paused = runner.State == RunState.Paused;
        var bw = w - sw - S(10);
        if (paused)
        {
            if (Kit.Button("##resume", at, "Resume", Btn.Primary, BtnSize.Lg, Icon.Play, bw)) OnFramework(runner.Resume);
        }
        else if (Kit.Button("##pause", at, "Pause", Btn.Glass, BtnSize.Lg, Icon.Pause, bw))
        {
            OnFramework(() => runner.Pause("Paused by you."));
        }
        if (Kit.Button("##stop", new Vector2(at.X + w - sw, at.Y), stop, Btn.Danger, BtnSize.Lg, Icon.Square))
            OnFramework(() => runner.Stop("Stopped by you."));
        Kit.Reserve(at, w, S(46));
        Kit.Gap(12);
        var runs = paused
            ? new[] { Run.Note("Resume carries on from the step it held at. "), Run.Note("/hstudio resume", Palette.Dim), Run.Note(" works too.") }
            // The mockup says "Pause finishes the piece in hand first"; the runner holds before its next
            // step instead, so the words say that. TODO(runner): pause between pieces, then use the mockup's line.
            : new[] { Run.Note("Pause holds before its next step. "), Run.Note("/hstudio pause", Palette.Dim), Run.Note(" works too.") };
        Txt.Para(runs, 19.5f, center: true);
        Frame.EndFooter();
    }
}
