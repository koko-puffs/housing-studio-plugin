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
