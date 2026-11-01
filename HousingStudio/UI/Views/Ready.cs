using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Ready (the 0.6 flow, docs/mockups/flow.html): a design loaded. The header; a banner when a run was
/// cut off; check chips (the item limit, clashes, dyes short…) that open a short detail; three stage
/// cards, Get (what's missing, where from, Get them for me first), Place (what Start does, and Keep /
/// Clear out / Swap) and Finish (the fixtures, Set them for me after placing, Tick off); and one
/// brass button whose words say what it does, with Preview.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Finish's Tick off: the fixtures' tick list shows.</summary>
    private bool fixturesOpen;
    private (Plan? Plan, int Count, Pace Pace, bool Dyes, TimeSpan Time) estimate;

    /// <summary>The time estimate, worked out again only when the plan or the pace changes.</summary>
    private TimeSpan Estimate(StartSummary s, List<PlanEntry> entries)
    {
        var key = (session.Plan, entries.Count, config.Pace, config.DyeAfterPlacing);
        if (estimate.Plan != key.Plan || estimate.Count != key.Count || estimate.Pace != key.Pace || estimate.Dyes != key.DyeAfterPlacing)
            estimate = (key.Plan, key.Count, key.Pace, key.DyeAfterPlacing, s.Estimate(entries, config.Pace));
        return estimate.Time;
    }

    /// <summary>The side Start works on: where you are, else the design's own.</summary>
    private Side ReadySide => session.PlanSide ?? session.Context.Side ?? (session.Layout is { Interior.Count: 0, Garden.Count: > 0 } ? Side.Garden : Side.Interior);

    /// <summary>The header pill for this design here: it fits and you can decorate, or why not.</summary>
    private (string Text, Tone Tone) ReadyPill()
    {
        var ctx = session.Context;
        var layout = session.Layout!;
        if (ctx.Side == null) return ("Go to your home to place it", Tone.Neutral);
        if (!ctx.HasPermission) return ctx.Kind == PlaceKind.Interior
            ? ($"In a {Words.HomeInSentence(ctx.HouseSize)} you can't decorate", Tone.Danger)
            : ("Go to your own plot to place its garden", Tone.Neutral);
        if (ctx.Side == Side.Interior)
        {
            if (layout.Interior.Count == 0) return ("Its pieces are all in the garden", Tone.Neutral);
            if (layout.HouseSize != null && ctx.HouseSize != null && !Districts.SameSize(layout.HouseSize, ctx.HouseSize))
                return ($"Made for a {Words.HomeInSentence(layout.HouseSize)} · you're in a {Words.HomeInSentence(ctx.HouseSize)}", Tone.Danger);
            return layout.HouseSize == null
                ? ($"In your {Words.HomeInSentence(ctx.HouseSize)} · you can decorate", Tone.Success)
                : ($"Fits your {Words.HomeInSentence(ctx.HouseSize)} · you can decorate", Tone.Success);
        }
        if (layout.Garden.Count == 0) return ("Its pieces are all inside", Tone.Neutral);
        if (session.Plan == null) return ("Can't tell which plot you're on", Tone.Danger);
        if (session.Warnings.Count > 0) return ("Made for another plot · you can decorate", Tone.Waiting);
        return ("Fits your garden · you can decorate", Tone.Success);
    }

    private void DrawReady()
    {
        var layout = session.Layout!;
        var side = ReadySide;
        var entries = Filtered().ToList();
        var plan = session.Plan;
        var summary = StartSummary.Of(entries, config.DyeAfterPlacing);

        // Header: eyebrow, the design's name, the pill; Pieces, Settings and close on the right.
        Frame.BeginHeader(24);
        var at = Kit.At;
        var w = Kit.Width;
        const string piecesLabel = "Pieces";
        var piecesW = Kit.ButtonWidth(piecesLabel, BtnSize.Md, Icon.List);
        var buttonsW = piecesW + S(8) + S(40) + S(8) + S(40);
        var leftW = w - buttonsW - S(16);
        var kind = layout.BuildName != null ? "Build" : "MakePlace";
        var eyebrowH = Kit.Eyebrow(at, $"{kind} · {(side == Side.Garden ? "garden" : "inside")}", null, leftW);
        var y = at.Y + eyebrowH + S(8);
        var titleH = Title(new Vector2(at.X, y), session.Title, null, leftW);
        y += titleH + S(8);
        var (pill, tone) = ReadyPill();
        // Below the buttons (40 tall, at the header's top), the pill has the whole width (0.3.4 kept it to the
        // width beside them, and cut "you can decorate" short).
        Kit.StatusPill(new Vector2(at.X, y), pill, tone, maxWidth: y >= at.Y + S(40) + S(4) ? w : leftW);
        y += S(28);
        var bx = at.X + w - buttonsW;
        if (Kit.Button("##pieces", new Vector2(bx, at.Y), piecesLabel, Btn.Glass, BtnSize.Md, Icon.List)) sub = Sub.Pieces;
        SettingsButton(new Vector2(bx + piecesW + S(8), at.Y));
        if (Kit.IconButton("##close", new Vector2(bx + piecesW + S(8) + S(48), at.Y), Icon.X, "Close this design"))
            Later(() =>
            {
                session.Unload();
                vm.Dismiss();
            });
        Kit.Reserve(at, w, y - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##ready", "ready-foot", 20, 22);
        if (DraggingLayout) DragHint();

        foreach (var warning in session.Warnings.ToArray())
        {
            AlertNote(warning, Palette.Danger, textColor: Palette.Text);
            Kit.Gap(10);
        }
        CareNotice();
        DrawCarryOnBanner();
        DrawReadyChecks(summary, entries.Count);
        DrawStages(layout, plan, summary);
        DyesPopover();
        Frame.EndBody();
        DropTarget();

        DrawReadyFooter(summary, entries);
    }

    /// <summary>While a file is dragged over the window: the body takes it.</summary>
    private void DragHint()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var h = S(56);
        Kit.Dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.BrassWash), S(Palette.RadiusXl));
        Kit.Dashed(at, at + new Vector2(w, h), S(Palette.RadiusXl), Palette.Brass, S(1.5f));
        const string text = "Let go to load it instead";
        var tw = Txt.Measure(text, Face.Sans, 14).X;
        Txt.Line(Kit.Dl, new Vector2(at.X + (w - tw) / 2, at.Y), text, Face.Sans, 14, Palette.BrassSoft, h / S(1));
        Kit.Reserve(at, w, h);
        Kit.Gap(18);
    }

    /// <summary>"See which": the dyes this design needs beyond your bags, with Copy list.</summary>
    private void DyesPopover()
    {
        if (!Kit.BeginMenu("##dyes-missing")) return;
        var missing = vm.MissingDyes;
        var pos = ImGui.GetCursorScreenPos();
        var w = S(260);
        var dl = ImGui.GetWindowDrawList();
        Txt.Spaced(dl, pos + new Vector2(S(12), S(8)), "DYES TO BUY", Face.Mono, 12, Palette.Brass, 0.18f);
        ImGui.Dummy(new Vector2(w, S(8) + Txt.Natural(Face.Mono, 12) + S(6)));
        foreach (var (stain, name, count) in missing)
        {
            var p = ImGui.GetCursorScreenPos();
            var h = S(32);
            Kit.Swatch(dl, p + new Vector2(S(12) + S(5), h / 2), data.DyeRgb(stain));
            Txt.Line(dl, p + new Vector2(S(12 + 10 + 8), 0), name, Face.Sans, 13, Palette.Ivory, 32);
            var c = $"×{count}";
            var cw = Txt.Measure(c, Face.Mono, 12).X;
            Txt.Line(dl, p + new Vector2(w - S(12) - cw, 0), c, Face.Mono, 12, Palette.Dim, 32);
            ImGui.Dummy(new Vector2(w, h));
        }
        ImGui.Dummy(new Vector2(w, S(6)));
        var bp = ImGui.GetCursorScreenPos() + new Vector2(S(6), 0);
        if (Kit.Button("##copy-dyes", bp, "Copy list", Btn.Glass, BtnSize.Sm, Icon.Clipboard, w - S(12)))
        {
            ImGui.SetClipboardText(DyeList(missing.Select(m => (m.Name, m.Count))));
            Notify("Dye list copied");
            ImGui.CloseCurrentPopup();
        }
        ImGui.SetCursorScreenPos(bp);
        ImGui.Dummy(new Vector2(w - S(12), S(34) + S(6)));
        Kit.EndMenu();
    }

    private string DyeList(IEnumerable<(string Name, int Count)> dyes)
    {
        var sb = new StringBuilder();
        sb.Append(session.Title).AppendLine(": dyes to buy").AppendLine();
        foreach (var (name, count) in dyes) sb.Append(count).Append(" × ").AppendLine(name);
        return sb.ToString().TrimEnd();
    }

    // ---------------------------------------------------------------------------------------------
    // The three stages: Get, Place, Finish. The first not done is the one in hand.

    private void DrawStages(Layout layout, Plan? plan, StartSummary s)
    {
        var shop = ShopPlan();
        var getDone = plan != null && shop.IsEmpty;
        var placeDone = plan != null && s.Moves.Count + s.Places + s.Dyes == 0 && ChosenClearOut() == null;
        var fixtures = FixtureCount(layout);
        var ticked = fixtures > 0 ? FixturesDone(layout) : 0;
        var finishDone = ticked >= fixtures;

        DrawGetStage(plan, shop, getDone ? StageState.Done : StageState.Now);
        DrawPlaceStage(layout, plan, s, placeDone ? StageState.Done : getDone ? StageState.Now : StageState.Todo);
        if (fixtures > 0)
            DrawFinishStage(layout, fixtures, ticked, finishDone ? StageState.Done : getDone && placeDone ? StageState.Now : StageState.Todo);
    }

    private const string ReadyGet = "GET";
    private const string SeeList = "See the list";
    private const string GetFirstLabel = "Get them for me first";

    /// <summary>The Get stage's words for a shopping plan (made again only when the plan changes).</summary>
    private (ShoppingPlan? Shop, int Things, string Big, string? Aside, (Icon Icon, string Count, string Label)[] Sources) getWords =
        (null, 0, string.Empty, null, Array.Empty<(Icon, string, string)>());

    private static readonly (SourceKind Source, Icon Icon, string Label)[] GetSources =
    {
        (SourceKind.Retainer, Icon.Bell, "retainers"),
        (SourceKind.Vendor, Icon.Store, "vendors"),
        (SourceKind.Exchange, Icon.Store, "currency"),
        (SourceKind.Market, Icon.Scale, "market"),
        (SourceKind.Craft, Icon.Hammer, "craft"),
        (SourceKind.Unknown, Icon.Info, "not known"),
    };

    private (ShoppingPlan? Shop, int Things, string Big, string? Aside, (Icon Icon, string Count, string Label)[] Sources) GetWords(ShoppingPlan shop)
    {
        if (ReferenceEquals(getWords.Shop, shop)) return getWords;
        var things = shop.Lines.Sum(l => l.Count);
        var sources = new List<(Icon, string, string)>();
        foreach (var (source, icon, label) in GetSources)
            if (shop.Count(source) is > 0 and var n) sources.Add((icon, n.ToString(), label));
        return getWords = (shop, things, $"{things} missing", shop.TotalGil > 0 ? $"about {ShoppingList.Gil(shop.TotalGil)}" : null, sources.ToArray());
    }


    /// <summary>Get: what's missing (pieces and dyes) with the gil, where it comes from, Get them for me first, and See the list.</summary>
    private void DrawGetStage(Plan? plan, ShoppingPlan shop, StageState state)
    {
        Kit.BeginStage("##stage-get", 1, state, first: true);
        if (plan == null)
        {
            Kit.StageHead(ReadyGet, "Not known yet");
            Kit.Gap(4);
            Txt.Note("Go to your home: what's missing is worked out from your storeroom and bags there.");
        }
        else if (shop.IsEmpty)
        {
            Kit.StageHead(ReadyGet, "You have everything");
        }
        else
        {
            var g = GetWords(shop);
            Kit.StageHead(ReadyGet, g.Big, g.Aside);
            Kit.Gap(10);
            var row = new Kit.Wrap(8);
            foreach (var (icon, count, label) in g.Sources)
                Kit.CountChip(row.Next(Kit.CountChipWidth(count, label, icon), S(26)), count, label, icon);
            row.End();
            Kit.Gap(10);
            var at = Kit.At;
            var w = Kit.Width;
            var listW = Kit.MoreButtonWidth(SeeList);
            var listAt = new Vector2(at.X + w - listW, at.Y);
            var h = S(30);
            if (Errands.Enabled)
            {
                var beside = Kit.LabeledSwitchWidth(GetFirstLabel, true) + S(12) + listW <= w;
                if (Kit.LabeledSwitch("##get-first", new Vector2(at.X, at.Y + S(3)), config.GetFirst, GetFirstLabel, false,
                        "The button below opens the errands first. They fetch what's missing from your retainers, vendors and the market board, then place. You see the gil before anything is bought."))
                {
                    config.GetFirst = !config.GetFirst;
                    config.Save();
                }
                if (!beside)
                {
                    listAt = new Vector2(at.X, at.Y + S(30) + S(6));
                    h += S(6) + S(30);
                }
            }
            if (Kit.MoreButton("##see-list", listAt, SeeList, Icon.ChevronRight, "Where each thing comes from, with the gil and Copy list"))
                OpenShopping(Sub.None);
            Kit.Reserve(at, w, h);
        }
        Kit.EndStage();
    }

    private const string ReadyPlace = "PLACE";

    /// <summary>The Place stage's words and cells for what Start does (made again only when the counts change).</summary>
    private (int Moves, int Places, int Dyes, int InPlace, string Big, string? Aside, (int, string)[] Cells) placeStage =
        (-1, 0, 0, 0, string.Empty, null, Array.Empty<(int, string)>());

    /// <summary>Place: what Start does (to move, to place, to dye), what's already in place, and Keep / Clear out / Swap.</summary>
    private void DrawPlaceStage(Layout layout, Plan? plan, StartSummary s, StageState state)
    {
        Kit.BeginStage("##stage-place", 2, state);
        if (plan == null)
        {
            var count = ReadySide == Side.Garden ? layout.Garden.Count : layout.Interior.Count;
            Kit.StageHead(ReadyPlace, Words.Count(count, "piece"));
            Kit.Gap(4);
            Txt.Note("Go to your home to see what Start does: what's placed there is matched against the design, what moves, what comes from your storeroom and bags, and what gets dyed.");
            Kit.EndStage();
            return;
        }
        var p = placeStage;
        if (p.Moves != s.Moves.Count || p.Places != s.Places || p.Dyes != s.Dyes || p.InPlace != s.InPlace)
            p = placeStage = (s.Moves.Count, s.Places, s.Dyes, s.InPlace, ReadyWords.PlaceBig(s.Moves.Count + s.Places, s.Dyes),
                s.InPlace > 0 ? $"{s.InPlace} already in place" : null,
                new[] { (s.Moves.Count, "to move"), (s.Places, "to place"), (s.Dyes, "to dye") });
        Kit.StageHead(ReadyPlace, p.Big, p.Aside);
        if (s.Moves.Count + s.Places + s.Dyes > 0)
        {
            Kit.Gap(10);
            Kit.StatCells(p.Cells);
        }
        if (s.Places > 0)
        {
            // Placing by hand (Settings): you put each piece down, then it's set where the design has it.
            var storeroomByHand = s.Storeroom > 0 && (ReadySide == Side.Garden || !config.StoreroomAutomatic);
            var bagsByHand = s.Bags > 0 && !config.BagsAutomatic;
            if (storeroomByHand || bagsByHand)
            {
                Kit.Gap(8);
                Txt.Note((storeroomByHand || s.Storeroom == 0) && (bagsByHand || s.Bags == 0)
                    ? "You put each one down, anywhere; it's then set exactly where the design has it."
                    : storeroomByHand ? "You put the storeroom ones down, anywhere; each is then set where the design has it."
                    : "You put the ones from your bags down, anywhere; each is then set where the design has it.");
            }
        }
        DrawPlaceChoice(plan);
        Kit.EndStage();
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures to set yourself, tickable, the ticks kept per design.

    private static readonly string[] LevelOrder = { "Ground Floor", "Upper Floor", "Basement", "Main", "Exterior" };

    private static string LevelName(string level) => level switch
    {
        "Main" => "The whole home",
        "Exterior" => "Outside",
        _ => level.Length > 0 ? char.ToUpperInvariant(level[0]) + level[1..].ToLowerInvariant() : level,
    };

    private static IEnumerable<LayoutFixture> Fixtures(Layout layout) => layout.InteriorFixtures.Concat(layout.ExteriorFixtures);

    private static string FixtureKey(LayoutFixture f) => FixtureTicks.KeyOf(f.Level, f.Type, f.ItemId);

    private string DesignKey => FixtureTicks.DesignKey(session.FilePath, session.Title);

    private (Layout? Layout, DateTime Until, int Done) fixturesDone = (null, DateTime.MinValue, 0);

    /// <summary>How many of the design's fixtures are ticked (counted again once a second, or when you tick one: the fixtures run ticks them too).</summary>
    private int FixturesDone(Layout layout)
    {
        var now = DateTime.UtcNow;
        if (ReferenceEquals(fixturesDone.Layout, layout) && now < fixturesDone.Until) return fixturesDone.Done;
        var done = FixtureTicks.CountDone(config.FixturesDone, DesignKey, Fixtures(layout).Select(FixtureKey));
        fixturesDone = (layout, now + TimeSpan.FromSeconds(1), done);
        return done;
    }

    /// <summary>"Walls, floors and lights on 3 floors, plus the window and door".</summary>
    private static string FixtureSummary(Layout layout)
    {
        var floors = layout.InteriorFixtures.Where(f => f.Level is not "Main").ToList();
        var main = layout.InteriorFixtures.Where(f => f.Level == "Main").ToList();
        var parts = new List<string>();
        var levels = floors.Select(f => f.Level).Distinct().Count();
        if (floors.Count > 0) parts.Add($"{Words.Kinds(floors.Select(f => f.Type), true)} on {Words.Count(levels, "floor")}");
        if (main.Count > 0) parts.Add(parts.Count == 0 ? $"The {Words.Kinds(main.Select(f => f.Type), false, plural: false)}" : $"plus the {Words.Kinds(main.Select(f => f.Type), false, plural: false)}");
        if (layout.ExteriorFixtures.Count > 0) parts.Add(parts.Count == 0 ? "The outside" : "plus the outside");
        return string.Join(", ", parts);
    }

    private static int FixtureCount(Layout layout) => layout.InteriorFixtures.Count + layout.ExteriorFixtures.Count;

    private const string ReadyFinish = "FINISH";
    private const string AfterLabel = "Set them for me after placing";
    private const string TickOff = "Tick off";

    /// <summary>Finish's line: what the fixtures are, and what the plugin can set from your bags (read again once a second).</summary>
    private (Layout? Layout, DateTime Until, string Line) finishLine = (null, DateTime.MinValue, string.Empty);

    private string FinishLine(Layout layout)
    {
        var now = DateTime.UtcNow;
        if (ReferenceEquals(finishLine.Layout, layout) && now < finishLine.Until) return finishLine.Line;
        var line = FixtureSummary(layout) + ".";
        if (Plugin.FixtureRun.Enabled && session.Context.Side != null) line += $" {FixtureBagWords()}.";
        finishLine = (layout, now + TimeSpan.FromSeconds(1), line);
        return line;
    }

    /// <summary>
    /// Finish: the fixtures still to set and how many are ticked, what they are, "Set them for me
    /// after placing", and Tick off (the tick list, with Set them for me). While the
    /// plugin sets them, its step, Continue and Stop.
    /// </summary>
    private void DrawFinishStage(Layout layout, int all, int done, StageState state)
    {
        var left = all - done;
        Kit.BeginStage("##stage-finish", 3, state);
        Kit.StageHead(ReadyFinish, left <= 0 ? "All set" : Words.Count(left, "fixture"), $"{done} of {all} set");
        Kit.Gap(4);
        Txt.Note(FinishLine(layout));
        if (!DrawFixtureRunPanel(10, 0))
        {
            Kit.Gap(10);
            var at = Kit.At;
            var w = Kit.Width;
            var chevron = fixturesOpen ? Icon.ChevronDown : Icon.ChevronRight;
            var tickW = Kit.MoreButtonWidth(TickOff, chevron);
            var tickAt = new Vector2(at.X + w - tickW, at.Y);
            var h = S(30);
            if (Plugin.FixtureRun.Enabled && left > 0)
            {
                var beside = Kit.LabeledSwitchWidth(AfterLabel, true) + S(12) + tickW <= w;
                if (Kit.LabeledSwitch("##fixtures-after", new Vector2(at.X, at.Y + S(3)), config.FixturesAfterPlacing, AfterLabel, false,
                        "When placing from here finishes, the game's fixtures window is opened and each fixture put in from your bags, step by step. You press its apply."))
                {
                    config.FixturesAfterPlacing = !config.FixturesAfterPlacing;
                    config.Save();
                }
                if (!beside)
                {
                    tickAt = new Vector2(at.X, at.Y + S(30) + S(6));
                    h += S(6) + S(30);
                }
            }
            if (Kit.MoreButton("##tick-off", tickAt, TickOff, chevron, "Tick each fixture as it's set (kept for this design)")) fixturesOpen = !fixturesOpen;
            Kit.Reserve(at, w, h);
            if (fixturesOpen) DrawFixtureTicks(layout);
        }
        Kit.EndStage();
    }

    /// <summary>Tick off: how to set them, Set them for me, and the fixtures by floor, each with its tick box.</summary>
    private void DrawFixtureTicks(Layout layout)
    {
        Kit.Gap(10);
        Txt.Note(Plugin.FixtureRun.Enabled
            ? "Set them with the game's Housing → Remodel Interior (or Remodel Exterior), or let the plugin put them in from your bags (experimental). Tick each one as it's set."
            : "The plugin doesn't set these: use the game's Housing → Remodel Interior (or Remodel Exterior), and tick each one as you set it.");
        Kit.Gap(10);
        DrawSetFixtures();
        var design = DesignKey;
        Kit.BeginPanel("##fixture-ticks", 0, 0, 8, Palette.Well, Palette.Line, Palette.RadiusCard);
        var first = true;
        foreach (var group in Fixtures(layout).GroupBy(f => f.Level).OrderBy(g => LevelRank(g.Key)))
        {
            var at = Kit.At;
            var w = Kit.Width;
            if (!first) Kit.Divider(at.X, at.X + w, at.Y);
            first = false;
            var h = S(14) + Txt.Natural(Face.Mono, 12) + S(4);
            Txt.Spaced(Kit.Dl, at + new Vector2(S(20), S(14)), LevelName(group.Key).ToUpperInvariant(), Face.Mono, 12, Palette.Dim, 0.18f);
            Kit.Reserve(at, w, h);
            foreach (var f in group) FixtureRow(design, f);
        }
        Kit.EndPanel();
    }

    private static int LevelRank(string level)
    {
        var i = Array.IndexOf(LevelOrder, level);
        return i >= 0 ? i : LevelOrder.Length;
    }

    private void FixtureRow(string design, LayoutFixture f)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var key = FixtureKey(f);
        var on = FixtureTicks.IsDone(config.FixturesDone, design, key);
        var nameH = Txt.Natural(Face.Sans, 14);
        var h = S(10) * 2 + nameH + S(2) + S(19.5f);
        var (clicked, hovered, _) = Kit.Hit($"##fixture{key}", at, new Vector2(w, h));
        if (clicked)
        {
            FixtureTicks.Set(config.FixturesDone, design, key, !on);
            config.Save();
            on = !on;
            fixturesDone.Until = DateTime.MinValue;
        }
        if (hovered) Kit.RowFill(at, at + new Vector2(w, h), Palette.RowHover, false, false);
        Kit.TickBox(new Vector2(at.X + S(20), at.Y + (h - S(18)) / 2), on, hovered);
        var x = at.X + S(20 + 18 + 12);
        var name = f.Name.Length > 0 ? f.Name : data.ItemName(f.ItemId);
        var dl = Kit.Dl;
        Txt.Line(dl, new Vector2(x, at.Y + S(10)), Txt.Fit(name, Face.Sans, 14, at.X + w - S(20) - x), Face.Sans, 14, on ? Palette.Ash : Palette.Ivory);
        var note = new List<Run> { Run.Note(f.Type) };
        if (f.DyeHex != null)
        {
            var stain = data.StainOf(f.DyeHex);
            note.Add(Run.Note(" · "));
            note.Add(Run.Dot(Dyes.RgbOf(f.DyeHex)));
            note.Add(Run.Note(data.DyeName(stain)));
        }
        Txt.Flow(new Vector2(x, at.Y + S(10) + nameH + S(2)), at.X + w - S(20) - x, note, 19.5f, out _);
        Kit.Reserve(at, w, h);
    }

    /// <summary>
    /// A disclosure row (16px or 14px 20px inside): the chevron, a title and its note, and something
    /// on the right (a chip, a count, a Copy button). Pressing the row opens or closes it; an
    /// interactive right part keeps its own clicks. Returns true when toggled.
    /// </summary>
    private static bool Disclosure(string id, ref bool open, string title, string? note, float padY, float rightW, float rightH,
        Action<Vector2, float>? right, bool rightInteractive, Vector4? chevronColor = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var x = at.X + S(20);
        var textX = x + S(16) + S(12);
        var rightX = at.X + w - S(20) - rightW;
        var textW = (right != null ? rightX - S(12) : at.X + w - S(20)) - textX;
        var titleH = Txt.Natural(Face.SansMedium, 14);
        var noteRuns = note != null ? new[] { Run.Note(note) } : Array.Empty<Run>();
        var noteH = note != null ? Txt.Flow(Vector2.Zero, textW, noteRuns, 19.5f, out _, draw: false) : 0;
        var wordsH = titleH + (note != null ? S(2) + noteH : 0);
        var h = S(padY) * 2 + MathF.Max(wordsH, rightH);
        var hitW = right != null && rightInteractive ? rightX - S(6) - at.X : w;

        var (clicked, hovered, _) = Kit.Hit(id, at, new Vector2(hitW, h));
        if (clicked) open = !open;
        if (hovered) Kit.RowFill(at, at + new Vector2(w, h), Palette.RowHover, true, !open);
        Kit.FocusRing(at + new Vector2(S(4)), at + new Vector2(hitW, h) - new Vector2(S(4)), S(12));
        var dl = Kit.Dl;
        var mid = at.Y + h / 2;
        Icons.Draw(dl, open ? Icon.ChevronDown : Icon.ChevronRight, new Vector2(x, mid - S(8)), S(16), chevronColor ?? Palette.Ash);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(textX, top), Txt.Fit(title, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ivory);
        if (note != null) Txt.Flow(new Vector2(textX, top + titleH + S(2)), textW, noteRuns, 19.5f, out _);
        right?.Invoke(new Vector2(rightX, mid - rightH / 2), rightH);
        Kit.Reserve(at, w, h);
        return clicked;
    }

    // ---------------------------------------------------------------------------------------------
    // The footer: one brass button that says what it does, Preview, and how long.

    private (ReadyMode Mode, int Things, int Clear, bool Getting, bool Fixtures, string Label) goLabel = (ReadyMode.Keep, -1, 0, false, false, string.Empty);
    private (TimeSpan Time, Pace Pace, bool Getting, int Trips, string? NotGetting, string Hint) goHint = (TimeSpan.Zero, Pace.Quick, false, -1, null, string.Empty);
    private (Side Side, bool Apartment, bool Backup, string Tip) goTip = (Side.Interior, false, false, string.Empty);

    /// <summary>What the brass button does now (Ready's, and Done's after a preview), with its words.</summary>
    /// <param name="Getting">It opens the errands' estimate (Get first is on and they can get something and go now).</param>
    /// <param name="NotGetting">Get first is on but the errands can't go (why); the button places what you have.</param>
    private readonly record struct GoPlan(bool Getting, string? NotGetting, ReadyMode Mode, IReadOnlyList<PlacedPiece>? Clear, bool FixturesAfter, string Label);

    /// <summary>
    /// The brass button as it stands: with "Get them for me first" on, it opens the errands only when
    /// their estimate gets something and they can set out now (both cached as the errands' screen caches
    /// them); else it places (Keep, Clear out or Swap), and says so.
    /// </summary>
    private GoPlan ReadyGo()
    {
        var shop = ShopPlan();
        string? notGetting = null;
        if (config.GetFirst && Errands.Enabled && session.Plan != null && !shop.IsEmpty)
            notGetting = Plugin.FixtureRun.Active ? "Wait until the fixtures are set." : ErrandsBlocked(ErrandEstimate().Plan);
        var getting = config.GetFirst && Errands.Enabled && session.Plan != null && !shop.IsEmpty && notGetting == null;
        var mode = ModeNow;
        var clear = ChosenClearOut();
        var things = getting ? GetWords(shop).Things : 0;
        var fixturesAfter = config.FixturesAfterPlacing && Plugin.FixtureRun.Enabled && session.Layout is { } layout
                            && FixtureCount(layout) > 0 && FixturesDone(layout) < FixtureCount(layout);
        var g = goLabel;
        if (g.Mode != mode || g.Things != things || g.Clear != (clear?.Count ?? 0) || g.Getting != getting || g.Fixtures != fixturesAfter)
            g = goLabel = (mode, things, clear?.Count ?? 0, getting, fixturesAfter, ReadyWords.Go(mode, things, clear?.Count ?? 0, getting, fixturesAfter));
        return new GoPlan(getting, notGetting, mode, clear, fixturesAfter, g.Label);
    }

    /// <summary>The brass button pressed: the errands' estimate, else Swap (asks first), else Start (asks before a clear out).</summary>
    private void Go(GoPlan g, List<PlanEntry> entries)
    {
        if (g.Getting)
        {
            errandsRehearse = false;
            errandsOpen = true;
        }
        else if (g.Mode == ReadyMode.Swap && g.Clear != null) AskSwap(KeepAsName(), g.Clear.Count);
        else StartFromReady(entries);
    }

    /// <summary>The brass button's tooltip: what Start does to the game while it runs (the old note under Ready).</summary>
    private string StartTip(Side side)
    {
        var apartment = session.Context.HouseSize == "Apartment";
        var t = goTip;
        if (t.Tip.Length > 0 && t.Side == side && t.Apartment == apartment && t.Backup == config.BackupBeforePlacing) return t.Tip;
        var stay = side == Side.Garden ? "Stay on your plot while it runs." : apartment ? "Stay in the apartment while it runs." : "Stay in the house while it runs.";
        var editor = side == Side.Garden ? "It opens the outdoor furnishing editor" : "It opens the furnishing editor";
        var backup = config.BackupBeforePlacing ? ", and saves a backup of what's placed first." : ". No backup is saved first (Settings).";
        goTip = (side, apartment, config.BackupBeforePlacing, $"{stay} {editor} and switches its modes for you{backup}");
        return goTip.Tip;
    }

    private void DrawReadyFooter(StartSummary s, List<PlanEntry> entries)
    {
        Frame.BeginFooter("ready-foot", 18, 22);
        var at = Kit.At;
        var w = Kit.Width;
        var g = ReadyGo();
        var nothing = session.Plan != null && s.Moves.Count + s.Places + s.Dyes == 0 && g.Clear == null;
        // Getting first opens the errands' estimate, which says itself why they can't go yet.
        var blocked = g.Getting ? null : runner.CanStart(false) ?? (nothing ? "Nothing to do: every piece you have is in place" : null);
        var previewBlocked = runner.CanStart(true) ?? (nothing ? "Nothing to preview" : null);
        // Rehearse the run goes as the brass button would: the errands (which say why they can't go), else placing.
        var rehearseBlocked = g.Getting ? null : runner.CanStart(false) ?? (nothing ? "Nothing to rehearse" : null);

        const string preview = "Preview";
        var pw = Kit.ButtonWidth(preview, BtnSize.Lg, Icon.Eye);
        // Side by side when the button's words fit beside Preview; else Preview goes under it.
        var beside = Kit.ButtonWidth(g.Label, BtnSize.Lg, Icon.Play, Btn.Primary) <= w - pw - S(10);
        var goW = beside ? w - pw - S(10) : w;
        var tip = g.Getting ? "Opens what the errands would get, the trips and the gil, before anything is bought. They place it all after." : StartTip(ReadySide);
        if (Kit.Button("##start", at, g.Label, Btn.Primary, BtnSize.Lg, Icon.Play, goW, blocked != null, blocked ?? tip))
            Go(g, entries);
        var previewAt = beside ? new Vector2(at.X + w - pw, at.Y) : new Vector2(at.X, at.Y + S(46) + S(10));
        // Preview offers two: Mark the spots (moves nothing) and Rehearse the run (every step, nothing committed).
        var neither = previewBlocked != null && rehearseBlocked != null;
        if (Kit.Button("##preview", previewAt, preview, Btn.Glass, BtnSize.Lg, Icon.Eye, beside ? 0 : w, neither,
                neither ? previewBlocked : "Mark the spots (moves nothing), or rehearse the run (does every step, commits nothing)."))
            ImGui.OpenPopup(PreviewMenu);
        DrawPreviewMenu(g, entries, previewBlocked, rehearseBlocked);
        Kit.Reserve(at, w, beside ? S(46) : S(46) * 2 + S(10));
        Kit.Gap(12);

        var time = Estimate(s, entries);
        var trips = g.Getting ? ErrandEstimate().Plan.Trips.Count : 0;
        var hint = goHint;
        if (hint.Time != time || hint.Pace != config.Pace || hint.Getting != g.Getting || hint.Trips != trips || hint.NotGetting != g.NotGetting)
            hint = goHint = (time, config.Pace, g.Getting, trips, g.NotGetting,
                ReadyWords.Hint(Words.About(time), PaceSetting.Word(config.Pace), g.Getting, trips, g.NotGetting));
        footNote[0] = Run.Note(blocked ?? hint.Hint);
        Txt.Para(footNote);
        Frame.EndFooter();
    }

    private readonly Run[] footNote = new Run[1];
}
