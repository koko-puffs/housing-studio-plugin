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

    /// <summary>
    /// A dye's row in the dye window's list, from the window's text values by index: the list's names
    /// start at <paramref name="first"/> with "No Color" (row 0), so Snow White, at first + 1, is row 1
    /// (recordings: Snow White [5, 1, 1, 0], Soot Black [5, 6, 6, 0]). The first match counts: the
    /// window repeats the chosen dyes further down. Null when the dye isn't listed.
    /// </summary>
    public static int? DyeRow(IReadOnlyDictionary<int, string> values, string dyeName, int first)
    {
        // The list ends where its first entry's words ("No Color") come round again: after it the
        // window shows the dyes chosen (Recording 2: [426] No Color … [432] Soot Black).
        values.TryGetValue(first, out var head);
        foreach (var (index, text) in values.OrderBy(kv => kv.Key))
        {
            if (index <= first) continue;
            if (head != null && string.Equals(Clean(text), Clean(head), StringComparison.OrdinalIgnoreCase)) break;
            if (string.Equals(Clean(text), Clean(dyeName), StringComparison.OrdinalIgnoreCase)) return index - first;
        }
        return null;
    }

    /// <summary>
    /// How many rows the dye window's list has: rows are numbered by their value's place after
    /// <paramref name="first"/> ("No Color", row 0), up to the last before its words come round again;
    /// 0 before it's loaded.
    /// </summary>
    public static int DyeListCount(IReadOnlyDictionary<int, string> values, int first)
    {
        if (!values.TryGetValue(first, out var head) || Clean(head).Length == 0) return 0;
        var last = first;
        foreach (var (index, text) in values.OrderBy(kv => kv.Key))
        {
            if (index <= first) continue;
            if (string.Equals(Clean(text), Clean(head), StringComparison.OrdinalIgnoreCase)) break;
            if (Clean(text).Length > 0) last = index;
        }
        return last - first + 1;
    }

    public static string Clean(string s) =>
        new string(s.Where(c => !char.IsControl(c) && c != '�' && !(c >= '' && c <= '')).ToArray()).Trim();
}
