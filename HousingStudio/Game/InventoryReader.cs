using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using HousingStudio.Core;

namespace HousingStudio.Game;

/// <summary>Furniture in your bags and in the house's storeroom, read straight from the game.</summary>
public static unsafe class InventoryReader
{
    public static readonly InventoryType[] Bags =
    {
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    };

    public static readonly InventoryType[] InteriorStoreroom =
    {
        InventoryType.HousingInteriorStoreroom1, InventoryType.HousingInteriorStoreroom2, InventoryType.HousingInteriorStoreroom3,
        InventoryType.HousingInteriorStoreroom4, InventoryType.HousingInteriorStoreroom5, InventoryType.HousingInteriorStoreroom6,
        InventoryType.HousingInteriorStoreroom7, InventoryType.HousingInteriorStoreroom8, InventoryType.HousingInteriorStoreroom9,
        InventoryType.HousingInteriorStoreroom10, InventoryType.HousingInteriorStoreroom11,
    };

    public static readonly InventoryType[] ExteriorStoreroom = { InventoryType.HousingExteriorStoreroom, InventoryType.HousingExteriorStoreroom2 };

    /// <summary>
    /// Every furnishing in your bags and this side's storeroom. <paramref name="storeroomLoaded"/>
    /// is false when the game hasn't sent the storeroom yet (it does once you open the furnishing
    /// window's storeroom tab in this house).
    /// </summary>
    public static List<StockSlot> Read(GameData data, Side side, out bool storeroomLoaded)
    {
        var list = new List<StockSlot>();
        Add(list, data, Bags, StockSource.Inventory, out _);
        Add(list, data, side == Side.Interior ? InteriorStoreroom : ExteriorStoreroom, StockSource.Storeroom, out _);
        storeroomLoaded = StoreroomLoaded(side);
        return list;
    }

