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

    public static string Clean(string s) =>
        new string(s.Where(c => !char.IsControl(c) && c != '�' && !(c >= '' && c <= '')).ToArray()).Trim();
}
