using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// "Get everything for me" (Settings → Experimental): the estimate before anything starts (what you have,
/// the trips with what each vendor sells and costs, the market's estimate under your ceiling, what can't
/// be got, the plugins it needs), then the run (the trip, what it's doing, the gil spent, what's got,
/// Pause and Stop), then a summary. Reached from Shopping's foot and Ready.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] HomeLabels = { "My house", "Company house", "Apartment" };

    private bool errandsOpen;
    private bool errandsLeftOpen = true;

    /// <summary>The estimate, worked out again only when what it's built from changes (or once every 2 seconds, for your bags).</summary>
    private (ShoppingPlan? Shop, HousingContext? Ctx, long Ceiling, bool Worlds, int Home, int Spare, DateTime Until, ErrandPlan Plan, HomeRef? Here) errandEstimate =
        (null, null, 0, false, 0, 0, DateTime.MinValue, ErrandPlan.Empty, null);

    private ErrandRunner Errands => Plugin.Errands;

    /// <summary>The errands' screen is what the window shows: they're running, their summary waits, or you opened their estimate.</summary>
    private bool ErrandsShown()
    {
        if (Errands.Active || Errands.ResultWaiting) return true;
        if (!errandsOpen) return false;
        if (!Errands.Enabled || session.Layout == null || runner.Active || Plugin.FixtureRun.Active)
        {
            errandsOpen = false;
            return false;
        }
        return true;
    }

    private (ErrandPlan Plan, HomeRef? Here) ErrandEstimate()
    {
        var shop = ShopPlan();
        var ctx = session.Context;
        var e = errandEstimate;
        if (ReferenceEquals(e.Shop, shop) && Equals(e.Ctx, ctx) && e.Ceiling == config.ErrandsMarketCeiling && e.Worlds == config.ErrandsOtherWorlds
            && e.Home == config.ErrandsHome && e.Spare == config.ErrandsSpareSlots && DateTime.UtcNow < e.Until)
            return (e.Plan, e.Here);
        HomeRef? here = null;
        var plan = ErrandPlan.Empty;
        try
        {
            here = ErrandGame.HomeHere(ctx);
            plan = Errands.Estimate(shop, here);
        }
        catch (Exception ex)
        {
            // Drawn every frame: a failure keeps the last estimate, logged once.
            if (!estimateFailed) Services.Log.Warning(ex, "Couldn't work out the errands");
            estimateFailed = true;
            plan = e.Plan;
            here = e.Here;
        }
        errandEstimate = (shop, ctx, config.ErrandsMarketCeiling, config.ErrandsOtherWorlds, config.ErrandsHome, config.ErrandsSpareSlots,
            DateTime.UtcNow + TimeSpan.FromSeconds(2), plan, here);
        return (plan, here);
    }

    private bool estimateFailed;

    private (ErrandPlan? Plan, DateTime Until, string? Why) errandsBlocked;

    /// <summary>Why the errands can't start (asked a few times a second, not every frame: it looks around you).</summary>
    private string? ErrandsBlocked(ErrandPlan plan)
    {
        if (ReferenceEquals(errandsBlocked.Plan, plan) && DateTime.UtcNow < errandsBlocked.Until) return errandsBlocked.Why;
        errandsBlocked = (plan, DateTime.UtcNow + TimeSpan.FromSeconds(0.5), Errands.CanStart(plan, session.Context));
        return errandsBlocked.Why;
    }

    // ---------------------------------------------------------------------------------------------
    // The way in: Shopping's foot and Ready.

    private const string ErrandsLabel = "Get everything for me";

    private bool ErrandsOffered(ShoppingPlan shop) => Errands.Enabled && session.Plan != null && !shop.IsEmpty;

    private static float ErrandsButtonWidth => Kit.ButtonWidth(ErrandsLabel, BtnSize.Md, Icon.Package);

    private void ErrandsButton(Vector2 pos)
    {
        if (Kit.Button("##errands", pos, ErrandsLabel, Btn.Glass, BtnSize.Md, Icon.Package, 0, false,
                "Experimental: fetches what's missing from your retainers, vendors and the market board, and places it. You see the gil first."))
            errandsOpen = true;
    }

    /// <summary>Ready's row, under what Start does (when the errands are switched on and something's missing).</summary>
    private void DrawErrandsEntry()
    {
        var shop = ShopPlan();
        if (!ErrandsOffered(shop)) return;
        Kit.Gap(12);
        var at = Kit.At;
        var w = Kit.Width;
        var bw = ErrandsButtonWidth;
        var note = $"Experimental: fetch the {Words.Count(shop.Lines.Sum(l => l.Count), "thing")} missing and place them.";
        var nw = w - bw - S(12);
        Txt.Line(Kit.Dl, new Vector2(at.X, at.Y + (S(40) - S(19.5f)) / 2), Txt.Fit(note, Face.Sans, 13, nw), Face.Sans, 13, Palette.Ash, 19.5f);
        ErrandsButton(new Vector2(at.X + w - bw, at.Y));
        Kit.Reserve(at, w, S(40));
    }

    // ---------------------------------------------------------------------------------------------

    private void DrawErrands()
    {
        if (Errands.Active) DrawErrandsRunning();
        else if (Errands.ResultWaiting) DrawErrandsSummary();
        else DrawErrandsEstimate();
    }

    private void ErrandsHeader(string eyebrow, string title, bool back, string? pill = null, Tone tone = Tone.Neutral)
    {
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var eyebrowH = Txt.Natural(Face.Mono, 12);
        var titleH = S(26 * 1.1f);
        var headH = MathF.Max(S(40), eyebrowH + S(2) + titleH);
        var tx = at.X;
        if (back)
        {
            if (BackButton(new Vector2(at.X, at.Y + (headH - S(40)) / 2), "Back")) errandsOpen = false;
            tx += S(40) + S(10);
        }
        var top = at.Y + (headH - (eyebrowH + S(2) + titleH)) / 2;
        Kit.Eyebrow(new Vector2(tx, top), eyebrow, null, at.X + w - tx);
        Title(new Vector2(tx, top + eyebrowH + S(2)), title, null, at.X + w - tx, 26);
        var h = headH;
        if (pill != null)
        {
            Kit.StatusPill(new Vector2(at.X, at.Y + headH + S(10)), pill, tone, tone == Tone.Live, w);
            h += S(10) + S(28);
        }
        Kit.Reserve(at, w, h);
        Frame.EndHeader();
    }

    // ---------------------------------------------------------------------------------------------
    // The estimate.

    private void DrawErrandsEstimate()
    {
        var (plan, here) = ErrandEstimate();
        var title = plan.IsEmpty ? "Nothing to fetch" : $"{Words.Count(plan.Count, "thing")} in {Words.Count(plan.Trips.Count, "trip")}";
        ErrandsHeader($"Experimental · {session.Title}", title, true);

        Frame.BeginBody("##errands", "errands-foot", 16, 22);
        var blocked = ErrandsBlocked(plan);
        if (blocked != null)
        {
            AlertNote(blocked, Palette.Danger, textColor: Palette.Text);
            Kit.Gap(14);
        }
        if (Plugin.Shop.Status() is { } status)
        {
            AlertNote(status, Palette.Dim, Icon.Info);
            Kit.Gap(14);
        }

        Kit.EyebrowRow("Plugins it uses");
        Kit.Gap(10);
        Kit.BeginPanel("##errand-plugins", 0, 0, 0);
        PluginRow(0, "vnavmesh", "Walks you to each NPC, bell and board", Errands.Nav.Installed, true);
        PluginRow(1, "Lifestream", "Teleports, goes home, visits other worlds", Errands.Travel.Installed, true);
        PluginRow(2, "AutoRetainer", "Kept out of the way while a bell is used (if you have it)", Plugins.Loaded("AutoRetainer"), false);
        Kit.EndPanel();

        Kit.Gap(18);
        Kit.EyebrowRow("You already have");
        Kit.Gap(10);
        Kit.BeginPanel("##errand-have", 14, 20, 14);
        Txt.Para(new[] { Run.Note(HaveWords(plan), Palette.Text) });
        Kit.EndPanel();

        for (var t = 0; t < plan.Trips.Count; t++)
        {
            var trip = plan.Trips[t];
            Kit.Gap(18);
            Kit.EyebrowRow(ErrandWords.Trip(trip));
            Kit.Gap(10);
            Kit.BeginPanel($"##errand-trip{t}", 14, 20, 14);
            foreach (var stop in trip.Stops)
            {
                Txt.Para(new[] { new Run(ErrandWords.Where(stop), Face.SansMedium, 14, Palette.Ivory) }, 21);
                Txt.Para(new[] { Run.Note(ErrandWords.What(stop, data.AnyItemName)) });
                Kit.Gap(8);
            }
            Txt.Para(new[] { Run.Note("Then home, to place what it got.", Palette.Dim) });
            Kit.EndPanel();
        }

        var left = ErrandWords.Left(plan.Left, data.AnyItemName);
        if (left.Count > 0)
        {
            Kit.Gap(18);
            Kit.BeginPanel("##errand-left", 0, 0, 0);
            Disclosure("##errand-left-row", ref errandsLeftOpen, "Not got this way", $"{Words.Count(plan.Left.Sum(l => l.Count), "thing")}: the Shopping list says where they come from",
                16, 0, 0, null, false, Palette.Dim);
            if (errandsLeftOpen)
            {
                Kit.PushBox(Kit.Left + S(20), Kit.Width - S(40));
                foreach (var (why, what) in left)
                {
                    Txt.Para(new[] { new Run(Words.Capital(why), Face.SansMedium, 13, Palette.Text) }, 19.5f);
                    Txt.Para(new[] { Run.Note(what) });
                    Kit.Gap(8);
                }
                Kit.Gap(8);
                Kit.PopBox();
            }
            Kit.EndPanel();
        }

        Kit.Gap(18);
        Kit.EyebrowRow("How it goes");
        Kit.Gap(10);
        Kit.BeginPanel("##errand-settings", 0, 0, 0);
        var ceilingNote = plan.MarketGil > 0 ? $"About {ErrandWords.Gil(plan.MarketGil)} planned; never more than this in all" : "The most it may spend on the market board in all";
        var d = StepperRow(0, "##errand-ceiling", "Market board ceiling", ceilingNote, ErrandWords.Gil(config.ErrandsMarketCeiling),
            config.ErrandsMarketCeiling > 0, config.ErrandsMarketCeiling < ErrandWords.Ceilings[^1]);
        if (d != 0)
        {
            config.ErrandsMarketCeiling = ErrandWords.StepCeiling(config.ErrandsMarketCeiling, d);
            config.Save();
        }
        if (SwitchRow(1, "##errand-worlds", "Buy on other worlds", "The other worlds of your data centre (Lifestream visits them). Off: your home world only.",
                config.ErrandsOtherWorlds))
        {
            config.ErrandsOtherWorlds = !config.ErrandsOtherWorlds;
            config.Save();
        }
        var homeNow = config.ErrandsHome is >= 0 and <= 2 ? config.ErrandsHome : here != null ? (int)here.Kind : 0;
        SetRow(2, "Home is", here is { Owned: true } && config.ErrandsHome < 0 ? "As the game says for this home" : "Where \"go home\" takes you (Lifestream)",
            Kit.SegmentedWidth(HomeLabels), S(40), p =>
            {
                var picked = Kit.Segmented("##errand-home", p, HomeLabels, homeNow);
                if (picked != homeNow)
                {
                    config.ErrandsHome = picked;
                    config.Save();
                }
            }, below: true);
        var spare = config.ErrandsSpareSlots;
        d = StepperRow(3, "##errand-spare", "Bag slots to keep free", "Each trip leaves these empty", spare.ToString(), spare > 0, spare < 20);
        if (d != 0)
        {
            config.ErrandsSpareSlots = Math.Clamp(spare + d, 0, 20);
            config.Save();
        }
        if (SwitchRow(4, "##errand-trust", "Trust the new clicks",
                "Off: each click not yet seen working (buying, a retainer's menu) waits for Continue, so you can watch it. On once you have.",
                config.ErrandsTrustGuesses))
        {
            config.ErrandsTrustGuesses = !config.ErrandsTrustGuesses;
            config.Save();
        }
        Kit.EndPanel();

        Frame.BeginBottom("errands-bottom");
        AlertNote("On the market board you search for each item and open its listings; it buys only at or under the planned price. "
                  + "It asks you to click through NPCs' words, and to go in if it can't find your door.", Palette.Dim, Icon.Info);
        Frame.EndBottom();
        Frame.EndBody();

        Frame.BeginFooter("errands-foot", 18, 22);
        var at = Kit.At;
        var w = Kit.Width;
        var label = plan.TotalGil > 0 ? $"Send them out · {ErrandWords.Gil(plan.TotalGil)}" : "Send them out";
        if (Kit.Button("##errands-start", at, label, Btn.Primary, BtnSize.Lg, Icon.Play, w, blocked != null, blocked))
        {
            var chosen = plan;
            Ask("Send the errands out?", ErrandWords.Confirm(chosen), plan.TotalGil > 0 ? $"Spend up to {ErrandWords.Gil(chosen.TotalGil)}" : "Send them out", "Not yet",
                () => OnFramework(() =>
                {
                    Errands.Start(chosen, session.Context);
                    if (!Errands.Active && Errands.StopReason != null) Notify(Errands.StopReason);
                }));
        }
        Kit.Reserve(at, w, S(46));
        Kit.Gap(12);
        Txt.Para(new[] { Run.Note(plan.IsEmpty ? "Nothing the errands can get." : $"{ErrandWords.Gil(plan.VendorGil)} at vendors, up to {ErrandWords.Gil(plan.MarketGil)} on the market board · you have {ErrandWords.Gil(ErrandGame.Gil())}") },
            19.5f, center: true);
        Frame.EndFooter();
    }

    private void PluginRow(int index, string name, string note, bool installed, bool needed)
    {
        var (text, tone) = installed ? ("Installed", Tone.Success) : needed ? ("Needed", Tone.Danger) : ("Not installed", Tone.Neutral);
        SetRow(index, name, note, Kit.StatusPillWidth(text), S(28), p => Kit.StatusPill(p, text, tone));
    }

    /// <summary>"12 placed, 4 in your storeroom, 2 in your bags; 3 on your retainers (fetched first)."</summary>
    private string HaveWords(ErrandPlan plan)
    {
        var p = session.Plan;
        if (p == null) return "Go to your home to see what you have.";
        var placed = p.Entries.Count(e => e.State is PieceState.InPlace or PieceState.Move);
        var storeroom = p.Entries.Count(e => e.State == PieceState.FromStoreroom);
        var bags = p.Entries.Count(e => e.State == PieceState.FromInventory);
        var onRetainers = plan.Stops(StopKind.Retainer).Sum(s => s.Count);
        var words = $"{placed} placed, {storeroom} in your storeroom, {bags} in your bags";
        return onRetainers > 0 ? $"{words}; {onRetainers} on your retainers (fetched first)." : words + ".";
    }

    // ---------------------------------------------------------------------------------------------
    // Running.

    private void DrawErrandsRunning()
    {
        var e = Errands;
        var placing = e.Placing;
        var waiting = e.AwaitingContinue || e.WaitingForYou || (placing && (runner.AwaitingContinue || runner.WaitingForYou));
        var paused = e.State == RunState.Paused || (placing && runner.State == RunState.Paused);
        var (pill, tone) = waiting ? ("Waiting for you", Tone.Waiting) : paused ? ("Paused", Tone.Neutral) : placing ? ("Placing what it got", Tone.Live) : ("On errands", Tone.Live);
        ErrandsHeader($"Errands · {session.Title}", $"Trip {e.Trip} of {Math.Max(e.Trip, e.TripsAtStart)}", false, pill, tone);

        Frame.BeginBody("##errands-run", "errands-run-foot", 22, 22);
        var at = Kit.At;
        var w = Kit.Width;
        var figure = e.GotCount.ToString();
        var fw = Txt.Draw(Kit.Dl, at, figure, Face.Serif, 56, Palette.Ivory);
        Txt.Draw(Kit.Dl, new Vector2(at.X + fw + S(14), at.Y + Txt.Ascent(Face.Serif, 56) - Txt.Ascent(Face.Sans, 15)), $"got · {ErrandWords.Gil(e.Spent)} spent of up to {ErrandWords.Gil(e.AgreedGil)}",
            Face.Sans, 15, Palette.Ash);
        Kit.Reserve(at, w, Txt.Natural(Face.Serif, 56));
        Kit.Gap(10);
        Txt.Note($"{ErrandWords.Gil(e.VendorSpent)} at vendors · {ErrandWords.Gil(e.MarketSpent)} on the market board (ceiling {ErrandWords.Gil(e.Ceiling)})");

        Kit.Gap(18);
        Kit.BeginPanel("##errand-doing", 16, 20, 16);
        Kit.EyebrowRow(placing ? "Placing" : "Doing");
        Kit.Gap(6);
        var doing = placing ? $"Placing what's in your bags: {runner.Done} of {runner.Total} done" : e.Doing ?? e.StatusText ?? "Starting";
        Txt.Para(new[] { new Run(doing, Face.Sans, 15, Palette.Ivory) }, 15 * 1.4f);
        var step = placing ? runner.StepLabel : e.StepLabel;
        if (step != null)
        {
            Kit.Gap(4);
            Txt.Para(new[] { new Run(step, Face.Mono, 12, Palette.Dim) }, 12 * 1.6f);
        }
        Kit.EndPanel();

        if (waiting)
        {
            Kit.Gap(18);
            DrawErrandCallout(placing);
        }
        Kit.Gap(18);
        DrawLog();
        Frame.BeginBottom("errands-run-bottom");
        Txt.Note("Leave the keyboard and mouse be while it walks and buys. Stop works at any time.", center: true);
        Frame.EndBottom();
        Frame.EndBody();

        Frame.BeginFooter("errands-run-foot", 18, 22);
        var row = Kit.At;
        const string stop = "Stop";
        var sw = Kit.ButtonWidth(stop, BtnSize.Lg, Icon.Square, Btn.Danger);
        var bw = w - sw - S(10);
        if (paused)
        {
            if (Kit.Button("##errand-resume", row, "Resume", Btn.Primary, BtnSize.Lg, Icon.Play, bw)) OnFramework(e.Resume);
        }
        else if (Kit.Button("##errand-pause", row, "Pause", Btn.Glass, BtnSize.Lg, Icon.Pause, bw))
        {
            OnFramework(() => e.Pause("Paused by you."));
        }
        if (Kit.Button("##errand-stop", new Vector2(row.X + w - sw, row.Y), stop, Btn.Danger, BtnSize.Lg, Icon.Square))
            OnFramework(() => e.Stop("Stopped by you."));
        Kit.Reserve(row, w, S(46));
        Frame.EndFooter();
    }

    /// <summary>The brass callout: the click it holds before (with Continue), or what it waits for you to do.</summary>
    private void DrawErrandCallout(bool placing)
    {
        var e = Errands;
        var holding = placing ? runner.AwaitingContinue : e.AwaitingContinue;
        var label = (placing ? runner.StepLabel : e.StepLabel) ?? string.Empty;
        Kit.BeginPanel("##errand-callout", 16, 20, 16, Palette.Over(Palette.BrassNow, Palette.Ink), Palette.BrassEdge);
        Kit.EyebrowRow(holding ? "Step by step · next" : "Waiting for you");
        Kit.Gap(6);
        var words = label.StartsWith("You: ", StringComparison.Ordinal) ? label[5..] : label;
        Txt.Para(new[] { new Run(words, Face.Sans, 15, Palette.Ivory) }, 15 * 1.4f);
        Kit.Gap(4);
        if (holding)
        {
            Txt.Para(new[]
            {
                Run.Note(!placing && e.HoldingAGuess ? "This click hasn't been seen working yet: watch what it does, and send the log if it's wrong. " : "Nothing is clicked until you continue. "),
                Run.Note("/hstudio continue", Palette.Dim),
                Run.Note(" works too."),
            });
            Kit.Gap(14);
            var row = Kit.At;
            const string cont = "Continue";
            var cw = Kit.ButtonWidth(cont, BtnSize.Md, Icon.Play, Btn.Primary);
            if (Kit.Button("##errand-continue", new Vector2(row.X + Kit.Width - cw, row.Y), cont, Btn.Primary, BtnSize.Md, Icon.Play))
                OnFramework(e.Continue);
            Kit.Reserve(row, Kit.Width, S(40));
        }
        else
        {
            Txt.Note("It carries on by itself once that's done.");
        }
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // The summary.

    private void DrawErrandsSummary()
    {
        var e = Errands;
        var stopped = e.State == RunState.Stopped;
        ErrandsHeader($"Errands · {session.Title}", stopped ? "Errands stopped" : "Errands done", false);

        Frame.BeginBody("##errands-done", "errands-done-foot", 16, 22);
        if (stopped && e.StopReason != null)
        {
            AlertNote(e.StopReason, Palette.Danger, textColor: Palette.Text);
            Kit.Gap(14);
        }
        else if (e.StatusText != null)
        {
            AlertNote(Words.Capital(e.StatusText), Palette.Success, Icon.Check, Palette.Text);
            Kit.Gap(14);
        }
        Kit.EyebrowRow($"Got · {e.GotCount}");
        Kit.Gap(10);
        Kit.BeginPanel("##errand-got", 14, 20, 14);
        var got = e.Got.Select(kv => (Name: data.AnyItemName(kv.Key), kv.Value)).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Value == 1 ? g.Name : $"{g.Value} × {g.Name}").ToList();
        Txt.Para(new[] { Run.Note(got.Count == 0 ? "Nothing." : string.Join(", ", got), Palette.Text) });
        Kit.Gap(8);
        Txt.Para(new[] { Run.Note($"{ErrandWords.Gil(e.Spent)} spent: {ErrandWords.Gil(e.VendorSpent)} at vendors, {ErrandWords.Gil(e.MarketSpent)} on the market board, in {Words.Count(e.Trip, "trip")}.") });
        Kit.EndPanel();

        var left = ErrandWords.Left(e.Plan.Left, data.AnyItemName);
        if (left.Count > 0)
        {
            Kit.Gap(18);
            Kit.EyebrowRow("Not got this way");
            Kit.Gap(10);
            Kit.BeginPanel("##errand-left-end", 14, 20, 14);
            foreach (var (why, what) in left)
            {
                Txt.Para(new[] { new Run(Words.Capital(why), Face.SansMedium, 13, Palette.Text) }, 19.5f);
                Txt.Para(new[] { Run.Note(what) });
                Kit.Gap(8);
            }
            Kit.EndPanel();
        }
        Kit.Gap(18);
        DrawLog();
        Frame.EndBody();

        Frame.BeginFooter("errands-done-foot", 18, 22);
        var at = Kit.At;
        if (Kit.Button("##errands-done", at, "Done", Btn.Primary, BtnSize.Lg, Icon.Check, Kit.Width))
        {
            e.Dismiss();
            vm.Dismiss();
            errandsOpen = false;
        }
        Kit.Reserve(at, Kit.Width, S(46));
        Frame.EndFooter();
    }
}
