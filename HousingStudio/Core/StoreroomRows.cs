using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HousingStudio.Core;

/// <summary>What one row of the furnishing list's storeroom tab holds: its storeroom slot and item.</summary>
public readonly record struct StoreroomRow(int Container, int Slot, uint ItemId, byte Stain);

/// <summary>The rows learnt in a house, saved between runs (Configuration), with what the storeroom held then.</summary>
[Serializable]
public sealed class SavedRows
{
    public string Fingerprint { get; set; } = string.Empty;
    public int Count { get; set; }

    /// <summary>Each row as [row, container, slot, item, stain].</summary>
    public List<long[]> Rows { get; set; } = new();
}

/// <summary>
/// The furnishing list's storeroom tab, row by row. The list isn't in the storeroom's slot order but
/// it is grouped by item (a user's run: rows 0–2 Simple Hingan Pillar, 3–4 Planter Partition, 5–6
/// Verdant Partition…), so:
/// <list type="bullet">
/// <item>rows are found by name first, when the list's own row names can be read
/// (<see cref="ByName"/>);</item>
/// <item>otherwise rows are picked from the top, and once a row turns out to start an item's group,
/// the rest of the group (the storeroom says how many of it there are) is inferred and skipped, so a
/// scan picks about one row per different item;</item>
/// <item>every row picked is kept (<see cref="Known"/>), for the rest of the run and, saved per house,
/// for later runs while the storeroom holds the same pieces (<see cref="Fingerprint"/>);</item>
/// <item>a piece placed from a row removes it, and the rows below move up one.</item>
/// </list>
/// Picking a row always shows which slot the game really picked, so a wrong guess costs one pick.
/// Pure, so it's tested without the game.
/// </summary>
public sealed class StoreroomRows
{
    private readonly Dictionary<int, StoreroomRow> rows = new();
    private readonly Dictionary<int, uint> inferred = new();
    private readonly Dictionary<int, string> names = new();

    /// <summary>How many rows the list has (the storeroom's pieces), when last told.</summary>
    public int Count { get; private set; }

    /// <summary>Rows picked, and what the game said they hold.</summary>
    public IReadOnlyDictionary<int, StoreroomRow> Known => rows;

    /// <summary>Rows not picked yet but taken to hold an item, from its group's size.</summary>
    public IReadOnlyDictionary<int, uint> Inferred => inferred;

    /// <summary>Rows whose name was read from the list itself (without picking them).</summary>
    public IReadOnlyDictionary<int, string> Names => names;

    /// <summary>Starts over (a new house, or the list was found to have changed).</summary>
    public void Reset(int count)
    {
        rows.Clear();
        inferred.Clear();
        names.Clear();
        Count = Math.Max(0, count);
    }

    /// <summary>
    /// The game said picking <paramref name="row"/> picks this slot and item. With
    /// <paramref name="groupSize"/> (how many of that item the storeroom holds), a row that starts a
    /// group (the row above holds something else, or it's the first) lets the rest of the group be inferred.
    /// </summary>
    public void Learn(int row, StoreroomRow what, int groupSize = 0)
    {
        if (row < 0) return;
        // A slot is in one row only: if it was thought to be elsewhere, that was out of date.
        foreach (var stale in rows.Where(kv => kv.Key != row && kv.Value.Container == what.Container && kv.Value.Slot == what.Slot).Select(kv => kv.Key).ToList())
            rows.Remove(stale);
        if (inferred.TryGetValue(row, out var guessed) && guessed != what.ItemId)
            inferred.Clear(); // a group wasn't the size it seemed: stop trusting the guesses
        inferred.Remove(row);
        rows[row] = what;
        if (row >= Count) Count = row + 1;

        var startsGroup = row == 0 || ItemAt(row - 1) is { } above && above != what.ItemId;
        if (startsGroup && groupSize > 1)
            for (var r = row + 1; r < row + groupSize && r < Count; r++)
                if (!rows.ContainsKey(r)) inferred[r] = what.ItemId;
    }

