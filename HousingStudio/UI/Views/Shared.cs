using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// What every screen shares (0.9.0's pass): the header below Home (back at the top left, the eyebrow,
/// a title on up to two lines, the screen's round buttons on the right), and pinning designs.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// A serif title on up to <paramref name="lines"/> lines, the italic part after it (in brass-soft),
    /// cut with an ellipsis only on the last line: a design's name gets room ("Solution9Loft
    /// APARTMENT FCROOM CUSTOMTV" rather than "Solution9Loft_A…"). Returns its height; with
    /// <paramref name="draw"/> false, only measures.
    /// </summary>
    private static float TitleWrap(Vector2 pos, string plain, string? italic, float maxWidth, int px = 32, int lines = 2, bool draw = true)
    {
        var lineCss = px * 1.1f;
        var lh = S(lineCss);
        maxWidth = MathF.Max(S(40), maxWidth);
        var rows = new List<string>();
        var cur = string.Empty;
        foreach (var word in plain.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length == 0 || Txt.Measure(candidate, Face.Serif, px).X <= maxWidth) cur = candidate;
            else
            {
                rows.Add(cur);
                cur = word;
            }
        }
        rows.Add(cur);
        if (rows.Count > lines)
        {
            var rest = string.Join(' ', rows.Skip(lines - 1));
            rows = rows.Take(lines - 1).Append(rest).ToList();
        }
        var italicText = italic != null ? " " + italic : null;
        var italicW = italicText != null ? Txt.Measure(italicText, Face.SerifItalic, px).X : 0;
        var italicOwnRow = false;
        if (italicText != null && Txt.Measure(rows[^1], Face.Serif, px).X + italicW > maxWidth && rows.Count < lines)
        {
            italicOwnRow = true;
            italicText = italic;
            italicW = Txt.Measure(italicText!, Face.SerifItalic, px).X;
        }
        var height = lh * (rows.Count + (italicOwnRow ? 1 : 0));
        if (!draw) return height;
        var dl = Kit.Dl;
        for (var i = 0; i < rows.Count; i++)
        {
            var last = i == rows.Count - 1;
            var room = last && italicText != null && !italicOwnRow ? maxWidth - italicW : maxWidth;
            var text = Txt.Fit(rows[i], Face.Serif, px, MathF.Max(S(40), room));
            var at = pos + new Vector2(0, lh * i);
            var w = Txt.Line(dl, at, text, Face.Serif, px, Palette.Ivory, lineCss);
            if (last && italicText != null && !italicOwnRow)
                Txt.Line(dl, at + new Vector2(w, 0), italicText, Face.SerifItalic, px, Palette.BrassSoft, lineCss);
        }
        if (italicOwnRow) Txt.Line(dl, pos + new Vector2(0, lh * rows.Count), Txt.Fit(italicText!, Face.SerifItalic, px, maxWidth), Face.SerifItalic, px, Palette.BrassSoft, lineCss);
        return height;
    }

    /// <summary>
    /// The header of every screen below Home (0.9.0): the round back arrow at the top left, the eyebrow
    /// and the title beside it (the title on up to two lines), and the screen's own round buttons on the
    /// right (<paramref name="right"/> draws them from the top-left corner it's given, in
    /// <paramref name="rightWidth"/>). Draws inside the caller's Frame header; returns true when back
    /// was pressed.
    /// </summary>
    private bool ScreenHeader(string backTip, string? eyebrow, string title, string? italic = null, int px = 26,
        float rightWidth = 0, Action<Vector2>? right = null, Vector4? eyebrowColor = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var tx = at.X + S(40) + S(12);
        var textW = at.X + w - tx - (rightWidth > 0 ? rightWidth + S(12) : 0);
        var back = BackButton(at, backTip);
        var y = at.Y;
        if (!string.IsNullOrEmpty(eyebrow))
        {
            y += Kit.Eyebrow(new Vector2(tx, y), eyebrow, eyebrowColor, textW);
            y += S(4);
        }
        else y += MathF.Max(0, (S(40) - S(px * 1.1f)) / 2);
        y += TitleWrap(new Vector2(tx, y), title, italic, textW, px);
        right?.Invoke(new Vector2(at.X + w - rightWidth, at.Y));
        Kit.Reserve(at, w, MathF.Max(S(40), y - at.Y));
        return back;
    }

    // ---------------------------------------------------------------------------------------------
    // Pins (0.9.0): up to three designs at the top of Home.

    private const int PinsMost = 3;

    private static string PinName(string path) => DesignNames.Display(Path.GetFileNameWithoutExtension(path));

    /// <summary>Pins a design, or unpins it; with three pinned, asks which one it takes the place of.</summary>
    private void TogglePin(string path, string? name = null)
    {
        name ??= PinName(path);
        var pins = config.PinnedDesigns;
        var at = pins.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            pins.RemoveAt(at);
            config.Save();
            Notify($"Unpinned {name}");
            return;
        }
        pins.RemoveAll(string.IsNullOrWhiteSpace);
        if (pins.Count < PinsMost)
        {
            pins.Add(path);
            config.Save();
            Notify($"Pinned {name}");
            return;
        }
        var names = vm.Pinned().Select(r => r.Name).ToList();
        AskChoice("Replace a pin?", $"Three designs are pinned. Pick the one {name} takes the place of.", names, "Replace", "Not now", i =>
        {
            if (i < 0 || i >= config.PinnedDesigns.Count) return;
            config.PinnedDesigns[i] = path;
            config.Save();
            Notify($"Pinned {name}");
        });
    }

    // ---------------------------------------------------------------------------------------------
    // A section label with a link on its right, and the pin star.

    /// <summary>
    /// A section's label (as <see cref="Kit.Section"/>) with, on its right, a link in ivory (true when
    /// pressed) or plain dim words.
    /// </summary>
    private static bool SectionLink(string id, string label, string? link, string? rest = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        Kit.Section(label, link == null ? rest : null);
        if (link == null) return false;
        var lw = Txt.Measure(link, Face.Sans, 12).X;
        var lh = Txt.Natural(Face.Sans, 12);
        var h = Txt.Natural(Face.Mono, 11);
        var pos = new Vector2(at.X + w - lw, at.Y + h - lh);
        var (clicked, hovered, _) = Kit.Hit(id, pos - new Vector2(S(4)), new Vector2(lw, lh) + new Vector2(S(8)));
        Txt.Draw(Kit.Dl, pos, link, Face.Sans, 12, hovered ? Palette.BrassSoft : Palette.Ivory);
        return clicked;
    }

    /// <summary>
    /// The pin star in a round <paramref name="size"/> button at <paramref name="pos"/>: filled brass when
    /// pinned, an outline when not. Called twice: first for the mouse (before what's under it, so the star
    /// answers there; true when pressed), then with <paramref name="drawOnly"/> to draw it on top.
    /// </summary>
    private static bool StarButton(string id, Vector2 pos, float size, bool on, string? tip, bool drawOnly = false)
    {
        var hovered = !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, pos + new Vector2(size));
        if (!drawOnly)
        {
            var (clicked, hov, _) = Kit.Hit(id, pos, new Vector2(size));
            if (hov && tip != null) Kit.Tooltip(tip);
            return clicked;
        }
        var dl = Kit.Dl;
        var c = pos + new Vector2(size / 2);
        if (hovered) dl.AddCircleFilled(c, size / 2, Palette.U32(Palette.LineFaint), 24);
        Star(dl, c, size * 0.3f, on || hovered ? Palette.Brass : Palette.Ash, on);
        return false;
    }

    /// <summary>A five-pointed star around <paramref name="c"/>, filled (as a fan of triangles: it isn't convex) or outlined.</summary>
    private static void Star(ImDrawListPtr dl, Vector2 c, float r, Vector4 color, bool filled)
    {
        Span<Vector2> p = stackalloc Vector2[10];
        for (var i = 0; i < 10; i++)
        {
            var a = -MathF.PI / 2 + i * MathF.PI / 5;
            var rad = i % 2 == 0 ? r : r * 0.45f;
            p[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rad;
        }
        var col = Palette.U32(color);
        if (filled)
        {
            for (var i = 0; i < 10; i++) dl.AddTriangleFilled(c, p[i], p[(i + 1) % 10], col);
            return;
        }
        dl.AddPolyline(ref p[0], 10, col, ImDrawFlags.Closed, MathF.Max(1f, S(1.4f)));
    }

    /// <summary>A header's round pin button (40px, as Settings beside it): a brass ★ when pinned, a ☆ when not.</summary>
    private static bool PinRound(string id, Vector2 pos, bool on, string tip)
    {
        var size = S(40);
        var (clicked, hovered, _) = Kit.Hit(id, pos, new Vector2(size));
        var dl = Kit.Dl;
        var c = pos + new Vector2(size / 2);
        dl.AddCircleFilled(c, size / 2, Palette.U32(hovered ? Palette.GlassLive : Palette.Glass), 32);
        dl.AddCircle(c, size / 2 - 0.5f, Palette.U32(on ? Palette.BrassEdge : hovered ? Palette.LineHover : Palette.Line), 32, 1f);
        Star(dl, c, size * 0.24f, on ? Palette.Brass : hovered ? Palette.Ivory : Palette.Ash, on);
        if (hovered) Kit.Tooltip(tip);
        return clicked;
    }
}
