using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>A check chip's colour: neutral, a warning (danger words and edge), or brass (something to look at).</summary>
public enum ChipTone
{
    Normal,
    Warn,
    Brass,
}

/// <summary>Where a stage of the flow is: still to come, the one in hand, or done.</summary>
public enum StageState
{
    Todo,
    Now,
    Done,
}

/// <summary>One check chip of a row: its id, words, tone, and an icon or a meter (0–1, past 1 when over); the icon in its own colour when given (a green tick).</summary>
public readonly record struct CheckChipSpec(string Id, string Label, ChipTone Tone = ChipTone.Normal, Icon? Icon = null, float? Meter = null, string? Tip = null,
    Vector4? IconInk = null);

/// <summary>
/// The 0.6 flow's controls (docs/mockups/flow.html), drawn from the same tokens as the rest of the
/// kit: check chips and their detail, the small meter, stage cards with their round numbers and
/// headings, count chips, the Beta tag, a switch with its words, a quiet "See the list ›" button, the
/// one-line banner, stat cells and the journey bar.
/// </summary>
public static partial class Kit
{
    // ---------------------------------------------------------------------------------------------
    // Wrapping rows: chips laid left to right, onto the next line when the box is full.

    /// <summary>
    /// Lays things of known size left to right from the cursor across the box, wrapping (CSS
    /// <c>flex-wrap</c>). Keep it in a local: <c>var row = new Kit.Wrap(8); var p = row.Next(w, h); … row.End();</c>
    /// </summary>
    public struct Wrap
    {
        private readonly Vector2 start;
        private readonly float width;
        private readonly float gap;
        private float x;
        private float y;
        private float line;

        /// <param name="gapPx">The gap between things and between lines, CSS pixels.</param>
        public Wrap(float gapPx = 8)
        {
            start = At;
            width = Width;
            gap = S(gapPx);
            x = start.X;
            y = start.Y;
            line = 0;
        }

        /// <summary>Where the next thing goes (on a new line when it wouldn't fit on this one).</summary>
        public Vector2 Next(float w, float h)
        {
            if (x > start.X && x + w > start.X + width + 0.5f)
            {
                y += line + gap;
                x = start.X;
                line = 0;
            }
            var p = new Vector2(x, y);
            x += w + gap;
            line = MathF.Max(line, h);
            return p;
        }

        public readonly float Height => y - start.Y + line;

        /// <summary>Takes up the rows' space, so what follows goes under them.</summary>
        public readonly void End() => Reserve(start, width, Height);
    }

    // ---------------------------------------------------------------------------------------------
    // Check chips: short states you press for a detail (the item limit, clashes, dyes short).

    /// <summary>A meter's fill goes warm from here (a home nearly full).</summary>
    public const float MeterTight = 0.95f;

    public static float CheckChipWidth(string label, Icon? icon = null, bool meter = false) =>
        S(12) * 2 + (meter ? S(64 + 8) : icon != null ? S(14 + 8) : 0) + Txt.Measure(label, Face.Sans, 13).X;

