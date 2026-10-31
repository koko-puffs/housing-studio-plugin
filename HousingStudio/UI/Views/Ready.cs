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

/// <summary>Ready (mockup 2): a design loaded. What Start does, the fixtures to set yourself, and Start / Preview.</summary>
public sealed partial class MainWindow
{
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
        var kind = layout.BuildName != null ? "Build file" : "MakePlace file";
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

        Frame.BeginBody("##ready", "ready-foot", 22, 22);
        if (DraggingLayout) DragHint();

        foreach (var warning in session.Warnings.ToArray())
        {
            AlertNote(warning, Palette.Danger, textColor: Palette.Text);
            Kit.Gap(10);
        }
        DrawReadyCare(entries);

        Kit.EyebrowRow("What Start does");
        Kit.Gap(10);
        if (plan == null) DrawNotMatched();
        else DrawStartSteps(summary);
        var line = SummaryLine(summary, entries.Count, plan);
        if (line.Length > 0)
        {
            Kit.Gap(10);
            Txt.Note(line);
        }

        if (layout.InteriorFixtures.Count + layout.ExteriorFixtures.Count > 0)
        {
            Kit.Gap(18);
            DrawFixtures(layout);
        }
        DrawDrift();

        Frame.BeginBottom("ready-bottom");
        var stay = side == Side.Garden ? "Stay on your plot while it runs."
            : session.Context.HouseSize == "Apartment" ? "Stay in the apartment while it runs." : "Stay in the house while it runs.";
        var editor = side == Side.Garden ? "It opens the outdoor furnishing editor" : "It opens the furnishing editor";
        var backup = config.BackupBeforePlacing ? ", and saves a backup of what's placed first." : ". No backup is saved first (Settings).";
        AlertNote($"{stay} {editor} and switches its modes for you{backup}", Palette.Dim, Icon.Info);
        Frame.EndBottom();
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

    /// <summary>Before you're home: one row saying so, in the steps' shape.</summary>
    private void DrawNotMatched()
    {
        Kit.BeginPanel("##steps-none", 0, 0, 0);
        StepRow(0, Icon.House, null, "Go to your home to see what it does",
            new[] { Run.Note("What's placed there is matched against the design: what moves, what comes from your storeroom and bags, and what gets dyed.") });
        Kit.EndPanel();
    }

    private void DrawStartSteps(StartSummary s)
    {
        Kit.BeginPanel("##steps", 0, 0, 0);
        var index = 0;
        if (s.Moves.Count > 0)
        {
            var names = s.Moves.Select(e => data.ItemName(e.Target.ItemId)).Distinct().ToList();
            var line = s.Moves.Count == 1 ? "Move the piece that's in the wrong spot" : $"Move the {s.Moves.Count} pieces that are in the wrong spot";
            StepRow(index++, Icon.Move, s.Moves.Count, line, new[] { Run.Note(Words.Some(names)) });
        }
        if (s.Places > 0)
        {
            var garden = ReadySide == Side.Garden;
            var storeroomByHand = s.Storeroom > 0 && (garden || !config.StoreroomAutomatic);
            var bagsByHand = s.Bags > 0 && !config.BagsAutomatic;
            var line = s.Storeroom > 0 && s.Bags > 0 ? "Place pieces from your storeroom and bags"
                : s.Storeroom > 0 ? "Place pieces from your storeroom" : "Place pieces from your bags";
            string note;
            if ((storeroomByHand || s.Storeroom == 0) && (bagsByHand || s.Bags == 0))
                note = "You put each one down, anywhere; it's then set exactly where the design has it";
            else
            {
                note = s.Storeroom > 0 && s.Bags > 0 ? $"{s.Storeroom} from your storeroom, {s.Bags} from your bags · each set exactly where the design has it" : "Each set exactly where the design has it";
                if (storeroomByHand) note += " · you put the storeroom ones down";
                if (bagsByHand) note += " · you put the ones from your bags down";
            }
            StepRow(index++, Icon.Package, s.Places, line, new[] { Run.Note(note) });
        }
        if (s.Dyes > 0)
        {
            var missing = vm.MissingDyes;
            var note = missing.Count > 0
                ? new[]
                {
                    Run.Note(Words.Count(missing.Count, "dye") + " missing", Palette.Danger),
                    Run.Note(" · those pieces stay undyed · "),
                    new Run("See which", Face.Sans, 13, Palette.BrassSoft, Link: "dyes"),
                }
                : new[] { Run.Note("With dyes from your bags") };
            if (StepRow(index++, Icon.Droplet, s.Dyes, "Dye pieces to match", note) == "dyes") ImGui.OpenPopup("##dyes-missing");
            DyesPopover();
        }
        if (index == 0)
            StepRow(0, Icon.Check, null, "Nothing to do here", new[] { Run.Note("Every piece you have is where the design has it, in its dye.") }, done: true);
        Kit.EndPanel();
    }

