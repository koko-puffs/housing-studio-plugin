namespace HousingStudio.Core;

/// <summary>Small text helpers. Pure, so they're tested.</summary>
public static class Text
{
    /// <summary>
    /// The first <paramref name="max"/> characters of <paramref name="s"/>, never cutting a character made of
    /// two (an emoji, or a rarer kanji, 1.0.2: half of one made a file name or a title the site refused).
    /// </summary>
    public static string Cut(string s, int max)
    {
        if (max <= 0) return string.Empty;
        if (s.Length <= max) return s;
        var n = max;
        if (char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n];
    }
}
