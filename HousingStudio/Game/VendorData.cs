using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HousingStudio.Core;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;

namespace HousingStudio.Game;

/// <summary>
/// Which NPCs sell which furnishings and dyes for gil, and where they stand: worked out once from the
/// game's data, in the background (it goes through every NPC and every gil shop), then kept. Until it's
/// ready every item has no offers; a sheet that can't be read leaves it empty (logged once).
/// </summary>
/// <remarks>
/// An NPC's shops are found from its ENpcBase's ENpcData: a GilShop row itself, or one reached through a
/// TopicSelect (a menu of shops), a PreHandler (a shop behind a check) or a CustomTalk's script arguments.
/// Its spot is its Level row; the residential districts' NPCs, which have none, are found in the wards'
/// own layout files (planevent.lgb, planlive.lgb, bg.lgb), so their housing vendors (Material Supplier,
/// Housing Merchant, Junkmonger) are always located.
/// </remarks>
public sealed class VendorData
{
    private readonly GameData data;
    private volatile Tables? tables;
    private readonly ConcurrentDictionary<uint, CraftOption?> crafts = new();
    private readonly ConcurrentDictionary<uint, bool> marketable = new();

    public VendorData(GameData data) => this.data = data;

    /// <summary>Whether the lookup is built (before that, no item has offers).</summary>
    public bool Ready => tables != null;

    /// <summary>Starts building the lookup off the framework thread.</summary>
    public void WarmUp() => System.Threading.Tasks.Task.Run(() =>
    {
        try
        {
            var started = DateTime.UtcNow;
            var t = Build();
            tables = t;
            Services.Log.Debug($"Vendors read: {t.Count} offers in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't read the game's vendors: the shopping list leaves them out");
            tables = new Tables();
        }
    });

    private static readonly IReadOnlyList<VendorOffer> None = Array.Empty<VendorOffer>();

    private static readonly IReadOnlyList<System.Numerics.Vector3> NoSpots = Array.Empty<System.Numerics.Vector3>();

    /// <summary>
    /// Everywhere an NPC of that name stands in a territory, from the game's data: a ward's main division
    /// and its subdivision each have their own Housing Merchant (an offer names only one of them).
    /// </summary>
    public IReadOnlyList<System.Numerics.Vector3> SpotsOf(string npcName, uint territory) =>
        tables is { } t && t.SpotsByName.TryGetValue((npcName, territory), out var list) ? list : NoSpots;

    private static readonly IReadOnlyList<ExchangeOffer> NoExchanges = Array.Empty<ExchangeOffer>();

    /// <summary>Every NPC exchanging an item for a currency other than gil (each place they stand), or none.</summary>
    public IReadOnlyList<ExchangeOffer> Exchanges(uint itemId) =>
        tables is { } t && t.ExchangesByItem.TryGetValue(itemId, out var list) ? list : NoExchanges;

    /// <summary>The currencies the vendors take for furnishings, dyes and fixtures (for Settings), by name; empty until read.</summary>
    public IReadOnlyList<(uint Item, string Name)> Currencies => tables?.Currencies ?? (IReadOnlyList<(uint, string)>)Array.Empty<(uint, string)>();

    /// <summary>A SpecialShop's items in its own order (every entry that gives one thing), or none.</summary>
    public IReadOnlyList<uint> ExchangeShopItems(uint shopId) =>
        tables is { } t && t.ExchangeShopItems.TryGetValue(shopId, out var list) ? list : Array.Empty<uint>();

    /// <summary>Every NPC selling an item for gil (each place they stand), or none.</summary>
    public IReadOnlyList<VendorOffer> Offers(uint itemId) =>
        tables is { } t && t.ByItem.TryGetValue(itemId, out var list) ? list : None;

    /// <summary>The recipe that makes an item, or null (read from RecipeLookup when first asked, then kept).</summary>
    public CraftOption? Craft(uint itemId) => crafts.GetOrAdd(itemId, ReadCraft);

    /// <summary>Whether the item can be sold on the market board (tradable, with a board category).</summary>
    public bool Marketable(uint itemId) => marketable.GetOrAdd(itemId, id =>
        Services.Data.GetExcelSheet<Item>().TryGetRow(id, out var item) && !item.IsUntradable && item.ItemSearchCategory.RowId != 0);

    /// <summary>The item UI category of currencies (gil, MGP, seals, tomestones…).</summary>
    private const uint CurrencyCategory = 100;

