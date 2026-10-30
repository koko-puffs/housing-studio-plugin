using HousingStudio.Core;

namespace HousingStudio.UI.Model;

public enum Pace
{
    Careful,
    Steady,
    Quick,
}

/// <summary>
/// Settings → Pace: the three presets of <see cref="Pacing.Presets"/> over the run's pause between
/// pieces (<c>IntervalMs</c>), shown Careful / Steady / Quick as the mockup orders them: Careful
/// 1.5 s, Steady 0.4 s (the default) and Quick 0.15 s. A pause set some other way shows as the nearest.
/// </summary>
public static class PaceSetting
{
    public static readonly string[] Labels = { "Careful", "Steady", "Quick" };

    public static int Ms(Pace pace) => pace switch
    {
        Pace.Careful => Pacing.CarefulMs,
        Pace.Quick => Pacing.QuickMs,
        _ => Pacing.SteadyMs,
    };

    /// <summary>The choice nearest a pause in milliseconds (the cut-offs are halfway between the presets).</summary>
    public static Pace Of(int ms) =>
        ms <= (Pacing.QuickMs + Pacing.SteadyMs) / 2 ? Pace.Quick
        : ms < (Pacing.SteadyMs + Pacing.CarefulMs) / 2 ? Pace.Steady
        : Pace.Careful;

    /// <summary>"careful", "steady", "quick", as in "at a steady pace".</summary>
    public static string Word(Pace pace) => Labels[(int)pace].ToLowerInvariant();
}
