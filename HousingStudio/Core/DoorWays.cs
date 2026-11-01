using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>
/// A way to a house's front door, walked once by you and recorded: vnavmesh's map of a residential
/// ward has no houses or fences in it (it's built from the zone, and the houses are placed later), so
/// its own way to a door goes straight through the gate posts and the walls (0.7.9 to 0.7.11). The
/// errands follow the recorded way instead. Kept per ward zone; a plot sits in the same spot in every
/// ward and on every world, so one recording serves the same plot anywhere.
/// </summary>
public sealed class DoorWay
{
    /// <summary>The ward's zone (TerritoryType) it was walked in.</summary>
    public uint Territory { get; set; }

    /// <summary>The spots walked through, x/y/z each, in order; the last is at the door.</summary>
    public List<float[]> Points { get; set; } = new();

    /// <summary>
    /// The plot whose house it goes into (1-60, read inside once you've gone in), or 0 when not known
    /// (0.7.12's recordings: such a way is taken as any plot's in its zone until one is followed in).
    /// </summary>
    public int Plot { get; set; }

    /// <summary>When it was recorded (shown in Settings).</summary>
    public DateTime RecordedAt { get; set; }

    public IReadOnlyList<Vector3> Spots() => Points.Where(p => p is { Length: >= 3 }).Select(p => new Vector3(p[0], p[1], p[2])).ToList();

    public Vector3? End => Points.Count > 0 && Points[^1] is { Length: >= 3 } e ? new Vector3(e[0], e[1], e[2]) : null;

    public static DoorWay From(uint territory, IEnumerable<Vector3> spots, DateTime at, int plot = 0) => new()
    {
        Territory = territory,
        Plot = plot,
        Points = spots.Select(s => new[] { s.X, s.Y, s.Z }).ToList(),
        RecordedAt = at,
    };
}

public static class DoorWays
{
    /// <summary>A spot is kept once you've gone this far from the last one kept.</summary>
    public const float Spacing = 0.75f;

    /// <summary>A recorded way's last spot is this near a door for it to be that door's way.</summary>
    public const float EndsAtDoor = 5f;

    /// <summary>The most spots one way keeps (about 300 yalms): a walk longer than that isn't a way to a door.</summary>
    public const int MostSpots = 400;

    /// <summary>A way shorter than this (yalms, end to end along it) isn't worth keeping.</summary>
    public const float Shortest = 3f;

    /// <summary>Adds <paramref name="at"/> to a recording when it's far enough from the last spot kept; false when it's full.</summary>
    public static bool Add(List<Vector3> spots, Vector3 at)
    {
        if (spots.Count >= MostSpots) return false;
        if (spots.Count == 0 || Vector3.Distance(spots[^1], at) >= Spacing) spots.Add(at);
        return true;
    }

    /// <summary>The length of a way along it.</summary>
    public static float Length(IReadOnlyList<Vector3> spots)
    {
        var sum = 0f;
        for (var i = 1; i < spots.Count; i++) sum += Vector3.Distance(spots[i - 1], spots[i]);
        return sum;
    }

    /// <summary>
    /// The way to the door at <paramref name="door"/> in <paramref name="territory"/>: the one whose end
    /// is nearest the door, within <see cref="EndsAtDoor"/>. Null when none is.
    /// </summary>
    public static DoorWay? For(IEnumerable<DoorWay> ways, uint territory, Vector3 door) => ways
        .Where(w => w.Territory == territory && w.End is { } e && Flat(e, door) <= EndsAtDoor)
        .OrderBy(w => Flat(w.End!.Value, door))
        .FirstOrDefault();

    /// <summary>
    /// Saves a way, in place of any other ending at the same door (the same zone, ends within
    /// <see cref="EndsAtDoor"/> of each other).
    /// </summary>
    public static void Keep(List<DoorWay> ways, DoorWay way)
    {
        if (way.End is not { } end) return;
        ways.RemoveAll(w => w.Territory == way.Territory && w.End is { } e && Flat(e, end) <= EndsAtDoor);
        ways.Add(way);
    }

    /// <summary>
    /// What's left to walk of <paramref name="spots"/> from <paramref name="me"/>: from the spot after the
    /// nearest one (so you don't turn back to it), and how far that nearest spot is. Empty when the way
    /// has nothing left (you're at its end).
    /// </summary>
    public static (List<Vector3> Left, float Off) From(IReadOnlyList<Vector3> spots, Vector3 me)
    {
        if (spots.Count == 0) return (new List<Vector3>(), float.MaxValue);
        var best = 0;
        var bestD = float.MaxValue;
        for (var i = 0; i < spots.Count; i++)
        {
            var d = Vector3.Distance(spots[i], me);
            // Ties go to the later spot: on a way that comes back near itself, the further along.
            if (d <= bestD)
            {
                bestD = d;
                best = i;
            }
        }
        // Off the way by more than a step: walk to the nearest spot itself first.
        var from = bestD > 2f ? best : best + 1;
        return (spots.Skip(from).ToList(), bestD);
    }

    /// <summary>
    /// A way is recorded for a home: one in its ward's zone going into its plot (or, not yet known which
    /// plot, any in that zone). A home with no front door to walk to (an apartment) needs none.
    /// </summary>
    public static bool HasFor(IEnumerable<DoorWay> ways, uint wardTerritory, int? plot) =>
        plot is not { } p || ways.Any(w => w.Territory == wardTerritory && (w.Plot == p || w.Plot == 0));

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
}
