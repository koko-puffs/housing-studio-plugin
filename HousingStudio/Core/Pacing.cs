using System;

namespace HousingStudio.Core;

/// <summary>
/// How long to wait between pieces: the interval you pick, give or take a little (a person never
/// clicks on an exact beat), and never quicker than <see cref="MinimumMs"/>.
/// </summary>
public static class Pacing
{
    /// <summary>The quickest the plugin ever goes between two pieces, whatever the setting.</summary>
    public const int MinimumMs = 600;

    public const int DefaultMs = 1500;

    /// <param name="intervalMs">The interval picked in Settings.</param>
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
