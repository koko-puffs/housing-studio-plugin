using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// "Get everything for me", as the 0.6 flow mock-up draws it (docs/mockups/flow.html):
/// "Get, before setting out" (the plugins as check chips, the route trip by trip, what isn't got this
/// way, the Limits, and one brass button naming the gil, which is the confirmation), then Running (the
/// journey Bell → Vendors → Market → Home → Place, where it is in words, what it's doing, Got and
/// Spent, the log, Pause and Stop; the usual placing figure and phases while it places), then a summary
/// like Done. Reached from Ready's brass button ("Get 12, then place") and Shopping's foot.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] HomeLabels = { "My house", "Company house", "Apartment" };

    private bool errandsOpen;

    /// <summary>The Get screen's Limits row is opened to change them.</summary>
    private bool errandLimitsOpen;

    /// <summary>The plugin chip whose detail shows.</summary>
    private string? errandChipOpen;

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
        // The same plan as before keeps its object, so what's drawn from it isn't made again.
        if (ErrandPlanner.Same(plan, e.Plan)) plan = e.Plan;
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

    private (ErrandPlan? Plan, DateTime Until, string? Why) errandsRehearsalBlocked;

    /// <summary>Why the errands can't be rehearsed now (too little gil doesn't stop a rehearsal: it's noted).</summary>
    private string? ErrandsRehearsalBlocked(ErrandPlan plan)
    {
        if (ReferenceEquals(errandsRehearsalBlocked.Plan, plan) && DateTime.UtcNow < errandsRehearsalBlocked.Until) return errandsRehearsalBlocked.Why;
        errandsRehearsalBlocked = (plan, DateTime.UtcNow + TimeSpan.FromSeconds(0.5), Errands.CanStart(plan, session.Context, rehearse: true));
        return errandsRehearsalBlocked.Why;
    }

    // ---------------------------------------------------------------------------------------------
    // The way in: Shopping's foot, and Ready's brass button with "Get them for me first" on.

    private const string ErrandsLabel = "Get everything for me";

    private bool ErrandsOffered(ShoppingPlan shop) => Errands.Enabled && session.Plan != null && !shop.IsEmpty;

    private static float ErrandsButtonWidth => Kit.ButtonWidth(ErrandsLabel, BtnSize.Md, Icon.Package);

    private void ErrandsButton(Vector2 pos)
    {
        if (Kit.Button("##errands", pos, ErrandsLabel, Btn.Glass, BtnSize.Md, Icon.Package, 0, false,
                "Fetches what's missing from your retainers, vendors and the market board, and places it. You see the gil first."))
        {
            errandsRehearse = false;
            errandsOpen = true;
        }
    }

    // ---------------------------------------------------------------------------------------------

    private void DrawErrands()
    {
        if (Errands.Active) DrawErrandsRunning();
        else if (Errands.ResultWaiting && Errands.Rehearsal) DrawRehearsalResult(errands: true);
        else if (Errands.ResultWaiting) DrawErrandsSummary();
        else DrawErrandsEstimate();
    }

    private static Icon LegIcon(ErrandLeg leg) => leg switch
    {
        ErrandLeg.Bell => Icon.Bell,
        ErrandLeg.Vendor => Icon.Store,
        ErrandLeg.Market => Icon.Scale,
        ErrandLeg.Home => Icon.House,
        _ => Icon.Sofa,
    };

    /// <summary>
    /// A serif title with its italic part in brass, both cut to fit (the italic one at most 70% of the
    /// width, so the plain words still show). Returns its height.
    /// </summary>
    private static float FitTitle(Vector2 pos, string plain, string? italic, float maxWidth)
    {
        if (italic != null && Txt.Measure(italic, Face.SerifItalic, 32).X > maxWidth * 0.7f) italic = Txt.Fit(italic, Face.SerifItalic, 32, maxWidth * 0.7f);
        return Title(pos, plain, italic, maxWidth);
    }

    /// <summary>A section label (mock-up: <c>.sec</c>): mono 11 capitals in dim, and its words on the right in sans 12.</summary>
    private static void SectionLabel(string label, string? rest)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var h = S(18);
        var lw = Txt.Spaced(dl, new Vector2(at.X + S(2), at.Y + (h - Txt.Natural(Face.Mono, 11)) / 2), label, Face.Mono, 11, Palette.Dim, 0.16f);
        if (rest != null)
        {
            var room = w - S(2) - lw - S(12);
            var text = Txt.Fit(rest, Face.Sans, 12, room);
            var tw = Txt.Measure(text, Face.Sans, 12).X;
            Txt.Line(dl, new Vector2(at.X + w - S(2) - tw, at.Y), text, Face.Sans, 12, Palette.Dim, 18);
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>A 34px thumb (mock-up: <c>.thumb</c>): the photo well with an icon in brass (or another colour).</summary>
    private static void IconThumb(Vector2 pos, float size, Icon icon, Vector4? ink = null)
    {
        var dl = Kit.Dl;
        var r = S(8);
        dl.AddRectFilled(pos, pos + new Vector2(size), Palette.U32(Palette.Photo), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(size) - new Vector2(0.5f), Palette.U32(Palette.LineFaint), r, ImDrawFlags.None, 1f);
        var px = size * 16f / 34f;
        Icons.Draw(dl, icon, pos + new Vector2((size - px) / 2), px, ink ?? Palette.Brass);
    }

    // ---------------------------------------------------------------------------------------------
    // Get, before setting out.

    /// <summary>The Get screen's words for one estimate, made again only when the plan changes.</summary>
    private sealed class GetScreenWords
    {
        public ErrandPlan? Plan;
        public Plan? From;
        public string Eyebrow = string.Empty;
        public string EyebrowUpper = string.Empty;
        public string For = string.Empty;
        public string TitlePlain = string.Empty;
        public string? TitleItalic;
        public string Button = string.Empty;
        public string Have = string.Empty;
        public string? Currencies;
        public List<RouteTrip> Route = new();
        public string[] RouteIds = Array.Empty<string>();
        public List<LeftRow> Left = new();
    }

    private readonly GetScreenWords getScreen = new();

    private GetScreenWords GetScreen(ErrandPlan plan, string title)
    {
        var g = getScreen;
        var p = session.Plan;
        if (ReferenceEquals(g.Plan, plan) && ReferenceEquals(g.From, p) && g.For == title) return g;
        g.Plan = plan;
        g.From = p;
        g.For = title;
        g.Eyebrow = $"{title} · get";
        g.EyebrowUpper = g.Eyebrow.ToUpperInvariant();
        (g.TitlePlain, g.TitleItalic) = ErrandWords.Title(plan);
        g.Button = ErrandWords.SetOut(plan.TotalGil, plan.CurrencyCosts);
        g.Currencies = ErrandWords.CurrencyLine(plan.CurrencyCosts, Plugin.Shop.Balance);
        g.Route = ErrandWords.Route(plan, data.AnyItemName, ErrandRunner.BellNearby(), ErrandGame.HomeWorld(), ErrandWords.City(config.ErrandsHub));
        g.RouteIds = g.Route.Select((_, i) => $"##errand-trip{i}").ToArray();
        g.Left = ErrandWords.LeftRows(plan.Left, data.AnyItemName);
        g.Have = p == null ? string.Empty
            : ErrandWords.Have(p.Entries.Count(e => e.State is PieceState.InPlace or PieceState.Move), p.Entries.Count(e => e.State == PieceState.FromStoreroom),
                p.Entries.Count(e => e.State == PieceState.FromInventory), plan.Stops(StopKind.Retainer).Sum(s => s.Count));
        return g;
    }

    /// <summary>The plugins' chips: made again only when one is installed or taken out, or retainers come in or go.</summary>
    private (bool Nav, bool Travel, bool Retainers, bool Ar, CheckChipSpec[] Chips) errandChips = (false, false, false, false, Array.Empty<CheckChipSpec>());

    private const string NavChip = "##chip-vnavmesh";
    private const string TravelChip = "##chip-lifestream";
    private const string ArChip = "##chip-autoretainer";

    private CheckChipSpec[] ErrandChips(bool retainers)
    {
        var nav = Errands.Nav.Installed;
        var travel = Errands.Travel.Installed;
        var ar = retainers && Plugins.Loaded("AutoRetainer");
        var c = errandChips;
        if (c.Chips.Length > 0 && c.Nav == nav && c.Travel == travel && c.Retainers == retainers && c.Ar == ar) return c.Chips;
        var chips = new List<CheckChipSpec>
        {
            Needed(NavChip, "vnavmesh", nav, "Walks you to each NPC, bell and board."),
            Needed(TravelChip, "Lifestream", travel, "Teleports, goes home, and visits other worlds."),
        };
        if (retainers)
            chips.Add(ar
                ? new CheckChipSpec(ArChip, "AutoRetainer", ChipTone.Normal, Icon.Check, null, "Asked to keep out of the way while a bell is used.", Palette.Success)
                : new CheckChipSpec(ArChip, "AutoRetainer · not installed", ChipTone.Normal, Icon.Info, null,
                    "Optional: if you have it, it's asked to keep out of the way while a bell is used."));
        errandChips = (nav, travel, retainers, ar, chips.ToArray());
        return errandChips.Chips;

        static CheckChipSpec Needed(string id, string name, bool installed, string does) => installed
            ? new CheckChipSpec(id, name, ChipTone.Normal, Icon.Check, null, does, Palette.Success)
            : new CheckChipSpec(id, $"{name} · not installed", ChipTone.Warn, Icon.Alert, null, $"Needed: {does}");
    }

    /// <summary>A plugin chip's detail: what it does for the errands, and its page.</summary>
    private void ErrandChipDetail(string id)
    {
        var (name, does, url, installed) = id switch
        {
            NavChip => ("vnavmesh", "walks you to each NPC, summoning bell and market board.", "https://github.com/awgil/ffxiv_navmesh", Errands.Nav.Installed),
            TravelChip => ("Lifestream", "teleports, takes you home and in, and visits the other worlds of your data centre.", "https://github.com/NightmareXIV/Lifestream",
                Errands.Travel.Installed),
            _ => ("AutoRetainer", "is asked to keep out of the way while the errands use a summoning bell (optional).", "https://github.com/PunishXIV/AutoRetainer",
                Plugins.Loaded("AutoRetainer")),
        };
        Kit.Gap(10);
        Kit.BeginDetail();
        Txt.Para(new[]
        {
            new Run(name, Face.SansMedium, 13, Palette.Ivory),
            Run.Note(" " + does + (installed ? " It's installed and on." : " It isn't installed (or it's turned off): add it from its own repository, then come back.")),
        });
        if (!installed)
        {
            Kit.Gap(8);
            var at = Kit.At;
            if (Kit.MoreButton("##chip-page", at, "Its page", Icon.ChevronRight, url)) Dalamud.Utility.Util.OpenLink(url);
            Kit.Reserve(at, Kit.Width, S(30));
        }
        Kit.EndDetail();
    }

    /// <summary>"Then clears out 14 before placing" (Ready's choice), made again only when it changes.</summary>
    private (ReadyMode Mode, int Count, string? Keep, string? Line) errandThen;

    private string? ErrandThen(ReadyMode mode, int count, string? keep)
    {
        var t = errandThen;
        if (t.Mode != mode || t.Count != count || t.Keep != keep)
            errandThen = t = (mode, count, keep, ErrandWords.Then(mode, count, keep));
        return t.Line;
    }

    private (long Ceiling, bool Worlds, int Spare, string Line) errandLimits = (-1, false, 0, string.Empty);

    private string ErrandLimits()
    {
        var l = errandLimits;
        if (l.Ceiling != config.ErrandsMarketCeiling || l.Worlds != config.ErrandsOtherWorlds || l.Spare != config.ErrandsSpareSlots)
            errandLimits = l = (config.ErrandsMarketCeiling, config.ErrandsOtherWorlds, config.ErrandsSpareSlots,
                ErrandWords.Limits(config.ErrandsMarketCeiling, config.ErrandsOtherWorlds, config.ErrandsSpareSlots));
        return l.Line;
    }

    private void DrawErrandsEstimate()
    {
        var (plan, here) = ErrandEstimate();
        var g = GetScreen(plan, session.Title);
        var rehearse = errandsRehearse;
        var blocked = rehearse ? ErrandsRehearsalBlocked(plan) : ErrandsBlocked(plan);

        // Header: "<design> · GET", the title, Back on the right.
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var leftW = w - S(40) - S(12);
        var eyebrowH = Txt.Natural(Face.Mono, 12);
        Kit.Eyebrow(at, g.Eyebrow, null, leftW);
        var titleH = FitTitle(new Vector2(at.X, at.Y + eyebrowH + S(8)), g.TitlePlain, g.TitleItalic, leftW);
        if (Kit.IconButton("##errands-back", new Vector2(at.X + w - S(40), at.Y), Icon.ArrowLeft, "Back"))
        {
            errandsOpen = false;
            errandsRehearse = false;
        }
        Kit.Reserve(at, w, MathF.Max(S(40), eyebrowH + S(8) + titleH));
        Frame.EndHeader();

        Frame.BeginBody("##errands", "errands-foot", 18, 22);
        if (rehearse)
        {
            AlertNote("A rehearsal: every trip as planned, each purchase asked for and then cancelled, nothing taken from your retainers; then placing rehearsed on what you have. "
                      + RehearsalWords.Teleports, Palette.Brass, Icon.Info, Palette.Text);
            Kit.Gap(14);
        }
        if (Plugin.Shop.Status() is { } status)
        {
            AlertNote(status, Palette.Dim, Icon.Info);
            Kit.Gap(14);
        }

        // No way to the front door known for this plot (none yet, or you've moved): said here, before
        // setting out. Nothing to set up: the first time home, you walk in once and it's remembered.
        if (!plan.IsEmpty && !Errands.HasWayFor(here))
        {
            AlertNote($"The first time it comes home, you'll be asked to walk from where you land to your door (plot {here!.Plot}) once. "
                      + "That walk is remembered, and from then on the errands walk to your door themselves.", Palette.Brass, Icon.Info, Palette.Text);
            Kit.Gap(14);
        }

        // The plugins it needs: green ticks, or a warning chip when one isn't there; pressing one says more.
        var chips = ErrandChips(plan.Needs(StopKind.Retainer));
        var pressed = Kit.CheckChips(chips, errandChipOpen);
        if (pressed >= 0) errandChipOpen = errandChipOpen == chips[pressed].Id ? null : chips[pressed].Id;
        foreach (var c in chips)
            if (c.Id == errandChipOpen)
            {
                ErrandChipDetail(c.Id);
                break;
            }

        if (g.Have.Length > 0)
        {
            Kit.Gap(12);
            DimLine("##errand-have", Icon.Backpack, g.Have);
        }
        if (g.Currencies != null)
        {
            Kit.Gap(8);
            DimLine("##errand-currencies", Icon.Scale, g.Currencies, Palette.Text);
        }

        // The limits above the trips (0.7.13): changing one changes the trips, and with the limits
        // below them, the row you were using moved away as the list above it grew or shrank.
        Kit.Gap(14);
        DrawErrandLimits(here);

        if (plan.IsEmpty)
        {
            Kit.Gap(14);
            Kit.BeginPanel("##errand-none", 14, 16, 14);
            Txt.Note(session.Plan == null ? "Go inside your home: what's missing is worked out there."
                : ShopPlan().IsEmpty ? "You have every piece, and the dyes for them."
                : "Nothing on the list can be got this way.");
            Kit.EndPanel();
        }
        for (var t = 0; t < g.Route.Count; t++)
        {
            var trip = g.Route[t];
            Kit.Gap(14);
            SectionLabel(trip.Label, trip.Rest);
            Kit.Gap(8);
            Kit.BeginPanel(g.RouteIds[t], 0, 0, 0);
            for (var i = 0; i < trip.Legs.Count; i++) LegRow(trip.Legs[i], i == trip.Legs.Count - 1);
            Kit.EndPanel();
        }

        // Ready's Keep / Clear out / Swap: the errands' first placing does it.
        var mode = ModeNow;
        var clear = ChosenClearOut();
        var keep = SwapKeepAs;
        if (!plan.IsEmpty && ErrandThen(mode, clear?.Count ?? 0, keep) is { } then)
        {
            Kit.Gap(12);
            DimLine("##errand-then", mode == ReadyMode.Swap ? Icon.Undo : Icon.Refresh, then, Palette.Text);
        }

        if (g.Left.Count > 0)
        {
            Kit.Gap(14);
            SectionLabel("NOT GOT THIS WAY", null);
            Kit.Gap(8);
            Kit.BeginPanel("##errand-left", 0, 0, 0);
            for (var i = 0; i < g.Left.Count; i++) LeftRowDraw(i, g.Left[i]);
            Kit.EndPanel();
        }

        Frame.EndBody();

        // The brass button names the most it spends: it is the confirmation. It starts the very plan shown.
        Frame.BeginFooter("errands-foot", 16, 20);
        var row = Kit.At;
        var fw = Kit.Width;
        var button = rehearse ? RehearsalWords.ErrandsButton : g.Button;
        // Can't set out: placing what you have is the other way on (as Ready's button did before).
        if (blocked != null && !rehearse && placeInstead is { } place)
        {
            const string instead = "Place what I have";
            var iw = Kit.ButtonWidth(instead, BtnSize.Lg, null, Btn.Glass);
            if (Kit.Button("##errands-place-instead", row, instead, Btn.Glass, BtnSize.Lg, null, iw, false, "Places what's in your storeroom and bags now, without getting the rest"))
            {
                errandsOpen = false;
                place();
            }
            Kit.Reserve(row, iw, S(46));
            Kit.Gap(10);
            row = Kit.At;
        }
        var bw = MathF.Min(fw, Kit.ButtonWidth(button, BtnSize.Lg, Icon.Play, Btn.Primary));
        if (Kit.Button("##errands-start", row, button, Btn.Primary, BtnSize.Lg, Icon.Play, bw, blocked != null,
                blocked ?? (rehearse ? "Sets out now on the trips shown: nothing is bought or taken, teleports are real." : "Sets out now: no question after this. It spends no more than this in all, and stops at anything it didn't expect.")))
            SetOut(plan, clear, keep, rehearse);
        const string hint1 = "You can stop at any time.";
        var hint2 = rehearse ? "Nothing is bought or taken." : "Nothing is bought above the price shown.";
        var hints = hint1 + " " + hint2;
        var hw = MathF.Max(Txt.Measure(hint1, Face.Sans, 12).X, Txt.Measure(hint2, Face.Sans, 12).X);
        if (blocked == null && bw + S(16) + hw <= fw)
        {
            var dl = Kit.Dl;
            var top = row.Y + (S(46) - S(36)) / 2;
            Txt.Line(dl, new Vector2(row.X + fw - Txt.Measure(hint1, Face.Sans, 12).X, top), hint1, Face.Sans, 12, Palette.Dim, 18);
            Txt.Line(dl, new Vector2(row.X + fw - Txt.Measure(hint2, Face.Sans, 12).X, top + S(18)), hint2, Face.Sans, 12, Palette.Dim, 18);
            Kit.Reserve(row, fw, S(46));
        }
        else
        {
            Kit.Reserve(row, fw, S(46));
            Kit.Gap(10);
            Txt.Para(new[] { Run.Note(blocked ?? hints, blocked != null ? Palette.Text : Palette.Dim) });
        }
        Frame.EndFooter();
    }

    /// <summary>
    /// Sets out on the plan shown (its gil is what was agreed to). On the framework thread, a fresh
    /// estimate must still say the same: if your bags, prices or limits changed since the screen was
    /// drawn, nothing starts, and the screen shows the new one.
    /// </summary>
    private void SetOut(ErrandPlan shown, IReadOnlyList<PlacedPiece>? clear, string? keepAs, bool rehearse = false)
    {
        if (shown.IsEmpty) return;
        OnFramework(() =>
        {
            if (Errands.Active) return;
            var now = Errands.Estimate(ShopPlan(), ErrandGame.HomeHere(session.Context));
            if (!ErrandPlanner.Same(shown, now))
            {
                errandEstimate.Until = DateTime.MinValue;
                Notify("The estimate changed: look at it again");
                return;
            }
            if (rehearse) Rehearsals.Report.Begin(session.Title, DateTime.Now);
            Errands.Start(shown, session.Context, clear, keepAs, rehearse);
            if (!Errands.Active && Errands.StopReason != null) Notify(Errands.StopReason);
            // Their placing comes after: Ready's "Set them for me after placing" follows them.
            if (Errands.Active)
            {
                errandLimitsOpen = false;
                errandChipOpen = null;
                ArmFixturesAfterErrands();
            }
        });
    }

    /// <summary>One dim line with an icon, cut to fit; the whole of it as a tooltip when cut.</summary>
    private static void DimLine(string id, Icon icon, string text, Vector4? color = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var h = S(19.5f);
        var dl = Kit.Dl;
        Icons.Draw(dl, icon, new Vector2(at.X + S(2), at.Y + (h - S(14)) / 2), S(14), Palette.Brass);
        var x = at.X + S(2 + 14 + 8);
        var shown = Txt.Fit(text, Face.Sans, 13, at.X + w - x);
        Txt.Line(dl, new Vector2(x, at.Y), shown, Face.Sans, 13, color ?? Palette.Dim, 19.5f);
        if (!ReferenceEquals(shown, text) && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, at + new Vector2(w, h))) Kit.Tooltip(text);
        Kit.Reserve(at, w, h);
    }

    /// <summary>
    /// A leg of a trip (mock-up: <c>.leg</c>): the round pin with its icon (home's in brass), where in
    /// ivory, what's got there in dim (cut to one line), the gil on the right in mono, and a thin line
    /// down to the next leg's pin.
    /// </summary>
    private static void LegRow(RouteLeg leg, bool last)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var padY = S(10);
        var nameH = S(20);
        var noteH = S(18);
        var h = padY * 2 + MathF.Max(S(22), nameH + noteH);
        var dl = Kit.Dl;
        var pin = new Vector2(at.X + S(12), at.Y + padY);
        var c = pin + new Vector2(S(11));
        if (!last) dl.AddLine(new Vector2(c.X, pin.Y + S(22)), new Vector2(c.X, at.Y + h + padY), Palette.U32(Palette.LineStrong), 1f);
        var home = leg.Kind == ErrandLeg.Home;
        dl.AddCircleFilled(c, S(11), Palette.U32(home ? Palette.Over(Palette.BrassWash, Palette.Ink) : Palette.Ink), 24);
        dl.AddCircle(c, S(11) - 0.5f, Palette.U32(home ? Palette.BrassEdge : Palette.LineStrong), 24, 1f);
        Icons.Draw(dl, LegIcon(leg.Kind), c - new Vector2(S(6)), S(12), Palette.Brass);
        var x = pin.X + S(22) + S(12);
        var right = at.X + w - S(14);
        var gilW = leg.Gil.Length > 0 ? Txt.Measure(leg.Gil, Face.Mono, 12).X : 0;
        var textW = right - (gilW > 0 ? gilW + S(12) : 0) - x;
        Txt.Line(dl, new Vector2(x, at.Y + padY), Txt.Fit(leg.Name, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ivory, 20);
        var what = Txt.Fit(leg.What, Face.Sans, 12, textW);
        Txt.Line(dl, new Vector2(x, at.Y + padY + nameH), what, Face.Sans, 12, Palette.Dim, 18);
        if (gilW > 0) Txt.Line(dl, new Vector2(right - gilW, at.Y + padY), leg.Gil, Face.Mono, 12, Palette.Text, 20);
        if (!ReferenceEquals(what, leg.What) && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, at.Y), new Vector2(x + textW, at.Y + h)))
            Kit.Tooltip(leg.What);
        Kit.Reserve(at, w, h);
    }

    private static Icon LeftIcon(LeftReason reason) => reason switch
    {
        LeftReason.Craft => Icon.Hammer,
        LeftReason.NoVendorReachable => Icon.Store,
        LeftReason.OverCeiling => Icon.Scale,
        LeftReason.NotListedHere => Icon.Scale,
        LeftReason.NoRoom => Icon.Backpack,
        LeftReason.Currency => Icon.Store,
        _ => Icon.Info,
    };

    /// <summary>A reason things aren't got this way (mock-up: <c>.li</c>): its icon, "1 to craft", the things; Teamcraft for the crafted ones.</summary>
    private static void LeftRowDraw(int index, LeftRow row)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var h = S(10) * 2 + MathF.Max(S(34), S(20) + S(18));
        var dl = Kit.Dl;
        if (index > 0) Kit.Divider(Kit.Panel.Start.X, Kit.Panel.Start.X + Kit.Panel.Width, at.Y - 0.5f);
        var mid = at.Y + h / 2;
        IconThumb(new Vector2(at.X + S(14), mid - S(17)), S(34), LeftIcon(row.Reason), row.Reason == LeftReason.Craft ? Palette.Brass : Palette.Dim);
        var x = at.X + S(14 + 34 + 12);
        const string teamcraft = "Teamcraft";
        var craft = row.Reason == LeftReason.Craft;
        var bw = craft ? Kit.ButtonWidth(teamcraft, BtnSize.Tiny) : 0;
        var right = at.X + w - S(14);
        var textW = right - (craft ? bw + S(12) : 0) - x;
        var top = mid - (S(20) + S(18)) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(row.Title, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ivory, 20);
        Txt.Line(dl, new Vector2(x, top + S(20)), Txt.Fit(row.What, Face.Sans, 12, textW), Face.Sans, 12, Palette.Dim, 18);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(new Vector2(x, at.Y), new Vector2(x + textW, at.Y + h)))
            Kit.Tooltip($"{row.Why} {row.What}.");
        if (craft && Kit.Button($"##errand-teamcraft{index}", new Vector2(right - bw, mid - S(15)), teamcraft, Btn.Glass, BtnSize.Tiny, null, 0, false,
                "Opens ffxivteamcraft.com in your browser with these in a new list"))
        {
            var url = Teamcraft.ImportUrl(row.Items);
            if (url != null)
            {
                Dalamud.Utility.Util.OpenLink(url);
                Notify("Opening Teamcraft");
            }
        }
        Kit.Reserve(at, w, h);
    }

    /// <summary>
    /// Limits (mock-up: one row, "Market up to 100,000 · home world only · 2 slots spare", and Change):
    /// Change opens the rows to set them here (the market ceiling, other worlds, which home, slots to
    /// keep free, and trusting the new clicks).
    /// </summary>
    private void DrawErrandLimits(HomeRef? here)
    {
        Kit.BeginPanel("##errand-limits", 0, 0, 0);
        var at = Kit.At;
        var w = Kit.Width;
        var label = errandLimitsOpen ? "Done" : "Change";
        var chevron = errandLimitsOpen ? Icon.ChevronDown : Icon.ChevronRight;
        var bw = Kit.MoreButtonWidth(label, chevron);
        var h = S(11) * 2 + MathF.Max(S(30), S(20) + S(18));
        var x = at.X + S(14);
        var textW = at.X + w - S(10) - bw - S(12) - x;
        var top = at.Y + (h - S(38)) / 2;
        var dl = Kit.Dl;
        Txt.Line(dl, new Vector2(x, top), "Limits", Face.Sans, 14, Palette.Ivory, 20);
        var limits = ErrandLimits();
        var shown = Txt.Fit(limits, Face.Sans, 12, textW);
        Txt.Line(dl, new Vector2(x, top + S(20)), shown, Face.Sans, 12, Palette.Dim, 18);
        if (!ReferenceEquals(shown, limits) && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, new Vector2(x + textW, at.Y + h))) Kit.Tooltip(limits);
        if (Kit.MoreButton("##errand-limits-change", new Vector2(at.X + w - S(10) - bw, at.Y + (h - S(30)) / 2), label, chevron,
                errandLimitsOpen ? null : "The market ceiling, other worlds, which home, bag slots to keep free"))
            errandLimitsOpen = !errandLimitsOpen;
        Kit.Reserve(at, w, h);
        if (errandLimitsOpen) DrawErrandLimitRows(1, here);
        Kit.EndPanel();
    }

    /// <summary>The errands' limits as setting rows (from <paramref name="first"/>, the faint line above each but the first).</summary>
    private void DrawErrandLimitRows(int first, HomeRef? here)
    {
        var i = first;
        CeilingRow(i++, "##errand-ceiling", "The most it spends on the market board in all",
            "Type a number in (2500000, 2.5m, 750k) or step through. 0 buys nothing there.");
        if (SwitchRow(i++, "##errand-worlds", "Other worlds", "Buy across your data centre (Lifestream visits them). Off: your home world only.",
                config.ErrandsOtherWorlds))
        {
            config.ErrandsOtherWorlds = !config.ErrandsOtherWorlds;
            config.Save();
        }
        DrawHomeIsRow(i++, here, "##errand-home");
        var spare = config.ErrandsSpareSlots;
        var d = StepperRow(i++, "##errand-spare", "Bag slots to keep free", "Each trip leaves these empty", spare.ToString(), spare > 0, spare < 20);
        if (d != 0)
        {
            config.ErrandsSpareSlots = Math.Clamp(spare + d, 0, 20);
            config.Save();
        }
        if (SwitchRow(i, "##errand-trust", "Trust the new clicks",
                "Off: each click not yet seen working (a retainer's menu, the board's search) waits for Continue, so you can watch it.",
                config.ErrandsTrustGuesses))
        {
            config.ErrandsTrustGuesses = !config.ErrandsTrustGuesses;
            config.Save();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Running.

    /// <summary>Running's words, made again only when the run moves on (another segment, step or trip).</summary>
    private sealed class RunWords
    {
        public int Segment = -2;
        public int Trip = -1;
        public string? Step;
        public ErrandTrip? ForTrip;
        public string Title = string.Empty;
        public string Eyebrow = string.Empty;
        public string Plain = string.Empty;
        public string Italic = string.Empty;
        public (Icon Icon, string Label)[] Stops = Array.Empty<(Icon, string)>();
        public List<ErrandLeg> Legs = new();
        public int At;
        public string Doing = string.Empty;
        public string? Note;
        public uint ItemIcon;
        public Icon Icon;
    }

    private readonly RunWords runWords = new();

    private RunWords ErrandRunWords(ErrandRunner e)
    {
        var r = runWords;
        var seg = e.SegmentIndex;
        var step = e.StepLabel;
        var tripNow = e.CurrentTrip;
        if (r.Segment == seg && r.Trip == e.Trip && ReferenceEquals(r.Step, step) && ReferenceEquals(r.ForTrip, tripNow) && r.Title == session.Title) return r;
        if (!ReferenceEquals(r.ForTrip, tripNow) || r.Stops.Length == 0)
        {
            r.Legs = ErrandWords.Journey(tripNow);
            r.Stops = r.Legs.Select(l => (LegIcon(l), ErrandWords.JourneyLabel(l))).ToArray();
        }
        r.Segment = seg;
        r.Trip = e.Trip;
        r.Step = step;
        r.ForTrip = tripNow;
        r.Title = session.Title;
        r.Eyebrow = $"Getting {session.Title} · trip {Math.Max(1, e.Trip)} of {Math.Max(e.Trip, e.TripsAtStart)}";
        var leg = seg < 0 ? ErrandLeg.Bell : e.Leg;
        (r.Plain, r.Italic) = seg < 0 ? ("Setting", "out") : ErrandWords.Where(leg, e.Going, e.Who, e.Replanning);
        r.At = seg < 0 ? 0 : ErrandWords.JourneyAt(r.Legs, leg);
        var item = e.CurrentItem;
        var name = item != null ? data.AnyItemName(item.ItemId) : string.Empty;
        r.Doing = ErrandWords.Doing(leg, item, name, e.Who) ?? e.Doing ?? "Starting";
        var answering = step != null && step.StartsWith("Answer yes", StringComparison.Ordinal);
        r.Note = ErrandWords.DoingNote(leg, item, answering)
                 ?? (step != null && !step.StartsWith("You:", StringComparison.Ordinal) ? step : null);
        r.ItemIcon = item != null ? data.ItemIcon(item.ItemId) : 0;
        r.Icon = LegIcon(leg);
        return r;
    }

    private readonly Run[] errandNote = new Run[1];

    /// <summary>The Got and Spent cards' words, made again only when the numbers change.</summary>
    private (int Got, int Of, long Spent, long Agreed, string GotV, string GotOf, string SpentV, string SpentOf) errandCards = (-1, 0, -1, 0, "", "", "", "");

    private void DrawErrandsRunning()
    {
        var e = Errands;
        var placing = e.Placing;
        var waiting = e.AwaitingContinue || e.WaitingForYou || (placing && (runner.AwaitingContinue || runner.WaitingForYou));
        var paused = e.State == RunState.Paused || (placing && runner.State == RunState.Paused);
        var (pill, tone) = waiting ? ("Waiting for you", Tone.Waiting) : paused ? ("Paused", Tone.Neutral) : ("Running", Tone.Live);
        var rehearsing = e.Rehearsal && tone == Tone.Live;
        if (rehearsing) pill = RehearsalWords.Pill(placing ? Words.About(runner.TimeLeft) : null);
        var r = ErrandRunWords(e);

        // Header: the eyebrow and the live pill on one row, where it is in words under them.
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var pw = Kit.StatusPillWidth(pill);
        if (rehearsing) RehearsalPill(new Vector2(at.X + w - pw, at.Y), pill);
        else Kit.StatusPill(new Vector2(at.X + w - pw, at.Y), pill, tone, tone == Tone.Live);
        var eyebrowH = Txt.Natural(Face.Mono, 12);
        Kit.Eyebrow(new Vector2(at.X, at.Y + (S(28) - eyebrowH) / 2), e.Rehearsal ? $"{RehearsalWords.Eyebrow} · trip {Math.Max(1, e.Trip)} of {Math.Max(e.Trip, e.TripsAtStart)}" : r.Eyebrow, null, w - pw - S(12));
        var titleH = FitTitle(new Vector2(at.X, at.Y + S(28) + S(6)), r.Plain, r.Italic, w);
        Kit.Reserve(at, w, S(28) + S(6) + titleH);
        Frame.EndHeader();

        Frame.BeginBody("##errands-run", "errands-run-foot", 18, 22);
        // Continue first, at the top (0.8.0): below the cards it moved with every step's words.
        if (waiting)
        {
            DrawErrandCallout(placing);
            Kit.Gap(14);
        }
        Kit.Journey(r.Stops, placing ? r.Stops.Length - 1 : r.At);
        Kit.Gap(14);
        if (placing)
        {
            // Placing what it got: the usual figure, phases and piece in hand, the journey on Place above.
            DrawFigure(false);
            Kit.Gap(18);
            DrawPhases();
            if (runner.Current is { } job)
            {
                Kit.Gap(14);
                DrawCurrent(job);
            }
        }
        else
        {
            DrawErrandCurrent(r);
        }
        Kit.Gap(14);
        DrawErrandCards(e);
        Kit.Gap(14);
        DrawLog("errands-run-bottom");
        Frame.BeginBottom("errands-run-bottom");
        Txt.Note(e.Rehearsal ? "Leave the keyboard and mouse alone while it walks. Nothing is bought or taken; teleports are real." : "Leave the keyboard and mouse alone while it walks and buys.", center: true);
        Frame.EndBottom();
        Frame.EndBody();

        // Pause / Resume, Stop, and the time left while it places.
        Frame.BeginFooter("errands-run-foot", 16, 20);
        var row = Kit.At;
        var fw = Kit.Width;
        const string stop = "Stop";
        var pauseLabel = paused ? "Resume" : "Pause";
        var bw = Kit.ButtonWidth(pauseLabel, BtnSize.Lg, Icon.Pause, paused ? Btn.Primary : Btn.Glass);
        var sw = Kit.ButtonWidth(stop, BtnSize.Lg, Icon.Square, Btn.Danger);
        if (paused)
        {
            if (Kit.Button("##errand-resume", row, "Resume", Btn.Primary, BtnSize.Lg, Icon.Play, bw)) OnFramework(e.Resume);
        }
        else if (Kit.Button("##errand-pause", row, "Pause", Btn.Glass, BtnSize.Lg, Icon.Pause, bw))
        {
            OnFramework(() => e.Pause("Paused by you."));
        }
        if (Kit.Button("##errand-stop", new Vector2(row.X + bw + S(10), row.Y), stop, Btn.Danger, BtnSize.Lg, Icon.Square))
            OnFramework(() => e.Stop("Stopped by you."));
        var hint = placing && !paused ? $"{Words.Capital(Words.About(runner.TimeLeft))} left" : paused ? "Resume carries on from where it held" : "Pause holds before its next step";
        var hx = row.X + bw + S(10) + sw + S(16);
        var hw = Txt.Measure(hint, Face.Sans, 12).X;
        if (hx + hw <= row.X + fw)
        {
            Txt.Line(Kit.Dl, new Vector2(row.X + fw - hw, row.Y), hint, Face.Sans, 12, Palette.Dim, 46);
            Kit.Reserve(row, fw, S(46));
        }
        else
        {
            Kit.Reserve(row, fw, S(46));
            Kit.Gap(8);
            Txt.Note(hint, Palette.Dim);
        }
        Frame.EndFooter();
    }

    /// <summary>The thing in hand (mock-up's card): its icon (or the stop's), "Buying Riviera Table ×2", and the price or the step.</summary>
    private void DrawErrandCurrent(RunWords r)
    {
        Kit.BeginPanel("##errand-current", 14, 16, 14);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var x = at.X + S(34) + S(12);
        var tw = at.X + w - x;
        Run[]? noteRuns = null;
        if (r.Note != null)
        {
            errandNote[0] = new Run(r.Note, Face.Sans, 12, Palette.Dim);
            noteRuns = errandNote;
        }
        var noteH = noteRuns != null ? Txt.Flow(Vector2.Zero, tw, noteRuns, 18, out _, draw: false) : 0;
        var wordsH = S(20) + (noteRuns != null ? S(2) + noteH : 0);
        var h = MathF.Max(S(34), wordsH);
        var thumb = new Vector2(at.X, at.Y + (h - S(34)) / 2);
        if (r.ItemIcon != 0) Kit.Thumb(thumb, S(34), S(8), r.ItemIcon);
        else IconThumb(thumb, S(34), r.Icon);
        var y = at.Y + (h - wordsH) / 2;
        Txt.Line(dl, new Vector2(x, y), Txt.Fit(r.Doing, Face.SansMedium, 14, tw), Face.SansMedium, 14, Palette.Ivory, 20);
        if (noteRuns != null) Txt.Flow(new Vector2(x, y + S(22)), tw, noteRuns, 18, out _);
        Kit.Reserve(at, w, h);
        Kit.EndPanel();
    }

    /// <summary>Got "6 of 12" and Spent "4,000 of 61,400", each with its bar, side by side (mock-up: <c>.kv</c>).</summary>
    private void DrawErrandCards(ErrandRunner e)
    {
        var got = e.GotCount;
        var of = Math.Max(got, e.CountAtStart);
        var c = errandCards;
        if (c.Got != got || c.Of != of || c.Spent != e.Spent || c.Agreed != e.AgreedGil)
        {
            var (gv, go) = ErrandWords.Of(got, of, false);
            var (sv, so) = ErrandWords.Of(e.Spent, e.AgreedGil, true);
            errandCards = c = (got, of, e.Spent, e.AgreedGil, gv, go, sv, so);
        }
        var at = Kit.At;
        var w = Kit.Width;
        var gap = S(10);
        var cw = (w - gap) / 2;
        var h = KvCard(at, cw, e.Rehearsal ? "WOULD GET" : "GOT", c.GotV, c.GotOf, of > 0 ? got / (float)of : 0);
        KvCard(new Vector2(at.X + cw + gap, at.Y), cw, e.Rehearsal ? "WOULD SPEND" : "SPENT", c.SpentV, c.SpentOf, e.AgreedGil > 0 ? e.Spent / (float)e.AgreedGil : 0);
        Kit.Reserve(at, w, h);
    }

    /// <summary>A card: the mono label, a serif figure with "of N" after it in dim, and the 8px bar. Returns its height.</summary>
    private static float KvCard(Vector2 pos, float width, string label, string value, string of, float fraction)
    {
        var dl = Kit.Dl;
        var labelH = Txt.Natural(Face.Mono, 10);
        var h = S(10) + labelH + S(6) + S(24) + S(6) + S(8) + S(12);
        var max = pos + new Vector2(width, h);
        var r = S(Palette.RadiusCard);
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Glass), r);
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(12);
        var y = pos.Y + S(10);
        Txt.Spaced(dl, new Vector2(x, y), label, Face.Mono, 10, Palette.Dim, 0.14f);
        y += labelH + S(6);
        var box = S(24);
        var baseline = y + (box - Txt.Natural(Face.Serif, 22)) / 2 + Txt.Ascent(Face.Serif, 22);
        var vw = Txt.Draw(dl, new Vector2(x, baseline - Txt.Ascent(Face.Serif, 22)), value, Face.Serif, 22, Palette.Ivory);
        Txt.Draw(dl, new Vector2(x + vw + S(6), baseline - Txt.Ascent(Face.Sans, 12)), Txt.Fit(of, Face.Sans, 12, width - S(24) - vw - S(6)), Face.Sans, 12, Palette.Dim);
        y += box + S(6);
        Kit.Progress(new Vector2(x, y), width - S(24), fraction);
        return h;
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
            // The placing rehearsal's let-go (not seen working yet): trusted from here as in Running's callout.
            if (placing && runner.HoldingALetGo
                && Kit.Button("##errand-trust-letgo", row, "Trust it from now on", Btn.Quiet, BtnSize.Md, Icon.Check, 0, false,
                    "This click hasn't been seen working yet: lets the rehearsal let go of pieces without waiting for Continue each time."))
            {
                config.RehearsalTrustLetGo = true;
                config.Save();
                OnFramework(e.Continue);
            }
            Kit.Reserve(row, Kit.Width, S(40));
        }
        else
        {
            Txt.Note("It carries on by itself once that's done.");
        }
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // The summary, as Done: the mark, one line of what happened, Got, Not got.

    private (int Got, int Trip, ErrandPlan? Plan, long Spent, string? Why, string Sentence, List<(uint ItemId, string Words)> Rows, List<LeftRow> Left) errandSummary =
        (-1, 0, null, 0, null, string.Empty, new(), new());

    private readonly Run[] summaryLine = new Run[1];

    private void DrawErrandsSummary()
    {
        var e = Errands;
        var stopped = e.State == RunState.Stopped;
        var byYou = e.StopReason?.StartsWith("Stopped by you", StringComparison.Ordinal) == true;
        var s = errandSummary;
        // The sentence says why it stopped, unless you stopped it.
        var why = stopped && !byYou ? e.StopReason : null;
        if (s.Got != e.GotCount || s.Trip != e.Trip || !ReferenceEquals(s.Plan, e.Plan) || s.Spent != e.Spent || s.Why != why)
        {
            var said = ErrandWords.Summary(e.GotCount, e.Trip, e.Spent);
            errandSummary = s = (e.GotCount, e.Trip, e.Plan, e.Spent, why, why != null ? $"{said}. {why}" : said + ".",
                ErrandWords.GotList(e.Got, data.AnyItemName), ErrandWords.LeftRows(e.Plan.Left, data.AnyItemName));
        }

        Frame.BeginHeader(28);
        var at = Kit.At;
        var w = Kit.Width;
        var (fill, ink, mark) = stopped
            ? byYou ? (Palette.LinePicked, Palette.Ash, Icon.Square) : (Palette.DangerWash, Palette.Danger, Icon.Alert)
            : (Palette.SuccessWash, Palette.Success, Icon.Check);
        Kit.Round(at, S(48), mark, S(24), fill, ink);
        var y = at.Y + S(48) + S(10);
        y += stopped ? Title(new Vector2(at.X, y), "Errands", "stopped", w) : Title(new Vector2(at.X, y), "Errands", "done", w);
        y += S(10);
        summaryLine[0] = new Run(s.Sentence, Face.Sans, 14, Palette.Ash);
        y += Txt.Flow(new Vector2(at.X, y), w, summaryLine, 21, out _);
        Kit.Reserve(at, w, y - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##errands-done-body", "errands-done-foot", 22, 22);
        SectionLabel("GOT", s.Rows.Count > 0 ? Words.Count(e.GotCount, "thing") : null);
        Kit.Gap(8);
        Kit.BeginPanel("##errand-got", 0, 0, 0);
        if (s.Rows.Count == 0)
        {
            var p = Kit.At;
            Txt.Line(Kit.Dl, new Vector2(p.X + S(14), p.Y + S(12)), "Nothing.", Face.Sans, 13, Palette.Dim, 19.5f);
            Kit.Reserve(p, Kit.Width, S(12) * 2 + S(19.5f));
        }
        for (var i = 0; i < s.Rows.Count; i++) GotRow(i, s.Rows[i].ItemId, s.Rows[i].Words);
        Kit.EndPanel();

        if (s.Left.Count > 0)
        {
            Kit.Gap(14);
            SectionLabel("NOT GOT", null);
            Kit.Gap(8);
            Kit.BeginPanel("##errand-left-end", 0, 0, 0);
            for (var i = 0; i < s.Left.Count; i++) LeftRowDraw(i, s.Left[i]);
            Kit.EndPanel();
        }
        Kit.Gap(14);
        DrawLog();
        Frame.EndBody();

        Frame.BeginFooter("errands-done-foot", 18, 22);
        var row = Kit.At;
        // Done goes on to the placing run's own result (what needs you, Put them back, the fixtures)
        // when the errands placed something: it used to skip it, and with it the only undo.
        var placingResult = vm.ResultWaiting && runner.State is RunState.Finished or RunState.Stopped;
        var finish = placingResult ? "See what was placed" : "Done";
        if (Kit.Button("##errands-finish", row, finish, Btn.Primary, BtnSize.Lg, Icon.Check, Kit.Width))
        {
            e.Dismiss();
            errandsOpen = false;
        }
        Kit.Reserve(row, Kit.Width, S(46));
        Frame.EndFooter();
    }

    /// <summary>A thing got: its icon and name ×count (offscreen rows only take their room).</summary>
    private void GotRow(int index, uint itemId, string words)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var h = S(10) * 2 + S(34);
        var clipTop = ImGui.GetWindowPos().Y;
        if (at.Y > clipTop + ImGui.GetWindowHeight() || at.Y + h < clipTop)
        {
            Kit.Reserve(at, w, h);
            return;
        }
        if (index > 0) Kit.Divider(Kit.Panel.Start.X, Kit.Panel.Start.X + Kit.Panel.Width, at.Y - 0.5f);
        Kit.Thumb(new Vector2(at.X + S(14), at.Y + S(10)), S(34), S(8), data.ItemIcon(itemId), S(16));
        var x = at.X + S(14 + 34 + 12);
        Txt.Line(Kit.Dl, new Vector2(x, at.Y), Txt.Fit(words, Face.SansMedium, 14, at.X + w - S(14) - x), Face.SansMedium, 14, Palette.Ivory, h / S(1));
        Kit.Reserve(at, w, h);
    }
}
