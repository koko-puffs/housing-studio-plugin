using System;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>
/// A design's name as shown (0.9.0): file names are often written with underscores
/// ("Solution9Loft_APARTMENT_FCROOM_CUSTOMTV"), which a title cut after 14 letters made unreadable.
/// Pure, so it's tested.
/// </summary>
public static class DesignNames
{
    /// <summary>The name with underscores as spaces and runs of spaces as one ("Solution9Loft APARTMENT FCROOM CUSTOMTV").</summary>
    public static string Display(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var words = raw.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }

    /// <summary>
    /// A short form for eyebrows ("SOLUTION9LOFT · SHOPPING"): the whole name when it's short, else its
    /// words up to about 18 letters (at least the first word, cut to 22).
    /// </summary>
    public static string Short(string? raw, int most = 18)
    {
        var name = Display(raw);
        if (name.Length <= most + 4) return name;
        var words = name.Split(' ');
        var taken = words[0];
        foreach (var w in words.Skip(1))
        {
            if (taken.Length + 1 + w.Length > most) break;
            taken += " " + w;
        }
        return taken.Length > most + 4 ? taken[..(most + 3)].TrimEnd() + "…" : taken;
    }
}