    /// <summary>
    /// A step of "What Start does": the round brass-wash icon, the serif number, the line and its
    /// note; 16px 20px inside, a faint line between. Returns the link pressed in the note.
    /// </summary>
    private static string? StepRow(int index, Icon icon, int? number, string line, IReadOnlyList<Run> note, bool done = false)
    {
        var at = Kit.At;
        var w = Kit.Width;
        if (index > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);
        var x = at.X + S(20);
        var numText = number?.ToString() ?? string.Empty;
        var numW = number != null ? Txt.Measure(numText, Face.Serif, 26).X + S(16) : 0;
        var wordsX = x + S(40) + S(16) + numW;
        var wordsW = at.X + w - S(20) - wordsX;
        var lineH = Txt.Natural(Face.Sans, 15);
        var noteH = Txt.Flow(Vector2.Zero, wordsW, note, 19.5f, out _, draw: false);
        var wordsH = lineH + S(3) + noteH;
        var h = S(16) * 2 + MathF.Max(S(40), wordsH);
        var mid = at.Y + h / 2;

        var dl = Kit.Dl;
        Kit.Round(new Vector2(x, mid - S(20)), S(40), icon, S(16), done ? Palette.SuccessWash : Palette.BrassWash, done ? Palette.Success : Palette.Brass);
        if (number != null) Txt.Line(dl, new Vector2(x + S(56), mid - S(13)), numText, Face.Serif, 26, Palette.Ivory, 26);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(wordsX, top), Txt.Fit(line, Face.Sans, 15, wordsW), Face.Sans, 15, Palette.Ivory);
        Txt.Flow(new Vector2(wordsX, top + lineH + S(3)), wordsW, note, 19.5f, out var clicked);
        Kit.Reserve(at, w, h);
        return clicked;
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

