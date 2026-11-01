using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>The four buttons of the design system.</summary>
public enum Btn
{
    /// <summary>The one brass button on a screen.</summary>
    Primary,

    /// <summary>Glass with a strong edge: everything else you press.</summary>
    Glass,

    /// <summary>Words only, a faint fill when pointed at.</summary>
    Quiet,

    /// <summary>A danger outline: Stop.</summary>
    Danger,

    /// <summary>A glass toggle that's on: brass-picked fill and a brass edge (Near me).</summary>
    Picked,
}

/// <summary>Control heights: 34 small, 40 default, 46 for the one big call; 30 for the log's Copy.</summary>
public enum BtnSize
{
    Tiny,
    Sm,
    Md,
    Lg,
}

/// <summary>The colour of a status pill.</summary>
public enum Tone
{
    /// <summary>Green: you can decorate here, it fits.</summary>
    Success,

    /// <summary>Brass wash: a run is going.</summary>
    Live,

    /// <summary>Brass, stronger: the run waits for you.</summary>
    Waiting,

    /// <summary>Ivory wash: paused, not in a home.</summary>
    Neutral,

    /// <summary>Danger: you can't decorate here, it doesn't fit.</summary>
    Danger,
}

/// <summary>
/// The design system's controls, drawn with ImGui's draw lists: pill buttons, round icon buttons,
/// switches, segmented pills, filter chips, status pills, panels with 1px edges, the dashed drop
/// zone, the progress bar, tick boxes, steppers, fields. Each takes the screen position to draw at
/// (layout is the views'), hit-tests with an invisible ImGui button (so keyboard navigation works),
/// and shows hover (edges go to the 0.34 line), press, focus (a 2px brass-soft ring, 2px out) and
/// disabled (50% for buttons, 40% for small controls).
/// </summary>
public static partial class Kit
{
    /// <summary>While a dialog is open, everything under it is drawn but can't be pointed at or pressed.</summary>
    public static bool Blocked { get; set; }

    // ---------------------------------------------------------------------------------------------
    // Layout: a stack of boxes (left edge and width) the views lay rows into, top to bottom.

    private static readonly Stack<(float Left, float Width)> Boxes = new();

    public static void PushBox(float left, float width) => Boxes.Push((left, MathF.Max(0, width)));

    /// <summary>At the start of each frame: nothing left over from a frame whose drawing threw part way.</summary>
    public static void ResetFrame()
    {
        Boxes.Clear();
        Panels.Clear();
        Details.Clear();
        Stages.Clear();
    }

    public static void PopBox() => Boxes.Pop();

    public static float Left => Boxes.Count > 0 ? Boxes.Peek().Left : ImGui.GetCursorScreenPos().X;

    public static float Width => Boxes.Count > 0 ? Boxes.Peek().Width : ImGui.GetContentRegionAvail().X;

    public static float Right => Left + Width;

    /// <summary>Where the next row starts: the box's left edge, at the cursor's height.</summary>
    public static Vector2 At => new(Left, ImGui.GetCursorScreenPos().Y);