    /// <summary>
    /// A check chip: a 30px pill in glass with the line edge, an icon or a meter, and its words; warn
    /// in danger, brass in brass-soft. Pointed at, or <paramref name="open"/> (its detail shows), the
    /// edge goes to the 0.34 line. <paramref name="iconInk"/> colours the icon alone (the plugins'
    /// green tick). Returns true when pressed.
    /// </summary>
    public static bool CheckChip(string id, Vector2 pos, string label, ChipTone tone = ChipTone.Normal, Icon? icon = null, float? meter = null,
        bool open = false, string? tip = null, Vector4? iconInk = null)
    {
        var w = CheckChipWidth(label, icon, meter != null);
        var h = S(30);
        var (clicked, hovered, held) = Hit(id, pos, new Vector2(w, h));
        if (tip != null && hovered) Tooltip(tip);
        var dl = Dl;
        var max = pos + new Vector2(w, h);
        var r = h / 2;
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Glass), r);
        if (held) dl.AddRectFilled(pos, max, Palette.U32(Palette.LinePicked), r);
        var lit = hovered || open;
        var (edge, ink) = tone switch
        {
            ChipTone.Warn => (lit ? Palette.Danger : Palette.WithAlpha(Palette.Danger, 0.3f), Palette.Danger),
            ChipTone.Brass => (lit ? Palette.Brass : Palette.BrassEdge, Palette.BrassSoft),
            _ => (lit ? Palette.LineHover : Palette.Line, lit ? Palette.Ivory : Palette.Ash),
        };
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(12);
        if (meter is { } m)
        {
            Meter(new Vector2(x, pos.Y + (h - S(6)) / 2), S(64), m);
            x += S(64 + 8);
        }
        else if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(14)) / 2), S(14), iconInk ?? ink);
            x += S(14 + 8);
        }
        Txt.Line(dl, new Vector2(x, pos.Y), label, Face.Sans, 13, ink, 30);
        FocusRing(pos, max, r);
        return clicked;
    }

    /// <summary>
    /// A row of check chips from the cursor, wrapping. <paramref name="openId"/> is the chip whose
    /// detail shows. Returns the index of the chip pressed, or -1.
    /// </summary>
    public static int CheckChips(IReadOnlyList<CheckChipSpec> chips, string? openId = null)
    {
        if (chips.Count == 0) return -1;
        var row = new Wrap(8);
        var pressed = -1;
        for (var i = 0; i < chips.Count; i++)
        {
            var c = chips[i];
            var p = row.Next(CheckChipWidth(c.Label, c.Icon, c.Meter != null), S(30));
            if (CheckChip(c.Id, p, c.Label, c.Tone, c.Icon, c.Meter, c.Id == openId, c.Tip, c.IconInk)) pressed = i;
        }
        row.End();
        return pressed;
    }

    /// <summary>
    /// The small meter, 6px tall: the faint track and a brass fill; warm (brass into danger) from
    /// <see cref="MeterTight"/>, danger past 1.
    /// </summary>
    public static void Meter(Vector2 pos, float width, float fraction)
    {
        var dl = Dl;
        var h = S(6);
        var r = h / 2;
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(Palette.Track), r);
        var f = Math.Clamp(fraction, 0, 1);
        if (f <= 0) return;
        var fw = MathF.Max(h, width * f);
        if (fraction > 1)
        {
            dl.AddRectFilled(pos, pos + new Vector2(fw, h), Palette.U32(Palette.Danger), r);
            return;
        }
        if (fraction < MeterTight)
        {
            dl.AddRectFilled(pos, pos + new Vector2(fw, h), Palette.U32(Palette.Brass), r);
            return;
        }
        // linear-gradient(90deg, brass, danger): a rounded brass cap, the gradient, a rounded danger cap.
        var brass = Palette.U32(Palette.Brass);
        var danger = Palette.U32(Palette.Danger);
        dl.AddCircleFilled(pos + new Vector2(r, r), r, brass, 12);
        dl.AddRectFilledMultiColor(pos + new Vector2(r, 0), pos + new Vector2(fw - r, h), brass, danger, danger, brass);
        dl.AddCircleFilled(pos + new Vector2(fw - r, r), r, danger, 12);
    }

    // A chip's detail: a short dashed panel under the row.
    private static readonly Stack<(Vector2 Start, float Width)> Details = new();

    /// <summary>Starts a chip's detail (the dashed 0.2 edge, radius 12, 10px 14px inside). Lay its rows, then <see cref="EndDetail"/>.</summary>
    public static void BeginDetail()
    {
        var at = At;
        var w = Width;
        Details.Push((at, w));
        Reserve(at, 1, S(10));
        PushBox(at.X + S(14), w - S(28));
    }

    public static void EndDetail()
    {
        PopBox();
        var (start, w) = Details.Pop();
        var bottom = ImGui.GetCursorScreenPos().Y + S(10);
        Dashed(start, new Vector2(start.X + w, bottom), S(12), Palette.LineStrong, 1f);
        Reserve(start, w, bottom - start.Y);
    }

    // ---------------------------------------------------------------------------------------------
    // Stage cards: Get, Place, Finish as numbered steps.

    private static readonly string[] Numbers = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

    private static readonly Stack<float> Stages = new();

    /// <summary>A stage's round number, 30px: to come (the strong edge, mono number), now (brass, dark number), done (green wash, a tick).</summary>
    public static void StageNumber(Vector2 pos, int number, StageState state)
    {
        var dl = Dl;
        var d = S(30);
        var c = pos + new Vector2(d / 2);
        var r = d / 2;
        switch (state)
        {
            case StageState.Done:
                dl.AddCircleFilled(c, r, Palette.U32(Palette.SuccessWash), 32);
                dl.AddCircle(c, r - 0.5f, Palette.U32(Palette.WithAlpha(Palette.Success, 0.45f)), 32, 1f);
                Icons.Draw(dl, Icon.Check, c - new Vector2(S(7.5f)), S(15), Palette.Success, 2.2f);
                return;
            case StageState.Now:
                dl.AddCircleFilled(c, r, Palette.U32(Palette.Brass), 32);
                break;
            default:
                dl.AddCircle(c, r - 0.5f, Palette.U32(Palette.LineStrong), 32, 1f);
                break;
        }
        var text = number is >= 0 and <= 9 ? Numbers[number] : number.ToString();
        var size = Txt.Measure(text, Face.Mono, 13);
        Txt.Draw(dl, c - size / 2, text, Face.Mono, 13, state == StageState.Now ? Palette.OnBrass : Palette.Ash);
    }

    /// <summary>
    /// Starts a stage card: a glass panel (14px 16px 14px 12px inside) with the number in a 34px
    /// column and the content beside it; after the first, a 10px gap with a 1px line joining it to the
    /// card above. Lay the content, then <see cref="EndStage"/>.
    /// </summary>
    public static void BeginStage(string id, int number, StageState state, bool first = false)
    {
        if (!first)
        {
            Gap(10);
            var a = At;
            var lx = a.X + S(28) + 0.5f;
            Dl.AddLine(new Vector2(lx, a.Y - S(11)), new Vector2(lx, a.Y + S(1)), Palette.U32(Palette.LineStrong), 1f);
        }
        var at = At;
        var w = Width;
        BeginPanel(id, 14, 0, 14);
        StageNumber(new Vector2(at.X + S(12), at.Y + S(14) + S(1)), number, state);
        PushBox(at.X + S(12 + 34 + 12), w - S(12 + 34 + 12 + 16));
        Stages.Push(at.Y);
    }

    public static void EndStage()
    {
        var top = Stages.Pop();
        PopBox();
        // At least as tall as its number.
        var least = top + S(14) + S(31);
        var y = ImGui.GetCursorScreenPos().Y;
        if (y < least) Reserve(At, 1, least - y);
        EndPanel();
    }

    /// <summary>
    /// A stage's heading on one baseline: its name (mono 11, capitals as given, .16em, dim), the big
    /// words (serif 22, ivory), and an aside on the right (13, ash), which goes under them when narrow.
    /// </summary>
    public static void StageHead(string label, string big, string? aside = null, Vector4? bigColor = null)
    {
        var at = At;
        var w = Width;
        var dl = Dl;
        var lineCss = 22 * 1.1f;
        var box = S(lineCss);
        var serifSize = Txt.Natural(Face.Serif, 22);
        var baseline = at.Y + (box - serifSize) / 2 + Txt.Ascent(Face.Serif, 22);
        var lw = Txt.Spaced(dl, new Vector2(at.X, baseline - Txt.Ascent(Face.Mono, 11)), label, Face.Mono, 11, Palette.Dim, 0.16f);
        var bx = at.X + lw + S(10);
        var asideW = aside != null ? Txt.Measure(aside, Face.Sans, 13).X : 0;
        var bigW = Txt.Measure(big, Face.Serif, 22).X;
        var beside = aside != null && bx + bigW + S(10) + asideW <= at.X + w;
        var shown = Txt.Fit(big, Face.Serif, 22, MathF.Max(S(20), at.X + w - bx));
        Txt.Line(dl, new Vector2(bx, at.Y), shown, Face.Serif, 22, bigColor ?? Palette.Ivory, lineCss);
        var h = box;
        if (aside != null)
        {
            var text = Txt.Fit(aside, Face.Sans, 13, w);
            if (beside) Txt.Draw(dl, new Vector2(at.X + w - asideW, baseline - Txt.Ascent(Face.Sans, 13)), text, Face.Sans, 13, Palette.Ash);
            else
            {
                Txt.Line(dl, new Vector2(at.X, at.Y + box + S(2)), text, Face.Sans, 13, Palette.Ash, 19.5f);
                h += S(2) + S(19.5f);
            }
        }
        Reserve(at, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Small things: count chips, the Beta tag, a switch with its words, a quiet button with a chevron.

    public static float CountChipWidth(string count, string label, Icon? icon = null) =>
        S(10) * 2 + (icon != null ? S(13 + 6) : 0) + Txt.Measure(count, Face.Sans, 13).X + S(4) + Txt.Measure(label, Face.Sans, 13).X;

    /// <summary>A count chip, 26 tall in the well with the line edge: a brass icon, the count, and its words in dim ("4 retainers").</summary>
    public static float CountChip(Vector2 pos, string count, string label, Icon? icon = null)
    {
        var w = CountChipWidth(count, label, icon);
        var h = S(26);
        var dl = Dl;
        var max = pos + new Vector2(w, h);
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Well), h / 2);
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Line), h / 2, ImDrawFlags.None, 1f);
        var x = pos.X + S(10);
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(13)) / 2), S(13), Palette.Brass);
            x += S(13 + 6);
        }
        x += Txt.Line(dl, new Vector2(x, pos.Y), count, Face.Sans, 13, Palette.Text, 26) + S(4);
        Txt.Line(dl, new Vector2(x, pos.Y), label, Face.Sans, 13, Palette.Dim, 26);
        return w;
    }

    private const string Beta = "BETA";

    public static float BetaTagWidth => S(6) * 2 + Txt.SpacedWidth(Beta, Face.Mono, 10, 0.14f);

    public static float BetaTagHeight => Txt.Natural(Face.Mono, 10) + S(4);

    /// <summary>The Beta tag after an experimental feature's name: mono 10 capitals in brass, a brass-edge pill. Returns its width.</summary>
    public static float BetaTag(Vector2 pos)
    {
        var w = BetaTagWidth;
        var h = BetaTagHeight;
        Dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.BrassEdge), h / 2, ImDrawFlags.None, 1f);
        Txt.Spaced(Dl, pos + new Vector2(S(6), S(2)), Beta, Face.Mono, 10, Palette.Brass, 0.14f);
        return w;
    }

    public static float LabeledSwitchWidth(string label, bool beta = false) =>
        S(40) + S(10) + Txt.Measure(label, Face.Sans, 13).X + (beta ? S(6) + BetaTagWidth : 0);

    /// <summary>
    /// A switch with its words after it (and the Beta tag), 24 tall; the words flip it too. Returns
    /// true when flipped.
    /// </summary>
    public static bool LabeledSwitch(string id, Vector2 pos, bool on, string label, bool beta = false, string? tip = null, bool disabled = false)
    {
        var flipped = Switch(id, pos, on, disabled);
        var h = S(24);
        var x = pos.X + S(40) + S(10);
        var lw = Txt.Measure(label, Face.Sans, 13).X + (beta ? S(6) + BetaTagWidth : 0);
        var (clicked, hovered, _) = Hit(id + "##words", new Vector2(x, pos.Y), new Vector2(lw, h), disabled);
        if (clicked) flipped = true;
        if (tip != null && !Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, new Vector2(x + lw, pos.Y + h))) Tooltip(tip);
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.4f;
        var dl = Dl;
        x += Txt.Line(dl, new Vector2(x, pos.Y), label, Face.Sans, 13, hovered ? Palette.Ivory : Palette.Text, 24);
        if (beta) BetaTag(new Vector2(x + S(6), pos.Y + (h - BetaTagHeight) / 2));
        Palette.Opacity = old;
        return flipped;
    }

    public static float MoreButtonWidth(string label, Icon? after = Icon.ChevronRight) =>
        S(8) * 2 + Txt.Measure(label, Face.SansMedium, 13).X + (after != null ? S(4 + 14) : 0);

    /// <summary>A quiet 30px button with an icon after its words ("See the list ›"). Returns true when pressed.</summary>
    public static bool MoreButton(string id, Vector2 pos, string label, Icon? after = Icon.ChevronRight, string? tip = null, bool disabled = false)
    {
        var w = MoreButtonWidth(label, after);
        var h = S(30);
        var (clicked, hovered, held) = Hit(id, pos, new Vector2(w, h), disabled);
        if (tip != null && !Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(w, h))) Tooltip(tip);
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.5f;
        var dl = Dl;
        var max = pos + new Vector2(w, h);
        if (held || hovered) dl.AddRectFilled(pos, max, Palette.U32(held ? Palette.LinePicked : Palette.LineFaint), h / 2);
        var ink = hovered ? Palette.Ivory : Palette.Ash;
        var x = pos.X + S(8);
        x += Txt.Line(dl, new Vector2(x, pos.Y), label, Face.SansMedium, 13, ink, 30);
        if (after is { } ic) Icons.Draw(dl, ic, new Vector2(x + S(4), pos.Y + (h - S(14)) / 2), S(14), ink);
        Palette.Opacity = old;
        FocusRing(pos, max, h / 2);
        return clicked;
    }

    // ---------------------------------------------------------------------------------------------
    // The banner: one line in brass, with up to two small buttons on the right.

    /// <summary>
    /// A one-line banner (brass edge, brass wash, radius 14, 10px 10px 10px 14px inside): an icon in
    /// brass, the words in ivory and a <paramref name="note"/> after them in dim, then a brass button
    /// and a quiet one on the right (under the words when narrow). Returns 1 for the first button
    /// pressed, 2 for the second, else 0.
    /// </summary>
    public static int Banner(string id, Icon icon, string text, string? note, string? first, string? second = null, bool firstDisabled = false,
        string? firstTip = null, string? secondTip = null)
    {
        var at = At;
        var w = Width;
        var padL = S(14);
        var padR = S(10);
        var padY = S(10);
        var bh = ButtonHeight(BtnSize.Tiny);
        var b1 = first != null ? ButtonWidth(first, BtnSize.Tiny, null, Btn.Primary) : 0;
        var b2 = second != null ? ButtonWidth(second, BtnSize.Tiny, null, Btn.Quiet) : 0;
        var buttonsW = b1 + (first != null && second != null ? S(8) : 0) + b2;
        var textX = at.X + padL + S(16) + S(10);
        var right = at.X + w - padR;
        var textW = right - (buttonsW > 0 ? buttonsW + S(10) : 0) - textX;
        var stacked = buttonsW > 0 && textW < S(140);
        if (stacked) textW = right - textX;
        var runs = note != null
            ? new[] { new Run(text, Face.SansMedium, 13, Palette.Ivory), new Run(" " + note, Face.Sans, 13, Palette.Dim) }
            : new[] { new Run(text, Face.SansMedium, 13, Palette.Ivory) };
        var th = Txt.Flow(Vector2.Zero, textW, runs, 19.5f, out _, draw: false);
        var contentH = stacked ? th + S(10) + bh : MathF.Max(th, buttonsW > 0 ? bh : 0);
        var h = padY * 2 + contentH;
        var dl = Dl;
        var max = at + new Vector2(w, h);
        var r = S(Palette.RadiusLg);
        dl.AddRectFilled(at, max, Palette.U32(Palette.BrassWash), r);
        dl.AddRect(at + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.BrassEdge), r, ImDrawFlags.None, 1f);
        var textTop = stacked ? at.Y + padY : at.Y + (h - th) / 2;
        Icons.Draw(dl, icon, new Vector2(at.X + padL, textTop + (S(19.5f) - S(16)) / 2), S(16), Palette.Brass);
        Txt.Flow(new Vector2(textX, textTop), textW, runs, 19.5f, out _);
        var pressed = 0;
        var bx = stacked ? textX : right - buttonsW;
        var by = stacked ? at.Y + padY + th + S(10) : at.Y + (h - bh) / 2;
        if (first != null)
        {
            if (Button(id + "##1", new Vector2(bx, by), first, Btn.Primary, BtnSize.Tiny, null, 0, firstDisabled, firstTip)) pressed = 1;
            bx += b1 + S(8);
        }
        if (second != null && Button(id + "##2", new Vector2(bx, by), second, Btn.Quiet, BtnSize.Tiny, null, 0, false, secondTip)) pressed = 2;
        Reserve(at, w, h);
        return pressed;
    }

    // ---------------------------------------------------------------------------------------------
    // Stat cells and the journey.

    /// <summary>
    /// Cells side by side (8px apart, radius 12, the well with the line edge, 8px 10px inside): a serif
    /// number over its words in dim; a 0 is dim too.
    /// </summary>
    public static void StatCells(IReadOnlyList<(int Value, string Label)> cells, int numberPx = 26)
    {
        var n = cells.Count;
        if (n == 0) return;
        var at = At;
        var w = Width;
        var gap = S(8);
        var cw = (w - gap * (n - 1)) / n;
        var h = S(8) * 2 + S(numberPx) + S(18);
        var dl = Dl;
        var r = S(Palette.RadiusCard);
        for (var i = 0; i < n; i++)
        {
            var (value, label) = cells[i];
            var p = new Vector2(at.X + i * (cw + gap), at.Y);
            var max = p + new Vector2(cw, h);
            dl.AddRectFilled(p, max, Palette.U32(Palette.Well), r);
            dl.AddRect(p + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
            var text = value is >= 0 and <= 9 ? Numbers[value] : value.ToString();
            Txt.Line(dl, p + new Vector2(S(10), S(8)), text, Face.Serif, numberPx, value == 0 ? Palette.Dim : Palette.Ivory, numberPx);
            Txt.Line(dl, p + new Vector2(S(10), S(8) + S(numberPx)), Txt.Fit(label, Face.Sans, 12, cw - S(20)), Face.Sans, 12, Palette.Dim, 18);
        }
        Reserve(at, w, h);
    }

    /// <summary>
    /// The journey bar (the errands and placing as one row of stops): a 28px circle each with its
    /// icon (done: brass with a tick; now: a brass edge in a brass-wash ring; to come: the strong
    /// edge), its words under it, and a 2px track between, brass up to the stop in hand. The words
    /// are left out when one doesn't fit its share of the width. <paramref name="current"/> past the
    /// last stop: all done.
    /// </summary>
    public static void Journey(IReadOnlyList<(Icon Icon, string Label)> stops, int current)
    {
        var n = stops.Count;
        if (n == 0) return;
        var at = At;
        var w = Width;
        var cell = w / n;
        var d = S(28);
        var labels = true;
        for (var i = 0; i < n && labels; i++)
            if (Txt.Measure(stops[i].Label, Face.Sans, 12).X > cell - S(4)) labels = false;
        var dl = Dl;
        var top = at.Y + S(4);
        var cy = top + d / 2;
        for (var i = 1; i < n; i++)
        {
            var x0 = at.X + cell * (i - 0.5f);
            var x1 = at.X + cell * (i + 0.5f);
            dl.AddRectFilled(new Vector2(x0, cy - S(1)), new Vector2(x1, cy + S(1)), Palette.U32(i <= current ? Palette.Brass : Palette.Track));
        }
        for (var i = 0; i < n; i++)
        {
            var c = new Vector2(at.X + cell * (i + 0.5f), cy);
            var r = d / 2;
            var iconAt = c - new Vector2(S(6.5f));
            if (i < current)
            {
                dl.AddCircleFilled(c, r, Palette.U32(Palette.Brass), 32);
                Icons.Draw(dl, Icon.Check, iconAt, S(13), Palette.OnBrass, 2.2f);
            }
            else if (i == current)
            {
                dl.AddCircleFilled(c, r + S(4), Palette.U32(Palette.BrassWash), 32);
                dl.AddCircleFilled(c, r, Palette.U32(Palette.Ink), 32);
                dl.AddCircle(c, r - 0.5f, Palette.U32(Palette.Brass), 32, 1f);
                Icons.Draw(dl, stops[i].Icon, iconAt, S(13), Palette.Brass);
            }
            else
            {
                dl.AddCircleFilled(c, r, Palette.U32(Palette.Ink), 32);
                dl.AddCircle(c, r - 0.5f, Palette.U32(Palette.LineStrong), 32, 1f);
                Icons.Draw(dl, stops[i].Icon, iconAt, S(13), Palette.Dim);
            }
            if (!labels) continue;
            var label = stops[i].Label;
            var lw = Txt.Measure(label, Face.Sans, 12).X;
            Txt.Line(dl, new Vector2(c.X - lw / 2, top + d + S(6)), label, Face.Sans, 12, i == current ? Palette.Ivory : Palette.Dim, 18);
        }
        Reserve(at, w, S(4) + d + (labels ? S(6) + S(18) : 0) + S(2));
    }
}
