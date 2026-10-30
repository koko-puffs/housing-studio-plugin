using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HousingStudio.Core;

/// <summary>A placed piece, to be written to a file.</summary>
/// <param name="Position">Inside: the house's coordinates. Garden: the plot's own.</param>
public sealed record SavedPiece(uint ItemId, string Name, Vector3 Position, float Yaw, string? DyeHex);

/// <summary>
/// Writes what's placed as a layout, the same JSON Housing Studio's site writes for a build
/// (build/model.ts <c>toLayout</c>) and MakePlace reads, so a backup taken before placing can be
/// loaded again here, uploaded to the site, or opened in MakePlace.
/// </summary>
public static class LayoutWriter
{
    public static string Write(
        string? houseSize,
        string? districtName,
        IReadOnlyList<SavedPiece> interior,
        IReadOnlyList<SavedPiece> garden,
        string? buildName = null,
        int? plot = null)
    {
        var root = new JsonObject
        {
            ["playerTransform"] = Transform(Vector3.Zero, 0),
            ["houseSize"] = houseSize ?? "Small",
            ["interiorScale"] = 1,
            ["interiorFixture"] = new JsonArray(new JsonObject
            {
                ["level"] = "Main",
                ["type"] = "District",
                ["name"] = districtName ?? string.Empty,
                ["itemId"] = 0,
            }),
            ["interiorFurniture"] = new JsonArray(interior.Select(Entry).ToArray<JsonNode?>()),
            ["exteriorScale"] = 1,
            ["exteriorFixture"] = new JsonArray(),
            ["exteriorFurniture"] = new JsonArray(garden.Select(Entry).ToArray<JsonNode?>()),
        };
        var props = new JsonObject();
        if (!string.IsNullOrWhiteSpace(buildName)) props["build"] = buildName;
        if (plot is >= 1 and <= 60) props["plot"] = plot;
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

    private static JsonObject Entry(SavedPiece p)
    {
        var properties = new JsonObject();
        if (p.DyeHex != null) properties["color"] = p.DyeHex.ToUpperInvariant() + "FF";
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
