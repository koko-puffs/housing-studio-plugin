using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>One of Settings' tabs: its words and, when it has one, a count after them in mono.</summary>
public readonly record struct TabSpec(string Label, string? Count = null);

/// <summary>
/// More of the 0.6 flow's controls (docs/mockups/flow.html), for Start, Done and Settings: the
/// section label ("RECENT", "NEEDS YOU 2"), the tabs as small pills, the 34px icon thumb, a card with
/// an icon, two lines and a small button (Done's undo and fixtures cards), and a small button with
/// the Beta tag.
/// </summary>
public static partial class Kit
{
    /// <summary>A section's label on its own row (mono 11 capitals, .16em, dim), with <paramref name="rest"/> on the right in plain 12px.</summary>
    public static void Section(string text, string? rest = null)
    {
        var at = At;
        var w = Width;
        var h = Txt.Natural(Face.Mono, 11);
        var restW = rest != null ? Txt.Measure(rest, Face.Sans, 12).X : 0;
        var label = FitSection(text, rest != null ? w - restW - S(8) : w);
        var dl = Dl;
        Txt.Spaced(dl, at, label, Face.Mono, 11, Palette.Dim, 0.16f);
        if (rest != null)
            Txt.Draw(dl, new Vector2(at.X + w - restW, at.Y + h - Txt.Natural(Face.Sans, 12)), rest, Face.Sans, 12, Palette.Dim);
        Reserve(at, w, h);
    }

    private static readonly Dictionary<string, string> Upper = new();

    /// <summary>A section label in capitals, made once per label (it's drawn every frame).</summary>
    private static string FitSection(string text, float width)
    {
        if (!Upper.TryGetValue(text, out var upper))
        {
            if (Upper.Count > 64) Upper.Clear();
            Upper[text] = upper = text.ToUpperInvariant();
        }
        return Txt.SpacedWidth(upper, Face.Mono, 11, 0.16f) <= width ? upper : Txt.Fit(upper, Face.Mono, 11, width);
    }

    // ---------------------------------------------------------------------------------------------
    // Tabs.

    public static float TabWidth(TabSpec tab) =>
        S(12) * 2 + Txt.Measure(tab.Label, Face.Sans, 13).X + (tab.Count != null ? S(6) + Txt.Measure(tab.Count, Face.Mono, 11).X : 0);

