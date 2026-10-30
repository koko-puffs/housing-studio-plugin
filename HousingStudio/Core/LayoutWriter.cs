using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HousingStudio.Core;

/// <summary>A placed piece, to be written to a file.</summary>
/// <param name="Position">Inside: the house's coordinates. Garden: the plot's own.</param>
/// <param name="Material">What's on it: a partition's wallpaper, a frame's painting (its item), or none.</param>
public sealed record SavedPiece(uint ItemId, string Name, Vector3 Position, float Yaw, string? DyeHex, SavedItem? Material = null);

/// <summary>An item named in a file (a piece's wallpaper).</summary>
public sealed record SavedItem(uint ItemId, string Name);

/// <summary>
/// A fixture as MakePlace writes it: inside, a floor's ("Ground Floor", "Upper Floor", "Basement")
/// wall, floor or light, or the house's own ("Main") walls, window and door; outside ("Exterior"),
/// the roof, walls, window, door, decorations, placard and fence, with their dyes.
/// </summary>
public sealed record SavedFixture(string Level, string Type, uint ItemId, string Name, string? DyeHex = null);

/// <summary>Everything about the home apart from its furniture.</summary>
/// <param name="Style">The District line: a district's name, or "Minimalist" (II), as the site reads it.</param>
public sealed record SavedHome(string? HouseSize, string? Style, IReadOnlyList<SavedFixture> Interior, IReadOnlyList<SavedFixture> Exterior, int? Plot);

/// <summary>
/// Writes what's placed as a layout, the same JSON Housing Studio's site writes for a build
/// (build/model.ts <c>toLayout</c>) and MakePlace reads, so a backup taken before placing can be
/// loaded again here, uploaded to the site, or opened in MakePlace.
/// </summary>
public static class LayoutWriter
{
    /// <summary>A path to save to, ending in .build unless it ends in .build or .json already.</summary>
    public static string WithExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (ext.Equals(".build", System.StringComparison.OrdinalIgnoreCase) || ext.Equals(".json", System.StringComparison.OrdinalIgnoreCase))
            return path;
        return path.TrimEnd('.', ' ') + ".build";
    }

    public static string Write(
        string? houseSize,
        string? districtName,
        IReadOnlyList<SavedPiece> interior,
        IReadOnlyList<SavedPiece> garden,
        string? buildName = null,
        int? plot = null) =>
        Write(new SavedHome(houseSize, districtName, [], [], plot), interior, garden, buildName);

    public static string Write(SavedHome home, IReadOnlyList<SavedPiece> interior, IReadOnlyList<SavedPiece> garden, string? buildName = null)
    {
        var inside = new JsonArray(new JsonObject
        {
            ["level"] = "Main",
            ["type"] = "District",
            ["name"] = home.Style ?? string.Empty,
            ["itemId"] = 0,
        });
        foreach (var f in home.Interior) inside.Add(Fixture(f));
        var root = new JsonObject
        {
            ["playerTransform"] = Transform(Vector3.Zero, 0),
            ["houseSize"] = home.HouseSize ?? "Small",
            ["interiorScale"] = 1,
            ["interiorFixture"] = inside,
            ["interiorFurniture"] = new JsonArray(interior.Select(Entry).ToArray<JsonNode?>()),
            ["exteriorScale"] = 1,
            ["exteriorFixture"] = new JsonArray(home.Exterior.Select(Fixture).ToArray<JsonNode?>()),
            ["exteriorFurniture"] = new JsonArray(garden.Select(Entry).ToArray<JsonNode?>()),
        };
        var props = new JsonObject();
        if (!string.IsNullOrWhiteSpace(buildName)) props["build"] = buildName;
        if (home.Plot is >= 1 and <= 60) props["plot"] = home.Plot;
        root["properties"] = props;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Transform(Vector3 position, float yaw)
    {
        var (location, rotation) = Transforms.ToFile(position, yaw);
        return new JsonObject
        {
            ["location"] = new JsonArray(location.Select(v => (JsonNode?)v).ToArray()),
            ["rotation"] = new JsonArray(rotation.Select(v => (JsonNode?)v).ToArray()),
            ["scale"] = new JsonArray(1, 1, 1),
        };
    }

    private static JsonObject Fixture(SavedFixture f)
    {
        var o = new JsonObject
        {
            ["level"] = f.Level,
            ["type"] = f.Type,
            ["name"] = f.Name,
            ["itemId"] = f.ItemId,
        };
        if (f.DyeHex != null) o["properties"] = new JsonObject { ["color"] = f.DyeHex.ToUpperInvariant() + "FF" };
        return o;
    }

    private static JsonObject Entry(SavedPiece p)
    {
        var properties = new JsonObject();
        if (p.DyeHex != null) properties["color"] = p.DyeHex.ToUpperInvariant() + "FF";
        // MakePlace's: a dyed piece has no material, and the other way round.
        else if (p.Material is { ItemId: > 0 } m) properties["material"] = new JsonObject { ["name"] = m.Name, ["itemId"] = m.ItemId };
        return new JsonObject
        {
            ["transform"] = Transform(p.Position, p.Yaw),
            ["properties"] = properties,
            ["attachments"] = new JsonArray(),
            ["name"] = p.Name,
            ["itemId"] = p.ItemId,
        };
    }
}