    /// <summary>A fixture (a house's or a room's walls, floor, light…): its Item's UI category is one of the fixtures'.</summary>
    private static bool IsFixtureItem(uint itemId) =>
        Services.Data.GetExcelSheet<Item>().TryGetRow(itemId, out var item) && GameData.IsFixtureCategory(item.ItemUICategory.RowId);

    private static CraftOption? ReadCraft(uint itemId)
    {
        try
        {
            if (!Services.Data.GetExcelSheet<RecipeLookup>().TryGetRow(itemId, out var look)) return null;
            var jobs = new[]
            {
                (look.CRP.RowId, "Carpenter"), (look.BSM.RowId, "Blacksmith"), (look.ARM.RowId, "Armorer"), (look.GSM.RowId, "Goldsmith"),
                (look.LTW.RowId, "Leatherworker"), (look.WVR.RowId, "Weaver"), (look.ALC.RowId, "Alchemist"), (look.CUL.RowId, "Culinarian"),
            };
            foreach (var (recipeId, job) in jobs)
            {
                if (recipeId == 0) continue;
                var level = Services.Data.GetExcelSheet<Recipe>().TryGetRow(recipeId, out var recipe) && recipe.RecipeLevelTable.ValueNullable is { } lt
                    ? lt.ClassJobLevel
                    : 0;
                return new CraftOption(itemId, recipeId, job, level);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, $"Couldn't read the recipe for item {itemId}");
        }
        return null;
    }

    private sealed class Tables
    {
        public readonly Dictionary<uint, IReadOnlyList<VendorOffer>> ByItem = new();

        /// <summary>The currency vendors (SpecialShops) for an item.</summary>
        public readonly Dictionary<uint, IReadOnlyList<ExchangeOffer>> ExchangesByItem = new();

        /// <summary>Every currency some vendor takes for a wanted item (its Item row and name), by name.</summary>
        public readonly List<(uint Item, string Name)> Currencies = new();

        /// <summary>Each SpecialShop's items as its window lists them (for finding a row by name).</summary>
        public readonly Dictionary<uint, IReadOnlyList<uint>> ExchangeShopItems = new();

        /// <summary>Every spot of a selling NPC, by its name and territory (both divisions of a ward).</summary>
        public readonly Dictionary<(string Name, uint Territory), List<System.Numerics.Vector3>> SpotsByName = new();

        public int Count => ByItem.Values.Sum(l => l.Count);
    }

    private readonly record struct Spot(uint Territory, uint Map, System.Numerics.Vector3 Position);

