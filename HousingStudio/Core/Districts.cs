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
