using System;

namespace HousingStudio.Core;

/// <summary>Settings → Pace, slowest first: Steady, Quick (the default) and Daring.</summary>
public enum Pace
{
    Steady,
    Quick,
    Daring,
}

/// <summary>
/// What a pace sets: the pause between two pieces, the least time between two game actions within a
/// piece (window clicks and the editor's own pick-up and set-down alike), and how long the run waits
/// for the game to settle (its "a few frames running" checks and short pauses; timeouts aren't touched,
/// they only decide when to give up).
/// </summary>
public readonly record struct PaceProfile(Pace Pace, string Name, int PieceGapMs, int ClickGapMs, double WaitScale)
{
    /// <summary>A settle or pause of the Quick pace, at this one.</summary>
    public TimeSpan Wait(int quickMs) => TimeSpan.FromMilliseconds(Math.Round(quickMs * WaitScale));
}

/// <summary>
/// How fast a run goes. Quick is what 0.3.2 ran (0.15 s between pieces, 0.3 s between clicks), and
/// the default since 0.3.3; Steady gives the game half as long again for everything, for a slow
/// connection or a busy house; Daring cuts it to two thirds, for whoever likes to live dangerously
/// (0.2.4's 0.25 s between clicks with no settling raced the game; the settling checks stay).
/// The pause between pieces varies a little (a person never clicks on an exact beat).
/// </summary>
public static class Pacing
{
    /// <summary>The quickest the plugin ever goes between two pieces, whatever the setting.</summary>
    public const int MinimumMs = 50;

    /// <summary>A new install's pace (0.7.13: Daring; it was Quick).</summary>
    public const Pace Default = Pace.Daring;

    /// <summary>The presets, in <see cref="Pace"/>'s order.</summary>
    public static readonly PaceProfile[] Profiles =
    {
        new(Pace.Steady, "Steady", 600, 450, 1.5),
        new(Pace.Quick, "Quick", 150, 300, 1.0),
        new(Pace.Daring, "Daring", 50, 200, 0.65),
    };

    /// <summary>A pace's profile (an unknown value, from a hand-edited config, is the default's).</summary>
    public static PaceProfile Profile(Pace pace) =>
        (int)pace >= 0 && (int)pace < Profiles.Length ? Profiles[(int)pace] : Profiles[(int)Default];

    /// <summary>
    /// The pace a pause between pieces from before 0.3.3 becomes: Careful (1.5 s) the safer Steady, the
    /// rest Quick, the new default.
    /// </summary>
    public static Pace FromOldInterval(int intervalMs) => intervalMs >= 950 ? Pace.Steady : Pace.Quick;

    /// <param name="intervalMs">The pace's pause between pieces.</param>
    /// <param name="jitter">How much it varies, 0 to 0.5 (0.25 is ±25%).</param>
    /// <param name="random">A value from 0 to 1.</param>
    public static int NextDelayMs(int intervalMs, float jitter, double random)
    {
        jitter = Math.Clamp(jitter, 0f, 0.5f);
        random = Math.Clamp(random, 0, 1);
        var baseMs = Math.Max(MinimumMs, intervalMs);
        var factor = 1 + jitter * (2 * random - 1);
        return Math.Max(MinimumMs, (int)Math.Round(baseMs * factor));
    }
}
