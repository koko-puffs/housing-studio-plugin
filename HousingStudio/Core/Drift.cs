using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>What changed in a home since the snapshot kept after a run.</summary>
/// <param name="Moved">Pieces at another spot now: where they were, and the piece as it is now.</param>
/// <param name="Added">Pieces placed since that weren't there.</param>
/// <param name="Gone">Pieces that were there and aren't now (put away, or taken out).</param>
/// <param name="Redyed">Pieces where they were, in another dye now.</param>
/// <param name="Same">Pieces just as they were.</param>
public sealed record DriftReport(
    IReadOnlyList<(Target Was, PlacedPiece Now)> Moved,
    IReadOnlyList<PlacedPiece> Added,
    IReadOnlyList<Target> Gone,
    IReadOnlyList<(Target Was, PlacedPiece Now)> Redyed,
    int Same)
{
    public bool Unchanged => Moved.Count + Added.Count + Gone.Count + Redyed.Count == 0;

    /// <summary>Something a run can put back (moved, gone or dyed; what was added is only listed).</summary>
    public bool CanPutBack => Moved.Count + Gone.Count + Redyed.Count > 0;
}

/// <summary>
/// Compares what was placed after a run (its snapshot) with what's placed now, pairing each piece of
/// the snapshot with a placed one of the same item as the matching does (the same dye, the nearest
/// first). A pair a little apart (the files round to a thousandth) counts as unmoved. Pure, so it's
/// tested.
/// </summary>
public static class Drift
{
    /// <summary>How far a piece may be from where it was and still count as there, in yalms and radians.</summary>
    public static readonly MatchOptions Tolerance = new(0.05f, 0.03f);

    public static DriftReport Compare(IReadOnlyList<Target> was, IReadOnlyList<PlacedPiece> now)
    {
        var plan = Matcher.Build(was, now, System.Array.Empty<StockSlot>(), Tolerance);
        var moved = new List<(Target, PlacedPiece)>();
        var redyed = new List<(Target, PlacedPiece)>();
        var gone = new List<Target>();
        var same = 0;
        foreach (var e in plan.Entries)
        {
            if (e.Placed is not { } p)
            {
                gone.Add(e.Target);
                continue;
            }
            if (e.State == PieceState.Move) moved.Add((e.Target, p));
            else if (p.Stain != e.Target.Stain) redyed.Add((e.Target, p));
            else same++;
        }
        return new DriftReport(moved, plan.Extras.ToList(), gone, redyed, same);
    }
}
