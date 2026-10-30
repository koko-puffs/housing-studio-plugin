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

    /// <summary>The line under the choice.</summary>
    public static string Note(Pace pace) => pace switch
    {
        Pace.Steady => "Gives the game more time between clicks: for a slow connection, or if pieces get skipped",
        Pace.Daring => "As fast as it goes: less time between clicks, for the daring. Back to Quick if pieces get skipped",
        _ => "Quick but sure: what most homes want",
    };
}
