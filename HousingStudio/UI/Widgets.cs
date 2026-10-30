using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

public enum ButtonStyle
{
    /// <summary>The one thing to do: a brass pill.</summary>
    Brass,

    /// <summary>Everything else: an outlined pill.</summary>
    Ghost,

    /// <summary>Stop, remove: an outlined pill in the danger ink.</summary>
    Danger,

    /// <summary>A quiet word in a row of things, with no outline.</summary>
    Quiet,
}

/// <summary>The site's controls, drawn with ImGui's draw lists (the way Lightless and others draw theirs).</summary>
public static class Widgets
{
    public static Fonts Fonts { get; set; } = null!;

    /// <summary>A pill button, as the site's <c>.hs-btn</c> (<c>.is-brass</c> for the main one).</summary>
    public static bool Button(string label, ButtonStyle style = ButtonStyle.Ghost, float width = 0, bool disabled = false, string? tooltip = null, bool small = false)
    {
        using var font = Fonts.BodyMedium.Push();
        var text = label.Split("##")[0];
        var textSize = ImGui.CalcTextSize(text);
        var height = S(small ? Palette.ControlSm : Palette.ControlMd);
        var size = new Vector2(width > 0 ? width : textSize.X + S(small ? 28 : 36), height);
        var pos = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(label, size) && !disabled;
        var hovered = ImGui.IsItemHovered() && !disabled;
        var held = ImGui.IsItemActive() && !disabled;
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Tooltip(tooltip);

        var dl = ImGui.GetWindowDrawList();
        var max = pos + size;
        var r = size.Y / 2;
        Vector4 fill, border, ink;
        switch (style)
        {
            case ButtonStyle.Brass:
                fill = held ? Palette.BrassSoft : hovered ? Palette.BrassHover : Palette.Brass;
                border = fill;
                ink = Palette.OnBrass;
                break;
            case ButtonStyle.Danger:
                fill = hovered ? Palette.DangerWash : Palette.WithAlpha(Palette.Panel, 0);
                border = hovered ? Palette.Danger : Palette.Mix(Palette.Panel, Palette.Danger, 0.5f);
                ink = Palette.Danger;
                break;
            case ButtonStyle.Quiet:
                fill = hovered ? Palette.LineFaint : Palette.WithAlpha(Palette.Panel, 0);
                border = Palette.WithAlpha(Palette.Panel, 0);
                ink = hovered ? Palette.Ivory : Palette.Ash;
                break;
            default:
                fill = held ? Palette.Line : hovered ? Palette.LineFaint : Palette.WithAlpha(Palette.Panel, 0);
                border = hovered ? Palette.LineHover : Palette.LineStrong;
                ink = Palette.Ivory;
                break;
        }
        if (disabled)
        {
            fill = Palette.WithAlpha(fill, fill.W * 0.5f);
            border = Palette.WithAlpha(border, 0.5f);
            ink = Palette.WithAlpha(ink, 0.5f);
        }
        dl.AddRectFilled(pos, max, Palette.U32(fill), r);
        if (border.W > 0) dl.AddRect(pos, max, Palette.U32(border), r, ImDrawFlags.None, 1f);
        dl.AddText(pos + (size - textSize) / 2, Palette.U32(ink), text);
        return clicked;
    }

    /// <summary>Small caps in Geist Mono, as the site's eyebrows ("INSIDE · MEDIUM HOUSE").</summary>
    public static void Eyebrow(string text, Vector4? color = null)
    {
        using var font = Fonts.Mono.Push();
        ImGui.TextColored(color ?? Palette.Dim, text.ToUpperInvariant());
    }

    public static void Heading(string text)
    {
        using var font = Fonts.Heading.Push();
        ImGui.TextColored(Palette.Ivory, text);
    }

