using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HousingStudio.UI.Model;

/// <summary>
/// The run's step labels are written for the log ("Set Hingan Bookshelf at its spot (the editor's
/// set-down function)", "Open the Storeroom tab: HousingGoods ← [i:11]"). The current piece card
/// says the same in a few plain words, as the mockup does ("Setting it at its spot"): no window
/// names or values, the piece called "it", and the verb as what it's doing now. Pure, so it's tested.
/// </summary>
public static partial class StepWords
{
    private static readonly Dictionary<string, string> Doing = new(StringComparer.Ordinal)
    {
        ["Aim"] = "Aiming",
        ["Answer"] = "Answering",
        ["Check"] = "Checking",
        ["Choose"] = "Choosing",
        ["Close"] = "Closing",
        ["Confirm"] = "Confirming",
        ["Find"] = "Finding",
        ["Give"] = "Giving",
        ["Open"] = "Opening",
        ["Pick"] = "Picking",
        ["Press"] = "Pressing",
        ["See"] = "Seeing",
        ["Set"] = "Setting",
        ["Switch"] = "Switching",
        ["Read"] = "Reading",
        ["Put"] = "Putting",
        ["Scroll"] = "Scrolling",
        ["Wait"] = "Waiting",
        ["Type"] = "Typing",
    };

    /// <param name="label">The runner's step label.</param>
    /// <param name="itemName">The piece's name, said as "it".</param>
    public static string? Friendly(string? label, string? itemName)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var s = label.Trim();
        var forYou = s.StartsWith("You:", StringComparison.Ordinal);
        if (forYou) s = s[4..].Trim();

        // Window calls and their values: everything after a colon that names one.
        var colon = s.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && (s.IndexOf('←', colon) > 0 || s.IndexOf('[', colon) > 0)) s = s[..colon];
        // Asides in brackets.
        s = Brackets().Replace(s, string.Empty).Trim();

        if (!string.IsNullOrEmpty(itemName))
        {
            s = s.Replace(itemName + "'s", "its", StringComparison.Ordinal).Replace(itemName, "it", StringComparison.Ordinal);
            s = s.Replace(" up it", " it up", StringComparison.Ordinal);
        }

        if (forYou) return Words.Capital(s);
        var space = s.IndexOf(' ');
        var first = space < 0 ? s : s[..space];
        if (Doing.TryGetValue(first, out var ing)) s = ing + (space < 0 ? string.Empty : s[space..]);
        return Words.Capital(s);
    }

    [GeneratedRegex(@"\s*\([^()]*\)")]
    private static partial Regex Brackets();
}

/// <summary>Which run the placing screen shows: a real one, a preview (Mark the spots) or a rehearsal.</summary>
public enum RunKind
{
    Placing,
    Preview,
    Rehearsal,
}

/// <summary>
/// The placing screen's words (0.9.0): the short eyebrow, the big figure's words with Ready's to-do
/// numbers (<see cref="RunFigure"/>), and the line under the piece in hand. Pure, so it's tested.
/// </summary>
public static class PlacingWords
{
    /// <summary>"Placing · Solution9Loft", "Previewing · …", "Rehearsal · …": the short name, never a cut-off file name.</summary>
    public static string Eyebrow(RunKind kind, string? shortTitle)
    {
        var head = kind switch
        {
            RunKind.Preview => "Previewing",
            RunKind.Rehearsal => "Rehearsal",
            _ => "Placing",
        };
        return string.IsNullOrWhiteSpace(shortTitle) ? head : $"{head} · {shortTitle}";
    }

    /// <summary>
    /// The words beside the big figure: "of 46 to do · then 20 dyes" (placing), "· 3 of 20 dyed" once
    /// the dyes are being done; "of 20 to dye" when the dyes are all there is; "of 12 to put away" when
    /// putting away is. A preview's are "marked", a rehearsal's "rehearsed".
    /// </summary>
    /// <param name="dyeing">The run is on its dyes now.</param>
    public static string Figure(RunFigure f, RunKind kind, bool dyeing)
    {
        if (f.ToDo > 0)
        {
            var head = kind switch
            {
                RunKind.Preview => $"of {f.ToDo} marked",
                RunKind.Rehearsal => $"of {f.ToDo} rehearsed",
                _ => $"of {f.ToDo} to do",
            };
            if (f.Dyes <= 0) return head;
            if (!dyeing && f.Dyed == 0) return $"{head} · then {Words.Count(f.Dyes, "dye")}";
            return kind switch
            {
                RunKind.Preview => $"{head} · {f.Dyed} of {Words.Count(f.Dyes, "dye")} marked",
                RunKind.Rehearsal => $"{head} · {f.Dyed} of {Words.Count(f.Dyes, "dye")} rehearsed",
                _ => $"{head} · {f.Dyed} of {f.Dyes} dyed",
            };
        }
        if (f.Dyes > 0)
            return kind switch
            {
                RunKind.Preview => $"of {Words.Count(f.Dyes, "dye")} marked",
                RunKind.Rehearsal => $"of {Words.Count(f.Dyes, "dye")} rehearsed",
                _ => $"of {f.Dyes} to dye",
            };
        if (f.PutAway > 0) return $"of {f.PutAway} to put away";
        return "nothing to do";
    }

    /// <summary>Under the piece in hand: "1 skipped so far · listed at the end" (pieces that didn't work, skipped or not).</summary>
    public static string Skipped(int skipped) => $"{skipped} skipped so far · listed at the end";
}
