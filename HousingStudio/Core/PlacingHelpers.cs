using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>
/// Lining up a part of a list read again (after scrolling) with the rows already known by name: the
/// furnishing list's component held 200 of its 445 rows in 0.2.2, so further rows are read in windows
/// and placed where their names overlap the known ones. Pure, so it's tested.
/// </summary>
public static class ListWindows
{
    /// <summary>
    /// The row the window's first name is, found where the window overlaps the known names by at
    /// least <paramref name="minOverlap"/> rows in a row (and only one such place); null when it can't
    /// be told (no overlap, or it fits in more than one place).
    /// </summary>
    public static int? Align(IReadOnlyDictionary<int, string> known, IReadOnlyList<string> window, int minOverlap = 8)
    {
        if (window.Count == 0 || known.Count == 0) return null;
        var max = known.Keys.Max();
        int? found = null;
        for (var offset = known.Keys.Min() - window.Count + 1; offset <= max; offset++)
        {
            var overlap = 0;
            var ok = true;
            for (var i = 0; i < window.Count; i++)
            {
                if (!known.TryGetValue(offset + i, out var name)) continue;
                if (!string.Equals(Menus.Clean(name), Menus.Clean(window[i]), StringComparison.OrdinalIgnoreCase))
                {
                    ok = false;
                    break;
                }
                overlap++;
            }
            if (!ok || overlap < Math.Min(minOverlap, window.Count)) continue;
            if (found != null && found != offset) return null; // fits in two places: can't tell
            found = offset;
        }
        return found;
    }
}

/// <summary>A placed piece: its furniture slot, item and where it stands.</summary>
public readonly record struct SeenPiece(int Key, uint ItemId, Vector3 Position);

public static class NewPieces
{
    /// <summary>
    /// The piece that was just placed: a furniture slot that wasn't there before, holding this item;
    /// of several, the one nearest to where the game's preview was. Null when there's none (the preview
    /// itself is never in the furniture list, so it can't be mistaken for one).
    /// </summary>
    public static int? Identify(ICollection<int> before, IEnumerable<SeenPiece> now, uint itemId, Vector3? previewAt)
    {
        var fresh = now.Where(p => p.ItemId == itemId && !before.Contains(p.Key)).ToList();
        if (fresh.Count == 0) return null;
        if (previewAt is { } at) return fresh.OrderBy(p => Vector3.Distance(p.Position, at)).ThenBy(p => p.Key).First().Key;
        return fresh.OrderBy(p => p.Key).First().Key;
    }
}

public static class DyeCounts
{
    /// <summary>Pieces the design has dyed (that you have: missing ones aren't counted).</summary>
    public static int Dyed(IEnumerable<PlanEntry> entries) => entries.Count(e => e.State != PieceState.Missing && e.Target.Stain != 0);

    /// <summary>
    /// Pieces that will need dyeing after placing: dyed in the design but not in that dye now (pieces
    /// from your bags are never dyed; storeroom pieces keep their dye, and the storeroom piece in the
    /// right dye is chosen first, so those already have it).
    /// </summary>
    public static int ToApply(IEnumerable<PlanEntry> entries) =>
        entries.Count(e => e.State != PieceState.Missing && e.Target.Stain != 0 && e.CurrentStain != e.Target.Stain);
}
