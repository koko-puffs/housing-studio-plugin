using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>
/// An NPC that sells an item for gil, and where to find them: everything an errand needs to walk there
/// and buy it. Read from the game's own data (GilShop, GilShopItem, ENpcBase, ENpcResident, Level, the
/// housing districts' layouts), once, in the background.
/// </summary>
/// <param name="Price">Gil for one (the item's PriceMid, which is what a gil shop asks).</param>
/// <param name="NpcId">The ENpcBase row: the NPC to talk to.</param>
/// <param name="NpcName">"Material Supplier", "Mutamix Bubblypots".</param>
/// <param name="ShopId">The GilShop row the item is in (an NPC may have several: "Purchase Furnishings (Tier 1)").</param>
/// <param name="ShopName">The shop's name as the NPC's menu lists it, or empty.</param>
/// <param name="TerritoryId">The TerritoryType the NPC stands in, 0 when not known.</param>
/// <param name="PlaceName">"Mist", "Ul'dah - Steps of Thal", or empty when not known.</param>
/// <param name="MapId">The Map the NPC's flag goes on, 0 when not known.</param>
/// <param name="MapPosition">Map coordinates as the game shows them ("X: 10.9 Y: 11.4"), or null.</param>
/// <param name="Position">Where the NPC stands in the territory (world coordinates, yalms), or null.</param>
/// <param name="District">A residential district's key ("mist", <see cref="Districts.Keys"/>) when the NPC is in one.</param>
/// <param name="NeedsUnlock">The shop only offers it after a quest or achievement.</param>
public sealed record VendorOffer(
    uint ItemId,
    uint Price,
    uint NpcId,
    string NpcName,
    uint ShopId,
    string ShopName,
    uint TerritoryId,
    string PlaceName,
    uint MapId,
    Vector2? MapPosition,
    Vector3? Position,
    string? District,
    bool NeedsUnlock = false)
{
    /// <summary>
    /// The quests the shop wants done before it offers the item (Quest rows), from GilShopItem's
    /// QuestRequired; empty when none (or not known). The errands go only where you've done them.
    /// </summary>
    public IReadOnlyList<uint> UnlockQuests { get; init; } = Array.Empty<uint>();

    /// <summary>Whether the NPC's spot is known (an errand can walk there).</summary>
    public bool Located => TerritoryId != 0 && Position != null;

    /// <summary>"Material Supplier, Mist (10.9, 11.4)", as far as it's known.</summary>
    public string Where()
    {
        var place = PlaceName.Length > 0 ? PlaceName : "somewhere not known";
        var at = MapPosition is { } m ? $" ({m.X:0.0}, {m.Y:0.0})" : string.Empty;
        return $"{NpcName}, {place}{at}";
    }
}

/// <summary>
/// An NPC that exchanges an item for a currency other than gil (MGP at the Gold Saucer, wolf marks,
/// allied seals, tomestones…): the game's SpecialShop rows. <see cref="Vendor"/> says who and where (its
/// Price is 0, its ShopId the SpecialShop row, its ShopName as the NPC's menu lists it).
/// </summary>
/// <param name="Currency">The currency's Item row (29 is MGP).</param>
/// <param name="CurrencyName">"MGP", "Wolf Mark".</param>
/// <param name="Cost">The currency one exchange costs.</param>
/// <param name="Gives">How many of the item one exchange gives (usually 1).</param>
public sealed record ExchangeOffer(VendorOffer Vendor, uint Currency, string CurrencyName, uint Cost, int Gives = 1)
{
    public uint ItemId => Vendor.ItemId;

    /// <summary>How many exchanges <paramref name="count"/> of the item takes.</summary>
    public int Exchanges(int count) => count <= 0 ? 0 : (count + Math.Max(1, Gives) - 1) / Math.Max(1, Gives);

    /// <summary>The currency <paramref name="count"/> of the item costs.</summary>
    public long CostFor(int count) => (long)Cost * Exchanges(count);

    /// <summary>How many of the item <paramref name="balance"/> of the currency buys (whole exchanges).</summary>
    public int Affords(long balance) => Cost == 0 ? int.MaxValue : (int)Math.Min(int.MaxValue, balance / Cost * Math.Max(1, Gives));
}

/// <summary>Currencies' Item rows the plugin names.</summary>
public static class CurrencyItems
{
    /// <summary>MGP (the Gold Saucer's).</summary>
    public const uint Mgp = 29;
}

