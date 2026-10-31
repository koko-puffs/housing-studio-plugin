using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using HousingStudio.Automation;
using HousingStudio.Core;
using Lumina.Excel.Sheets;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace HousingStudio.Game;

/// <summary>Your home as the errands know it, read once while you're inside it.</summary>
/// <param name="WardTerritory">The ward's territory (outside), from the house's own id.</param>
/// <param name="Ward">The ward's index (0-based), as the game counts it.</param>
/// <param name="Plot">The plot (1-based), or null for an apartment.</param>
public sealed record HomeRef(uint Territory, ulong HouseId, uint WardTerritory, int Ward, int? Plot, string? District, HomeKind Kind, bool Owned);

/// <summary>
/// What the errand runner reads and does in the game beyond windows: your bags and gil, stack sizes,
/// worlds, aetherytes, the objects around you, talking to one, the retainers' bags, the board's
/// listings, and whether it's safe to carry on. Framework thread only.
/// </summary>
public static unsafe class ErrandGame
{
    public static readonly InventoryType[] RetainerPages =
    {
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
        InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
    };

    /// <summary>Limsa Lominsa Lower Decks: its aetheryte stands above Hawkers' Alley, where the market boards and summoning bells are.</summary>
    public const uint HubTerritory = 129;

    /// <summary>Free slots in your four bags.</summary>
    public static int FreeSlots()
    {
        var mgr = InventoryManager.Instance();
        return mgr == null ? 0 : (int)mgr->GetEmptySlotsInBag();
    }

    public static long Gil()
    {
        var mgr = InventoryManager.Instance();
        return mgr == null ? 0 : mgr->GetGil();
    }

    /// <summary>How many of an item your bags hold (HQ copies too).</summary>
    public static int BagCount(uint itemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;
        return mgr->GetInventoryItemCount(itemId, false, false, false, 0) + mgr->GetInventoryItemCount(itemId, true, false, false, 0);
    }

    private static readonly Dictionary<uint, int> Stacks = new();

    /// <summary>How many make a stack (the Item sheet's StackSize), 1 when not known.</summary>
    public static int StackSize(uint itemId)
    {
        if (Stacks.TryGetValue(itemId, out var n)) return n;
        n = Services.Data.GetExcelSheet<Item>().TryGetRow(itemId, out var item) ? (int)Math.Max(1, item.StackSize) : 1;
        Stacks[itemId] = n;
        return n;
    }

    private static readonly Dictionary<uint, string> Names = new();

    /// <summary>An item's name in your client's language (any item, not only furnishings).</summary>
    public static string ItemName(uint itemId)
    {
        if (Names.TryGetValue(itemId, out var n)) return n;
        n = Services.Data.GetExcelSheet<Item>().TryGetRow(itemId, out var item) ? item.Name.ExtractText() : string.Empty;
        Names[itemId] = n;
        return n;
    }

    public static string CurrentWorld() => Services.PlayerState.IsLoaded ? Services.PlayerState.CurrentWorld.ValueNullable?.Name.ExtractText() ?? string.Empty : string.Empty;

    public static string HomeWorld() => Services.PlayerState.IsLoaded ? Services.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty : string.Empty;

    private static Dictionary<uint, uint>? aetherytes;

    /// <summary>The main aetheryte standing in a territory (one you teleport to), or 0 when it has none.</summary>
    public static uint AetheryteOf(uint territory)
    {
        if (aetherytes == null)
        {
            aetherytes = new Dictionary<uint, uint>();
            foreach (var a in Services.Data.GetExcelSheet<Aetheryte>())
                if (a.IsAetheryte && a.Territory.RowId != 0 && !aetherytes.ContainsKey(a.Territory.RowId))
                    aetherytes[a.Territory.RowId] = a.RowId;
        }
        return aetherytes.TryGetValue(territory, out var id) ? id : 0;
    }

