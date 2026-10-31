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
/// Looking after a home, on Start, Ready and Pieces: how full it is and whether a design fits
/// (<see cref="ItemLimits"/>), the pieces the game would likely refuse (<see cref="Clashes"/>),
/// carrying on a run that was cut off (<see cref="RunProgress"/>), and, under Start's This house
/// card, the changes since the last run, with Put them back where they were (<see cref="Care"/>).
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
    // Ready: a run to carry on (a banner), and the check chips: whether it fits, what the game would
    // refuse, dyes short, what's left out. Each chip opens a short detail with its words and actions.

    /// <summary>What a chip's detail offers to do.</summary>
    [Flags]
    private enum CheckActions
    {
        None = 0,
        ShowClashes = 1,
        ShowWrongSide = 2,
        Pieces = 4,
        SeeDyes = 8,
        CopyDyes = 16,
    }

    private sealed record ReadyCheck(CheckChipSpec Chip, Run[] Detail, CheckActions Actions);

    /// <summary>Everything the chips are made from: they're made again only when one of these changes.</summary>
    private readonly record struct ChecksKey(
        Plan? Of, Layout? Design, Side? PlanSide, Side Side, Side? Here, string? Size, int Live, int Clearing, int Places, int Shown,
        object? Clashes, bool Anywhere, object? Dyes, bool Dyeing, bool Storeroom, bool NearMe, float Reach, bool B, bool G, bool U);

    private (ChecksKey Key, List<ReadyCheck>? Checks, CheckChipSpec[] Chips) readyChecks = (default, null, Array.Empty<CheckChipSpec>());

    /// <summary>The chip whose detail shows (one at a time; pressing it again closes it).</summary>
    private string? openCheck;

    private void DrawReadyChecks(StartSummary s, int shown)
    {
        var checks = ReadyChecks(s, shown);
        if (checks.Count == 0)
        {
            openCheck = null;
            return;
        }
        var pressed = Kit.CheckChips(readyChecks.Chips, openCheck);
        if (pressed >= 0) openCheck = openCheck == checks[pressed].Chip.Id ? null : checks[pressed].Chip.Id;
        ReadyCheck? open = null;
        foreach (var c in checks)
            if (c.Chip.Id == openCheck) open = c;
        if (open == null) openCheck = null;
        else
        {
            Kit.Gap(10);
            Kit.BeginDetail();
            Txt.Para(open.Detail);
            if (open.Actions != CheckActions.None)
            {
                Kit.Gap(8);
                CheckActionsRow(open.Actions);
            }
            Kit.EndDetail();
        }
        Kit.Gap(14);
    }

    private void CheckActionsRow(CheckActions actions)
    {
        var row = new Kit.Wrap(8);
        var h = Kit.ButtonHeight(BtnSize.Tiny);
        var cantShow = session.Context.Side == null ? "Go to your home to see where they go" : null;
        if ((actions & (CheckActions.ShowClashes | CheckActions.ShowWrongSide)) != 0)
        {
            const string show = "Show where";
            var wrongSide = (actions & CheckActions.ShowWrongSide) != 0;
            if (Kit.Button("##check-show", row.Next(Kit.ButtonWidth(show, BtnSize.Tiny, Icon.MapPin), h), show, Btn.Glass, BtnSize.Tiny, Icon.MapPin, 0,
                    cantShow != null, cantShow))
                ShowWhere(ClashesNow().Where(c => (c.Kind == ClashKind.WrongSide) == wrongSide).SelectMany(c => c.Pieces));
        }
        if ((actions & CheckActions.Pieces) != 0)
        {
            const string pieces = "Pieces";
            if (Kit.Button("##check-pieces", row.Next(Kit.ButtonWidth(pieces, BtnSize.Tiny, Icon.List), h), pieces, Btn.Glass, BtnSize.Tiny, Icon.List, 0, false,
                    "Pieces lists them under “Pieces the game may refuse”, each with Show where."))
                sub = Sub.Pieces;
        }
        if ((actions & CheckActions.SeeDyes) != 0)
        {
            const string see = "See which";
            if (Kit.Button("##check-see-dyes", row.Next(Kit.ButtonWidth(see, BtnSize.Tiny, Icon.Droplet), h), see, Btn.Glass, BtnSize.Tiny, Icon.Droplet))
                ImGui.OpenPopup("##dyes-missing");
        }
        if ((actions & CheckActions.CopyDyes) != 0)
        {
            const string copy = "Copy list";
            if (Kit.Button("##check-copy-dyes", row.Next(Kit.ButtonWidth(copy, BtnSize.Tiny, Icon.Clipboard), h), copy, Btn.Glass, BtnSize.Tiny, Icon.Clipboard))
            {
                ImGui.SetClipboardText(DyeList(vm.MissingDyes.Select(m => (m.Name, m.Count))));
                Notify("Dye list copied");
            }
        }
        row.End();
    }

    /// <summary>
    /// The chips Ready shows, made again only when what they're made from changes: the item limit as a
    /// meter (a chosen clear out's pieces don't count), the pieces the game may refuse, those that can't
    /// go on this side, the dyes short, the storeroom not read, Near me, the floors left out, and the
    /// pieces placed from the other side. None when there's nothing to say.
    /// </summary>
    private List<ReadyCheck> ReadyChecks(StartSummary s, int shown)
    {
        var plan = session.Plan;
        var ctx = session.Context;
        var layout = session.Layout!;
        // A chosen clear out (or swap) puts its pieces away before placing: they don't count against the limit.
        var clearing = ChosenClearOut()?.Count ?? 0;
        var clashes = ClashesNow();
        var dyes = vm.MissingDyes;
        var side = ReadySide;
        var key = new ChecksKey(plan, layout, session.PlanSide, side, ctx.Side, ctx.HouseSize, session.Live.Count, clearing, s.Places, shown,
            clashes, config.PlaceAnywhere, dyes, s.Dyes > 0, session.StoreroomLoaded, config.NearMe, config.NearMeRadius,
            config.ShowBasement, config.ShowGround, config.ShowUpper);
        if (readyChecks.Checks != null && readyChecks.Key == key) return readyChecks.Checks;

        var list = new List<ReadyCheck>();
        static Run[] Para(string text) => new[] { Run.Note(text, Palette.Text) };
        static Run[] Lead(string lead, string rest) => new[] { new Run(lead, Face.SansMedium, 13, Palette.Ivory), Run.Note(rest, Palette.Text) };

        if (plan != null && session.PlanSide is { } planSide)
        {
            var check = ItemLimits.Check(planSide, ctx.HouseSize, layout.Pieces(planSide).Count(), session.Live.Count - clearing,
                Math.Max(0, plan.Extras.Count - clearing), s.Places);
            if (check is { } c)
                list.Add(new ReadyCheck(new CheckChipSpec("##check-limit", $"{c.After} of {c.Limit}", c.Over > 0 ? ChipTone.Warn : ChipTone.Normal, null,
                        c.Limit > 0 ? (float)c.After / c.Limit : 0, "Pieces in the home once Start is done, of the most it holds"),
                    Para(CareWords.LimitDetail(c, planSide, ctx.HouseSize, clearing > 0)), CheckActions.None));
        }

        var words = clashCache.Words;
        if (words.Others > 0)
        {
            var lead = CareWords.ClashLead(words.Others, config.PlaceAnywhere, false);
            list.Add(new ReadyCheck(new CheckChipSpec("##check-refuse", $"{words.Others} may be refused", config.PlaceAnywhere ? ChipTone.Normal : ChipTone.Warn,
                Icon.AlertTriangle), Lead(lead, $" {words.OthersList}."), CheckActions.ShowClashes | CheckActions.Pieces));
        }
        if (words.WrongSide != null)
            list.Add(new ReadyCheck(new CheckChipSpec("##check-side", $"{words.All - words.Others} can't go here", ChipTone.Warn, Icon.AlertTriangle),
                Para(words.WrongSide), CheckActions.ShowWrongSide));

        if (s.Dyes > 0 && dyes.Count > 0)
        {
            var count = dyes.Sum(d => d.Count);
            var names = Words.Some(dyes.Select(d => $"{d.Name} ×{d.Count}").ToList(), 3);
            var one = dyes.Count == 1 && count == 1;
            list.Add(new ReadyCheck(new CheckChipSpec("##check-dyes", ReadyWords.DyesShort(count), ChipTone.Normal, Icon.Flask),
                Lead(names, $" {(one ? "isn't" : "aren't")} in your bags. {(one ? "It's" : "They're")} on the list to get; until then, those pieces stay undyed."),
                CheckActions.SeeDyes | CheckActions.CopyDyes));
        }

        if (plan != null && side == Side.Interior && !session.StoreroomLoaded)
            list.Add(new ReadyCheck(new CheckChipSpec("##check-storeroom", "Storeroom not read", ChipTone.Normal, Icon.Info),
                Para("The storeroom isn't read yet: open Housing → Indoor Furnishings and its Storeroom tab once, and its pieces count too."), CheckActions.None));
        if (plan != null && config.NearMe)
            list.Add(new ReadyCheck(new CheckChipSpec("##check-near", $"Near me · {Words.Count(shown, "piece")}", ChipTone.Brass, Icon.MapPin),
                Para($"Near me is on: only the {Words.Count(shown, "piece")} within {config.NearMeRadius:0} yalms of you are worked on (Settings → More options)."),
                CheckActions.None));
        if (plan != null && side == Side.Interior)
        {
            var off = new List<string>();
            if (!config.ShowBasement) off.Add("basement");
            if (!config.ShowGround) off.Add("ground floor");
            if (!config.ShowUpper) off.Add("upper floor");
            if (off.Count > 0)
                list.Add(new ReadyCheck(new CheckChipSpec("##check-floors", ReadyWords.FloorsLeftOut(off), ChipTone.Normal, Icon.Shelf),
                    Para($"The {Words.List(off)} {(off.Count == 1 ? "is" : "are")} left out (Settings → More options)."), CheckActions.None));
        }

        if (ctx.Side == Side.Interior && layout.Garden.Count > 0)
            list.Add(new ReadyCheck(new CheckChipSpec("##check-other", $"{layout.Garden.Count} in the garden", ChipTone.Normal, Icon.House),
                Para($"Its {Words.Count(layout.Garden.Count, "garden piece")} {(layout.Garden.Count == 1 ? "is" : "are")} placed from your plot."), CheckActions.None));
        else if (ctx.Side == Side.Garden && layout.Interior.Count > 0)
            list.Add(new ReadyCheck(new CheckChipSpec("##check-other", $"{layout.Interior.Count} inside", ChipTone.Normal, Icon.House),
                Para($"Its {Words.Count(layout.Interior.Count, "piece")} inside {(layout.Interior.Count == 1 ? "is" : "are")} placed from inside the house."),
                CheckActions.None));

        readyChecks = (key, list, list.Select(c => c.Chip).ToArray());
        return list;
    }

    /// <summary>The clashes in words, made once per clash list: the other side's line, and the rest's count and list.</summary>
    private (string? WrongSide, int Others, string OthersList, int All) ClashWords(List<Clash> clashes)
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

    private (object? Of, Side Side, string? Size, string? District, List<Clash> List, (string? WrongSide, int Others, string OthersList, int All) Words) clashCache =
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

    private (RunProgress? Of, DateTime Day, string Text, string? When, string Tip) carryOnWords = (null, DateTime.MinValue, string.Empty, null, string.Empty);

    /// <summary>Ready's banner when a run of this design was cut off: Carry on (from where it stopped) or Forget.</summary>
    private void DrawCarryOnBanner()
    {
        var plan = session.Plan;
        if (care == null || plan == null || care.Offer(session.FilePath, session.PlanSide) is not { } progress) return;
        if (!ReferenceEquals(carryOnWords.Of, progress) || carryOnWords.Day != DateTime.Today)
        {
            var when = progress.SavedUtc == DateTime.MinValue ? null : When.Relative(progress.SavedUtc.ToLocalTime(), DateTime.Now, Clock);
            carryOnWords = (progress, DateTime.Today, CareWords.CutOffShort(progress), when != null ? $"({when})" : null, CareWords.CutOff(progress, when));
        }
        var w = carryOnWords;
        var blocked = runner.CanStart(false);
        var pressed = Kit.Banner("##carry-on", Icon.Refresh, w.Text, w.When, "Carry on", "Forget", blocked != null, blocked ?? w.Tip,
            "Forgets the run that was cut off. Start placing still does what's left.");
        if (pressed == 1) CarryOn(progress, plan);
        else if (pressed == 2) OnFramework(care!.Forget);
        Kit.Gap(14);
    }

    private void CarryOn(RunProgress progress, Plan plan)
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
            StartRun(false, again, ArmFixturesAfterPlacing);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Check for changes since the last run, and Put them back where they were.

    private bool driftChecking;
    private (DateTime? Kept, DateTime Day, string Tip) changesTip = (null, DateTime.MinValue, string.Empty);
    private (DriftCheck? Check, List<string> Lines) driftLines = (null, new List<string>());

    /// <summary>The changes chip's tooltip: when the snapshot was kept (made again only when it or the day changes).</summary>
    private string ChangesTip(DateTime kept)
    {
        if (changesTip.Kept != kept || changesTip.Day != DateTime.Today)
            changesTip = (kept, DateTime.Today,
                $"Since your last run here ({When.Relative(kept, DateTime.Now, Clock)}): compares what's placed now with the snapshot kept then. Nothing is touched.");
        return changesTip.Tip;
    }

    /// <summary>Check for changes (on the framework thread): what's placed now against the snapshot of your last run here.</summary>
    private void CheckForChanges()
    {
        if (care == null || driftChecking) return;
        driftChecking = true;
        OnFramework(() =>
        {
            try
            {
                if (care.CheckForChanges() is { } why)
                {
                    Notify(why);
                    changesOpen = false;
                }
            }
            finally
            {
                driftChecking = false;
            }
        });
    }

    /// <summary>
    /// Start, under This house: the changes since your last run (in a dashed detail), what moved, was
    /// taken away, dyed again or added, with Show where they were and Put them back where they were.
    /// </summary>
    private void DrawChanges(DriftCheck? check)
    {
        Kit.Gap(10);
        Kit.BeginDetail();
        if (check == null || driftChecking)
        {
            Txt.Note("Checking what's placed against your last run…");
            Kit.EndDetail();
            return;
        }
        var r = check.Report;
        if (!ReferenceEquals(driftLines.Check, check))
        {
            // Made once per check, not every frame.
            var made = new List<string> { $"{CareWords.Drift(r)} since your last run (checked {Clock(check.CheckedAt)})" };
            if (r.Moved.Count > 0) made.Add($"Moved: {CareWords.Names(r.Moved.Select(m => m.Was.ItemId), data.ItemName)}");
            if (r.Gone.Count > 0) made.Add($"Taken away: {CareWords.Names(r.Gone.Select(t => t.ItemId), data.ItemName)}");
            if (r.Redyed.Count > 0) made.Add($"In another dye: {CareWords.Names(r.Redyed.Select(m => m.Was.ItemId), data.ItemName)}");
            if (r.Added.Count > 0) made.Add($"Added since: {CareWords.Names(r.Added.Select(p => p.ItemId), data.ItemName)} (these stay)");
            driftLines = (check, made);
        }
        Txt.Para(new[] { new Run(driftLines.Lines[0], Face.SansMedium, 13, r.Unchanged ? Palette.Success : Palette.Ivory) });
        for (var i = 1; i < driftLines.Lines.Count; i++)
        {
            Kit.Gap(2);
            Txt.Note(driftLines.Lines[i]);
        }
        if (r.CanPutBack)
        {
            Kit.Gap(10);
            var row = new Kit.Wrap(8);
            var h = Kit.ButtonHeight(BtnSize.Tiny);
            if (r.Moved.Count + r.Gone.Count > 0)
            {
                const string show = "Show where they were";
                var elsewhere = session.Context.Side != check.Side;
                if (Kit.Button("##drift-show", row.Next(Kit.ButtonWidth(show, BtnSize.Tiny, Icon.MapPin), h), show, Btn.Glass, BtnSize.Tiny, Icon.MapPin, 0,
                        elsewhere, elsewhere ? "Go back to the side of your home it checked" : null))
                    ShowWhere(r.Moved.Select(m => m.Was).Concat(r.Gone));
            }
            const string put = "Put them back where they were";
            var allowed = config.Experimental && config.PutBackChanges;
            var cannot = runner.Active ? "Already running." : !allowed ? "Switch it on in Settings → Beta → Put back what changed." : null;
            if (Kit.Button("##drift-put", row.Next(Kit.ButtonWidth(put, BtnSize.Tiny, Icon.Undo), h), put, Btn.Glass, BtnSize.Tiny, Icon.Undo, 0, cannot != null,
                    cannot ?? "Moves the pieces moved since back, and places the ones taken away (asks first)."))
                AskPutBack(check);
            row.End();
        }
        Kit.EndDetail();
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
                IssueRow(i++, new Issue(Icon.Alert, name, Words.Capital(CareWords.Reason(c.Kind, c.Pieces.Count)), "Show where", () => ShowWhere(pieces), null, Icon.MapPin, true));
            }
        }
        Kit.EndPanel();
        Kit.Gap(14);
    }
}
