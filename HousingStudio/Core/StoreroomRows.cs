using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>What one row of the furnishing list's storeroom tab holds: its storeroom slot and item.</summary>
public readonly record struct StoreroomRow(int Container, int Slot, uint ItemId, byte Stain);

/// <summary>
/// The furnishing list's storeroom tab, row by row, as learnt during a run. The list isn't in the
/// storeroom's slot order (0.2.0's guess was wrong: a user's recording showed rows 26–37 holding
/// slots of three containers in no slot order), and there's no known way yet to read it without
/// picking a row, so each row is picked once, what the game says it holds is kept here, and every
/// later piece of the run looks here first. Picking a row and placing its piece removes that row,
/// so the rows below it move up one. Pure, so it's tested without the game.
/// </summary>
public sealed class StoreroomRows
{
    private readonly Dictionary<int, StoreroomRow> rows = new();

    /// <summary>How many rows the list has (the storeroom's pieces), when last told.</summary>
    public int Count { get; private set; }

    public IReadOnlyDictionary<int, StoreroomRow> Known => rows;

    /// <summary>Starts over (a new run, or the list was found to have changed).</summary>
    public void Reset(int count)
    {
        rows.Clear();
        Count = Math.Max(0, count);
    }

    /// <summary>The game said picking <paramref name="row"/> picks this slot and item.</summary>
    public void Learn(int row, StoreroomRow what)
    {
        if (row < 0) return;
        // A slot is in one row only: if it was thought to be elsewhere, that was out of date.
        foreach (var stale in rows.Where(kv => kv.Key != row && kv.Value.Container == what.Container && kv.Value.Slot == what.Slot).Select(kv => kv.Key).ToList())
            rows.Remove(stale);
        rows[row] = what;
        if (row >= Count) Count = row + 1;
    }

    /// <summary>
    /// A known row holding this item: one in the wanted dye first, then any. Null when none is known
    /// yet (pick <see cref="NextUnknown"/> rows until one turns up).
    /// </summary>
    public int? Find(uint itemId, byte stain)
    {
        int? any = null;
        foreach (var (row, what) in rows.OrderBy(kv => kv.Key))
        {
            if (what.ItemId != itemId) continue;
            if (what.Stain == stain) return row;
            any ??= row;
        }
        return any;
    }

    /// <summary>The first row not picked yet, or null when every row is known.</summary>
    public int? NextUnknown()
    {
        for (var r = 0; r < Count; r++)
            if (!rows.ContainsKey(r)) return r;
        return null;
    }

    /// <summary>The piece in <paramref name="row"/> was placed: the row is gone and the ones below move up.</summary>
    public void Removed(int row)
    {
        if (row < 0) return;
        var moved = rows.Where(kv => kv.Key > row).OrderBy(kv => kv.Key).ToList();
        rows.Remove(row);
        foreach (var (r, _) in moved) rows.Remove(r);
        foreach (var (r, what) in moved) rows[r - 1] = what;
        Count = Math.Max(0, Count - 1);
    }

    /// <summary>Picking a known row picked something else: the list changed, so forget it all.</summary>
    public bool Matches(int row, StoreroomRow seen) =>
        rows.TryGetValue(row, out var known) && known.Container == seen.Container && known.Slot == seen.Slot;

    /// <summary>The rows as text, for the log ("12 → 27006/36 Oak Table").</summary>
    public IEnumerable<string> Describe(Func<uint, string> itemName) =>
        rows.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} → {kv.Value.Container}/{kv.Value.Slot} {itemName(kv.Value.ItemId)}");
}
