using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace HousingStudio.Core;

/// <summary>A file that isn't a MakePlace layout or a Housing Studio build, said plainly.</summary>
public sealed class LayoutFormatException(string message) : Exception(message);

/// <summary>
/// Reads a MakePlace layout (.json) or a Housing Studio build (.build, the same JSON), the way
/// Housing Studio's site does (src/features/housing-studio/makeplace.ts): pieces inside and in the
/// garden, with whatever is placed on them (MakePlace stores those as <c>attachments</c>, each with
/// its own full transform), their dyes as "RRGGBBAA", and the fixtures.
/// </summary>
public static class LayoutReader
{
    /// <summary>How many levels of things-on-things are read (the site reads the same).</summary>
    private const int MaxDepth = 4;

    /// <summary>A file bigger than this isn't a layout (the biggest real ones are a few MB).</summary>
    public const int MaxBytes = 32 * 1024 * 1024;
    /// <summary>The most pieces a side may have (1.0.0).</summary>
    public const int MaxPiecesASide = 5_000;

    public static Layout Parse(string text)
    {
        if (text.Length > MaxBytes) throw new LayoutFormatException("That file is too big to be a layout.");
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            throw new LayoutFormatException("That file isn't a MakePlace layout or a build: it couldn't be read as JSON.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new LayoutFormatException("That file isn't a MakePlace layout or a build.");
            var hasFurniture = root.TryGetProperty("interiorFurniture", out var inF) && inF.ValueKind == JsonValueKind.Array;
            var hasGarden = root.TryGetProperty("exteriorFurniture", out var outF) && outF.ValueKind == JsonValueKind.Array;
            if (!hasFurniture && !hasGarden && !root.TryGetProperty("houseSize", out _))
                throw new LayoutFormatException("That file has no furniture. Save one from MakePlace (File → Save) or download a build from Housing Studio.");

            var interiorScale = Scale(root, "interiorScale");
            var exteriorScale = Scale(root, "exteriorScale");

            var interior = new List<LayoutPiece>();
            if (hasFurniture) ReadPieces(inF, Side.Interior, interiorScale, interior);
            var garden = new List<LayoutPiece>();
            if (hasGarden) ReadPieces(outF, Side.Garden, exteriorScale, garden);
            // 1.0.0: far more than any home holds (a few hundred) is not a layout; matching it would stall the game.
            if (interior.Count > MaxPiecesASide || garden.Count > MaxPiecesASide)
                throw new LayoutFormatException($"That file has more than {MaxPiecesASide:N0} pieces on one side, far more than a home holds.");

            var interiorFixtures = ReadFixtures(root, "interiorFixture", outside: false, out var districtName);
            var exteriorFixtures = ReadFixtures(root, "exteriorFixture", outside: true, out _);

            string? buildName = null;
            int? plot = null;
            int? light = null;
            if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                if (props.TryGetProperty("build", out var b) && b.ValueKind == JsonValueKind.String) buildName = b.GetString();
                if (props.TryGetProperty("plot", out var p) && TryNumber(p, out var pn) && pn is >= 1 and <= 60 && pn == Math.Floor(pn))
                    plot = (int)pn;
                if (props.TryGetProperty("light", out var l) && TryNumber(l, out var ln)) light = Lighting.FromFile(ln);
            }

            // A garden far from the plot's origin is in the ward's own coordinates (the site's
            // plotScene tells them apart the same way: its centre more than 40 yalms out).
            var inWard = false;
            if (garden.Count > 0)
            {
                var cx = garden.Average(g => g.Position.X);
                var cz = garden.Average(g => g.Position.Z);
                inWard = Math.Sqrt(cx * cx + cz * cz) > 40;
            }

            return new Layout
            {
                HouseSize = SizeOf(root),
                DistrictName = districtName,
                District = Districts.FromName(districtName),
                BuildName = string.IsNullOrWhiteSpace(buildName) ? null : buildName.Trim(),
                Plot = plot,
                Light = light,
                InteriorScale = interiorScale,
                ExteriorScale = exteriorScale,
                Interior = interior,
                Garden = garden,
                InteriorFixtures = interiorFixtures,
                ExteriorFixtures = exteriorFixtures,
                GardenInWardCoordinates = inWard,
            };
        }
    }

    private static float Scale(JsonElement root, string key)
    {
        // MakePlace writes 1; anything outside a thousandth to a thousand times is a broken file.
        if (root.TryGetProperty(key, out var v) && TryNumber(v, out var n) && n > 0 && double.IsFinite(n)) return (float)Math.Clamp(n, 0.001, 1000);
        return 1;
    }

    private static string? SizeOf(JsonElement root)
    {
        if (!root.TryGetProperty("houseSize", out var v) || v.ValueKind != JsonValueKind.String) return null;
        var s = (v.GetString() ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Contains("apartment") || s.Contains("room") || s.Contains("chamber")) return "Apartment";
        return s switch
        {
            "small" => "Small",
            "medium" => "Medium",
            "large" => "Large",
            _ => null,
        };
    }

    private static void ReadPieces(JsonElement list, Side side, float scale, List<LayoutPiece> into)
    {
        foreach (var raw in list.EnumerateArray()) Add(raw, side, scale, null, 0, into);
    }

    private static void Add(JsonElement raw, Side side, float scale, int? parent, int depth, List<LayoutPiece> into)
    {
        if (raw.ValueKind != JsonValueKind.Object) return;
        int? self = null;
        if (raw.TryGetProperty("itemId", out var idEl) && TryNumber(idEl, out var id) && id >= 1 && id <= uint.MaxValue && id == Math.Floor(id)
            && raw.TryGetProperty("transform", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            var (pos, yaw) = Transforms.FromFile(Numbers(t, "location"), Numbers(t, "rotation"), scale);
            // A number too big for a float (1e39 reads as Infinity) or far past any home isn't a place
            // to put a piece: the game would be handed it as it is.
            if (!Usable(pos) || !float.IsFinite(yaw)) goto attachments;
            string? dye = null;
            uint? wall = null;
            if (raw.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                if (props.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String) dye = Dyes.HexOf(c.GetString());
                if (props.TryGetProperty("material", out var m) && m.ValueKind == JsonValueKind.Object
                    && m.TryGetProperty("itemId", out var mid) && TryNumber(mid, out var w) && w >= 1 && w <= uint.MaxValue && w == Math.Floor(w))
                    wall = (uint)w;
            }
            var name = raw.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? (n.GetString() ?? string.Empty).Trim() : string.Empty;
            self = into.Count;
            into.Add(new LayoutPiece(self.Value, (uint)id, name, side, pos, yaw, dye, wall, parent));
        }
        attachments:
        if (depth < MaxDepth && raw.TryGetProperty("attachments", out var att) && att.ValueKind == JsonValueKind.Array)
            foreach (var a in att.EnumerateArray()) Add(a, side, scale, self ?? parent, depth + 1, into);
    }

    /// <summary>The farthest a piece can be from a home's origin, in yalms (a ward is about 300 across).</summary>
    public const float MaxReach = 2000;

    private static bool Usable(System.Numerics.Vector3 p) =>
        float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && Math.Abs(p.X) <= MaxReach && Math.Abs(p.Y) <= MaxReach && Math.Abs(p.Z) <= MaxReach;

    private static List<double>? Numbers(JsonElement t, string key)
    {
        if (!t.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var list = new List<double>();
        foreach (var v in arr.EnumerateArray()) list.Add(TryNumber(v, out var d) && double.IsFinite(d) ? d : 0);
        return list;
    }

    private static bool TryNumber(JsonElement v, out double d)
    {
        d = 0;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                return v.TryGetDouble(out d);
            case JsonValueKind.String:
                // Some tools write numbers as text, a few with a decimal comma.
                var s = (v.GetString() ?? string.Empty).Trim().Replace(',', '.');
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            default:
                return false;
        }
    }

    private static List<LayoutFixture> ReadFixtures(JsonElement root, string key, bool outside, out string? district)
    {
        district = null;
        var list = new List<LayoutFixture>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        var seen = new HashSet<string>();
        foreach (var f in arr.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) continue;
            var type = Str(f, "type");
            if (type == "District")
            {
                district ??= Str(f, "name");
                continue;
            }
            if (!f.TryGetProperty("itemId", out var idEl) || !TryNumber(idEl, out var id) || id < 1 || id > uint.MaxValue || id != Math.Floor(id)) continue;
            var level = outside ? "Exterior" : (Str(f, "level") is { Length: > 0 } l ? l : "Main");
            var dedupe = $"{level}:{type}:{id}";
            if (!seen.Add(dedupe)) continue;
            string? dye = null;
            if (f.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String)
                dye = Dyes.HexOf(c.GetString());
            else if (f.TryGetProperty("color", out var c2) && c2.ValueKind == JsonValueKind.String)
                dye = Dyes.HexOf(c2.GetString());
            list.Add(new LayoutFixture(level, type, (uint)id, Str(f, "name"), dye));
        }
        return list;
    }

    private static string Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty).Trim() : string.Empty;
}
