using System;
using System.Collections.Generic;
using System.Globalization;

namespace HousingStudio.Core;

/// <summary>A dye the game lets you put on furniture: its Stain sheet row and colour (0xRRGGBB).</summary>
public readonly record struct DyeColor(byte StainId, uint Rgb, string Name);

/// <summary>Layout files keep a dye as its colour ("RRGGBBAA"); the game keeps a Stain row. These go between.</summary>
public static class Dyes
{
    /// <summary>"4F2D1FFF" or "#4f2d1f" → "4f2d1f"; anything else (no dye) → null. As the site's <c>dyeHex</c>.</summary>
    public static string? HexOf(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var s = color.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 && s.Length != 8) return null;
        foreach (var ch in s)
            if (!Uri.IsHexDigit(ch)) return null;
        return s[..6].ToLowerInvariant();
    }

    public static uint RgbOf(string hex) => uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public static string HexOfRgb(uint rgb) => (rgb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    /// <summary>
    /// The dye closest to a file's colour (exact when the file was written from a game dye, which
    /// MakePlace and Housing Studio both do), or null when there are none to choose from.
    /// </summary>
    public static DyeColor? Closest(string hex, IReadOnlyList<DyeColor> dyes)
    {
        if (dyes.Count == 0) return null;
        var rgb = RgbOf(hex);
        DyeColor? best = null;
        var bestDist = long.MaxValue;
        foreach (var d in dyes)
        {
            var dist = Distance(rgb, d.Rgb);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }
        return best;
    }

    /// <summary>Squared distance between two colours, weighted for the eye (a "redmean" approximation).</summary>
    public static long Distance(uint a, uint b)
    {
        long r1 = (a >> 16) & 0xFF, g1 = (a >> 8) & 0xFF, b1 = a & 0xFF;
        long r2 = (b >> 16) & 0xFF, g2 = (b >> 8) & 0xFF, b2 = b & 0xFF;
        var rm = (r1 + r2) / 2;
        long dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return (((512 + rm) * dr * dr) >> 8) + 4 * dg * dg + (((767 - rm) * db * db) >> 8);
    }
}
