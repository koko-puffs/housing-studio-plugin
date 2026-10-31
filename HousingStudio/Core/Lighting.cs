namespace HousingStudio.Core;

/// <summary>
/// A home's indoor lighting level, as layouts keep it (<c>properties.light</c>, a whole number 0–5,
/// 5 the brightest and 0 the lights out; the site lights a room at level × 20%) so the site can light
/// its 3D view of the house as the game does. Only an interior has one: a garden's file leaves it
/// out, and an older file hasn't got it.
/// </summary>
public static class Lighting
{
    public const int Darkest = 0;
    public const int Brightest = 5;

    /// <summary>
    /// The game's own reading (FFXIVClientStructs' <c>HousingManager.GetBrightness()</c>, which is
    /// <c>5 - GetInvertedBrightness()</c>, the inverted one 0–5 with higher darker) as a layout's
    /// level: 0–5 as it is. Null for anything else: a byte that wrapped below 0 (an inverted level
    /// over 5 reads 251–255) isn't a level.
    /// </summary>
    public static int? FromGame(int brightness) => Valid(brightness);

    /// <summary>A file's <c>properties.light</c>: a whole number 0–5, else null (none, or not one).</summary>
    public static int? FromFile(double value) =>
        value is >= Darkest and <= Brightest && value == System.Math.Floor(value) ? (int)value : null;

    /// <summary>A level to write: 0–5 as it is, else null (left out of the file).</summary>
    public static int? Valid(int? level) => level is >= Darkest and <= Brightest ? level : null;

    /// <summary>This house's words for it: "lighting 3 of 5", or "lighting off" at 0.</summary>
    public static string Words(int level) => level <= Darkest ? "lighting off" : $"lighting {level} of {Brightest}";
}