    /// <summary>Takes up a row's space, so the next row goes under it.</summary>
    public static void Reserve(Vector2 start, float width, float height)
    {
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(MathF.Max(width, 1), MathF.Max(height, 0)));
    }

    /// <summary>A vertical gap of CSS pixels.</summary>
    public static void Gap(float px)
    {
        var at = At;
        Reserve(at, 1, S(px));
    }

    public static ImDrawListPtr Dl => ImGui.GetWindowDrawList();

    // ---------------------------------------------------------------------------------------------
    // Hit testing.

    /// <summary>An invisible button over a rectangle: (clicked, hovered, held). Disabled and blocked areas never react.</summary>
    public static (bool Clicked, bool Hovered, bool Held) Hit(string id, Vector2 pos, Vector2 size, bool disabled = false)
    {
        var keep = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, Vector2.Max(size, new Vector2(1)));
        var live = !disabled && !Blocked;
        var hovered = live && ImGui.IsItemHovered();
        var held = live && ImGui.IsItemActive();
        if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGui.SetCursorScreenPos(keep);
        return (clicked && live, hovered, held);
    }

    /// <summary>The focus ring, when the keyboard is driving and the last item has focus.</summary>
    public static void FocusRing(Vector2 min, Vector2 max, float radius)
    {
        if (Blocked || !ImGui.IsItemFocused() || !ImGui.GetIO().NavVisible) return;
        var o = S(2);
        Dl.AddRect(min - new Vector2(o + S(1)), max + new Vector2(o + S(1)), Palette.U32(Palette.BrassSoft), radius + o + S(1), ImDrawFlags.None, S(2));
    }

    // ---------------------------------------------------------------------------------------------
    // Buttons.

    private static (float Height, float Pad, int Px) Spec(BtnSize size) => size switch
    {
        BtnSize.Tiny => (30, 10, 13),
        BtnSize.Sm => (34, 14, 13),
        BtnSize.Lg => (46, 24, 15),
        _ => (40, 18, 14),
    };

    private static Face FaceOf(Btn style) => style == Btn.Primary ? Face.SansSemibold : Face.SansMedium;

    public static float ButtonHeight(BtnSize size) => S(Spec(size).Height);

    public static float ButtonWidth(string label, BtnSize size = BtnSize.Md, Icon? icon = null, Btn style = Btn.Glass)
    {
        var (h, pad, px) = Spec(size);
        if (label.Length == 0) return S(h);
        var text = Txt.Measure(label, FaceOf(style), px).X;
        return S(pad) * 2 + text + (icon != null ? S(16 + 8) : 0);
    }

    /// <summary>A pill button. Width 0 fits its words; an empty label makes it a round icon button.</summary>
    public static bool Button(string id, Vector2 pos, string label, Btn style = Btn.Glass, BtnSize size = BtnSize.Md, Icon? icon = null,
        float width = 0, bool disabled = false, string? tip = null, float height = 0)
    {
        var (hCss, _, px) = Spec(size);
        var h = height > 0 ? height : S(hCss);
        var w = width > 0 ? width : label.Length == 0 ? h : ButtonWidth(label, size, icon, style);
        var (clicked, hovered, held) = Hit(id, pos, new Vector2(w, h), disabled);
        if (tip != null && !Blocked && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(w, h)) && ImGui.IsWindowHovered()) Tooltip(tip);

        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.5f;
        Vector4 fill, edge, ink;
        var iconInk = (Vector4?)null;
        switch (style)
        {
            case Btn.Primary:
                fill = held ? Palette.Brass : hovered ? Palette.BrassHover : Palette.Brass;
                edge = fill;
                ink = Palette.OnBrass;
                break;
            case Btn.Quiet:
                fill = held ? Palette.LinePicked : hovered ? Palette.LineFaint : Vector4.Zero;
                edge = Vector4.Zero;
                ink = hovered ? Palette.Ivory : Palette.Ash;
                break;
            case Btn.Danger:
                fill = held ? Palette.DangerWash : Vector4.Zero;
                edge = hovered ? Palette.Danger : Palette.DangerEdge;
                ink = Palette.Danger;
                break;
            case Btn.Picked:
                fill = held ? Palette.BrassWash : Palette.BrassPicked;
                edge = hovered ? Palette.Brass : Palette.BrassEdge;
                ink = Palette.Ivory;
                iconInk = Palette.Brass;
                break;
            default:
                fill = Palette.GlassLive;
                edge = hovered ? Palette.LineHover : Palette.LineStrong;
                ink = Palette.Ivory;
                break;
        }
        var dl = Dl;
        var max = pos + new Vector2(w, h);
        var r = h / 2;
        if (fill.W > 0) dl.AddRectFilled(pos, max, Palette.U32(fill), r);
        if (style == Btn.Glass && held) dl.AddRectFilled(pos, max, Palette.U32(Palette.LinePicked), r);
        if (edge.W > 0) dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);

        var face = FaceOf(style);
        var textW = label.Length > 0 ? Txt.Measure(label, face, px).X : 0;
        var iconPx = S(16);
        var content = (icon != null ? iconPx : 0) + (icon != null && label.Length > 0 ? S(8) : 0) + textW;
        var x = pos.X + (w - content) / 2;
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - iconPx) / 2), iconPx, iconInk ?? ink);
            x += iconPx + (label.Length > 0 ? S(8) : 0);
        }
        if (label.Length > 0) Txt.Line(dl, new Vector2(x, pos.Y), label, face, px, ink, h / S(1));
        Palette.Opacity = old;
        FocusRing(pos, max, r);
        return clicked;
    }

    /// <summary>A round (or square-pill) button with only an icon; its words are the tooltip, as aria-label is.</summary>
    public static bool IconButton(string id, Vector2 pos, Icon icon, string tip, float size = 40, Btn style = Btn.Glass, bool disabled = false) =>
        Button(id, pos, string.Empty, style, BtnSize.Md, icon, S(size), disabled, tip, S(size));

    // ---------------------------------------------------------------------------------------------
    // Switch, segmented pill, chips, pills.

    /// <summary>The design system's switch, 40×24: brass with a dark knob when on. Returns true when flipped.</summary>
    public static bool Switch(string id, Vector2 pos, bool on, bool disabled = false)
    {
        var size = new Vector2(S(40), S(24));
        var (clicked, hovered, _) = Hit(id, pos, size, disabled);
        var storage = ImGui.GetStateStorage();
        var key = ImGui.GetID(id + "##knob");
        var target = on ? 1f : 0f;
        var t = storage.GetFloat(key, target);
        // duration-move: 220ms.
        t = ReducedMotion ? target : MoveTowards(t, target, ImGui.GetIO().DeltaTime / 0.22f);
        storage.SetFloat(key, t);

        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.4f;
        var dl = Dl;
        var max = pos + size;
        var r = size.Y / 2;
        var e = Ease(t);
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Well), r);
        if (e > 0) dl.AddRectFilled(pos, max, Palette.U32(Palette.WithAlpha(Palette.Brass, e)), r);
        var edge = on ? Palette.Brass : hovered ? Palette.LineHover : Palette.LineStrong;
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        var knob = pos + new Vector2(S(1 + 3 + 8) + S(16) * e, size.Y / 2);
        var knobColor = Mix(Palette.Ash, Palette.OnBrass, e);
        dl.AddCircleFilled(knob, S(8), Palette.U32(knobColor), 24);
        Palette.Opacity = old;
        FocusRing(pos, max, r);
        return clicked;
    }

    private static float MoveTowards(float v, float target, float step) =>
        v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    /// <summary>cubic-bezier(.3,.7,.4,1), near enough.</summary>
    private static float Ease(float t) => 1 - MathF.Pow(1 - t, 3);

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * t;

    /// <summary>The segmented pill's width; with <paramref name="maxWidth"/>, what it shrinks to (its words keep at least 6px either side).</summary>
    public static float SegmentedWidth(IReadOnlyList<string> items, float maxWidth = 0) =>
        SegmentedFrame(items) + items.Count * 2 * SegmentPad(items, maxWidth);

    /// <summary>The pill's edges, inner padding, gaps and words: everything but the words' own padding.</summary>
    private static float SegmentedFrame(IReadOnlyList<string> items)
    {
        var w = S(3) * 2 + S(1) * 2 + S(2) * (items.Count - 1);
        foreach (var s in items) w += Txt.Measure(s, Face.Sans, 13).X;
        return w;
    }

    /// <summary>The padding either side of each item's words: 14px, less when the pill must fit a narrower width.</summary>
    private static float SegmentPad(IReadOnlyList<string> items, float maxWidth)
    {
        var pad = S(14);
        if (maxWidth <= 0 || items.Count == 0) return pad;
        var full = SegmentedFrame(items) + items.Count * 2 * pad;
        if (full <= maxWidth) return pad;
        return MathF.Max(S(6), pad - (full - maxWidth) / (items.Count * 2));
    }

    /// <summary>
    /// The segmented pill (Settings → Pace, Ready's Keep / Clear out / Swap): 40 tall outside, 32
    /// inside, the picked one in brass-picked. Items whose bit is set in <paramref name="disabledMask"/>
    /// are at 40% and can't be picked; <paramref name="tips"/> are each item's tooltip (null for none).
    /// With <paramref name="maxWidth"/>, the items' padding shrinks so the pill fits.
    /// </summary>
    public static int Segmented(string id, Vector2 pos, IReadOnlyList<string> items, int selected, int disabledMask = 0, float maxWidth = 0,
        IReadOnlyList<string?>? tips = null) =>
        SegmentedPress(id, pos, items, selected, disabledMask, maxWidth, tips) is var pressed and >= 0 ? pressed : selected;

    /// <summary>As <see cref="Segmented"/>, but returns the item pressed (the one already picked too), or -1 when none was.</summary>
    public static int SegmentedPress(string id, Vector2 pos, IReadOnlyList<string> items, int selected, int disabledMask = 0, float maxWidth = 0,
        IReadOnlyList<string?>? tips = null)
    {
        var dl = Dl;
        var pad = SegmentPad(items, maxWidth);
        var total = SegmentedFrame(items) + items.Count * 2 * pad;
        var h = S(40);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(total, h) - new Vector2(0.5f), Palette.U32(Palette.LineStrong), h / 2, ImDrawFlags.None, 1f);
        var x = pos.X + S(1) + S(3);
        var result = -1;
        for (var i = 0; i < items.Count; i++)
        {
            var w = Txt.Measure(items[i], Face.Sans, 13).X + pad * 2;
            var p = new Vector2(x, pos.Y + S(1) + S(3));
            var size = new Vector2(w, S(32));
            var on = i == selected;
            var disabled = (disabledMask & (1 << i)) != 0;
            var (clicked, hovered, _) = Hit($"{id}{i}", p, size, disabled);
            if (clicked) result = i;
            if (tips != null && i < tips.Count && tips[i] is { } tip && !Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(p, p + size))
                Tooltip(tip);
            var old = Palette.Opacity;
            if (disabled) Palette.Opacity = old * 0.4f;
            if (on) dl.AddRectFilled(p, p + size, Palette.U32(Palette.BrassPicked), size.Y / 2);
            else if (hovered) dl.AddRectFilled(p, p + size, Palette.U32(Palette.LineFaint), size.Y / 2);
            Txt.Line(dl, new Vector2(p.X + pad, p.Y), items[i], Face.Sans, 13, on || hovered ? Palette.Ivory : Palette.Ash, 32);
            Palette.Opacity = old;
            FocusRing(p + new Vector2(S(4)), p + size - new Vector2(S(4)), size.Y / 2 - S(4));
            x += w + S(2);
        }
        return result;
    }

    public static float FilterChipWidth(string label, int count) =>
        S(14) * 2 + Txt.Measure(label, Face.Sans, 13).X + S(8) + Txt.Measure(count.ToString(), Face.Mono, 12).X;

    /// <summary>A filter chip with its count in mono (Pieces). A chip with nothing in it is disabled at 40%.</summary>
    public static bool FilterChip(string id, Vector2 pos, string label, int count, bool on)
    {
        var disabled = count == 0 && !on;
        var w = FilterChipWidth(label, count);
        var h = S(34);
        var (clicked, hovered, _) = Hit(id, pos, new Vector2(w, h), disabled);
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.4f;
        var dl = Dl;
        var max = pos + new Vector2(w, h);
        if (on) dl.AddRectFilled(pos, max, Palette.U32(Palette.BrassPicked), h / 2);
        var edge = on ? Palette.BrassEdge : hovered ? Palette.LineHover : Palette.LineStrong;
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), h / 2, ImDrawFlags.None, 1f);
        var x = pos.X + S(14);
        x += Txt.Line(dl, new Vector2(x, pos.Y), label, Face.Sans, 13, on || hovered ? Palette.Ivory : Palette.Ash, 34) + S(8);
        // Mono 12 sits on the same middle as the 13px words.
        Txt.Line(dl, new Vector2(x, pos.Y), count.ToString(), Face.Mono, 12, on ? Palette.BrassSoft : Palette.Dim, 34);
        Palette.Opacity = old;
        FocusRing(pos, max, h / 2);
        return clicked;
    }

    private static (Vector4 Fill, Vector4 Ink, Vector4 Dot) ToneColors(Tone tone) => tone switch
    {
        Tone.Success => (Palette.SuccessWash, Palette.Success, Palette.Success),
        Tone.Live => (Palette.BrassWash, Palette.BrassSoft, Palette.Brass),
        Tone.Waiting => (Palette.BrassPicked, Palette.BrassSoft, Palette.BrassSoft),
        Tone.Danger => (Palette.DangerWash, Palette.Danger, Palette.Danger),
        _ => (Palette.LinePicked, Palette.Text, Palette.Ash),
    };

    public static float StatusPillWidth(string text) => S(12) * 2 + S(7) + S(8) + Txt.Measure(text, Face.Sans, 12).X;

    /// <summary>"In your house (medium) · you can decorate": 28 tall, a 7px dot, 12px words. The live one's dot breathes.</summary>
    public static float StatusPill(Vector2 pos, string text, Tone tone, bool pulse = false, float maxWidth = 0)
    {
        var (fill, ink, dot) = ToneColors(tone);
        var full = StatusPillWidth(text);
        if (maxWidth > 0 && full > maxWidth) text = Txt.Fit(text, Face.Sans, 12, maxWidth - (full - Txt.Measure(text, Face.Sans, 12).X));
        var w = StatusPillWidth(text);
        var h = S(28);
        var dl = Dl;
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(fill), h / 2);
        var c = pos + new Vector2(S(12) + S(3.5f), h / 2);
        if (pulse)
        {
            var p = Pulse();
            dl.AddCircleFilled(c, S(3.5f) + S(3) * p, Palette.U32(Palette.WithAlpha(dot, 0.25f * (1 - p))), 16);
        }
        dl.AddCircleFilled(c, S(3.5f), Palette.U32(dot), 16);
        Txt.Line(dl, new Vector2(pos.X + S(12 + 7 + 8), pos.Y), text, Face.Sans, 12, ink, 28);
        return w;
    }

    /// <summary>The small outlined chip ("0 of 11 done"), 24 tall; green with a tick when it's all done.</summary>
    public static float Chip(Vector2 pos, string text, bool done = false)
    {
        var tick = done ? S(12 + 6) : 0;
        var w = S(10) * 2 + tick + Txt.Measure(text, Face.Sans, 12).X;
        var h = S(24);
        var dl = Dl;
        if (done) dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.SuccessWash), h / 2);
        else dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.LineStrong), h / 2, ImDrawFlags.None, 1f);
        var x = pos.X + S(10);
        if (done)
        {
            Icons.Draw(dl, Icon.Check, new Vector2(x, pos.Y + (h - S(12)) / 2), S(12), Palette.Success, 2.2f);
            x += tick;
        }
        Txt.Line(dl, new Vector2(x, pos.Y), text, Face.Sans, 12, done ? Palette.Success : Palette.Text, 24);
        return w;
    }

    public static float ChipWidth(string text, bool done = false) => S(10) * 2 + (done ? S(18) : 0) + Txt.Measure(text, Face.Sans, 12).X;

    public static string StateWord(PieceState s) => s switch
    {
        PieceState.Move => "MOVE",
        PieceState.FromStoreroom => "STOREROOM",
        PieceState.FromInventory => "BAGS",
        PieceState.InPlace => "IN PLACE",
        _ => "MISSING",
    };

    public static float StatePillWidth(PieceState s) => S(8) * 2 + Txt.SpacedWidth(StateWord(s), Face.Mono, 11, 0.1f);

    /// <summary>A piece's state, in mono uppercase with .1em spacing: MOVE in brass wash, STOREROOM and BAGS neutral, IN PLACE green, MISSING danger.</summary>
    public static float StatePill(Vector2 pos, PieceState state)
    {
        var (fill, ink) = state switch
        {
            PieceState.Move => (Palette.BrassWash, Palette.BrassSoft),
            PieceState.InPlace => (Palette.SuccessWash, Palette.Success),
            PieceState.Missing => (Palette.DangerWash, Palette.Danger),
            _ => (Palette.LinePicked, Palette.Text),
        };
        var word = StateWord(state);
        var w = StatePillWidth(state);
        var line = Txt.Natural(Face.Mono, 11);
        var h = line + S(3) * 2;
        var dl = Dl;
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(fill), h / 2);
        Txt.Spaced(dl, pos + new Vector2(S(8), S(3)), word, Face.Mono, 11, ink, 0.1f);
        return w;
    }

    public static float StatePillHeight => Txt.Natural(Face.Mono, 11) + S(6);

    // ---------------------------------------------------------------------------------------------
    // Small things.

    /// <summary>A dye's swatch: a 10px dot in its colour with the 0.34 edge (an empty ring when undyed).</summary>
    public static void Swatch(ImDrawListPtr dl, Vector2 center, uint? rgb)
    {
        var r = S(5);
        if (rgb is { } v) dl.AddCircleFilled(center, r, Palette.U32(Palette.Hex(v)), 16);
        dl.AddCircle(center, r - 0.5f, Palette.U32(Palette.LineHover), 16, 1f);
    }

    /// <summary>A round well with an icon (brass wash and brass for the steps, green for done).</summary>
    public static void Round(Vector2 pos, float diameter, Icon icon, float iconPx, Vector4 fill, Vector4 ink, float stroke = 1.8f)
    {
        var dl = Dl;
        var c = pos + new Vector2(diameter / 2);
        dl.AddCircleFilled(c, diameter / 2, Palette.U32(fill), 48);
        Icons.Draw(dl, icon, c - new Vector2(iconPx / 2), iconPx, ink, stroke);
    }

    /// <summary>An item's icon in a rounded photo well, or a placeholder icon when it has none.</summary>
    public static void Thumb(Vector2 pos, float size, float radius, uint iconId, float placeholderPx = 0)
    {
        var dl = Dl;
        dl.AddRectFilled(pos, pos + new Vector2(size), Palette.U32(Palette.Photo), radius);
        if (iconId != 0)
        {
            var wrap = Services.Textures.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(iconId)).GetWrapOrDefault();
            if (wrap != null)
            {
                var inset = S(2);
                dl.AddImageRounded(wrap.Handle, pos + new Vector2(inset), pos + new Vector2(size - inset), Vector2.Zero, Vector2.One,
                    Palette.U32(Vector4.One), MathF.Max(0, radius - inset));
                return;
            }
        }
        if (placeholderPx > 0)
            Icons.Draw(dl, Icon.Shelf, pos + new Vector2((size - placeholderPx) / 2), placeholderPx, Palette.Dim, 1.6f);
    }

    /// <summary>A tick box, 18px with a 5px radius: the 0.34 edge, brass with a dark tick when ticked.</summary>
    public static void TickBox(Vector2 pos, bool on, bool hovered)
    {
        var dl = Dl;
        var s = S(18);
        var r = S(5);
        if (on)
        {
            dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(hovered ? Palette.BrassHover : Palette.Brass), r);
            Icons.Draw(dl, Icon.Check, pos + new Vector2(S(2)), S(14), Palette.OnBrass, 2.6f);
        }
        else
        {
            dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(Palette.Well), r);
            dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(s) - new Vector2(0.5f), Palette.U32(hovered ? Palette.Brass : Palette.LineHover), r, ImDrawFlags.None, 1f);
        }
    }

    /// <summary>A stepper for a number in Settings: − value +, a 40px pill. Returns −1, 0 or +1.</summary>
    public static int Stepper(string id, Vector2 pos, string value, bool canDown, bool canUp)
    {
        var dl = Dl;
        var h = S(40);
        var valueW = MathF.Max(S(64), Txt.Measure(value, Face.Mono, 13).X + S(16));
        var w = h * 2 + valueW;
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.LineStrong), h / 2, ImDrawFlags.None, 1f);
        var result = 0;
        foreach (var (dir, x, icon, can) in new[] { (-1, pos.X, Icon.Minus, canDown), (1, pos.X + h + valueW, Icon.Plus, canUp) })
        {
            var p = new Vector2(x, pos.Y);
            var (clicked, hovered, held) = Hit($"{id}{dir}", p, new Vector2(h), !can);
            if (clicked) result = dir;
            var old = Palette.Opacity;
            if (!can) Palette.Opacity = old * 0.4f;
            if (hovered || held) dl.AddCircleFilled(p + new Vector2(h / 2), h / 2 - S(4), Palette.U32(held ? Palette.LinePicked : Palette.LineFaint), 32);
            Icons.Draw(dl, icon, p + new Vector2((h - S(16)) / 2), S(16), hovered ? Palette.Ivory : Palette.Ash);
            Palette.Opacity = old;
            FocusRing(p + new Vector2(S(4)), p + new Vector2(h - S(4)), h / 2 - S(4));
        }
        var tw = Txt.Measure(value, Face.Mono, 13).X;
        Txt.Line(dl, new Vector2(pos.X + h + (valueW - tw) / 2, pos.Y), value, Face.Mono, 13, Palette.Ivory, 40);
        return result;
    }

    public static float StepperWidth(string value) => S(40) * 2 + MathF.Max(S(64), Txt.Measure(value, Face.Mono, 13).X + S(16));

    /// <summary>What's typed in an editable stepper while it's being typed in (by id).</summary>
    private static readonly Dictionary<string, string> StepperTyping = new();

    /// <summary>
    /// A stepper whose value can be typed in too: − value +, as <see cref="Stepper"/>, the value a field
    /// (pressed, all of it is picked, so typing replaces it). Returns −1, 0 or +1 for the buttons;
    /// <paramref name="typed"/> is what was typed once Enter is pressed or the field is left.
    /// </summary>
    public static int StepperEdit(string id, Vector2 pos, string value, bool canDown, bool canUp, out string? typed)
    {
        typed = null;
        var dl = Dl;
        var h = S(40);
        var valueW = MathF.Max(S(64), Txt.Measure(value, Face.Mono, 13).X + S(16));
        var w = h * 2 + valueW;
        var typing = StepperTyping.TryGetValue(id, out var buffer);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(typing ? Palette.Brass : Palette.LineStrong), h / 2, ImDrawFlags.None, 1f);
        var result = 0;
        foreach (var (dir, x, icon, can) in new[] { (-1, pos.X, Icon.Minus, canDown), (1, pos.X + h + valueW, Icon.Plus, canUp) })
        {
            var p = new Vector2(x, pos.Y);
            var (clicked, hovered, held) = Hit($"{id}{dir}", p, new Vector2(h), !can);
            if (clicked) result = dir;
            var old = Palette.Opacity;
            if (!can) Palette.Opacity = old * 0.4f;
            if (hovered || held) dl.AddCircleFilled(p + new Vector2(h / 2), h / 2 - S(4), Palette.U32(held ? Palette.LinePicked : Palette.LineFaint), 32);
            Icons.Draw(dl, icon, p + new Vector2((h - S(16)) / 2), S(16), hovered ? Palette.Ivory : Palette.Ash);
            Palette.Opacity = old;
            FocusRing(p + new Vector2(S(4)), p + new Vector2(h - S(4)), h / 2 - S(4));
        }
        var text = typing ? buffer! : value;
        using (Txt.Fonts.Push(Face.Mono, 13))
        {
            var fh = ImGui.GetFontSize();
            // Not being typed in: the value drawn centred as the stepper's, the field's own words hidden.
            ImGui.PushStyleColor(ImGuiCol.Text, typing ? Palette.Ivory : new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(S(4), (h - fh) / 2));
            var keep = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(pos.X + h + S(4), pos.Y));
            ImGui.SetNextItemWidth(valueW - S(8));
            if (Blocked) ImGui.BeginDisabled();
            ImGui.InputTextWithHint($"{id}-type", string.Empty, ref text, 24, ImGuiInputTextFlags.AutoSelectAll);
            if (Blocked) ImGui.EndDisabled();
            var active = ImGui.IsItemActive();
            var hovered = ImGui.IsItemHovered() && !Blocked;
            if (ImGui.IsItemDeactivated())
            {
                if (typing) typed = text;
                StepperTyping.Remove(id);
            }
            else if (active)
            {
                StepperTyping[id] = text;
            }
            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
            ImGui.SetCursorScreenPos(keep);
        }
        if (!typing)
        {
            var tw = Txt.Measure(value, Face.Mono, 13).X;
            Txt.Line(dl, new Vector2(pos.X + h + (valueW - tw) / 2, pos.Y), value, Face.Mono, 13, Palette.Ivory, 40);
        }
        return result;
    }

    /// <summary>
    /// A field you type in: 40 tall, radius 10, the strong edge (brass while you type in it), the well
    /// inside, an icon before the words, the hint in dim. Returns true when the text changed.
    /// </summary>
    /// <param name="flags">ImGui's own, as Password for a secret.</param>
    public static bool Field(string id, Vector2 pos, float width, ref string text, string hint, Icon? icon = null, int maxLength = 128,
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        var dl = Dl;
        var h = S(40);
        var r = S(Palette.RadiusMd);
        var max = pos + new Vector2(width, h);
        dl.AddRectFilled(pos, max, Palette.U32(Palette.Well), r);
        var x = pos.X + S(14);
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(16)) / 2), S(16), Palette.Dim);
            x += S(16 + 10);
        }
        bool changed;
        bool active;
        using (Txt.Fonts.Push(Face.Sans, 14))
        {
            var fh = ImGui.GetFontSize();
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.Ivory);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Palette.Dim);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, (h - fh) / 2));
            var keep = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(x, pos.Y));
            ImGui.SetNextItemWidth(MathF.Max(S(20), max.X - S(14) - x));
            if (Blocked) ImGui.BeginDisabled();
            changed = ImGui.InputTextWithHint(id, hint, ref text, maxLength, flags);
            if (Blocked) ImGui.EndDisabled();
            active = ImGui.IsItemActive();
            var hovered = ImGui.IsItemHovered() && !Blocked;
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
            ImGui.SetCursorScreenPos(keep);
            var edge = active ? Palette.Brass : hovered ? Palette.LineHover : Palette.LineStrong;
            dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        }
        return changed;
    }

    /// <summary>The 8px progress bar: a faint track and a brass fill, both pills.</summary>
    public static void Progress(Vector2 pos, float width, float fraction)
    {
        var dl = Dl;
        var h = S(8);
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(Palette.Track), h / 2);
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction > 0) dl.AddRectFilled(pos, pos + new Vector2(MathF.Max(h, width * fraction), h), Palette.U32(Palette.Brass), h / 2);
    }

    /// <summary>An eyebrow: Geist Mono 12, uppercase, .18em apart, brass. Returns its height.</summary>
    public static float Eyebrow(Vector2 pos, string text, Vector4? color = null, float maxWidth = 0)
    {
        var upper = maxWidth > 0 ? FitEyebrow(text, maxWidth) : text.ToUpperInvariant();
        Txt.Spaced(Dl, pos, upper, Face.Mono, 12, color ?? Palette.Brass, 0.18f);
        return Txt.Natural(Face.Mono, 12);
    }

    private static readonly Dictionary<(string Text, float Width), string> EyebrowFits = new();

    /// <summary>
    /// An eyebrow's words in capitals, cut with "…" to fit: the longest start that fits, found by
    /// halving (0.3.2 took a character off at a time, measuring each letter every time: a long design
    /// name in "Placing …" cost tens of thousands of measurements a frame). Remembered per width.
    /// </summary>
    private static string FitEyebrow(string text, float maxWidth)
    {
        if (EyebrowFits.TryGetValue((text, maxWidth), out var fit)) return fit;
        var upper = text.ToUpperInvariant();
        bool Fits(string s) => Txt.SpacedWidth(s, Face.Mono, 12, 0.18f) <= maxWidth;
        if (!Fits(upper))
        {
            var lo = 0;
            var hi = upper.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Fits(Cut(upper, mid))) lo = mid;
                else hi = mid - 1;
            }
            upper = Cut(upper, lo);
        }
        if (EyebrowFits.Count > 256) EyebrowFits.Clear();
        EyebrowFits[(text, maxWidth)] = upper;
        return upper;
    }

    /// <summary>The first <paramref name="n"/> characters and "…", never splitting a surrogate pair.</summary>
    private static string Cut(string s, int n)
    {
        if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n].TrimEnd() + "…";
    }

    /// <summary>An eyebrow on its own row.</summary>
    public static void EyebrowRow(string text, Vector4? color = null)
    {
        var at = At;
        var h = Eyebrow(at, text, color, Width);
        Reserve(at, Width, h);
    }

    /// <summary>A row divider: the faint line.</summary>
    public static void Divider(float x0, float x1, float y) => Dl.AddLine(new Vector2(x0, y + 0.5f), new Vector2(x1, y + 0.5f), Palette.U32(Palette.LineFaint), 1f);

    /// <summary>
    /// A dashed rounded rectangle, as the browser draws a dashed border: dashes three times the
    /// line's width with gaps as long, round the whole edge (corners too), evened out so the pattern
    /// closes. The dashes come from <see cref="Model.Dashes"/>, whose loops are bounded.
    /// </summary>
    public static void Dashed(Vector2 min, Vector2 max, float radius, Vector4 color, float thickness)
    {
        var half = thickness / 2;
        var outline = Model.Dashes.RoundedRect(min + new Vector2(half), max - new Vector2(half), MathF.Max(0, radius - half));
        var col = Palette.U32(color);
        var dl = Dl;
        foreach (var dash in Model.Dashes.Along(outline, thickness * 6))
            if (dash.Length >= 2) dl.AddPolyline(ref dash[0], dash.Length, col, ImDrawFlags.None, thickness);
    }

    // ---------------------------------------------------------------------------------------------
    // Panels: the content is laid first, the glass is drawn behind it at the height it had last frame.

    private static readonly Dictionary<uint, float> Heights = new();
    private static readonly Stack<(uint Id, Vector2 Start, float Width, float PadBottom, Vector4 Edge, float Radius)> Panels = new();

    /// <summary>
    /// A panel (glass over ink, the 1px line edge, radius 16, the glass's lit top edge). Lay its rows
    /// between this and <see cref="EndPanel"/>; padding is CSS pixels (top, right, bottom, left).
    /// </summary>
    public static void BeginPanel(string id, float padTop, float padX, float padBottom, Vector4? fill = null, Vector4? edge = null, float radius = Palette.RadiusXl)
    {
        var at = At;
        var w = Width;
        var key = ImGui.GetID(id);
        var r = S(radius);
        if (Heights.TryGetValue(key, out var h) && h > 0)
            Dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(fill ?? Palette.Glass), r);
        Panels.Push((key, at, w, S(padBottom), edge ?? Palette.Line, r));
        ImGui.SetCursorScreenPos(at + new Vector2(0, S(padTop)));
        ImGui.Dummy(Vector2.Zero);
        ImGui.SetCursorScreenPos(at + new Vector2(0, S(padTop)));
        PushBox(at.X + S(padX), w - S(padX) * 2);
    }

    /// <summary>The panel being laid: its top-left and width (for rows that go edge to edge).</summary>
    public static (Vector2 Start, float Width, float Radius) Panel => Panels.Count > 0 ? (Panels.Peek().Start, Panels.Peek().Width, Panels.Peek().Radius) : (At, Width, 0);

    public static void EndPanel()
    {
        PopBox();
        var (key, start, w, padBottom, edge, r) = Panels.Pop();
        var bottom = ImGui.GetCursorScreenPos().Y + padBottom;
        var h = bottom - start.Y;
        Heights[key] = h;
        var dl = Dl;
        var max = start + new Vector2(w, h);
        // shadow-glass-edge: a 1px lit line just inside the top.
        dl.AddLine(new Vector2(start.X + r * 0.6f, start.Y + 1.5f), new Vector2(max.X - r * 0.6f, start.Y + 1.5f), Palette.U32(Palette.GlassEdge), 1f);
        dl.AddRect(start + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
        Reserve(start, w, h);
    }

    /// <summary>A row inside a panel pointed at: a fill edge to edge, rounded where it meets the panel's corners.</summary>
    public static void RowFill(Vector2 min, Vector2 max, Vector4 color, bool first, bool last)
    {
        var (start, w, r) = Panel;
        var flags = (first ? ImDrawFlags.RoundCornersTop : 0) | (last ? ImDrawFlags.RoundCornersBottom : 0);
        if (flags == 0) flags = ImDrawFlags.RoundCornersNone;
        var a = new Vector2(start.X + 1, min.Y + (first ? 1 : 0));
        var b = new Vector2(start.X + w - 1, max.Y - (last ? 1 : 0));
        Dl.AddRectFilled(a, b, Palette.U32(color), r - 1, flags);
    }

    // ---------------------------------------------------------------------------------------------
    // Tooltips and menus.

    /// <summary>A tooltip in the float material: radius 10, the line edge, 13px ivory words.</summary>
    public static void Tooltip(string text)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(12), S(8)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(Palette.RadiusMd));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Palette.Over(Palette.Float, Palette.Ink));
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line);
        ImGui.PushStyleColor(ImGuiCol.Text, Palette.Ivory);
        using (Txt.Fonts.Push(Face.Sans, 13))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(S(300));
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(3);
    }

    /// <summary>Opens a popup styled as a menu (float, radius 14, the line edge, 6px inside). Pair with <see cref="EndMenu"/> when true.</summary>
    public static bool BeginMenu(string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(6)));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(Palette.RadiusLg));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Palette.Over(Palette.Float, Palette.Ink));
        ImGui.PushStyleColor(ImGuiCol.Border, Palette.Line);
        var open = ImGui.BeginPopup(id, ImGuiWindowFlags.NoMove);
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
        return open;
    }

    public static void EndMenu() => ImGui.EndPopup();

    /// <summary>A row in a menu: 36 tall, radius 8, a faint fill when pointed at.</summary>
    public static bool MenuItem(string id, string label, Icon? icon = null, float width = 0)
    {
        var pos = ImGui.GetCursorScreenPos();
        var w = width > 0 ? width : MathF.Max(S(180), Txt.Measure(label, Face.Sans, 13).X + S(24) + (icon != null ? S(24) : 0));
        var h = S(36);
        var clicked = ImGui.InvisibleButton(id, new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered)
        {
            dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusSm));
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }
        var x = pos.X + S(12);
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(16)) / 2), S(16), hovered ? Palette.Ivory : Palette.Ash);
            x += S(24);
        }
        Txt.Line(dl, new Vector2(x, pos.Y), label, Face.Sans, 13, hovered ? Palette.Ivory : Palette.Text, 36);
        return clicked;
    }
}
