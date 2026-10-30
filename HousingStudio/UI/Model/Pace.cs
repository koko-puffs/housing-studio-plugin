namespace HousingStudio.UI.Model;

public enum Pace
{
    Careful,
    Steady,
    Quick,
}

/// <summary>
/// Settings → Pace, three choices over the run's pause between pieces (<c>IntervalMs</c>):
/// Careful 2.5 s, Steady 1.5 s (the default since 0.1) and Quick 0.9 s (still above the 0.6 s floor
/// in <see cref="Core.Pacing"/>). A pause set by hand in an older version shows as the nearest.
/// </summary>
public static class PaceSetting
{
    public const int CarefulMs = 2500;
    public const int SteadyMs = 1500;
    public const int QuickMs = 900;

    public static readonly string[] Labels = { "Careful", "Steady", "Quick" };

    public static int Ms(Pace pace) => pace switch
    {
        Pace.Careful => CarefulMs,
        Pace.Quick => QuickMs,
        _ => SteadyMs,
    };

    /// <summary>The choice nearest a pause in milliseconds.</summary>
    public static Pace Of(int ms) => ms switch
    {
        <= 1200 => Pace.Quick,
        < 2000 => Pace.Steady,
        _ => Pace.Careful,
    };

    /// <summary>"careful", "steady", "quick", as in "at a steady pace".</summary>
    public static string Word(Pace pace) => Labels[(int)pace].ToLowerInvariant();
}
