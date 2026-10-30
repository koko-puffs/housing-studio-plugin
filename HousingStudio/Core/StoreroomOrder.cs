using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>
/// Where an item sorts in the furnishing list's Storeroom tab: by its ItemUICategory's order (major,
/// then minor: Furnishing 20, Table 21, Tabletop 22, Wall-mounted 23, Rug 24), then the item's
/// SubcategorySort, then its item id. Found from the rows a user's runs learnt (0.3.1): every one of
/// the 36 rows seen, from Simple Hingan Pillar (row 0, sort 8230) to Hingan Bookshelf (row 272,
/// Table, sort 4230), is in this order; e.g. rows 3–165 are all SubcategorySort 10000 and go up by
/// item id (Planter Partition 7979 … Blank Minimalist Partition 48134).
/// </summary>
public readonly record struct ListSortKey(int Major, int Minor, int Sub, uint ItemId) : IComparable<ListSortKey>
{
    public int CompareTo(ListSortKey other)
    {
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Sub.CompareTo(other.Sub);
        if (c == 0) c = ItemId.CompareTo(other.ItemId);
        return c;
    }

    public override string ToString() => $"{Major}.{Minor}/{Sub}/{ItemId}";
}

/// <summary>A row picked to look for a piece, and the rows it can still be in.</summary>
public readonly record struct Probe(int Row, int Low, int High, bool Predicted);

/// <summary>
/// Finding a storeroom piece's row without scanning the list (0.3.1 picked rows 219–246 one by one,
/// 0.6 s each, for one bookshelf). Pure, so it's tested.
/// <list type="number">
/// <item>The row is predicted from the storeroom's own contents: the list holds one row per piece,
/// grouped by item, in <see cref="ListSortKey"/> order, so an item's group starts after every piece
/// that sorts before it (<see cref="FirstRow"/>).</item>
/// <item>Every row whose item is known (read by name, or picked) bounds where the piece can be:
/// rows holding items that sort before it are above it, the rest below (<see cref="Next"/>). A pick
/// that misses tightens the bounds; the search steps away from the prediction 1, 3, 7… rows, then
/// halves what's left: one pick when the prediction is right (it is when the key is), two or three
/// when it's a little off, and under 20 for 445 rows when it's far off.</item>
/// <item>When the bounds cross (the order isn't what the key says, for some item), it returns null
/// and the caller goes back to trying rows in turn.</item>
/// </list>
/// </summary>
public static class StoreroomOrder
{
    /// <summary>The storeroom's pieces in the list's order (one entry per piece).</summary>
    public static List<uint> Predict(IEnumerable<uint> storeroomItems, Func<uint, ListSortKey> key) =>
        storeroomItems.OrderBy(key).ToList();

    /// <summary>The row the item's group starts at: how many pieces sort before it.</summary>
    public static int FirstRow(IEnumerable<uint> storeroomItems, Func<uint, ListSortKey> key, uint itemId)
    {
        var want = key(itemId);
        return storeroomItems.Count(i => key(i).CompareTo(want) < 0);
    }

    /// <summary>
    /// The next row to pick for <paramref name="itemId"/>, after <paramref name="picks"/> picks of this
    /// search: the predicted row first (kept within what the known rows allow); after a miss, rows
    /// further from it in the direction the miss showed, 1, 3, 7, 15… rows on (a prediction a little
    /// off is corrected in a pick or two); once the rows left are few for that, their middle (halving
    /// them). <paramref name="seen"/> is what's known of the list (row → item); rows in
    /// <paramref name="tried"/> aren't picked again. Null when the known rows leave no room (the list
    /// isn't in the key's order) or nothing untried is left.
    /// </summary>
    public static Probe? Next(uint itemId, Func<uint, ListSortKey> key, int rowCount, IReadOnlyDictionary<int, uint> seen, ICollection<int> tried, int predicted, int picks = 0)
    {
        if (rowCount <= 0) return null;
        var want = key(itemId);
        int low = 0, high = rowCount - 1;
        foreach (var (row, item) in seen)
        {
            if (row < 0 || row >= rowCount) continue;
            if (item == itemId)
            {
                if (!tried.Contains(row)) return new Probe(row, row, row, false);
                continue;
            }
            var c = key(item).CompareTo(want);
            if (c < 0) low = Math.Max(low, row + 1);
            else if (c > 0) high = Math.Min(high, row - 1);
        }
        if (low > high) return null;
        bool Free(int r) => !tried.Contains(r) && !(seen.TryGetValue(r, out var i) && i != itemId);

        int guess;
        var gap = (1 << Math.Min(picks, 20)) - 1;
        if (picks == 0 || (predicted > low && predicted < high && Free(predicted))) guess = Math.Clamp(predicted, low, high);
        else if (2 * gap >= high - low) guess = low + (high - low) / 2;
        else if (predicted <= low) guess = low + gap;
        else guess = high - gap;
        if (Free(guess)) return new Probe(guess, low, high, guess == predicted);
        // Taken: the free row nearest it.
        for (var d = 1; d <= high - low; d++)
        {
            if (guess - d >= low && Free(guess - d)) return new Probe(guess - d, low, high, false);
            if (guess + d <= high && Free(guess + d)) return new Probe(guess + d, low, high, false);
        }
        return null;
    }

    /// <summary>
    /// How many rows whose item is known the prediction gets right, of how many known: a check of the
    /// key against the list as it is (logged once per run).
    /// </summary>
    public static (int Right, int Known) Check(IReadOnlyList<uint> predicted, IReadOnlyDictionary<int, uint> seen)
    {
        var right = seen.Count(kv => kv.Key >= 0 && kv.Key < predicted.Count && predicted[kv.Key] == kv.Value);
        return (right, seen.Count);
    }

    /// <summary>
    /// Of the pieces waiting to be placed (in the design's order), the one whose name is in the rows
    /// read from the list nearest its top; null when none is. Every placement takes a row out, so rows
    /// past the 200 the list lets be read move up into them as a run goes: placing what can be read
    /// first means the rest rarely has to be searched for.
    /// </summary>
    public static int? FirstReadable(IReadOnlyList<string> candidateNames, IReadOnlyDictionary<int, string> rowNames)
    {
        if (candidateNames.Count == 0 || rowNames.Count == 0) return null;
        var firstRow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (row, name) in rowNames)
        {
            var n = Menus.Clean(name);
            if (n.Length > 0 && (!firstRow.TryGetValue(n, out var r) || row < r)) firstRow[n] = row;
        }
        int? best = null;
        var bestRow = int.MaxValue;
        for (var i = 0; i < candidateNames.Count; i++)
        {
            if (!firstRow.TryGetValue(Menus.Clean(candidateNames[i]), out var row) || row >= bestRow) continue;
            best = i;
            bestRow = row;
        }
        return best;
    }
}