    /// <summary>Whether the aetheryte is one you've attuned to (else the game won't teleport there).</summary>
    public static bool Attuned(uint aetheryte)
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui != null && ui->IsAetheryteUnlocked(aetheryte);
    }

    public static uint Territory => Services.ClientState.TerritoryType;

    public static Vector3? Me => Services.Objects.LocalPlayer?.Position;

    public static float DistanceTo(Vector3 p) => Me is { } me ? Vector3.Distance(me, p) : float.MaxValue;

    /// <summary>Whether something in the game rules out carrying on, and what (null when nothing does).</summary>
    public static string? Blocked(bool travelling)
    {
        var c = Services.Condition;
        if (c[ConditionFlag.InCombat]) return "You're in combat.";
        if (c[ConditionFlag.BoundByDuty] || c[ConditionFlag.BoundByDuty56] || c[ConditionFlag.BoundByDuty95]) return "You're in a duty.";
        if (GameUi.IsOpen("ContentsFinderConfirm")) return "A duty popped.";
        if (c[ConditionFlag.WatchingCutscene] || c[ConditionFlag.WatchingCutscene78] || c[ConditionFlag.OccupiedInCutSceneEvent]) return "A cutscene started.";
        if (c[ConditionFlag.LoggingOut]) return "You're logging out.";
        if (c[ConditionFlag.Unconscious]) return "You were knocked out.";
        if (!travelling && (c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51])) return "You changed zones when no travel was asked for.";
        return null;
    }

    /// <summary>Between zones, teleporting, or casting (a teleport's cast).</summary>
    public static bool Moving => Services.Condition[ConditionFlag.BetweenAreas] || Services.Condition[ConditionFlag.BetweenAreas51]
                                 || Services.Condition[ConditionFlag.Casting] || Services.Condition[ConditionFlag.Jumping61]
                                 || Services.Condition[ConditionFlag.WaitingToVisitOtherWorld];

    /// <summary>Standing still somewhere: logged in, not between zones, the player there.</summary>
    public static bool Settled => Services.ClientState.IsLoggedIn && Services.Objects.LocalPlayer != null && !Moving;

    // ---------------------------------------------------------------------------------------------
    // Your home.

    /// <summary>The home you're inside (and who owns it, as far as the game says), or null when you aren't in one.</summary>
    public static HomeRef? HomeHere(HousingContext ctx)
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IndoorTerritory == null || ctx.Kind != PlaceKind.Interior) return null;
        var id = hm->GetCurrentIndoorHouseId();
        var kind = id.IsApartment ? HomeKind.Apartment : HomeKind.House;
        var owned = false;
        try
        {
            if (hm->GetOwnedHouseId(EstateType.PersonalEstate, -1).Id == id.Id) owned = true;
            else if (hm->GetOwnedHouseId(EstateType.FreeCompanyEstate, -1).Id == id.Id) (kind, owned) = (HomeKind.CompanyHouse, true);
            else if (hm->GetOwnedHouseId(EstateType.ApartmentRoom, -1).Id == id.Id) (kind, owned) = (HomeKind.Apartment, true);
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't read which home is yours");
        }
        int? plot = id.IsApartment ? null : id.PlotIndex + 1;
        return new HomeRef(ctx.TerritoryId, ctx.HouseId, hm->GetOriginalHouseTerritoryTypeId(), id.WardIndex, plot, ctx.District, kind, owned);
    }

    /// <summary>Inside that home now.</summary>
    public static bool InsideHome(HomeRef home, HousingContext ctx) => ctx.Kind == PlaceKind.Interior && ctx.HouseId == home.HouseId;

    /// <summary>Out in that home's ward (where its teleport lands you).</summary>
    public static bool InHomeWard(HomeRef home)
    {
        var hm = HousingManager.Instance();
        return hm != null && hm->OutdoorTerritory != null && Territory == home.WardTerritory && hm->GetCurrentWard() == home.Ward;
    }

    // ---------------------------------------------------------------------------------------------
    // Objects around you.

    private static readonly Dictionary<uint, string> ObjectNames = new();

    /// <summary>An event object's name in your client's language (EObjName), or empty.</summary>
    public static string EObjName(uint row)
    {
        if (ObjectNames.TryGetValue(row, out var n)) return n;
        n = Services.Data.GetExcelSheet<EObjName>().TryGetRow(row, out var r) ? r.Singular.ExtractText() : string.Empty;
        ObjectNames[row] = n;
        return n;
    }

    /// <summary>The nearest targetable object (of those kinds) whose name is one of <paramref name="names"/>, within reach of <paramref name="from"/>.</summary>
    public static IGameObject? Nearest(IReadOnlyCollection<string> names, Vector3 from, float within, params ObjectKind[] kinds)
    {
        IGameObject? best = null;
        var bestD = within;
        foreach (var o in Services.Objects)
        {
            if (o == null || !o.IsTargetable || (kinds.Length > 0 && Array.IndexOf(kinds, o.ObjectKind) < 0)) continue;
            var d = Vector3.Distance(o.Position, from);
            if (d > bestD) continue;
            var name = o.Name.TextValue;
            if (!names.Any(w => string.Equals(w, name, StringComparison.OrdinalIgnoreCase))) continue;
            best = o;
            bestD = d;
        }
        return best;
    }

    /// <summary>The NPC with that ENpcBase row, nearest you within reach.</summary>
    public static IGameObject? Npc(uint npcId, float within)
    {
        if (Me is not { } me) return null;
        IGameObject? best = null;
        var bestD = within;
        foreach (var o in Services.Objects)
        {
            if (o == null || o.ObjectKind != ObjectKind.EventNpc || o.BaseId != npcId) continue;
            var d = Vector3.Distance(o.Position, me);
            if (d <= bestD)
            {
                best = o;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary>The names of the objects near you (for the log, when the one wanted isn't found).</summary>
    public static string NamesNear(float within)
    {
        if (Me is not { } me) return "nothing";
        var names = Services.Objects.Where(o => o != null && o.IsTargetable && o.ObjectKind is ObjectKind.EventObj or ObjectKind.EventNpc or ObjectKind.HousingEventObject
                                                && Vector3.Distance(o.Position, me) <= within)
            .Select(o => $"{o.Name.TextValue} ({o.ObjectKind}, {o.BaseId}, {Vector3.Distance(o.Position, me):0} y)").Distinct().Take(12).ToList();
        return names.Count == 0 ? "nothing" : string.Join("; ", names);
    }

    /// <summary>Talks to (or uses) an object, as pressing on it does (the game's TargetSystem.InteractWithObject).</summary>
    public static bool Interact(IGameObject o)
    {
        var ts = TargetSystem.Instance();
        if (ts == null || o.Address == nint.Zero) return false;
        ts->InteractWithObject((GameObjectStruct*)o.Address, true);
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Menus and shops.

    /// <summary>The entries of an open SelectString or SelectIconString menu, or null when neither is open.</summary>
    public static (string Addon, List<string> Entries)? MenuEntries()
    {
        var s = (AddonSelectString*)GameUi.Ready(ErrandTable.SelectString);
        if (s != null) return (ErrandTable.SelectString, Read(s->PopupMenu.PopupMenu.EntryNames, s->PopupMenu.PopupMenu.EntryCount));
        var i = (AddonSelectIconString*)GameUi.Ready(ErrandTable.SelectIconString);
        if (i != null) return (ErrandTable.SelectIconString, Read(i->PopupMenu.PopupMenu.EntryNames, i->PopupMenu.PopupMenu.EntryCount));
        return null;
    }

    private static List<string> Read(InteropGenerator.Runtime.CStringPointer* names, int count)
    {
        var list = new List<string>();
        if (names == null) return list;
        for (var n = 0; n < count && n < 64; n++)
            list.Add(names[n].Value != null ? GameUi.Text(names[n].Value) : string.Empty);
        return list;
    }

    /// <summary>The first entry whose words start with one of <paramref name="words"/> (and none of <paramref name="not"/>), or -1.</summary>
    public static int Find(IReadOnlyList<string> entries, IReadOnlyList<string> words, IReadOnlyList<string>? not = null)
    {
        foreach (var w in words)
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i].Trim().TrimEnd('.');
                if (!e.StartsWith(w, StringComparison.OrdinalIgnoreCase)) continue;
                if (not != null && not.Any(x => e.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                return i;
            }
        return -1;
    }

    private static readonly Dictionary<uint, List<string>> ShopNames = new();

    /// <summary>The names of a gil shop's items, in the game's order (every item, wanted or not).</summary>
    public static List<string> ShopItemNames(uint shopId)
    {
        if (ShopNames.TryGetValue(shopId, out var list)) return list;
        list = new List<string>();
        if (Services.Data.GetSubrowExcelSheet<GilShopItem>().TryGetRow(shopId, out var rows))
            foreach (var r in rows)
                if (r.Item.RowId != 0) list.Add(ItemName(r.Item.RowId));
        ShopNames[shopId] = list;
        return list;
    }

    /// <summary>
    /// The row of the open shop window that sells an item: its name among the shop's item names as the
    /// window lists them (its text values, in order, that are the shop's items: those it doesn't show,
    /// behind a quest, aren't there). -1 when it isn't shown.
    /// </summary>
    public static int ShopRow(uint shopId, uint itemId)
    {
        var wanted = ItemName(itemId);
        if (wanted.Length == 0) return -1;
        var names = new HashSet<string>(ShopItemNames(shopId));
        var row = 0;
        foreach (var (_, text) in GameUi.TextValues(ErrandTable.Shop).OrderBy(kv => kv.Key))
        {
            if (!names.Contains(text)) continue;
            if (text == wanted) return row;
            row++;
        }
        return -1;
    }

    // ---------------------------------------------------------------------------------------------
    // Retainers.

    /// <summary>The retainer's row in the summoning bell's list (its sorted order), or -1.</summary>
    public static int RetainerRow(ulong retainerId)
    {
        var rm = RetainerManager.Instance();
        if (rm == null || !rm->IsReady) return -1;
        for (uint i = 0; i < rm->GetRetainerCount(); i++)
        {
            var r = rm->GetRetainerBySortedIndex(i);
            if (r != null && r->RetainerId == retainerId) return (int)i;
        }
        return -1;
    }

    /// <summary>The retainer whose bags are open, or 0.</summary>
    public static ulong ActiveRetainer()
    {
        var rm = RetainerManager.Instance();
        if (rm == null || !rm->IsReady) return 0;
        var r = rm->GetActiveRetainer();
        return r == null ? 0 : r->RetainerId;
    }

    /// <summary>A slot in the open retainer's bags holding the item (the biggest stack first), or null.</summary>
    public static (InventoryType Container, int Slot, int Quantity)? RetainerSlot(uint itemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return null;
        (InventoryType, int, int)? best = null;
        foreach (var t in RetainerPages)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item == null || item->IsEmpty() || item->GetBaseItemId() != itemId) continue;
                var q = (int)item->GetQuantity();
                if (best == null || q > best.Value.Item3) best = (t, i, q);
            }
        }
        return best;
    }

    /// <summary>Opens the item menu on a slot of the retainer's bags, owned by its open bag window.</summary>
    public static bool OpenRetainerItemMenu(InventoryType container, int slot)
    {
        var agent = AgentInventoryContext.Instance();
        if (agent == null) return false;
        uint owner = 0;
        foreach (var name in ErrandTable.RetainerInventory)
        {
            var a = GameUi.Ready(name);
            if (a != null)
            {
                owner = a->Id;
                break;
            }
        }
        if (owner == 0) return false;
        agent->OpenForItemSlot(container, slot, 0, owner);
        return true;
    }

    public static bool RetainerBagsOpen => ErrandTable.RetainerInventory.Any(GameUi.IsOpen);

    // ---------------------------------------------------------------------------------------------
    // The market board.

    /// <summary>A listing as the board shows it on this world (its row in the listings shown).</summary>
    public readonly record struct BoardListing(int Row, ulong ListingId, uint UnitPrice, int Quantity, uint TotalTax, bool Hq);

    /// <summary>The listings the board has loaded for an item (in the order shown), or null while it isn't that item's or they're still loading.</summary>
    public static List<BoardListing>? Listings(uint itemId)
    {
        var p = InfoProxyItemSearch.Instance();
        if (p == null || p->SearchItemId != itemId || p->WaitingForListings) return null;
        var list = new List<BoardListing>();
        var span = p->Listings;
        var count = (int)Math.Min(p->ListingCount, (uint)span.Length);
        for (var i = 0; i < count; i++)
        {
            ref var l = ref span[i];
            if (l.ItemId != itemId || l.Quantity == 0) continue;
            list.Add(new BoardListing(i, l.ListingId, l.UnitPrice, (int)l.Quantity, l.TotalTax, l.IsHqItem));
        }
        return list;
    }
}
