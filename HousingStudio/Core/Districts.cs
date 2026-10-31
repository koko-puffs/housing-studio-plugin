using System;

namespace HousingStudio.Core;

/// <summary>Housing Studio's district keys, from a file's District line or the game's territory names.</summary>
public static class Districts
{
    public static readonly string[] Keys = { "mist", "lavender-beds", "goblet", "shirogane", "empyreum" };

    /// <summary>"The Lavender Beds" → "lavender-beds" (as the site's places.ts <c>districtFrom</c>); null otherwise.</summary>
    public static string? FromName(string? name)
    {
        var s = (name ?? string.Empty).ToLowerInvariant();
        if (s.Contains("mist")) return "mist";
        if (s.Contains("lavender")) return "lavender-beds";
        if (s.Contains("goblet")) return "goblet";
        if (s.Contains("shirogane")) return "shirogane";
        if (s.Contains("empyreum")) return "empyreum";
        return null;
    }

    /// <summary>
    /// The district of a ward's or a district's interior's TerritoryType name, by its first two
    /// letters ("s1h1" the Mist's ward, "s1i2" a medium house there, "f1…" the Lavender Beds); null
    /// for anything else (a Minimalist interior's "h1i2" says no district).
    /// </summary>
    public static string? OfTerritoryName(string? territoryName)
    {
        var n = (territoryName ?? string.Empty).ToLowerInvariant();
        if (n.Length < 4) return null;
        return n[..2] switch
        {
            "s1" => "mist",
            "f1" => "lavender-beds",
            "w1" => "goblet",
            "e1" => "shirogane",
            "r1" => "empyreum",
            _ => null,
        };
    }

    public static string Display(string? key) => key switch
    {
        "mist" => "Mist",
        "lavender-beds" => "The Lavender Beds",
        "goblet" => "The Goblet",
        "shirogane" => "Shirogane",
        "empyreum" => "Empyreum",
        _ => "Unknown district",
    };

    /// <summary>
    /// The style an interior is built in, from its TerritoryType name, as a file's District line
    /// names it: "s1i2" → "Mist", "h1i2" → "Minimalist", "h2i2" → "Minimalist II"; null when it
    /// isn't a home's interior or the style is one this doesn't know.
    /// </summary>
    public static string? StyleOfInterior(string territoryName)
    {
        var n = territoryName.ToLowerInvariant();
        if (n.Length < 4 || n[2] != 'i') return null;
        return n[..2] switch
        {
            "s1" => "Mist",
            "f1" => "The Lavender Beds",
            "w1" => "The Goblet",
            "e1" => "Shirogane",
            "r1" => "Empyreum",
            "h1" => "Minimalist",
            ['h', var k] when char.IsDigit(k) => $"Minimalist {Roman(k - '0')}",
            _ => null,
        };
    }

    private static string Roman(int n) => n switch { 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", _ => n.ToString() };

    /// <summary>
    /// The size of a house from its interior's TerritoryType name ("s1i2" is a medium house in
    /// Mist): "Small", "Medium", "Large" or "Apartment", or null when it isn't a home's interior.
    /// </summary>
    public static string? HouseSizeOfInterior(string territoryName)
    {
        var n = territoryName.ToLowerInvariant();
        if (n.Length < 4 || n[2] != 'i') return null;
        return n[3] switch
        {
            '1' => "Small",
            '2' => "Medium",
            '3' => "Large",
            '4' => "Apartment",
            _ => null,
        };
    }

    /// <summary>"s", "m", "l" → "Small", "Medium", "Large".</summary>
    public static string? HouseSizeOfPlot(string size) => size switch
    {
        "s" => "Small",
        "m" => "Medium",
        "l" => "Large",
        _ => null,
    };

    public static bool SameSize(string? a, string? b) =>
        a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