    private static void Add(List<StockSlot> into, GameData data, InventoryType[] types, StockSource source, out bool anyLoaded)
    {
        anyLoaded = false;
        var mgr = InventoryManager.Instance();
        if (mgr == null) return;
        foreach (var t in types)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            anyLoaded = true;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item == null || item->IsEmpty()) continue;
                var id = item->GetBaseItemId();
                if (!data.IsFurnishing(id)) continue;
                into.Add(new StockSlot(source, (int)t, i, id, item->GetStain(0), (int)System.Math.Max(1, item->GetQuantity())));
            }
        }
    }

    /// <summary>Is this slot still holding this item (and dye)?</summary>
    public static bool StillHolds(StockSlot s)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return false;
        var c = mgr->GetInventoryContainer((InventoryType)s.Container);
        if (c == null || !c->IsLoaded || s.Slot < 0 || s.Slot >= c->Size) return false;
        var item = c->GetInventorySlot(s.Slot);
        return item != null && !item->IsEmpty() && item->GetBaseItemId() == s.ItemId;
    }

    /// <summary>"Bag 2, row 3, slot 4" or "Storeroom": where to find a piece.</summary>
    public static string Describe(StockSlot s)
    {
        if (s.Source == StockSource.Storeroom) return "the storeroom (Furnishings → Storeroom tab)";
        var bag = s.Container - (int)InventoryType.Inventory1 + 1;
        // Each bag is 35 slots, 5 to a row.
        return $"bag {bag}, row {s.Slot / 5 + 1}, slot {s.Slot % 5 + 1}";
    }

    /// <summary>Every occupied storeroom slot on a side, in container and slot order (the list's order).</summary>
    public static List<(int Container, int Slot, uint ItemId, byte Stain)> StoreroomOccupied(Side side)
    {
        var list = new List<(int, int, uint, byte)>();
        var mgr = InventoryManager.Instance();
        if (mgr == null) return list;
        foreach (var t in side == Side.Interior ? InteriorStoreroom : ExteriorStoreroom)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item == null || item->IsEmpty()) continue;
                list.Add(((int)t, i, item->GetBaseItemId(), item->GetStain(0)));
            }
        }
        return list;
    }

    /// <summary>
    /// How many more pieces the storeroom takes on a side: its pages' slots, less those holding a piece;
    /// null before the game has sent it. (The pages the game sends are the storeroom's size: 9 pages
    /// of 50 for one holding 449 of 450.)
    /// </summary>
    public static int? StoreroomRoom(Side side)
    {
        if (StoreroomCount(side) is not { } n) return null;
        // 0.9.1: the pages are not always the storeroom's size: a company's private chambers had 8 pages
        // (400 slots) and held 150, so a clear-out read "room for 400" and went round and round. The size
        // the game showed by refusing is kept for the home, and caps the pages.
        // (A size it holds more than is wrong: not used.)
        var slots = SizeHere(side) is { } size && size >= n.Used ? Math.Min(size, n.Slots) : n.Slots;
        return Math.Max(0, slots - n.Used);
    }

    /// <summary>The storeroom's pages' slots and the pieces in them on a side; null before the game has sent it.</summary>
    public static (int Slots, int Used)? StoreroomCount(Side side)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return null;
        int slots = 0, used = 0;
        var loaded = false;
        foreach (var t in side == Side.Interior ? InteriorStoreroom : ExteriorStoreroom)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            loaded = true;
            slots += (int)c->Size;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item != null && !item->IsEmpty()) used++;
            }
        }
        return loaded ? (slots, used) : null;
    }

    /// <summary>
    /// Storeroom sizes learnt from the game's "Insufficient space in storeroom" (the configuration's, by
    /// <see cref="SizeKey"/>), and how to save them. Set by the plugin at load.
    /// </summary>
    public static Dictionary<string, int>? LearntSizes { get; set; }

    public static Action? SaveSizes { get; set; }

    /// <summary>A company's private chambers hold 150 furnishings in the storeroom (the game's pages say 400).</summary>
    public const int ChamberSize = 150;

    /// <summary>"&lt;house id&gt;:interior": the home you're in and the side, or null when you aren't in one.</summary>
    public static string? SizeKey(Side side)
    {
        var hm = HousingManager.Instance();
        if (hm == null) return null;
        ulong id = hm->IndoorTerritory != null ? hm->IndoorTerritory->HouseId.Id : hm->OutdoorTerritory != null ? hm->OutdoorTerritory->HouseId.Id : 0;
        return id == 0 ? null : $"{id:X16}:{(side == Side.Interior ? "interior" : "garden")}";
    }

    /// <summary>The storeroom's real size where you are, when it's known (learnt here, or private chambers').</summary>
    public static int? SizeHere(Side side)
    {
        if (SizeKey(side) is { } key && LearntSizes != null && LearntSizes.TryGetValue(key, out var learnt) && learnt > 0) return learnt;
        var hm = HousingManager.Instance();
        if (side == Side.Interior && hm != null && hm->IndoorTerritory != null)
        {
            var id = hm->IndoorTerritory->HouseId;
            if (!id.IsApartment && !id.IsWorkshop && id.RoomNumber > 0) return ChamberSize;
        }
        return null;
    }

    /// <summary>
    /// The game said the storeroom is full: what's in it now is its size here, kept for this home.
    /// Returns that size (null when it couldn't be read).
    /// </summary>
    public static int? LearnFull(Side side)
    {
        if (StoreroomCount(side) is not { } n || n.Used <= 0 || SizeKey(side) is not { } key || LearntSizes == null) return null;
        if (!LearntSizes.TryGetValue(key, out var had) || had != n.Used)
        {
            LearntSizes[key] = n.Used;
            SaveSizes?.Invoke();
        }
        return n.Used;
    }

    /// <summary>How long the storeroom's pages stay as they are before it counts as sent.</summary>
    private static readonly TimeSpan PagesSteady = TimeSpan.FromMilliseconds(300);

    private static readonly int[] pagesSeen = new int[2];
    private static readonly DateTime[] pagesSeenSince = new DateTime[2];

    /// <summary>
    /// Has the game sent this side's storeroom: some of its pages, and no more coming (as many for a
    /// moment). 0.8.0 took the first page as all of it, so pieces on the later pages read as missing.
    /// Framework thread.
    /// </summary>
    public static bool StoreroomLoaded(Side side)
    {
        var pages = StoreroomPagesLoaded(side);
        var i = side == Side.Interior ? 0 : 1;
        var now = DateTime.UtcNow;
        if (pages != pagesSeen[i])
        {
            pagesSeen[i] = pages;
            pagesSeenSince[i] = now;
        }
        if (pages > 0) seenEmpty[i] = null;
        else if (seenEmpty[i] != null && seenEmpty[i] == SizeKey(side)) return true;
        return pages > 0 && now - pagesSeenSince[i] >= PagesSteady;
    }

    /// <summary>The home (<see cref="SizeKey"/>) whose storeroom was seen to be empty, per side.</summary>
    private static readonly string?[] seenEmpty = new string?[2];

    /// <summary>
    /// The furnishing window has been open a while in this home and no storeroom page came: the game
    /// sends none for an empty storeroom (0.9.9; before, a run waited for it and stopped). Counts as
    /// sent, empty, in this home until a page comes. Framework thread.
    /// </summary>
    public static void MarkStoreroomEmpty(Side side)
    {
        if (StoreroomPagesLoaded(side) == 0) seenEmpty[side == Side.Interior ? 0 : 1] = SizeKey(side);
    }

    /// <summary>How many of a side's storeroom pages (containers) the game has sent so far.</summary>
    public static int StoreroomPagesLoaded(Side side)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;
        var n = 0;
        foreach (var t in side == Side.Interior ? InteriorStoreroom : ExteriorStoreroom)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c != null && c->IsLoaded) n++;
        }
        return n;
    }

    /// <summary>Is the furnishing window showing its Storeroom tab?</summary>
    public static bool StoreroomTabShown()
    {
        var hm = HousingManager.Instance();
        return hm != null && hm->IndoorTerritory != null && hm->IndoorTerritory->ShowStoreroomTab;
    }

    /// <summary>Which storeroom slot the furnishing window has picked right now, or null.</summary>
    public static (int Container, int Slot)? StoreroomSelection()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IndoorTerritory == null) return null;
        var t = hm->IndoorTerritory;
        if (t->StoreroomItemInventorySlot < 0) return null;
        return ((int)t->StoreroomItemInventoryType, t->StoreroomItemInventorySlot);
    }
}
