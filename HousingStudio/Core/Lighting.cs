using System;

namespace HousingStudio.Core;

/// <summary>
/// A home's indoor lighting level, as layouts keep it (<c>properties.light</c>, a whole number 1–5,
/// 5 the brightest) so the site can light its 3D view of the house as the game does. Only an interior
/// has one: a garden's file leaves it out, and an older file hasn't got it.
/// </summary>
public static class Lighting
{
    public const int Darkest = 1;
    public const int Brightest = 5;

    /// <summary>
    /// The game's own reading (FFXIVClientStructs' <c>HousingManager.GetBrightness()</c>, which is
    /// <c>5 - GetInvertedBrightness()</c>, the inverted one 0–5 with higher darker) as a layout's
    /// level: 1–5, the game's darkest step (0, the lights out) kept as 1, the darkest a layout says.
    /// Null for anything outside 0–5: a byte that wrapped below 0 (an inverted level over 5 reads
    /// 251–255) isn't a level.
    /// </summary>
    public static int? FromGame(int brightness) =>
        brightness is >= 0 and <= Brightest ? Math.Clamp(brightness, Darkest, Brightest) : null;

    /// <summary>A file's <c>properties.light</c>: a whole number 1–5, else null (none, or not one).</summary>
    public static int? FromFile(double value) =>
        value is >= Darkest and <= Brightest && value == Math.Floor(value) ? (int)value : null;

    /// <summary>A level to write: 1–5 as it is, else null (left out of the file).</summary>
    public static int? Valid(int? level) => level is >= Darkest and <= Brightest ? level : null;

    /// <summary>This house's words for it: "lighting 3 of 5".</summary>
    public static string Words(int level) => $"lighting {level} of {Brightest}";
}
