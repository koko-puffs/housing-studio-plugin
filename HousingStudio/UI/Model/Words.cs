using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.UI.Model;

/// <summary>
/// The window's words, built the same way everywhere: counts with the right noun, sizes of home in
/// the game's words ("House (medium)"), lists ("A, B and C"), and times ("about 25 minutes").
/// Pure, so it's tested.
/// </summary>
public static class Words
{
    /// <summary>"1 piece", "3 pieces".</summary>
    public static string Count(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    /// <summary>The size of a home as the game names it: "Cottage (small)", "House (medium)", "Mansion (large)", "Apartment".</summary>
    public static string Home(string? size) => size switch
    {
        "Small" => "Cottage (small)",
        "Medium" => "House (medium)",
        "Large" => "Mansion (large)",
        "Apartment" => "Apartment",
        _ => "Any home",
    };

    /// <summary>The same inside a sentence: "house (medium)", "apartment", "home".</summary>
    public static string HomeInSentence(string? size) => size switch
    {
        "Small" => "cottage (small)",
        "Medium" => "house (medium)",
        "Large" => "mansion (large)",
        "Apartment" => "apartment",
        _ => "home",
    };

    /// <summary>"A", "A and B", "A, B and C".</summary>
    public static string List(IEnumerable<string> items)
    {
        var list = items.Where(s => !string.IsNullOrEmpty(s)).ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }

    /// <summary>The first few names then "and N more": "Oak Table, Bed and 3 more".</summary>
    public static string Some(IReadOnlyList<string> names, int show = 2)
    {
        if (names.Count <= show + 1) return List(names);
        return $"{string.Join(", ", names.Take(show))} and {names.Count - show} more";
    }

    public static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>What there is to buy, for the Pieces footer: "2 dyes to buy, no pieces".</summary>
    public static string ToBuy(int pieces, int dyes) => (pieces, dyes) switch
    {
        (0, 0) => "Nothing to buy",
        (0, _) => $"{Count(dyes, "dye")} to buy, no pieces",
        (_, 0) => $"{Count(pieces, "piece")} to buy, no dyes",
        _ => $"{Count(pieces, "piece")} and {Count(dyes, "dye")} to buy",
    };

    /// <summary>
    /// How long, in plain words: "under a minute", "about a minute", "about 12 minutes", rounded to
    /// five minutes from 20 ("about 25 minutes"), then to half hours ("about an hour", "about 1½ hours").
    /// </summary>
    public static string About(TimeSpan t)
    {
        var minutes = t.TotalMinutes;
        if (minutes < 1) return "under a minute";
        if (minutes < 1.5) return "about a minute";
        if (minutes < 20) return $"about {(int)Math.Round(minutes, MidpointRounding.AwayFromZero)} minutes";
        if (minutes < 57.5) return $"about {(int)(Math.Round(minutes / 5, MidpointRounding.AwayFromZero) * 5)} minutes";
        var halves = (int)Math.Round(minutes / 30, MidpointRounding.AwayFromZero);
        var hours = halves / 2;
        var half = halves % 2 == 1;
        if (hours == 1 && !half) return "about an hour";
        return $"about {hours}{(half ? "½" : string.Empty)} hours";
    }

    /// <summary>"Walls, floors and lights" (or "window and door"), from fixture types ("Wall", "Floor", "Light").</summary>
    public static string Kinds(IEnumerable<string> types, bool capital, bool plural = true) =>
        Capitalise(List(types.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().Select(t => plural ? Plural(t) : t)), capital);

    private static string Capitalise(string s, bool capital) => capital ? Capital(s) : s;

    /// <summary>"wall" → "walls", "light" → "lights", "glass" → "glasses".</summary>
    public static string Plural(string noun) =>
        noun.EndsWith('s') || noun.EndsWith("sh", StringComparison.Ordinal) || noun.EndsWith("ch", StringComparison.Ordinal) ? noun + "es"
        : noun.EndsWith('y') && noun.Length > 1 && !"aeiou".Contains(noun[^2]) ? noun[..^1] + "ies"
        : noun + "s";
}
