using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HousingStudio.Core;

/// <summary>Which home: the side, the world, the territory, the house and (a garden) the plot, as one string.</summary>
public static class Homes
{
    /// <summary>"interior-w73-t341-0123456789ABCDEF", "garden-w73-t339-…-p12"; null when not at a home.</summary>
    public static string? Key(Side? side, uint world, uint territory, ulong house, int? plot)
    {
        if (side == null || territory == 0) return null;
        var s = string.Create(CultureInfo.InvariantCulture, $"{(side == Side.Garden ? "garden" : "interior")}-w{world}-t{territory}-{house:X16}");
        return side == Side.Garden ? s + string.Create(CultureInfo.InvariantCulture, $"-p{plot ?? 0}") : s;
    }
}

/// <summary>
/// A run's progress, kept in a small file as it goes, so a run the game closed under (a crash, a
/// disconnect) can be carried on the next time its design is loaded in its home: which design file,
/// which home, the design's pieces it was given, how many of its pieces were done and the phase.
/// Read back with care: a file cut short or edited reads as none.
/// </summary>
public sealed record RunProgress(
    string Design,
    string Title,
    string Home,
    Side Side,
    IReadOnlyList<(int Key, uint ItemId)> Pieces,
    int Done,
    int Total,
    RunPhase Phase,
    DateTime SavedUtc)
{
    public const int Version = 1;

    /// <summary>The most pieces a file is read with (a design has a few hundred; this is only against nonsense).</summary>
    public const int MaxPieces = 20_000;

    /// <summary>"piece 38 of 120": the next piece and how many in all.</summary>
    public (int Next, int Of) NextPiece => (Math.Clamp(Done + 1, 1, Math.Max(1, Total)), Math.Max(1, Total));

    /// <summary>Is this the run of this design in this home (the same file, the same home)?</summary>
    public bool Fits(string? designPath, string? homeKey, Side? side) =>
        designPath != null && homeKey != null && side == Side
        && string.Equals(Design, designPath, StringComparison.OrdinalIgnoreCase) && Home == homeKey;

    /// <summary>The plan's entries this run was given, as they're matched now (done ones are in place, so nothing is done twice).</summary>
    public List<PlanEntry> EntriesOf(IEnumerable<PlanEntry> plan)
    {
        var wanted = Pieces.ToHashSet();
        return plan.Where(e => wanted.Contains((e.Target.Key, e.Target.ItemId))).ToList();
    }

    public string ToJson()
    {
        var pieces = new JsonArray();
        foreach (var (key, item) in Pieces) pieces.Add(new JsonArray(key, item));
        var o = new JsonObject
        {
            ["version"] = Version,
            ["design"] = Design,
            ["title"] = Title,
            ["home"] = Home,
            ["side"] = Side == Side.Garden ? "garden" : "interior",
            ["done"] = Done,
            ["total"] = Total,
            ["phase"] = Phase.ToString(),
            ["saved"] = SavedUtc.ToString("o", CultureInfo.InvariantCulture),
            ["pieces"] = pieces,
        };
        return o.ToJsonString();
    }

    /// <summary>The progress in a file's text, or null when it isn't one (empty, cut short, another version, nonsense).</summary>
    public static RunProgress? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return null;
            if (Int(o["version"]) != Version) return null;
            var design = Str(o["design"]);
            var home = Str(o["home"]);
            if (string.IsNullOrWhiteSpace(design) || string.IsNullOrWhiteSpace(home)) return null;
            var side = Str(o["side"]) switch
            {
                "interior" => Side.Interior,
                "garden" => Side.Garden,
                _ => (Side?)null,
            };
            if (side == null) return null;
            if (!Enum.TryParse<RunPhase>(Str(o["phase"]), out var phase) || !Enum.IsDefined(phase)) phase = RunPhase.Move;
            var done = Int(o["done"]) ?? -1;
            var total = Int(o["total"]) ?? -1;
            if (done < 0 || total <= 0 || done > total) return null;
            if (o["pieces"] is not JsonArray list || list.Count == 0 || list.Count > MaxPieces) return null;
            var pieces = new List<(int, uint)>(list.Count);
            foreach (var node in list)
            {
                if (node is not JsonArray pair || pair.Count != 2) return null;
                if (Int(pair[0]) is not { } key || Long(pair[1]) is not { } item || item <= 0 || item > uint.MaxValue) return null;
                pieces.Add((key, (uint)item));
            }
            var saved = DateTime.TryParse(Str(o["saved"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : DateTime.MinValue;
            return new RunProgress(design, Str(o["title"]) ?? string.Empty, home, side.Value, pieces, done, total, phase, saved);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonNode? n) => Long(n) is { } l && l >= int.MinValue && l <= int.MaxValue ? (int)l : null;

    private static long? Long(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d) && Math.Floor(d) == d && Math.Abs(d) < 1e15) return (long)d;
        return null;
    }
}
