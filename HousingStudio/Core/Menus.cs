using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>Finding things in the game's lists and menus (pure helpers, tested without the game).</summary>
public static class Menus
{
    /// <summary>
    /// The index of the context-menu entry whose words match one of <paramref name="labels"/>
    /// (ignoring case, spacing and the game's formatting characters), or -1. An exact match wins over
    /// one that only starts with a label ("Place" before "Place Furnishing").
    /// </summary>
    public static int FindEntry(IReadOnlyList<string> entries, IEnumerable<string> labels)
    {
        var wanted = labels.Select(Clean).Where(l => l.Length > 0).ToList();
        for (var i = 0; i < entries.Count; i++)
            if (wanted.Contains(Clean(entries[i]), StringComparer.OrdinalIgnoreCase)) return i;
        for (var i = 0; i < entries.Count; i++)
        {
            var e = Clean(entries[i]);
            if (wanted.Any(w => e.StartsWith(w, StringComparison.OrdinalIgnoreCase))) return i;
        }
        return -1;
    }

    /// <summary>The most rows a dye palette's list has: its names are values first … first + 35, and from
    /// first + 36 on the window lists the item each row would use (Recording 4: [239] Terebinth for "No
    /// Color", [240] Standard Spectrum Dye for Snow White…).</summary>
    public const int DyeListMost = 36;

    /// <summary>
    /// A dye's row in the dye window's list, from the window's text values by index: the palette's names
    /// are the values from <paramref name="first"/> on, one a row, up to the first empty one (1.0.0, from
    /// Recordings 3 and 4). The first palette starts with "No Color" (row 0), so Snow White is row 1
    /// (recordings: Snow White [5, 1, 1, 0], Soot Black [5, 6, 6, 0]); the others start with a dye (the
    /// browns' row 0 is Sunset Orange), which 0.9.x never found. Null when the dye isn't in the list.
    /// </summary>
    public static int? DyeRow(IReadOnlyDictionary<int, string> values, string dyeName, int first)
    {
        var want = Clean(dyeName);
        if (want.Length == 0) return null;
        for (var row = 0; row < DyeListMost; row++)
        {
            if (!values.TryGetValue(first + row, out var text) || Clean(text).Length == 0) break;
            if (string.Equals(Clean(text), want, StringComparison.OrdinalIgnoreCase)) return row;
        }
        return null;
    }

    /// <summary>
    /// How many rows the dye window's list has: its names from <paramref name="first"/> on, up to the first
    /// empty value (at most <see cref="DyeListMost"/>); 0 before it's loaded. 0.9.x counted on to the last
    /// name before "No Color" came round again, which took in the items' names after the list (38 for a
    /// palette of 7), so a row past the list's end wasn't caught.
    /// </summary>
    public static int DyeListCount(IReadOnlyDictionary<int, string> values, int first)
    {
        var n = 0;
        while (n < DyeListMost && values.TryGetValue(first + n, out var text) && Clean(text).Length > 0) n++;
        return n;
    }

    public static string Clean(string s) =>
        new string(s.Where(c => !char.IsControl(c) && c != '�' && !(c >= '' && c <= '')).ToArray()).Trim();
}
