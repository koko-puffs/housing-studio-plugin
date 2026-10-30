using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.UI.Model;

namespace HousingStudio.UI;

/// <summary>The Lucide icons the mockups use.</summary>
public enum Icon
{
    Play,
    Pause,
    Square,
    Eye,
    List,
    Sliders,
    X,
    ChevronRight,
    ChevronDown,
    MapPin,
    Search,
    Clipboard,
    Droplet,
    Package,
    Move,
    House,
    Upload,
    Info,
    Alert,
    Refresh,
    Check,
    ArrowLeft,
    Shelf,
    Minus,
    Plus,
    Backpack,
}

/// <summary>
/// Lucide's outlined icons, drawn with ImGui's draw lists from the very SVG paths in the mockups
/// (<c>viewBox="0 0 24 24"</c>, <c>stroke-width:1.8</c>, round caps and joins). The stroke is in the
/// icon's 24-unit grid as in SVG, so a 16px icon has the mockup's 1.2px line and a 24px one 1.8px.
/// </summary>
public static class Icons
{
    /// <summary>Each icon's SVG elements as path data (circles, rects and lines written as paths).</summary>
    private static readonly Dictionary<Icon, (string[] Paths, bool Filled)> Source = new()
    {
        [Icon.Play] = (new[] { "M7 4 20 12 7 20 7 4Z" }, true),
        [Icon.Pause] = (new[] { Rect(14, 4, 4, 16, 1), Rect(6, 4, 4, 16, 1) }, false),
        [Icon.Square] = (new[] { Rect(5, 5, 14, 14, 2) }, false),
        [Icon.Eye] = (new[] { "M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z", Circle(12, 12, 3) }, false),
        [Icon.List] = (new[] { "M8 6H21", "M8 12H21", "M8 18H21", "M3 6H3.01", "M3 12H3.01", "M3 18H3.01" }, false),
        [Icon.Sliders] = (new[]
        {
            "M21 4H14", "M10 4H3", "M21 12H12", "M8 12H3", "M21 20H16", "M12 20H3", "M14 2V6", "M8 10V14", "M16 18V22",
        }, false),
        [Icon.X] = (new[] { "M18 6 6 18", "m6 6 12 12" }, false),
        [Icon.ChevronRight] = (new[] { "m9 18 6-6-6-6" }, false),
        [Icon.ChevronDown] = (new[] { "m6 9 6 6 6-6" }, false),
        [Icon.MapPin] = (new[] { "M20 10c0 4.993-5.539 10.193-7.399 11.799a1 1 0 0 1-1.202 0C9.539 20.193 4 14.993 4 10a8 8 0 0 1 16 0", Circle(12, 10, 3) }, false),
        [Icon.Search] = (new[] { Circle(11, 11, 8), "m21 21-4.3-4.3" }, false),
        [Icon.Clipboard] = (new[] { Rect(8, 2, 8, 4, 1), "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" }, false),
        [Icon.Droplet] = (new[] { "M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5c-.5 2.5-2 4.9-4 6.5C6 11.1 5 13 5 15a7 7 0 0 0 7 7z" }, false),
        [Icon.Package] = (new[]
        {
            "m7.5 4.27 9 5.15",
            "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z",
            "m3.3 7 8.7 5 8.7-5",
            "M12 22V12",
        }, false),
        [Icon.Move] = (new[] { "M5 9l-3 3 3 3", "M9 5l3-3 3 3", "M15 19l-3 3-3-3", "M19 9l3 3-3 3", "M2 12h20", "M12 2v20" }, false),
        [Icon.House] = (new[]
        {
            "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8",
            "M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        }, false),
        [Icon.Upload] = (new[] { "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "M17 8 12 3 7 8", "M12 3V15" }, false),
        [Icon.Info] = (new[] { Circle(12, 12, 10), "M12 16v-4", "M12 8h.01" }, false),
        [Icon.Alert] = (new[] { Circle(12, 12, 10), "M12 8V12", "M12 16H12.01" }, false),
        [Icon.Refresh] = (new[] { "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", "M3 3v5h5" }, false),
        [Icon.Check] = (new[] { "M20 6 9 17l-5-5" }, false),
        [Icon.ArrowLeft] = (new[] { "m12 19-7-7 7-7", "M19 12H5" }, false),
        [Icon.Shelf] = (new[] { Rect(4, 3, 16, 18, 1), "M4 9h16", "M4 15h16" }, false),
        [Icon.Minus] = (new[] { "M5 12h14" }, false),
        [Icon.Plus] = (new[] { "M5 12h14", "M12 5v14" }, false),
        [Icon.Backpack] = (new[]
        {
            "M4 10a4 4 0 0 1 4-4h8a4 4 0 0 1 4 4v10a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2Z",
            "M8 10h8", "M8 18h8", "M8 22v-6a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v6", "M9 6V4a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2",
        }, false),
    };

    private static readonly Dictionary<Icon, (List<SvgPath.Sub> Subs, bool Filled)> Parsed = new();

    private static string Circle(float cx, float cy, float r) => FormattableString.Invariant($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0Z");

    private static string Rect(float x, float y, float w, float h, float rx) => FormattableString.Invariant(
        $"M{x + rx} {y}h{w - 2 * rx}a{rx} {rx} 0 0 1 {rx} {rx}v{h - 2 * rx}a{rx} {rx} 0 0 1 {-rx} {rx}h{-(w - 2 * rx)}a{rx} {rx} 0 0 1 {-rx} {-rx}v{-(h - 2 * rx)}a{rx} {rx} 0 0 1 {rx} {-rx}Z");

    private static (List<SvgPath.Sub> Subs, bool Filled) Get(Icon icon)
    {
        if (Parsed.TryGetValue(icon, out var p)) return p;
        var (paths, filled) = Source[icon];
        var subs = new List<SvgPath.Sub>();
        foreach (var d in paths) subs.AddRange(SvgPath.Parse(d));
        return Parsed[icon] = (subs, filled);
    }

    /// <summary>Draws an icon with its top-left at <paramref name="pos"/>, <paramref name="size"/> pixels square (already scaled).</summary>
    /// <param name="stroke">The stroke in the icon's 24-unit grid (Lucide's strokeWidth): 1.8, or 1.6 at 18px and up.</param>
    public static void Draw(ImDrawListPtr dl, Icon icon, Vector2 pos, float size, Vector4 color, float stroke = 1.8f)
    {
        var (subs, filled) = Get(icon);
        var k = size / 24f;
        var col = Palette.U32(color);
        var thickness = stroke * k;
        Span<Vector2> buffer = stackalloc Vector2[128];
        foreach (var sub in subs)
        {
            var n = Math.Min(sub.Points.Count, buffer.Length);
            for (var i = 0; i < n; i++) buffer[i] = pos + sub.Points[i] * k;
            if (filled)
            {
                dl.AddConvexPolyFilled(ref buffer[0], n, col);
                continue;
            }
            var length = 0f;
            for (var i = 1; i < n; i++) length += Vector2.Distance(buffer[i - 1], buffer[i]);
            if (n == 1 || length < thickness * 0.5f)
            {
                // A dot ("h.01" with a round cap).
                dl.AddCircleFilled(buffer[0], thickness * 0.75f, col, 8);
                continue;
            }
            dl.AddPolyline(ref buffer[0], n, col, sub.Closed ? ImDrawFlags.Closed : ImDrawFlags.None, thickness);
            if (!sub.Closed)
            {
                // Round caps.
                dl.AddCircleFilled(buffer[0], thickness * 0.5f, col, 6);
                dl.AddCircleFilled(buffer[n - 1], thickness * 0.5f, col, 6);
            }
        }
    }
}