    public static void Muted(string text, bool wrap = true)
    {
        using var font = Fonts.Small.Push();
        if (wrap)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Palette.Ash);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextColored(Palette.Ash, text);
        }
    }

    public static void Wrapped(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(S(320));
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>A rounded panel (the site's glass cards). Call <see cref="EndCard"/> after, always.</summary>
    public static bool BeginCard(string id, float height = 0, Vector4? background = null, Vector4? border = null)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, background ?? Palette.Panel);
        ImGui.PushStyleColor(ImGuiCol.Border, border ?? Palette.Line);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, S(Palette.RadiusLg));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(S(Palette.S16), S(Palette.S14)));
        var open = ImGui.BeginChild(id, new Vector2(0, height), true, ImGuiWindowFlags.AlwaysUseWindowPadding | (height <= 0 ? ImGuiWindowFlags.AlwaysAutoResize : ImGuiWindowFlags.None));
        return open;
    }

    public static void EndCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static readonly Stack<(Vector2 Start, float Width)> Panels = new();

    /// <summary>The width left on the line: the panel's inside when in one.</summary>
    public static float Avail()
    {
        if (Panels.Count == 0) return ImGui.GetContentRegionAvail().X;
        var (start, width) = Panels.Peek();
        return start.X + width - S(Palette.S16) - ImGui.GetCursorScreenPos().X;
    }

    /// <summary>
    /// A rounded panel as tall as what's in it: the content is drawn first on a layer above, then the
    /// panel behind it once its height is known (the same trick Lightless uses for its grouped boxes).
    /// Not nestable. Call <see cref="EndPanel"/> after, always.
    /// </summary>
    public static void BeginPanel(float width = 0)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        var start = ImGui.GetCursorScreenPos();
        var w = width > 0 ? width : ImGui.GetContentRegionAvail().X;
        Panels.Push((start, w));
        ImGui.SetCursorScreenPos(start + new Vector2(S(Palette.S16), S(Palette.S14)));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w - S(Palette.S16) * 2);
    }

    public static void EndPanel(Vector4? background = null, Vector4? border = null)
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var (start, w) = Panels.Pop();
        var bottom = ImGui.GetItemRectMax().Y + S(Palette.S14);
        var max = new Vector2(start.X + w, bottom);
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(start, max, Palette.U32(background ?? Palette.Panel), S(Palette.RadiusLg));
        dl.AddRect(start, max, Palette.U32(border ?? Palette.Line), S(Palette.RadiusLg));
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(w, bottom - start.Y));
    }

    /// <summary>A small pill with a word in it (states, dyes), on the line where the cursor is.</summary>
    public static void Chip(string text, Vector4 ink, Vector4 fill, Vector4? border = null)
    {
        using var font = Fonts.Small.Push();
        var size = ImGui.CalcTextSize(text);
        var pad = new Vector2(S(9), S(3));
        var pos = ImGui.GetCursorScreenPos();
        var box = size + pad * 2;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + box, Palette.U32(fill), box.Y / 2);
        if (border is { } b) dl.AddRect(pos, pos + box, Palette.U32(b), box.Y / 2);
        dl.AddText(pos + pad, Palette.U32(ink), text);
        ImGui.Dummy(box);
    }

    /// <summary>A rounded track with a brass fill.</summary>
    public static void Progress(float fraction, float height = 8, Vector4? fill = null)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var width = Avail();
        var h = S(height);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(Palette.Field), h / 2);
        if (fraction > 0) dl.AddRectFilled(pos, pos + new Vector2(Math.Max(h, width * fraction), h), Palette.U32(fill ?? Palette.Brass), h / 2);
        ImGui.Dummy(new Vector2(width, h));
    }

    /// <summary>The site's segmented control: a row of pills, one picked.</summary>
    public static bool Segmented(string id, IReadOnlyList<string> items, ref int selected)
    {
        using var font = Fonts.BodyMedium.Push();
        var changed = false;
        var h = S(Palette.ControlSm);
        var pad = S(4);
        var widths = new float[items.Count];
        var total = pad * 2;
        for (var i = 0; i < items.Count; i++)
        {
            widths[i] = ImGui.CalcTextSize(items[i]).X + S(28);
            total += widths[i];
        }
        var start = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(start, start + new Vector2(total, h + pad * 2), Palette.U32(Palette.Field), (h + pad * 2) / 2);
        dl.AddRect(start, start + new Vector2(total, h + pad * 2), Palette.U32(Palette.Line), (h + pad * 2) / 2);
        var x = start.X + pad;
        for (var i = 0; i < items.Count; i++)
        {
            var pos = new Vector2(x, start.Y + pad);
            ImGui.SetCursorScreenPos(pos);
            if (ImGui.InvisibleButton($"{id}{i}", new Vector2(widths[i], h)) && selected != i)
            {
                selected = i;
                changed = true;
            }
            var hovered = ImGui.IsItemHovered();
            var on = selected == i;
            if (on) dl.AddRectFilled(pos, pos + new Vector2(widths[i], h), Palette.U32(Palette.BrassPicked), h / 2);
            else if (hovered) dl.AddRectFilled(pos, pos + new Vector2(widths[i], h), Palette.U32(Palette.LineFaint), h / 2);
            var ts = ImGui.CalcTextSize(items[i]);
            dl.AddText(pos + new Vector2((widths[i] - ts.X) / 2, (h - ts.Y) / 2), Palette.U32(on ? Palette.BrassSoft : hovered ? Palette.Ivory : Palette.Ash), items[i]);
            x += widths[i];
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(total, h + pad * 2));
        return changed;
    }

    /// <summary>The site's on/off switch, with its label to the right.</summary>
    public static bool Toggle(string label, ref bool value, string? help = null)
    {
        var h = S(20);
        var w = S(36);
        var pos = ImGui.GetCursorScreenPos();
        var text = label.Split("##")[0];
        var textSize = ImGui.CalcTextSize(text);
        var changed = false;
        if (ImGui.InvisibleButton(label, new Vector2(w + S(10) + textSize.X, Math.Max(h, textSize.Y))))
        {
            value = !value;
            changed = true;
        }
        var hovered = ImGui.IsItemHovered();
        if (help != null && hovered) Tooltip(help);
        var dl = ImGui.GetWindowDrawList();
        var top = pos + new Vector2(0, (Math.Max(h, textSize.Y) - h) / 2);
        dl.AddRectFilled(top, top + new Vector2(w, h), Palette.U32(value ? Palette.Brass : hovered ? Palette.LineStrong : Palette.Line), h / 2);
        var knob = value ? top + new Vector2(w - h / 2, h / 2) : top + new Vector2(h / 2, h / 2);
        dl.AddCircleFilled(knob, h / 2 - S(3), Palette.U32(value ? Palette.OnBrass : Palette.Ivory));
        dl.AddText(pos + new Vector2(w + S(10), (Math.Max(h, textSize.Y) - textSize.Y) / 2), Palette.U32(Palette.Text), text);
        return changed;
    }

    /// <summary>A big figure over a small label, as the site's stat tiles.</summary>
    public static void Stat(string value, string label, Vector4 accent, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        var h = S(64);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(Palette.Panel), S(Palette.RadiusLg));
        dl.AddRect(pos, pos + new Vector2(width, h), Palette.U32(Palette.Line), S(Palette.RadiusLg));
        dl.AddRectFilled(pos + new Vector2(S(14), S(14)), pos + new Vector2(S(17), h - S(14)), Palette.U32(accent), S(2));
        using (Fonts.Heading.Push())
            dl.AddText(pos + new Vector2(S(26), S(8)), Palette.U32(Palette.Ivory), value);
        using (Fonts.Mono.Push())
            dl.AddText(pos + new Vector2(S(26), h - S(22)), Palette.U32(Palette.Dim), label.ToUpperInvariant());
        ImGui.Dummy(new Vector2(width, h));
    }

    /// <summary>An item's icon in a rounded well.</summary>
    public static void ItemIcon(uint iconId, float size, float alpha = 1f)
    {
        var px = S(size);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(px), Palette.U32(Palette.Photo), S(Palette.RadiusSm));
        if (iconId != 0)
        {
            var wrap = Services.Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            dl.AddImageRounded(wrap.Handle, pos + new Vector2(S(2)), pos + new Vector2(px - S(2)), Vector2.Zero, Vector2.One,
                Palette.U32(new Vector4(1, 1, 1, alpha)), S(Palette.RadiusSm - 2));
        }
        ImGui.Dummy(new Vector2(px));
    }

    /// <summary>A dye's swatch, a small round dot (with a ring when undyed).</summary>
    public static void Swatch(uint? rgb, float size = 12)
    {
        var px = S(size);
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, (ImGui.GetTextLineHeight() - px) / 2);
        var dl = ImGui.GetWindowDrawList();
        var c = pos + new Vector2(px / 2);
        if (rgb is { } v) dl.AddCircleFilled(c, px / 2, Palette.U32(Palette.Hex(v)));
        dl.AddCircle(c, px / 2, Palette.U32(Palette.LineHover));
        ImGui.Dummy(new Vector2(px, ImGui.GetTextLineHeight()));
    }

    /// <summary>A thin rule across the panel.</summary>
    public static void Rule()
    {
        var pos = ImGui.GetCursorScreenPos();
        var w = Avail();
        ImGui.GetWindowDrawList().AddLine(pos, pos + new Vector2(w, 0), Palette.U32(Palette.Line));
        ImGui.Dummy(new Vector2(w, S(1)));
    }

    public static void Gap(float px) => ImGui.Dummy(new Vector2(0, S(px)));
}
