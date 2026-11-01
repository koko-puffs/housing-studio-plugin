using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>
/// Settings → Pace: the three presets of <see cref="Pacing.Profiles"/>, shown Steady / Quick / Daring,
/// slowest first, with a line under them saying what the one chosen does.
/// </summary>
public static class PaceSetting
{
    public static readonly string[] Labels = Pacing.Profiles.Select(p => p.Name).ToArray();

    /// <summary>"steady", "quick", "daring", as in "at a quick pace".</summary>
    public static string Word(Pace pace) => Pacing.Profile(pace).Name.ToLowerInvariant();

    /// <summary>Settings' one line for the pace chosen: "0.15 s between pieces" (<see cref="Note"/> is its tooltip).</summary>
    public static string Short(Pace pace)
    {
        var ms = Pacing.Profile(pace).PieceGapMs;
        return $"{(ms / 1000.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} s between pieces";
    }

    /// <summary>What the pace chosen does, in a sentence.</summary>
    public static string Note(Pace pace) => pace switch
    {
        Pace.Steady => "Gives the game more time between clicks: for a slow connection, or if pieces get skipped",
        Pace.Daring => "As fast as the plugin goes, placing and on errands (the dye window keeps Quick's timing): back to Quick if pieces get skipped or a click is missed",
        _ => "Quick and sure: what most homes want",
    };
}
