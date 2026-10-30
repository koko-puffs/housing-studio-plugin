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
        Add(list, data, side == Side.Interior ? InteriorStoreroom : ExteriorStoreroom, StockSource.Storeroom, out storeroomLoaded);
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
