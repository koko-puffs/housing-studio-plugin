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
/// Looking after a home, on Start, Ready and Pieces: how full it is and whether a design fits
/// (<see cref="ItemLimits"/>), the pieces the game would likely refuse (<see cref="Clashes"/>),
/// carrying on a run that was cut off (<see cref="RunProgress"/>), and Check for changes since the
/// last run, with Put them back where they were (<see cref="Care"/>).
/// </summary>
public sealed partial class MainWindow
{
    private Care? care;

    /// <summary>Hands the window what looks after the home (made after the window, with the runner).</summary>
    internal void Use(Care c) => care = c;

    private void CareNotice()
    {
        if (care?.TakeNotice() is { } n) Notify(n);
    }

    // ---------------------------------------------------------------------------------------------
    // Start: how full the home is, and Check for changes.

    private void DrawHomeCare()
    {
        if (care == null) return;
        CareNotice();
        var ctx = session.Context;
        if (ctx.Side == null || !ctx.HasPermission) return;
        care.WantCount();
        if (care.Count is { } count && count.Home == care.CurrentHome)
        {
            Kit.Gap(10);
            Txt.Note(CareWords.HowFull(count.Placed, ItemLimits.Of(count.Side, ctx.HouseSize), count.Side), center: true);
        }
        DrawDrift();
    }

    // ---------------------------------------------------------------------------------------------
    // Ready: a run to carry on, whether it fits, what the game would refuse.

    private void DrawReadyCare(List<PlanEntry> entries)
    {
        CareNotice();
        var plan = session.Plan;
        var ctx = session.Context;

        if (care != null && plan != null && care.Offer(session.FilePath, session.PlanSide) is { } progress)
        {
            DrawCarryOn(progress, plan);
            Kit.Gap(12);
        }

        if (plan != null && session.PlanSide is { } side && session.Layout is { } layout)
        {
            var places = entries.Count(e => e.State is PieceState.FromStoreroom or PieceState.FromInventory);
            var check = ItemLimits.Check(side, ctx.HouseSize, layout.Pieces(side).Count(), session.Live.Count, plan.Extras.Count, places);
            if (check is { } c && CareWords.Limit(c, side, ctx.HouseSize) is { } limitWords)
            {
                AlertNote(limitWords, c.Over > 0 ? Palette.Danger : Palette.Dim, c.Over > 0 ? Icon.Alert : Icon.Info, Palette.Text);
                Kit.Gap(10);
            }
        }

        ClashesNow();
        var words = clashCache.Words;
        if (words.WrongSide != null)
        {
            AlertNote(words.WrongSide, Palette.Danger, textColor: Palette.Text);
            Kit.Gap(10);
        }
        if (words.Rest > 0)
        {
            var lead = CareWords.ClashLead(words.Rest, config.PlaceAnywhere, false);
            if (config.PlaceAnywhere) AlertNote($"{lead} Pieces lists them.", Palette.Dim, Icon.Info);
            else AlertNote($"{lead} {words.RestList}. Pieces lists them with Show where.", Palette.Danger, textColor: Palette.Text);
            Kit.Gap(10);
        }
    }

    /// <summary>The clashes in words, made once per clash list: the other side's line, and the rest's count and list.</summary>
    private (string? WrongSide, int Rest, string RestList, int All) ClashWords(List<Clash> clashes)
    {
        var wrongSide = clashes.Where(c => c.Kind == ClashKind.WrongSide).ToList();
        var rest = clashes.Where(c => c.Kind != ClashKind.WrongSide).ToList();
        var side = wrongSide.Count > 0 ? $"{CareWords.ClashLead(Clashes.PieceCount(wrongSide), false, true)} {ClashList(wrongSide)}." : null;
        return (side, Clashes.PieceCount(rest), ClashList(rest), Clashes.PieceCount(clashes));
    }

