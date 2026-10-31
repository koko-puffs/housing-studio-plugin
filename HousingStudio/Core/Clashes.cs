using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HousingStudio.Core;

public enum ClashKind
{
    /// <summary>Two or more of the same item at the same spot (the game takes the second for overlapping).</summary>
    Stacked,

    /// <summary>Placed below the home's lowest floor.</summary>
    UnderFloor,

    /// <summary>Placed above the ceiling of the home's top floor (out of the house).</summary>
    AboveCeiling,

    /// <summary>Outside the house's walls altogether.</summary>
    OutsideWalls,

    /// <summary>An indoor furnishing in the garden, or an outdoor one inside: the game places it only on its own side.</summary>
    WrongSide,
}

/// <summary>Pieces of the design the game would likely refuse, and why.</summary>
/// <param name="Pieces">The pieces it's about (for Stacked, every copy at the spot after the first).</param>
public sealed record Clash(ClashKind Kind, uint ItemId, IReadOnlyList<Target> Pieces);

/// <summary>
/// What can be told about a design before a run, without the game's own placement check and without
/// the pieces' sizes (which the plugin doesn't have): copies of an item stacked at one spot, pieces
/// whose spot is below the lowest floor or above the top floor's ceiling, outside the house, and furnishings for the
/// other side. With Place anywhere off the game refuses most of these; with it on, it takes them.
/// Pure, so it's tested.
/// </summary>
public static class Clashes
{
    /// <summary>How close two copies count as the same spot, in yalms.</summary>
    public const float SameSpot = 0.02f;

    /// <summary>How far past a floor or ceiling a spot may be before it's called under or above it (the files round to a thousandth).</summary>
    public const float Slack = 0.05f;

    /// <summary>
    /// A floor's surface and its ceiling inside, as the game's interiors have them (the site's
    /// public/housing/interiors.json, read from the game's own models): the basement at -7 with its
    /// ceiling at -0.5 (-0.6 in a mansion, -0.8 in Shirogane), the ground floor at 0 to 6.5 (6.2 in
    /// Shirogane), the upper floor at 7 to 13.5 (13.2). Null for a floor the home hasn't got.
    /// </summary>
    public static (float Floor, float Ceiling)? Room(Floor floor, string? houseSize, string? district)
    {
        var shirogane = district == "shirogane";
        return floor switch
        {
            Floor.Basement when houseSize is "Small" or "Medium" or "Large" =>
                (-7f, shirogane ? -0.8f : houseSize == "Large" ? -0.6f : -0.5f),
            Floor.Ground when houseSize is "Small" or "Medium" or "Large" or "Apartment" => (0f, shirogane ? 6.2f : 6.5f),
            Floor.Upper when houseSize is "Medium" or "Large" => (7f, shirogane ? 13.2f : 13.5f),
            _ => null,
        };
    }

    /// <summary>
    /// The box the house's model fills, across (x) and front to back (z), by size (the same in every
    /// district): a spot outside it is outside the walls. Generous: it holds the outer walls too.
    /// </summary>
    public static (Vector2 Min, Vector2 Max)? Walls(string? houseSize) => houseSize switch
    {
        "Small" => (new Vector2(-8, -8), new Vector2(12, 18)),
        "Medium" => (new Vector2(-14, -10), new Vector2(14, 10)),
        "Large" => (new Vector2(-22, -18), new Vector2(22, 17)),
        "Apartment" => (new Vector2(-7, -9), new Vector2(7, 9)),
        _ => null,
    };

    /// <summary>
    /// The clashes in a side's pieces. <paramref name="houseSize"/> and <paramref name="district"/> are
    /// the home's (inside: the heights and walls are checked only when the size is known);
    /// <paramref name="placeableHere"/> says whether an item can go on this side (null: not checked).
    /// </summary>
    public static List<Clash> Find(IReadOnlyList<Target> targets, Side side, string? houseSize, string? district, Func<uint, bool>? placeableHere = null)
    {
        var found = new List<Clash>();

        if (placeableHere != null)
            foreach (var g in targets.Where(t => !placeableHere(t.ItemId)).GroupBy(t => t.ItemId))
                found.Add(new Clash(ClashKind.WrongSide, g.Key, g.ToList()));

        // Copies of one item at one spot: by a grid of the distance, so hundreds of one partition stay quick.
        foreach (var item in targets.GroupBy(t => t.ItemId))
        {
            var list = item.OrderBy(t => t.Key).ToList();
            if (list.Count < 2) continue;
            var grid = new Dictionary<(long, long, long), List<Target>>();
            (long, long, long) Cell(Vector3 v) => ((long)MathF.Floor(v.X / SameSpot), (long)MathF.Floor(v.Y / SameSpot), (long)MathF.Floor(v.Z / SameSpot));
            var extra = new List<Target>();
            foreach (var t in list)
            {
                if (!float.IsFinite(t.Position.X) || !float.IsFinite(t.Position.Y) || !float.IsFinite(t.Position.Z)) continue;
                var (cx, cy, cz) = Cell(t.Position);
                var stacked = false;
                for (var dx = -1; dx <= 1 && !stacked; dx++)
                    for (var dy = -1; dy <= 1 && !stacked; dy++)
                        for (var dz = -1; dz <= 1 && !stacked; dz++)
                            if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var near))
                                stacked = near.Any(o => Vector3.Distance(o.Position, t.Position) <= SameSpot);
                if (stacked) extra.Add(t);
                if (!grid.TryGetValue((cx, cy, cz), out var cell)) grid[(cx, cy, cz)] = cell = new List<Target>();
                cell.Add(t);
            }
            if (extra.Count > 0) found.Add(new Clash(ClashKind.Stacked, item.Key, extra));
        }

        if (side != Side.Interior || houseSize == null) return found;
        var walls = Walls(houseSize);
        // Below the lowest floor, or above the top floor's ceiling. Between two floors (above one's
        // ceiling, under the next one's surface) isn't told: the stairs run through there, and where
        // they are isn't known.
        var floors = new[] { Floor.Basement, Floor.Ground, Floor.Upper }.Select(f => Room(f, houseSize, district)).Where(r => r != null).Select(r => r!.Value).ToList();
        if (floors.Count == 0) return found;
        var bottom = floors.Min(r => r.Floor);
        var top = floors.Max(r => r.Ceiling);
        var under = new List<Target>();
        var above = new List<Target>();
        var outside = new List<Target>();
        foreach (var t in targets)
        {
            var p = t.Position;
            if (walls is { } box && (p.X < box.Min.X || p.X > box.Max.X || p.Z < box.Min.Y || p.Z > box.Max.Y))
            {
                outside.Add(t);
                continue;
            }
            if (p.Y < bottom - Slack) under.Add(t);
            else if (p.Y > top + Slack) above.Add(t);
        }
        foreach (var (kind, list) in new[] { (ClashKind.UnderFloor, under), (ClashKind.AboveCeiling, above), (ClashKind.OutsideWalls, outside) })
            foreach (var g in list.GroupBy(t => t.ItemId))
                found.Add(new Clash(kind, g.Key, g.ToList()));
        return found;
    }

    /// <summary>How many pieces the clashes are about (a piece counts once, whatever the reasons).</summary>
    public static int PieceCount(IEnumerable<Clash> clashes) =>
        clashes.SelectMany(c => c.Pieces).Select(t => (t.Key, t.ItemId)).Distinct().Count();
}