    /// <summary>"2 pieces are already in place. 29 pieces in your home aren't in this design and stay where they are." and the like.</summary>
    private string SummaryLine(StartSummary s, int shown, Plan? plan)
    {
        var parts = new List<string>();
        if (plan != null)
        {
            if (s.InPlace > 0) parts.Add(s.InPlace == 1 ? "1 piece is already in place." : $"{s.InPlace} pieces are already in place.");
            if (plan.Extras.Count > 0)
                parts.Add(plan.Extras.Count == 1 ? "1 piece in your home isn't in this design and stays where it is." : $"{plan.Extras.Count} pieces in your home aren't in this design and stay where they are.");
            if (s.Missing > 0) parts.Add(s.Missing == 1 ? "You don't have 1 piece, so it's skipped (Pieces lists it)." : $"You don't have {s.Missing} pieces, so they're skipped (Pieces lists them).");
            if (config.NearMe) parts.Add($"Near me is on: only the {Words.Count(shown, "piece")} within {config.NearMeRadius:0} yalms of you.");
            var off = new[] { (config.ShowBasement, "basement"), (config.ShowGround, "ground floor"), (config.ShowUpper, "upper floor") }.Where(f => !f.Item1).Select(f => f.Item2).ToList();
            if (off.Count > 0 && ReadySide == Side.Interior) parts.Add($"The {Words.List(off)} {(off.Count == 1 ? "is" : "are")} left out (Settings → More options).");
            if (ReadySide == Side.Interior && !session.StoreroomLoaded)
                parts.Add("The storeroom isn't read yet: open Housing → Indoor Furnishings and its Storeroom tab once, and its pieces count too.");
        }
        var layout = session.Layout!;
        if (session.Context.Side == Side.Interior && layout.Garden.Count > 0)
            parts.Add($"Its {Words.Count(layout.Garden.Count, "garden piece")} {(layout.Garden.Count == 1 ? "is" : "are")} placed from your plot.");
        else if (session.Context.Side == Side.Garden && layout.Interior.Count > 0)
            parts.Add($"Its {Words.Count(layout.Interior.Count, "piece")} inside {(layout.Interior.Count == 1 ? "is" : "are")} placed from inside the house.");
        return string.Join(" ", parts);
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

    private int FixturesDone(Layout layout) => FixtureTicks.CountDone(config.FixturesDone, DesignKey, Fixtures(layout).Select(FixtureKey));

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

    private void DrawFixtures(Layout layout)
    {
        var all = Fixtures(layout).ToList();
        var done = FixturesDone(layout);
        Kit.BeginPanel("##fixtures", 0, 0, 0);
        var chip = $"{done} of {all.Count} done";
        var allDone = done == all.Count;
        Disclosure("##fixtures-row", ref fixturesOpen, "Fixtures to set yourself", FixtureSummary(layout), 16, Kit.ChipWidth(chip, allDone), S(24),
            (p, _) => Kit.Chip(p, chip, allDone), false);
        if (fixturesOpen)
        {
            Kit.PushBox(Kit.Left + S(20), Kit.Width - S(40));
            Txt.Note("The plugin doesn't set these: use the game's Housing → Interior Fixtures (or Exterior Fixtures), and tick each one as you set it.");
            Kit.Gap(6);
            Kit.PopBox();
            var design = DesignKey;
            var groups = all.GroupBy(f => f.Level).OrderBy(g => LevelRank(g.Key));
            foreach (var group in groups)
            {
                var at = Kit.At;
                var w = Kit.Width;
                Kit.Divider(at.X, at.X + w, at.Y);
                var h = S(14) + Txt.Natural(Face.Mono, 12) + S(4);
                Txt.Spaced(Kit.Dl, at + new Vector2(S(20), S(14)), LevelName(group.Key).ToUpperInvariant(), Face.Mono, 12, Palette.Dim, 0.18f);
                Kit.Reserve(at, w, h);
                var list = group.ToList();
                foreach (var f in list) FixtureRow(design, f);
            }
            Kit.Gap(8);
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
    // The footer: Start placing, Preview first, and how long.

    private void DrawReadyFooter(StartSummary s, List<PlanEntry> entries)
    {
        Frame.BeginFooter("ready-foot", 18, 22);
        var at = Kit.At;
        var w = Kit.Width;
        var nothing = session.Plan != null && s.Moves.Count + s.Places + s.Dyes == 0;
        var blocked = runner.CanStart(false) ?? (nothing ? "Nothing to do: every piece you have is in place" : null);
        var previewBlocked = runner.CanStart(true) ?? (nothing ? "Nothing to preview" : null);

        const string preview = "Preview first";
        var pw = Kit.ButtonWidth(preview, BtnSize.Lg, Icon.Eye);
        if (Kit.Button("##start", at, "Start placing", Btn.Primary, BtnSize.Lg, Icon.Play, w - pw - S(10), blocked != null, blocked))
            StartRun(false, entries);
        if (Kit.Button("##preview", new Vector2(at.X + w - pw, at.Y), preview, Btn.Glass, BtnSize.Lg, Icon.Eye, 0, previewBlocked != null,
                previewBlocked ?? "Walks through every piece, marking each spot in your home, and moves nothing."))
            StartRun(true, entries);
        Kit.Reserve(at, w, S(46));
        Kit.Gap(12);

        var left = blocked ?? $"{Words.Capital(Words.About(Estimate(s, entries)))} at a {PaceSetting.Word(config.Pace)} pace";
        const string right = "Preview marks each spot, moving nothing";
        var row = Kit.At;
        var lw = Txt.Measure(left, Face.Sans, 13).X;
        var rw = Txt.Measure(right, Face.Sans, 13).X;
        var dl = Kit.Dl;
        if (lw + S(16) + rw <= w)
        {
            Txt.Line(dl, row, left, Face.Sans, 13, Palette.Ash, 19.5f);
            Txt.Line(dl, new Vector2(row.X + w - rw, row.Y), right, Face.Sans, 13, Palette.Ash, 19.5f);
            Kit.Reserve(row, w, S(19.5f));
        }
        else
        {
            Txt.Para(new[] { Run.Note(left) });
            Txt.Para(new[] { Run.Note(right) });
        }
        Frame.EndFooter();
    }
}