    /// <summary>
    /// Tabs as small pills from the cursor, wrapping (30 tall, 4px apart): the one picked in glass with
    /// the strong edge and ivory words, the others words in ash with no edge; a count in mono dim.
    /// Returns the tab picked (the one given when none was pressed).
    /// </summary>
    public static int Tabs(string id, IReadOnlyList<TabSpec> tabs, int selected)
    {
        var row = new Wrap(4);
        var result = selected;
        var h = S(30);
        var dl = Dl;
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            var w = TabWidth(tab);
            var p = row.Next(w, h);
            var max = p + new Vector2(w, h);
            var (clicked, hovered, held) = Hit($"{id}{i}", p, new Vector2(w, h));
            if (clicked) result = i;
            var on = i == selected;
            if (on)
            {
                dl.AddRectFilled(p, max, Palette.U32(Palette.Glass), h / 2);
                dl.AddRect(p + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.LineStrong), h / 2, ImDrawFlags.None, 1f);
            }
            else if (held || hovered) dl.AddRectFilled(p, max, Palette.U32(held ? Palette.LinePicked : Palette.LineFaint), h / 2);
            var x = p.X + S(12);
            x += Txt.Line(dl, new Vector2(x, p.Y), tab.Label, Face.Sans, 13, on || hovered ? Palette.Ivory : Palette.Ash, 30);
            if (tab.Count != null) Txt.Line(dl, new Vector2(x + S(6), p.Y), tab.Count, Face.Mono, 11, Palette.Dim, 30);
            FocusRing(p, max, h / 2);
        }
        row.End();
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Thumbs and cards.

    /// <summary>The 34px icon thumb (the photo well, radius 8, the faint edge) with a 16px icon in <paramref name="ink"/> (brass when not given).</summary>
    public static void IconThumb(Vector2 pos, Icon icon, Vector4? ink = null)
    {
        var dl = Dl;
        var s = S(34);
        var r = S(Palette.RadiusSm);
        dl.AddRectFilled(pos, pos + new Vector2(s), Palette.U32(Palette.Photo), r);
        dl.AddRect(pos + new Vector2(0.5f), pos + new Vector2(s) - new Vector2(0.5f), Palette.U32(Palette.LineFaint), r, ImDrawFlags.None, 1f);
        Icons.Draw(dl, icon, pos + new Vector2((s - S(16)) / 2), S(16), ink ?? Palette.Brass);
    }

    private static readonly Run[] CardLine = new Run[1];

    /// <summary>
    /// A card (glass, 12px 14px inside): the 34px icon thumb, a title in ivory 500 with a line under
    /// it in dim (wrapping), and a small glass button on the right (with the Beta tag when
    /// <paramref name="beta"/>); the button goes under the words when they'd be too narrow beside it.
    /// No <paramref name="button"/>: words only. Returns true when the button is pressed.
    /// </summary>
    public static bool ActionCard(string id, Icon icon, Vector4? iconInk, string title, string line, string? button, Icon? buttonIcon = null,
        bool beta = false, bool disabled = false, string? tip = null)
    {
        BeginPanel(id, 12, 14, 12);
        var at = At;
        var w = Width;
        var bh = ButtonHeight(BtnSize.Tiny);
        var bw = button == null ? 0 : beta ? BetaButtonWidth(button, buttonIcon) : ButtonWidth(button, BtnSize.Tiny, buttonIcon);
        var textX = at.X + S(34) + S(12);
        var besideW = at.X + w - (bw > 0 ? bw + S(12) : 0) - textX;
        var stacked = bw > 0 && besideW < S(150);
        var textW = stacked ? at.X + w - textX : besideW;
        var titleH = Txt.Natural(Face.SansMedium, 14);
        CardLine[0] = new Run(line, Face.Sans, 13, Palette.Dim);
        var lineH = Txt.Flow(Vector2.Zero, textW, CardLine, 19.5f, out _, draw: false);
        var wordsH = titleH + S(2) + lineH;
        var rowH = MathF.Max(S(34), stacked ? wordsH : MathF.Max(wordsH, bh));
        var dl = Dl;
        IconThumb(new Vector2(at.X, at.Y + (rowH - S(34)) / 2), icon, iconInk);
        var top = at.Y + (rowH - wordsH) / 2;
        Txt.Line(dl, new Vector2(textX, top), Txt.Fit(title, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ivory);
        Txt.Flow(new Vector2(textX, top + titleH + S(2)), textW, CardLine, 19.5f, out _);
        var pressed = false;
        var h = rowH;
        if (button != null)
        {
            var p = stacked ? new Vector2(textX, at.Y + rowH + S(10)) : new Vector2(at.X + w - bw, at.Y + (rowH - bh) / 2);
            pressed = beta
                ? BetaButton(id + "##btn", p, button, buttonIcon, disabled, tip)
                : Button(id + "##btn", p, button, Btn.Glass, BtnSize.Tiny, buttonIcon, 0, disabled, tip);
            if (stacked) h += S(10) + bh;
        }
        Reserve(at, w, h);
        EndPanel();
        return pressed;
    }

    public static float BetaButtonWidth(string label, Icon? icon = null) => ButtonWidth(label, BtnSize.Tiny, icon) + S(6) + BetaTagWidth;

    /// <summary>A small (30px) glass button with the Beta tag after its words ("Set them BETA"). Returns true when pressed.</summary>
    public static bool BetaButton(string id, Vector2 pos, string label, Icon? icon = null, bool disabled = false, string? tip = null)
    {
        var w = BetaButtonWidth(label, icon);
        var h = ButtonHeight(BtnSize.Tiny);
        var (clicked, hovered, held) = Hit(id, pos, new Vector2(w, h), disabled);
        if (tip != null && !Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(w, h))) Tooltip(tip);
        var old = Palette.Opacity;
        if (disabled) Palette.Opacity = old * 0.5f;
        var dl = Dl;
        var max = pos + new Vector2(w, h);
        var r = h / 2;
        dl.AddRectFilled(pos, max, Palette.U32(Palette.GlassLive), r);
        if (held) dl.AddRectFilled(pos, max, Palette.U32(Palette.LinePicked), r);
        dl.AddRect(pos + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(hovered ? Palette.LineHover : Palette.LineStrong), r, ImDrawFlags.None, 1f);
        var x = pos.X + S(10);
        if (icon is { } ic)
        {
            Icons.Draw(dl, ic, new Vector2(x, pos.Y + (h - S(16)) / 2), S(16), Palette.Ivory);
            x += S(16 + 8);
        }
        x += Txt.Line(dl, new Vector2(x, pos.Y), label, Face.SansMedium, 13, Palette.Ivory, h / S(1));
        BetaTag(new Vector2(x + S(6), pos.Y + (h - BetaTagHeight) / 2));
        Palette.Opacity = old;
        FocusRing(pos, max, r);
        return clicked;
    }
}
