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

    /// <summary>
    /// Where a storeroom slot most likely sits in the furnishing window's storeroom list: the list
    /// shows the storeroom's pieces in container and slot order, empty slots left out. Only a first
    /// guess: the plugin reads which piece the game actually picked, and looks further if it's wrong.
    /// </summary>
    public static int GuessStoreroomIndex(IEnumerable<(int Container, int Slot)> occupied, (int Container, int Slot) wanted) =>
        occupied.Where(s => s.Container < wanted.Container || (s.Container == wanted.Container && s.Slot < wanted.Slot)).Distinct().Count();

    /// <summary>
    /// The list positions to try, nearest the guess first (guess, guess+1, guess-1, …), within the
    /// list's length. The plugin stops at the first that picks the right piece.
    /// </summary>
    public static IEnumerable<int> SearchOrder(int guess, int count, int maxTries)
    {
        if (count <= 0) yield break;
        guess = Math.Clamp(guess, 0, count - 1);
        var tried = 0;
        for (var d = 0; tried < maxTries && (guess + d < count || guess - d >= 0); d++)
        {
            if (guess + d < count)
            {
                yield return guess + d;
                tried++;
            }
            if (d > 0 && guess - d >= 0 && tried < maxTries)
            {
                yield return guess - d;
                tried++;
            }
        }
    }
}