    /// <summary>"Oak Table ×2 sit on others of the same at the same spot; Bed is below the floor".</summary>
    private string ClashList(IEnumerable<Clash> clashes) =>
        string.Join("; ", clashes.GroupBy(c => c.Kind).Select(g =>
        {
            var pieces = g.SelectMany(c => c.Pieces).ToList();
            return $"{CareWords.Names(pieces.Select(t => t.ItemId), data.ItemName)} {CareWords.Reason(g.Key, pieces.Count)}";
        }));

    private (object? Of, Side Side, string? Size, string? District, List<Clash> List, (string? WrongSide, int Rest, string RestList, int All) Words) clashCache =
        (null, Side.Interior, null, null, new List<Clash>(), (null, 0, string.Empty, 0));

    /// <summary>The design's clashes for the side shown, worked out again only when the plan, the side or the home changes.</summary>
    private List<Clash> ClashesNow()
    {
        if (session.Layout == null)
        {
            if (clashCache.Of != null) clashCache = (null, Side.Interior, null, null, new List<Clash>(), (null, 0, string.Empty, 0));
            return clashCache.List;
        }
        var side = ReadySide;
        var ctx = session.Context;
        // Inside, the heights and walls are the home's you're in, else the design's own size.
        var size = side == Side.Interior ? ctx.Side == Side.Interior ? ctx.HouseSize : session.Layout.HouseSize : ctx.HouseSize;
        var district = side == Side.Interior && ctx.Side == Side.Interior ? ctx.District : null;
        object of = (object?)session.Plan ?? session.Layout;
        if (!ReferenceEquals(clashCache.Of, of) || clashCache.Side != side || clashCache.Size != size || clashCache.District != district)
        {
            var targets = session.Plan is { } plan && session.PlanSide == side ? plan.Entries.Select(e => e.Target).ToList() : session.Targets(side, toList: true);
            var found = Clashes.Find(targets, side, size, district, item => data.PlaceableOn(item, side));
            clashCache = (of, side, size, district, found, ClashWords(found));
        }
        return clashCache.List;
    }