    private uint? ItemAt(int row) =>
        rows.TryGetValue(row, out var k) ? k.ItemId : inferred.TryGetValue(row, out var i) ? i : null;

    /// <summary>
    /// A known row holding this item (one in the wanted dye first), else an inferred one. Null when
    /// none is known yet.
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
        if (any != null) return any;
        foreach (var (row, item) in inferred.OrderBy(kv => kv.Key))
            if (item == itemId) return row;
        return null;
    }

    /// <summary>
    /// Rows the list's own names say hold this item (<paramref name="rowNames"/>: the list's rows in
    /// order), leaving out rows already known to hold something else.
    /// </summary>
    public List<int> ByName(IReadOnlyList<string> rowNames, string itemName, uint itemId)
    {
        var list = new List<int>();
        for (var r = 0; r < rowNames.Count; r++)
        {
            if (!string.Equals(Menus.Clean(rowNames[r]), Menus.Clean(itemName), StringComparison.OrdinalIgnoreCase)) continue;
            if (rows.TryGetValue(r, out var known) && known.ItemId != itemId) continue;
            list.Add(r);
        }
        return list;
    }

    /// <summary>
    /// Takes the names read from the list, the first of them being row <paramref name="offset"/>
    /// (a list may hold only part of its rows at a time: 0.2.2 read 200 of 445).
    /// </summary>
    public void LearnNames(int offset, IReadOnlyList<string> rowNames)
    {
        for (var i = 0; i < rowNames.Count; i++)
        {
            var name = Menus.Clean(rowNames[i]);
            if (name.Length > 0 && offset + i >= 0) names[offset + i] = name;
        }
    }

    /// <summary>
    /// The list's names read again (after a piece left it, or a pick showed the list had changed): the
    /// names are replaced, rows picked earlier whose item no longer matches the name now shown there
    /// are forgotten (the list re-sorts as well as shifting), and group guesses are dropped (the names
    /// say it better). Returns how many picked rows were forgotten.
    /// </summary>
    public int RefreshNames(int offset, IReadOnlyList<string> rowNames, Func<uint, string> itemName)
    {
        names.Clear();
        inferred.Clear();
        LearnNames(offset, rowNames);
        var stale = rows.Where(kv => names.TryGetValue(kv.Key, out var n)
                                     && !string.Equals(n, Menus.Clean(itemName(kv.Value.ItemId)), StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();
        foreach (var r in stale) rows.Remove(r);
        return stale.Count;
    }

    /// <summary>Rows whose read name is this piece's, leaving out rows known (picked) to hold something else.</summary>
    public List<int> ByName(string itemName, uint itemId)
    {
        var wanted = Menus.Clean(itemName);
        return names.Where(kv => string.Equals(kv.Value, wanted, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .Where(r => !(rows.TryGetValue(r, out var k) && k.ItemId != itemId))
            .OrderBy(r => r)
            .ToList();
    }

    /// <summary>
    /// The first row worth picking when looking for a piece by scanning: not picked, not inferred, and
    /// not read to have another name. Null when there's none left.
    /// </summary>
    public int? NextUnknownFor(string itemName, ICollection<int>? skip = null)
    {
        var wanted = Menus.Clean(itemName);
        for (var r = 0; r < Count; r++)
        {
            if (rows.ContainsKey(r) || inferred.ContainsKey(r) || (skip != null && skip.Contains(r))) continue;
            if (names.TryGetValue(r, out var n) && !string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) continue;
            return r;
        }
        return null;
    }

    /// <summary>
    /// The next row worth picking for a piece when no row is known for it: first rows not read at all,
    /// then rows only guessed to belong to another piece's group (guesses were never read, so they're
    /// tried before giving up). Only rows picked, or whose names were read, and found to hold something
    /// else are ruled out. Null only when every other row has been tried.
    /// </summary>
    public int? NextCandidate(string itemName, uint itemId, ICollection<int> tried)
    {
        var next = NextUnknownFor(itemName, tried);
        if (next != null) return next;
        var wanted = Menus.Clean(itemName);
        for (var r = 0; r < Count; r++)
        {
            if (tried.Contains(r)) continue;
            if (rows.TryGetValue(r, out var k) && k.ItemId != itemId) continue;
            if (names.TryGetValue(r, out var n) && !string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)) continue;
            return r;
        }
        return null;
    }

    /// <summary>The first row neither picked nor inferred, or null when every row is accounted for.</summary>
    public int? NextUnknown()
    {
        for (var r = 0; r < Count; r++)
            if (!rows.ContainsKey(r) && !inferred.ContainsKey(r)) return r;
        return null;
    }

    /// <summary>The piece in <paramref name="row"/> was placed: the row is gone and the ones below move up.</summary>
    public void Removed(int row)
    {
        if (row < 0) return;
        Shift(rows, row);
        Shift(inferred, row);
        Shift(names, row);
        Count = Math.Max(0, Count - 1);
    }

    /// <summary>
    /// A piece came into the storeroom (a bag piece stored to be placed) and its row is this one: the
    /// rows from here on move down one. What was learnt stays (0.3.2 forgot every row each time); a row
    /// put in the wrong place shows when it's picked, and is learnt again.
    /// </summary>
    public void Inserted(int row)
    {
        row = Math.Clamp(row, 0, Count);
        ShiftDown(rows, row);
        ShiftDown(inferred, row);
        ShiftDown(names, row);
        Count++;
    }

    private static void ShiftDown<T>(Dictionary<int, T> map, int row)
    {
        var moved = map.Where(kv => kv.Key >= row).OrderByDescending(kv => kv.Key).ToList();
        foreach (var (r, _) in moved) map.Remove(r);
        foreach (var (r, what) in moved) map[r + 1] = what;
    }

    private static void Shift<T>(Dictionary<int, T> map, int row)
    {
        var moved = map.Where(kv => kv.Key > row).OrderBy(kv => kv.Key).ToList();
        map.Remove(row);
        foreach (var (r, _) in moved) map.Remove(r);
        foreach (var (r, what) in moved) map[r - 1] = what;
    }

    /// <summary>Does a known row still pick the slot it did?</summary>
    public bool Matches(int row, StoreroomRow seen) =>
        rows.TryGetValue(row, out var known) && known.Container == seen.Container && known.Slot == seen.Slot;

    /// <summary>The rows as text, for the log ("12 → 27006/36 Oak Table").</summary>
    public IEnumerable<string> Describe(Func<uint, string> itemName) =>
        rows.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} → {kv.Value.Container}/{kv.Value.Slot} {itemName(kv.Value.ItemId)}");

    /// <summary>
    /// A short hash of what the storeroom holds (every slot, item and dye), to tell whether rows saved
    /// earlier still apply: the list only stays the same while the storeroom does.
    /// </summary>
    public static string Fingerprint(IEnumerable<StoreroomRow> occupied)
    {
        var text = string.Join(";", occupied.OrderBy(o => o.Container).ThenBy(o => o.Slot).Select(o => $"{o.Container}/{o.Slot}={o.ItemId}:{o.Stain}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    public SavedRows Export(string fingerprint) => new()
    {
        Fingerprint = fingerprint,
        Count = Count,
        Rows = rows.OrderBy(kv => kv.Key).Select(kv => new long[] { kv.Key, kv.Value.Container, kv.Value.Slot, kv.Value.ItemId, kv.Value.Stain }).ToList(),
    };

    /// <summary>Loads saved rows if they were saved for the storeroom as it is now; false (and nothing loaded) otherwise.</summary>
    public bool Import(SavedRows? saved, string fingerprint)
    {
        if (saved == null || saved.Fingerprint != fingerprint || saved.Rows.Count == 0) return false;
        Reset(saved.Count);
        foreach (var r in saved.Rows)
            if (r.Length >= 5 && r[0] >= 0 && r[0] < saved.Count)
                rows[(int)r[0]] = new StoreroomRow((int)r[1], (int)r[2], (uint)r[3], (byte)r[4]);
        return true;
    }
}