/// <summary>Choosing among the currency vendors for an item. Pure, so it's tested.</summary>
public static class ExchangeChoice
{
    /// <summary>
    /// The offers you may use (their currency allowed, <paramref name="allowed"/>), the vendor's spot
    /// known first, then the least of the currency, then the lowest NPC (steady).
    /// </summary>
    public static IEnumerable<ExchangeOffer> Ranked(IEnumerable<ExchangeOffer> offers, Func<uint, bool> allowed) =>
        offers.Where(o => allowed(o.Currency) && o.Cost > 0)
            .OrderBy(o => o.Vendor.Located ? 0 : 1)
            .ThenBy(o => o.Vendor.NeedsUnlock)
            .ThenBy(o => (double)o.Cost / Math.Max(1, o.Gives))
            .ThenBy(o => o.Vendor.NpcId)
            .ThenBy(o => o.Vendor.ShopId);
}

/// <summary>Choosing among the vendors who sell an item. Pure, so it's tested.</summary>
public static class VendorChoice
{
    /// <summary>
    /// The offer to go to: the residential districts' housing vendors first (the district you're in, or
    /// whose house this is, before the others), then any vendor whose spot is known, then the rest;
    /// within each, the cheapest, then offers that need no unlock, then the lowest NPC (steady).
    /// </summary>
    public static VendorOffer? Best(IEnumerable<VendorOffer> offers, string? preferDistrict = null) =>
        Ranked(offers, preferDistrict).FirstOrDefault();

    public static IEnumerable<VendorOffer> Ranked(IEnumerable<VendorOffer> offers, string? preferDistrict = null) =>
        offers.OrderBy(o => Rank(o, preferDistrict))
            .ThenBy(o => o.NeedsUnlock)
            .ThenBy(o => o.Price)
            .ThenBy(o => o.NpcId)
            .ThenBy(o => o.ShopId);

    private static int Rank(VendorOffer o, string? preferDistrict)
    {
        if (o.District != null && o.Located) return preferDistrict != null && o.District == preferDistrict ? 0 : 1;
        if (o.Located) return 2;
        return o.District != null ? 3 : 4;
    }
}

/// <summary>The game's map coordinates ("X: 10.9 Y: 11.4") from a spot in the world, as the map draws them.</summary>
public static class MapCoords
{
    /// <summary>One axis: <paramref name="world"/> in yalms, the Map row's offset on that axis and its size factor (100 = 1×).</summary>
    public static float FromWorld(float world, short offset, ushort sizeFactor)
    {
        var c = (sizeFactor == 0 ? 100 : sizeFactor) / 100f;
        var raw = (world + offset) * c;
        return 41f / c * ((raw + 1024f) / 2048f) + 1f;
    }

    /// <summary>The map's X and Y (the world's X and Z), rounded to a tenth as the game shows them.</summary>
    public static Vector2 FromWorld(Vector3 world, short offsetX, short offsetY, ushort sizeFactor) =>
        new(MathF.Round(FromWorld(world.X, offsetX, sizeFactor), 1), MathF.Round(FromWorld(world.Z, offsetY, sizeFactor), 1));
}

/// <summary>The residential districts' outdoor areas, from a TerritoryType's name ("s1h1" Mist … "r1h1" Empyreum).</summary>
public static class Residential
{
    /// <summary>The district's key for a ward's territory name, or null when it isn't one.</summary>
    public static string? DistrictOf(string territoryName)
    {
        var n = territoryName.ToLowerInvariant();
        if (n.Length != 4 || n[1] != '1' || n[2] != 'h' || n[3] != '1') return null;
        return n[0] switch
        {
            's' => "mist",
            'f' => "lavender-beds",
            'w' => "goblet",
            'e' => "shirogane",
            'r' => "empyreum",
            _ => null,
        };
    }

    /// <summary>
    /// What the game keeps at an ENpcBase's ENpcData: rows of many sheets, told apart by their range
    /// (the row id's high 16 bits). These are the ones a gil shop is reached through.
    /// </summary>
    public static NpcDataKind KindOf(uint dataId) => (dataId >> 16) switch
    {
        0x0004 => NpcDataKind.GilShop,
        0x000B => NpcDataKind.CustomTalk,
        0x001B => NpcDataKind.SpecialShop,
        0x0032 => NpcDataKind.TopicSelect,
        0x0036 => NpcDataKind.PreHandler,
        _ => NpcDataKind.Other,
    };
}

public enum NpcDataKind
{
    Other,
    GilShop,
    CustomTalk,
    TopicSelect,
    PreHandler,

    /// <summary>A shop that exchanges items for a currency other than gil (0x1B…).</summary>
    SpecialShop,
}