    private void DrawCarryOn(RunProgress progress, Plan plan)
    {
        var when = When.Relative(progress.SavedUtc.ToLocalTime(), DateTime.Now, Clock);
        Kit.BeginPanel("##carry-on", 16, 20, 16, Palette.BrassWash, Palette.BrassEdge);
        Txt.Para(new[] { Run.Note(CareWords.CutOff(progress, progress.SavedUtc == DateTime.MinValue ? null : when), Palette.Text) });
        Kit.Gap(12);
        var at = Kit.At;
        var label = CareWords.CarryOn(progress);
        var lw = Kit.ButtonWidth(label, BtnSize.Sm, Icon.Play, Btn.Primary);
        var blocked = runner.CanStart(false);
        if (Kit.Button("##carry-on-btn", at, label, Btn.Primary, BtnSize.Sm, Icon.Play, 0, blocked != null, blocked))
        {
            var again = progress.EntriesOf(plan.Entries);
            var summary = StartSummary.Of(again, config.DyeAfterPlacing);
            if (again.Count == 0 || summary.Moves.Count + summary.Places + summary.Dyes == 0)
            {
                Notify(again.Count == 0 ? "None of that run's pieces are in this design now" : "Every piece of that run is done");
                OnFramework(care!.Forget);
            }
            else
            {
                care!.CarryingOn(progress);
                StartRun(false, again);
            }
        }
        if (Kit.Button("##carry-on-forget", new Vector2(at.X + lw + S(8), at.Y), "Forget it", Btn.Quiet, BtnSize.Sm, null, 0, false,
                "Forgets the run that was cut off. Start placing still does what's left."))
            OnFramework(care!.Forget);
        Kit.Reserve(at, Kit.Width, Kit.ButtonHeight(BtnSize.Sm));
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // Check for changes since the last run, and Put them back where they were.

    private bool driftChecking;
    private (DriftCheck? Check, List<string> Lines) driftLines = (null, new List<string>());

    private void DrawDrift()
    {
        if (care == null || runner.Active) return;
        var ctx = session.Context;
        var home = care.CurrentHome;
        if (home == null || !ctx.HasPermission) return;
        var kept = care.SnapshotAt(home);
        var check = care.LastCheck is { } c && c.Home == home ? c : null;
        if (kept == null && check == null) return;

        Kit.Gap(18);
        Kit.BeginPanel("##drift", 16, 20, 16);
        var at = Kit.At;
        var w = Kit.Width;
        const string label = "Check for changes";
        var bw = Kit.ButtonWidth(label, BtnSize.Sm, Icon.Search);
        var x = at.X + S(16) + S(14);
        var tw = at.X + w - bw - S(14) - x;
        var titleH = Txt.Natural(Face.SansMedium, 14);
        var noteText = kept is { } k
            ? $"A snapshot of what was placed was kept after your last run here ({When.Relative(k, DateTime.Now, Clock)})."
            : "The snapshot of your last run here is gone.";
        var note = new[] { Run.Note(noteText) };
        var noteH = Txt.Flow(Vector2.Zero, tw, note, 19.5f, out _, draw: false);
        var wordsH = titleH + S(2) + noteH;
        var h = MathF.Max(S(34), wordsH);
        var mid = at.Y + h / 2;
        var dl = Kit.Dl;
        Icons.Draw(dl, Icon.Refresh, new Vector2(at.X, mid - S(8)), S(16), Palette.Dim);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(x, top), "Since your last run", Face.SansMedium, 14, Palette.Ivory);
        Txt.Flow(new Vector2(x, top + titleH + S(2)), tw, note, 19.5f, out _);
        if (Kit.Button("##drift-check", new Vector2(at.X + w - bw, mid - S(17)), label, Btn.Glass, BtnSize.Sm, Icon.Search, 0, kept == null || driftChecking,
                "Compares what's placed now with the snapshot: what moved, what was added, what was taken away. Nothing is touched."))
        {
            driftChecking = true;
            OnFramework(() =>
            {
                try
                {
                    if (care.CheckForChanges() is { } why) Notify(why);
                }
                finally
                {
                    driftChecking = false;
                }
            });
        }
        Kit.Reserve(at, w, h);

        if (check != null) DrawDriftResult(check);
        Kit.EndPanel();
    }

    private void DrawDriftResult(DriftCheck check)
    {
        var r = check.Report;
        Kit.Gap(12);
        var at = Kit.At;
        Kit.Divider(Kit.Panel.Start.X, Kit.Panel.Start.X + Kit.Panel.Width, at.Y - S(6));
        if (!ReferenceEquals(driftLines.Check, check))
        {
            // Made once per check, not every frame.
            var made = new List<string> { $"{CareWords.Drift(r)} (checked {Clock(check.CheckedAt)})" };
            if (r.Moved.Count > 0) made.Add($"Moved: {CareWords.Names(r.Moved.Select(m => m.Was.ItemId), data.ItemName)}");
            if (r.Gone.Count > 0) made.Add($"Taken away: {CareWords.Names(r.Gone.Select(t => t.ItemId), data.ItemName)}");
            if (r.Redyed.Count > 0) made.Add($"In another dye: {CareWords.Names(r.Redyed.Select(m => m.Was.ItemId), data.ItemName)}");
            if (r.Added.Count > 0) made.Add($"Added since: {CareWords.Names(r.Added.Select(p => p.ItemId), data.ItemName)} (these stay)");
            driftLines = (check, made);
        }
        Txt.Para(new[] { new Run(driftLines.Lines[0], Face.Sans, 14, r.Unchanged ? Palette.Success : Palette.Ivory) });
        foreach (var line in driftLines.Lines.Skip(1))
        {
            Kit.Gap(4);
            Txt.Note(line);
        }
        if (!r.CanPutBack) return;

        Kit.Gap(12);
        var row = Kit.At;
        var x = row.X;
        if (r.Moved.Count + r.Gone.Count > 0)
        {
            const string show = "Show where they were";
            if (Kit.Button("##drift-show", row, show, Btn.Glass, BtnSize.Sm, Icon.MapPin, 0, session.Context.Side != check.Side))
                ShowWhere(r.Moved.Select(m => m.Was).Concat(r.Gone));
            x += Kit.ButtonWidth(show, BtnSize.Sm, Icon.MapPin) + S(8);
        }
        if (r.CanPutBack)
        {
            const string put = "Put them back where they were";
            var allowed = config.Experimental && config.PutBackChanges;
            var cannot = runner.Active ? "Already running." : !allowed ? "Switch it on in Settings → Experimental → Put back what changed since a run." : null;
            if (Kit.Button("##drift-put", new Vector2(x, row.Y), put, Btn.Primary, BtnSize.Sm, Icon.Refresh, 0, cannot != null, cannot))
                AskPutBack(check);
        }
        Kit.Reserve(row, Kit.Width, Kit.ButtonHeight(BtnSize.Sm));
    }