    private Tables Build()
    {
        // The items worth knowing about: furnishings and the dyes.
        var wanted = new HashSet<uint>();
        var items = Services.Data.GetExcelSheet<Item>();
        foreach (var dye in data.HousingDyes)
            foreach (var id in data.DyeItems(dye.StainId))
                wanted.Add(id);

        // Gil shops' items (GilShopItem's subrows, one row per shop).
        var shopItems = new Dictionary<uint, List<(uint Item, bool Unlock, uint[] Quests)>>();
        foreach (var row in Services.Data.GetSubrowExcelSheet<GilShopItem>())
            foreach (var sub in row)
            {
                var id = sub.Item.RowId;
                // Furnishings, dyes, and (0.7.13, for the errands) fixtures: the Item's UI category.
                if (id == 0 || !(data.IsFurnishing(id) || wanted.Contains(id) || IsFixtureItem(id))) continue;
                var unlock = sub.AchievementRequired.RowId != 0;
                var quests = new List<uint>();
                foreach (var q in sub.QuestRequired)
                    if (q.RowId != 0)
                    {
                        unlock = true;
                        quests.Add(q.RowId);
                    }
                if (!shopItems.TryGetValue(row.RowId, out var list)) shopItems[row.RowId] = list = new List<(uint, bool, uint[])>();
                list.Add((id, unlock, quests.ToArray()));
            }
        // Currency shops (SpecialShop: MGP, wolf marks, allied seals, tomestones…), 0.7.14: each entry
        // that gives one wanted item for one cost in a currency (an Item of the Currency UI category). A
        // cost item below 8 is an index the shop's UseCurrencyType resolves (4: a tomestone, by the
        // TomestonesItem sheet); the others (scrips' 16, gil's 8) are left out.
        var exchangeItems = new Dictionary<uint, List<(uint Item, int Gives, uint Currency, uint Cost)>>();
        var exchangeAll = new Dictionary<uint, List<uint>>();
        var exchangeNames = new Dictionary<uint, string>();
        var tomestones = new Dictionary<uint, uint>();
        try
        {
            foreach (var t in Services.Data.GetExcelSheet<TomestonesItem>())
                if (t.Tomestones.RowId != 0 && t.Item.RowId != 0) tomestones.TryAdd(t.Tomestones.RowId, t.Item.RowId);
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't read the tomestones' items");
        }
        uint CurrencyOf(uint costItem, long useCurrencyType)
        {
            if (costItem >= 8) return costItem;
            if (costItem == 0) return 0;
            return useCurrencyType == 4 && tomestones.TryGetValue(costItem, out var item) ? item : 0;
        }
        try
        {
            foreach (var sp in Services.Data.GetExcelSheet<SpecialShop>())
            {
                var listed = new List<uint>();
                foreach (var entry in sp.Item)
                {
                    if (entry.ReceiveItems.Count == 0) continue;
                    var gets = entry.ReceiveItems[0].Item.RowId;
                    if (gets == 0) continue;
                    listed.Add(gets);
                    // One thing for one cost only (not "a and b for x and y").
                    var more = false;
                    for (var r = 1; r < entry.ReceiveItems.Count; r++) more |= entry.ReceiveItems[r].Item.RowId != 0;
                    var costItem = 0u;
                    var costAmount = 0u;
                    var costCount = 0;
                    foreach (var c in entry.ItemCosts)
                    {
                        if (c.ItemCost.RowId == 0 || c.CurrencyCost <= 0) continue;
                        costCount++;
                        costItem = c.ItemCost.RowId;
                        costAmount = (uint)c.CurrencyCost;
                    }
                    if (more || costCount != 1) continue;
                    if (!(data.IsFurnishing(gets) || wanted.Contains(gets) || IsFixtureItem(gets))) continue;
                    var currency = CurrencyOf(costItem, sp.UseCurrencyType);
                    if (currency <= 1 || !items.TryGetRow(currency, out var cur) || cur.ItemUICategory.RowId != CurrencyCategory) continue;
                    var gives = Math.Max(1, (int)entry.ReceiveItems[0].ReceiveCount);
                    if (!exchangeItems.TryGetValue(sp.RowId, out var list)) exchangeItems[sp.RowId] = list = new List<(uint, int, uint, uint)>();
                    list.Add((gets, gives, currency, costAmount));
                }
                if (exchangeItems.ContainsKey(sp.RowId))
                {
                    exchangeAll[sp.RowId] = listed;
                    exchangeNames[sp.RowId] = sp.Name.ExtractText();
                }
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't read the currency vendors: the errands leave them out");
        }

        var shopNames = new Dictionary<uint, string>();
        var gilShops = Services.Data.GetExcelSheet<GilShop>();
        foreach (var id in shopItems.Keys)
            shopNames[id] = gilShops.TryGetRow(id, out var shop) ? shop.Name.ExtractText() : string.Empty;

        // Each NPC's shops (only those selling something wanted).
        var topics = Services.Data.GetExcelSheet<TopicSelect>();
        var handlers = Services.Data.GetExcelSheet<PreHandler>();
        var talks = Services.Data.GetExcelSheet<CustomTalk>();
        // Many NPCs share a menu or a handler: what each leads to is worked out once.
        var reached = new Dictionary<uint, uint[]>();
        void ShopsOf(uint dataId, HashSet<uint> into, int depth)
        {
            if (dataId == 0 || depth > 3) return;
            var kind = Residential.KindOf(dataId);
            if (kind == NpcDataKind.Other) return;
            if (kind != NpcDataKind.GilShop && kind != NpcDataKind.SpecialShop && reached.TryGetValue(dataId, out var known))
            {
                into.UnionWith(known);
                return;
            }
            var found = new HashSet<uint>();
            Walk(dataId, kind, found, depth);
            if (kind != NpcDataKind.GilShop && kind != NpcDataKind.SpecialShop) reached[dataId] = found.ToArray();
            into.UnionWith(found);
        }
        void Walk(uint dataId, NpcDataKind kind, HashSet<uint> into, int depth)
        {
            switch (kind)
            {
                case NpcDataKind.GilShop:
                    if (shopItems.ContainsKey(dataId)) into.Add(dataId);
                    break;
                case NpcDataKind.SpecialShop:
                    if (exchangeItems.ContainsKey(dataId)) into.Add(dataId);
                    break;
                case NpcDataKind.TopicSelect:
                    if (topics.TryGetRow(dataId, out var topic))
                        foreach (var s in topic.Shop)
                            ShopsOf(s.RowId, into, depth + 1);
                    break;
                case NpcDataKind.PreHandler:
                    if (handlers.TryGetRow(dataId, out var pre)) ShopsOf(pre.Target.RowId, into, depth + 1);
                    break;
                case NpcDataKind.CustomTalk:
                    if (talks.TryGetRow(dataId, out var talk))
                        foreach (var s in talk.Script)
                            if (Residential.KindOf(s.ScriptArg) is NpcDataKind.GilShop or NpcDataKind.SpecialShop) ShopsOf(s.ScriptArg, into, depth + 1);
                    break;
            }
        }
        var npcShops = new Dictionary<uint, HashSet<uint>>();
        var scratch = new HashSet<uint>();
        foreach (var npc in Services.Data.GetExcelSheet<ENpcBase>())
        {
            scratch.Clear();
            foreach (var d in npc.ENpcData)
                ShopsOf(d.RowId, scratch, 0);
            if (scratch.Count > 0) npcShops[npc.RowId] = new HashSet<uint>(scratch);
        }

        // Where they stand: their Level rows, and the residential wards' layouts.
        var spots = new Dictionary<uint, List<Spot>>();
        // Every spot, not only the first in each territory (the offers' one): a ward's subdivision has its own.
        var allSpots = new Dictionary<uint, List<Spot>>();
        void AddSpot(uint npc, Spot spot)
        {
            if (!spots.TryGetValue(npc, out var list)) spots[npc] = list = new List<Spot>();
            if (list.All(s => s.Territory != spot.Territory)) list.Add(spot);
            if (!allSpots.TryGetValue(npc, out var all)) allSpots[npc] = all = new List<Spot>();
            if (all.All(s => s.Territory != spot.Territory || System.Numerics.Vector3.Distance(s.Position, spot.Position) > 3)) all.Add(spot);
        }
        foreach (var level in Services.Data.GetExcelSheet<Level>())
        {
            var npc = level.Object.RowId;
            if (npc == 0 || !npcShops.ContainsKey(npc) || level.Territory.RowId == 0) continue;
            AddSpot(npc, new Spot(level.Territory.RowId, level.Map.RowId, new System.Numerics.Vector3(level.X, level.Y, level.Z)));
        }
        var territories = Services.Data.GetExcelSheet<TerritoryType>();
        var layoutsRead = new HashSet<string>();
        foreach (var t in territories)
        {
            if (Residential.DistrictOf(t.Name.ExtractText()) == null) continue;
            var bg = t.Bg.ExtractText();
            var cut = bg.IndexOf("/level/", StringComparison.Ordinal);
            // Several rows can share a ward's layout: each is read once.
            if (cut < 0 || !layoutsRead.Add(bg[..cut])) continue;
            foreach (var file in new[] { "planevent.lgb", "planlive.lgb", "bg.lgb" })
            {
                LgbFile? lgb = null;
                try
                {
                    lgb = Services.Data.GetFile<LgbFile>($"bg/{bg[..cut]}/level/{file}");
                }
                catch (Exception ex)
                {
                    Services.Log.Debug(ex, $"Couldn't read {file} of {t.Name.ExtractText()}");
                }
                if (lgb == null) continue;
                foreach (var layer in lgb.Layers)
                    foreach (var obj in layer.InstanceObjects)
                    {
                        if (obj.AssetType != LayerEntryType.EventNPC || obj.Object is not LayerCommon.ENPCInstanceObject e) continue;
                        var npc = e.ParentData.ParentData.BaseId;
                        if (!npcShops.ContainsKey(npc)) continue;
                        var p = obj.Transform.Translation;
                        AddSpot(npc, new Spot(t.RowId, t.Map.RowId, new System.Numerics.Vector3(p.X, p.Y, p.Z)));
                    }
            }
        }

        // The offers.
        var residents = Services.Data.GetExcelSheet<ENpcResident>();
        var maps = Services.Data.GetExcelSheet<Map>();
        var places = new Dictionary<uint, (string Place, string? District)>();
        (string Place, string? District) PlaceOf(uint territory)
        {
            if (places.TryGetValue(territory, out var p)) return p;
            p = territories.TryGetRow(territory, out var t)
                ? (t.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty, Residential.DistrictOf(t.Name.ExtractText()))
                : (string.Empty, null);
            places[territory] = p;
            return p;
        }
        var byItem = new Dictionary<uint, List<VendorOffer>>();
        var byExchange = new Dictionary<uint, List<ExchangeOffer>>();
        var currencies = new Dictionary<uint, string>();
        foreach (var (npc, shops) in npcShops)
        {
            var name = residents.TryGetRow(npc, out var res) ? NpcName(res.Singular.ExtractText()) : string.Empty;
            if (name.Length == 0) name = "Vendor";
            var at = spots.TryGetValue(npc, out var list) ? list : new List<Spot>();
            foreach (var shop in shops.Where(exchangeItems.ContainsKey))
                foreach (var (item, gives, currency, cost) in exchangeItems[shop])
                {
                    var currencyName = items.TryGetRow(currency, out var ci) ? ci.Name.ExtractText() : $"Currency {currency}";
                    if (!byExchange.TryGetValue(item, out var offers)) byExchange[item] = offers = new List<ExchangeOffer>();
                    foreach (var spot in at.Count > 0 ? at : new List<Spot> { default })
                    {
                        var located = spot.Territory != 0;
                        var (place, district) = located ? PlaceOf(spot.Territory) : (string.Empty, null);
                        System.Numerics.Vector2? coords = located && maps.TryGetRow(spot.Map, out var map)
                            ? MapCoords.FromWorld(spot.Position, map.OffsetX, map.OffsetY, map.SizeFactor)
                            : null;
                        var vendor = new VendorOffer(item, 0, npc, name, shop, exchangeNames[shop], spot.Territory, place, spot.Map, coords,
                            located ? spot.Position : null, district);
                        offers.Add(new ExchangeOffer(vendor, currency, currencyName, cost, gives));
                    }
                    if (!currencies.ContainsKey(currency)) currencies[currency] = currencyName;
                }
            foreach (var shop in shops.Where(shopItems.ContainsKey))
                foreach (var (item, unlock, quests) in shopItems[shop])
                {
                    var price = items.TryGetRow(item, out var it) ? it.PriceMid : 0;
                    if (price == 0) continue;
                    if (!byItem.TryGetValue(item, out var offers)) byItem[item] = offers = new List<VendorOffer>();
                    if (at.Count == 0)
                    {
                        offers.Add(new VendorOffer(item, price, npc, name, shop, shopNames[shop], 0, string.Empty, 0, null, null, null, unlock) { UnlockQuests = quests });
                        continue;
                    }
                    foreach (var spot in at)
                    {
                        var (place, district) = PlaceOf(spot.Territory);
                        System.Numerics.Vector2? coords = maps.TryGetRow(spot.Map, out var map)
                            ? MapCoords.FromWorld(spot.Position, map.OffsetX, map.OffsetY, map.SizeFactor)
                            : null;
                        offers.Add(new VendorOffer(item, price, npc, name, shop, shopNames[shop], spot.Territory, place, spot.Map, coords, spot.Position, district, unlock) { UnlockQuests = quests });
                    }
                }
        }

        var tables = new Tables();
        foreach (var (npc, all) in allSpots)
        {
            var name = residents.TryGetRow(npc, out var res) ? NpcName(res.Singular.ExtractText()) : string.Empty;
            if (name.Length == 0) continue;
            foreach (var spot in all)
            {
                if (!tables.SpotsByName.TryGetValue((name, spot.Territory), out var list)) tables.SpotsByName[(name, spot.Territory)] = list = new List<System.Numerics.Vector3>();
                if (list.All(p => System.Numerics.Vector3.Distance(p, spot.Position) > 3)) list.Add(spot.Position);
            }
        }
        foreach (var (item, offers) in byItem)
            tables.ByItem[item] = VendorChoice.Ranked(offers).ToList();
        foreach (var (item, offers) in byExchange)
            tables.ExchangesByItem[item] = offers;
        tables.Currencies.AddRange(currencies.Select(c => (c.Key, c.Value)).OrderBy(c => c.Item2, StringComparer.OrdinalIgnoreCase));
        foreach (var (shop, listed) in exchangeAll)
            tables.ExchangeShopItems[shop] = listed;
        Services.Log.Debug($"Currency vendors read: {tables.ExchangesByItem.Values.Sum(l => l.Count)} offers for {tables.ExchangesByItem.Count} items, "
                           + $"in {tables.Currencies.Count} currencies ({string.Join(", ", tables.Currencies.Select(c => c.Name))})");
        return tables;
    }

    /// <summary>An NPC's name as the game writes it in a sentence ("material supplier") with capitals, as on its nameplate.</summary>
    private static string NpcName(string singular) =>
        singular.Length > 0 && char.IsLower(singular[0]) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(singular) : singular;
}
