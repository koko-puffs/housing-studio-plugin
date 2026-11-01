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
    /// <summary>A piece that isn't placed yet, in <see cref="Needs"/> when predicting: it has no furniture slot.</summary>
    public const int NotPlacedYet = -1;

    /// <summary>Pieces the design has dyed (that you have: missing ones aren't counted).</summary>
    public static int Dyed(IEnumerable<PlanEntry> entries) => entries.Count(e => e.State != PieceState.Missing && e.Target.Stain != 0);

    /// <summary>
    /// The one calculation of what the dye phase does, used for the run's first line and for the dye
    /// phase itself (0.3.0 said "then 9 to dye" and then "Dyes: 0 pieces to dye" in a dry run: the
    /// first counted from the plan, the second only pieces placed by then). For each piece you have:
    /// <list type="bullet">
    /// <item>placed (its slot from <paramref name="keyOf"/>): the dye it has now, from
    /// <paramref name="liveStain"/> (null: no longer there, so it can't be dyed), or the plan's when
    /// <paramref name="predict"/> and there's nothing live to read;</item>
    /// <item>not placed (yet): when <paramref name="predict"/>, the dye it will have once placed (the
    /// plan's: storeroom pieces keep theirs, bag pieces are undyed), with slot <see cref="NotPlacedYet"/>;
    /// otherwise left out (it didn't get placed, so it can't be dyed).</item>
    /// </list>
    /// <see cref="DyePlanner.Plan"/> then keeps those whose dye differs from the design's.
    /// </summary>
    public static List<DyeNeed> Needs(IEnumerable<PlanEntry> entries, Func<PlanEntry, int?> keyOf, Func<int, byte?> liveStain, bool predict)
    {
        var needs = new List<DyeNeed>();
        foreach (var e in entries)
        {
            if (e.State == PieceState.Missing) continue;
            var key = keyOf(e);
            if (key is { } k)
            {
                var stain = liveStain(k) ?? (predict ? e.CurrentStain : (byte?)null);
                if (stain is { } s) needs.Add(new DyeNeed(k, e.Target, s));
            }
            else if (predict)
            {
                needs.Add(new DyeNeed(NotPlacedYet, e.Target, e.CurrentStain));
            }
        }
        return needs;
    }

    /// <summary>
    /// Pieces that will need dyeing after placing: dyed in the design but not in that dye now (pieces
    /// from your bags are never dyed; storeroom pieces keep their dye, and the storeroom piece in the
    /// right dye is chosen first, so those already have it). The same as <see cref="Needs"/> predicting.
    /// </summary>
    public static int ToApply(IEnumerable<PlanEntry> entries, bool undye = false) => ToDye(Needs(entries, _ => null, _ => null, predict: true), undye);

    /// <summary>
    /// How many of these the dye phase dyes (a dye the design wants, not the one it has; with
    /// <paramref name="undye"/>, a dye taken off a piece the design has undyed too, 0.9.12).
    /// </summary>
    public static int ToDye(IEnumerable<DyeNeed> needs, bool undye = false) => needs.Count(n => (n.Want != 0 || undye) && n.Want != n.Has);
}

/// <summary>The ways a piece from your bags is tried, in order (<see cref="BagRoute"/>).</summary>
public enum BagWay
{
    /// <summary>The item's menu → "Store": into the storeroom.</summary>
    Store,

    /// <summary>Then placed from the storeroom, the way that works (the storeroom's own retries; skipped if they fail).</summary>
    FromStoreroom,

    /// <summary>The item's menu → "Place", the editor holds it, set down with place anywhere on for that one set-down.</summary>
    Held,

    /// <summary>You put it down; the plugin sets it at its spot.</summary>
    ByHand,
}

/// <summary>
/// How a bag piece is placed (0.3.2). Placing straight from the bag got "Unable to place item." in
/// 0.3.0 while the storeroom's way works, so inside a house it's stored first and placed from the
/// storeroom; if the game won't store it (a full storeroom says so), the bag's own way with place
/// anywhere, and if that's refused too, by hand. Gardens have no storeroom tab to place from. Pure, so it's tested.
/// </summary>
public static class BagRoute
{
    public static BagWay First(bool interior) => interior ? BagWay.Store : BagWay.Held;

    /// <summary>What comes after a way worked or didn't; null when the piece is done with (placed, or skipped by the storeroom's steps).</summary>
    public static BagWay? After(BagWay way, bool worked) => way switch
    {
        BagWay.Store => worked ? BagWay.FromStoreroom : BagWay.Held,
        BagWay.Held => worked ? null : BagWay.ByHand,
        _ => null,
    };

    /// <summary>The piece has gone into the storeroom: the storeroom holds one more of it and your bags one fewer.</summary>
    public static bool Stored(int storedBefore, int storedNow, int inBagsBefore, int inBagsNow) =>
        storedNow > storedBefore && inBagsNow < inBagsBefore;
}