    private void AskPutBack(DriftCheck check)
    {
        var r = check.Report;
        var what = new List<string>();
        if (r.Moved.Count > 0) what.Add($"moves the {Words.Count(r.Moved.Count, "piece")} moved since back");
        if (r.Gone.Count > 0) what.Add($"places the {Words.Count(r.Gone.Count, "piece")} taken away from your storeroom or bags");
        if (r.Redyed.Count > 0 && config.DyeAfterPlacing) what.Add($"dyes the {Words.Count(r.Redyed.Count, "piece")} in another dye as they were");
        var instead = session.Layout != null ? $", in place of {session.Title}" : string.Empty;
        var added = r.Added.Count > 0 ? $" The {Words.Count(r.Added.Count, "piece")} added since stay where they are." : string.Empty;
        Ask("Put them back?",
            $"This loads the snapshot kept after your last run as the design{instead}, and starts a run that {Words.List(what)}.{added}",
            "Put them back", "Keep it as it is", () =>
            {
                OnFramework(() =>
                {
                    if (care!.PutBack() is { } why)
                    {
                        Notify(why);
                        return;
                    }
                    vm.Dismiss();
                    sub = Sub.None;
                    Notify("Putting them back…");
                });
            });
    }

    // ---------------------------------------------------------------------------------------------
    // Pieces: the pieces the game may refuse, each with Show where.

    private bool clashesOpen;

    private void DrawPiecesClashes()
    {
        var clashes = ClashesNow();
        if (clashes.Count == 0) return;
        var count = clashCache.Words.All;
        var chip = Words.Count(count, "piece");
        var hard = !config.PlaceAnywhere || clashes.Any(c => c.Kind == ClashKind.WrongSide);
        Kit.BeginPanel("##clashes", 0, 0, 0);
        Disclosure("##clashes-row", ref clashesOpen, "Pieces the game may refuse",
            config.PlaceAnywhere ? "Place anywhere is on, so most are placed anyway" : "Place anywhere is off (Settings → More options)",
            14, Kit.ChipWidth(chip), S(24), (p, _) => Kit.Chip(p, chip), false, hard ? Palette.Danger : Palette.Dim);
        if (clashesOpen)
        {
            var i = 0;
            foreach (var c in clashes.OrderBy(c => c.Kind).ThenBy(c => data.ItemName(c.ItemId), StringComparer.OrdinalIgnoreCase))
            {
                var name = data.ItemName(c.ItemId) + (c.Pieces.Count > 1 ? $" ×{c.Pieces.Count}" : string.Empty);
                var pieces = c.Pieces;
                IssueRow(i++, new Issue(Icon.Alert, name, Words.Capital(CareWords.Reason(c.Kind, c.Pieces.Count)), "Show where", () => ShowWhere(pieces)));
            }
        }
        Kit.EndPanel();
        Kit.Gap(14);
    }
}
