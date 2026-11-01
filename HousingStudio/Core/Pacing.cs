using System;

namespace HousingStudio.Core;

/// <summary>Settings → Pace, slowest first: Steady, Quick (the default) and Daring (0.7.14: each a step faster).</summary>
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
/// <param name="ErrandGapMs">The least time between two of the errands' clicks (shops, menus, the board).</param>
public readonly record struct PaceProfile(Pace Pace, string Name, int PieceGapMs, int ClickGapMs, double WaitScale, int ErrandGapMs = 300)
{
    /// <summary>A settle or pause as 0.3.2's pace has it (today's Steady, scale 1), at this one.</summary>
    public TimeSpan Wait(int quickMs) => TimeSpan.FromMilliseconds(Math.Round(quickMs * WaitScale));
}

/// <summary>
/// How fast a run goes. 0.7.14 moved each a step faster, as asked: Steady is what Quick was (0.3.2's
/// pace, 0.15 s between pieces, 0.3 s between clicks); Quick, the default, is what Daring was (0.05 s,
/// 0.2 s, waits at two thirds); Daring is new and as fast as the plugin goes (0.02 s between pieces,
/// 0.12 s between clicks, waits at 0.4, the errands' clicks 0.15 s apart and the board's results read
/// sooner). The settling checks stay at every pace: they wait for the game, not for a clock. The pause
/// between pieces varies a little (a person never clicks on an exact beat).
/// </summary>
public static class Pacing
{
    /// <summary>The quickest the plugin ever goes between two pieces, whatever the setting.</summary>
    public const int MinimumMs = 20;

    /// <summary>A new install's pace: Quick (0.7.14's Quick is what Daring was).</summary>
    public const Pace Default = Pace.Quick;

    /// <summary>The presets, in <see cref="Pace"/>'s order.</summary>
    public static readonly PaceProfile[] Profiles =
    {
        new(Pace.Steady, "Steady", 150, 300, 1.0, 400),
        new(Pace.Quick, "Quick", 50, 200, 0.65, 300),
        new(Pace.Daring, "Daring", 20, 120, 0.4, 150),
    };

    /// <summary>A pace's profile (an unknown value, from a hand-edited config, is the default's).</summary>
    public static PaceProfile Profile(Pace pace) =>
        (int)pace >= 0 && (int)pace < Profiles.Length ? Profiles[(int)pace] : Profiles[(int)Default];

    /// <summary>
    /// The pace a pause between pieces from before 0.3.3 becomes: Careful (1.5 s) the safer Steady, the
    /// rest Quick (in that day's names; <see cref="FromBefore0714"/> then moves them).
    /// </summary>
    public static Pace FromOldInterval(int intervalMs) => intervalMs >= 950 ? Pace.Steady : Pace.Quick;

    /// <summary>
    /// A pace chosen before 0.7.14, at the same speed (or the nearest) in 0.7.14's names: Daring was
    /// today's Quick; Quick is today's Steady; the old Steady (0.6 s) is gone, so Steady too.
    /// </summary>
    public static Pace FromBefore0714(Pace old) => old == Pace.Daring ? Pace.Quick : Pace.Steady;

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
