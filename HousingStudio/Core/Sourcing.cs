using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

public enum NeedKind
{
    /// <summary>A piece of the design you don't have.</summary>
    Piece,

    /// <summary>A dye to buy (beyond what's in your bags).</summary>
    Dye,

    /// <summary>A fixture of the design for the side you're on (a floor's walls, floor or light) not in your bags (0.7.13).</summary>
    Fixture,
}

/// <summary>Something to get: an item and how many (the design less what's placed, in the storeroom or in your bags).</summary>
/// <param name="Stain">For a dye, the Stain row it applies; 0 for pieces.</param>
public sealed record Need(uint ItemId, int Count, NeedKind Kind, byte Stain = 0);

public enum HoldingOrigin
{
    /// <summary>Read by this plugin while the retainer's inventory was open.</summary>
    Seen,

    /// <summary>Told by Allagan Tools.</summary>
    AllaganTools,
}

/// <summary>What a retainer holds (its bags, item id → count), as last seen.</summary>
/// <param name="SeenAt">When it was read (UTC), or null when not known (Allagan Tools doesn't say).</param>
public sealed record RetainerStock(ulong RetainerId, string Name, DateTime? SeenAt, IReadOnlyDictionary<uint, int> Items, HoldingOrigin Origin)
{
    public int Count(uint itemId) => Items.TryGetValue(itemId, out var n) ? n : 0;
}

public enum SourceKind
{
    /// <summary>Take it from a retainer.</summary>
    Retainer,

    /// <summary>Buy it from an NPC for gil.</summary>
    Vendor,

    /// <summary>Buy it on the market board.</summary>
    Market,

    /// <summary>Exchange a currency other than gil for it (MGP, wolf marks…) at an NPC (0.7.14).</summary>
    Exchange,

    /// <summary>Craft it (no vendor, nothing listed).</summary>
    Craft,

    /// <summary>No way to get it known here.</summary>
    Unknown,
}

/// <summary>
/// One errand: how many of a need come from one source, and the source itself (which retainer, which
/// NPC where, which listings, which recipe). An item may need several lines (2 from a retainer, the
/// other 3 from a vendor).
/// </summary>
public sealed record ShoppingLine(Need Need, SourceKind Source, int Count)
{
    public uint ItemId => Need.ItemId;

    /// <summary><see cref="SourceKind.Retainer"/>: the retainer to take it from.</summary>
    public RetainerStock? Retainer { get; init; }

    /// <summary><see cref="SourceKind.Vendor"/>: the NPC to buy from (<see cref="VendorChoice.Best"/>).</summary>
    public VendorOffer? Vendor { get; init; }

    /// <summary><see cref="SourceKind.Exchange"/>: the NPC, the currency and what it costs.</summary>
    public ExchangeOffer? Exchange { get; init; }

    /// <summary><see cref="SourceKind.Exchange"/>: the currency this line costs (in <see cref="ExchangeOffer.CurrencyName"/>).</summary>
    public long CurrencyCost { get; init; }

    /// <summary><see cref="SourceKind.Market"/>: what's listed, and what buying the count would cost.</summary>
    public MarketQuote? Market { get; init; }

    public MarketCost? MarketCost { get; init; }

    /// <summary><see cref="SourceKind.Craft"/> (and any other line for something craftable): the recipe.</summary>
    public CraftOption? Craft { get; init; }

    /// <summary>Gil for this line: a vendor's price times the count, or the market's estimate; 0 otherwise.</summary>
    public long Gil { get; init; }

    /// <summary>The gil is an estimate (the market's, from listings that may have changed).</summary>
    public bool Estimated => Source == SourceKind.Market;
}

/// <summary>Where everything still needed comes from, with the gil it costs.</summary>
public sealed class ShoppingPlan
{
    public static readonly ShoppingPlan Empty = new(new List<ShoppingLine>());

    public ShoppingPlan(List<ShoppingLine> lines) => Lines = lines;

    public IReadOnlyList<ShoppingLine> Lines { get; }

    public IEnumerable<ShoppingLine> Of(SourceKind source) => Lines.Where(l => l.Source == source);

    /// <summary>How many items (counting each one) come from a source.</summary>
    public int Count(SourceKind source) => Lines.Where(l => l.Source == source).Sum(l => l.Count);

    public long VendorGil => Lines.Where(l => l.Source == SourceKind.Vendor).Sum(l => l.Gil);

    /// <summary>The market board's part: an estimate.</summary>
    public long MarketGil => Lines.Where(l => l.Source == SourceKind.Market).Sum(l => l.Gil);

    public long TotalGil => VendorGil + MarketGil;

    /// <summary>What the currency vendors cost, by currency ("MGP" → 45,000), in the order first met.</summary>
    public IReadOnlyList<(string Currency, long Cost)> CurrencyCosts =>
        Lines.Where(l => l.Source == SourceKind.Exchange && l.Exchange != null)
            .GroupBy(l => l.Exchange!.CurrencyName)
            .Select(g => (g.Key, g.Sum(l => l.CurrencyCost)))
            .ToList();

    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Chooses where each need comes from: a retainer first (the one holding most of it, then the one seen
/// last), then a vendor (<see cref="VendorChoice.Best"/>), then the market board (whole stacks,
/// cheapest first, as many as are listed), then crafting, and what's left is unknown. Pure (no game, no
/// network): the window and an errand runner both use it, and it's tested.
/// </summary>
public static class ShoppingPlanner
{
    /// <param name="offers">The vendors selling an item (empty when none, or not known yet).</param>
    /// <param name="quotes">The market board's listings by item, for those asked about.</param>
    /// <param name="craft">The recipe for an item, or null.</param>
    /// <param name="preferDistrict">The residential district to buy in when it has a vendor (yours).</param>
    public static ShoppingPlan Build(
        IReadOnlyList<Need> needs,
        IReadOnlyList<RetainerStock> retainers,
        Func<uint, IReadOnlyList<VendorOffer>> offers,
        IReadOnlyDictionary<uint, MarketQuote> quotes,
        Func<uint, CraftOption?> craft,
        string? preferDistrict = null,
        Func<uint, IReadOnlyList<ExchangeOffer>>? exchanges = null,
        Func<uint, long>? balance = null,
        Func<uint, bool>? currencyAllowed = null)
    {
        var lines = new List<ShoppingLine>();
        // What's left of each currency, shared out as needs take it.
        var spent = new Dictionary<uint, long>();
        // What's left on each retainer, shared out as needs take it.
        var taken = new Dictionary<(ulong, uint), int>();
        foreach (var need in needs.Where(n => n.ItemId != 0 && n.Count > 0))
        {
            var left = need.Count;
            var recipe = craft(need.ItemId);

            var holders = retainers
                .Select(r => (Stock: r, Has: r.Count(need.ItemId) - taken.GetValueOrDefault((r.RetainerId, need.ItemId))))
                .Where(h => h.Has > 0)
                .OrderByDescending(h => h.Has)
                .ThenByDescending(h => h.Stock.SeenAt ?? DateTime.MinValue)
                .ThenBy(h => h.Stock.RetainerId);
            foreach (var (stock, has) in holders)
            {
                if (left <= 0) break;
                var n = Math.Min(has, left);
                taken[(stock.RetainerId, need.ItemId)] = taken.GetValueOrDefault((stock.RetainerId, need.ItemId)) + n;
                lines.Add(new ShoppingLine(need, SourceKind.Retainer, n) { Retainer = stock, Craft = recipe });
                left -= n;
            }
            if (left <= 0) continue;

            if (VendorChoice.Best(offers(need.ItemId), preferDistrict) is { } offer)
            {
                lines.Add(new ShoppingLine(need, SourceKind.Vendor, left) { Vendor = offer, Gil = (long)offer.Price * left, Craft = recipe });
                continue;
            }

            // A currency vendor (0.7.14), after the gil vendors and before the board: only a currency you
            // allow, and only as many as what you have of it buys (the rest from the board).
            if (exchanges != null && currencyAllowed != null)
                foreach (var ex in ExchangeChoice.Ranked(exchanges(need.ItemId), currencyAllowed))
                {
                    if (left <= 0) break;
                    var have = (balance?.Invoke(ex.Currency) ?? 0) - spent.GetValueOrDefault(ex.Currency);
                    var n = Math.Min(left, ex.Affords(Math.Max(0, have)));
                    if (n <= 0) continue;
                    var cost = ex.CostFor(n);
                    spent[ex.Currency] = spent.GetValueOrDefault(ex.Currency) + cost;
                    lines.Add(new ShoppingLine(need, SourceKind.Exchange, n) { Exchange = ex, CurrencyCost = cost, Craft = recipe });
                    left -= n;
                }
            if (left <= 0) continue;

            if (quotes.TryGetValue(need.ItemId, out var quote) && quote.Listings.Count > 0)
            {
                var cost = quote.CostFor(left);
                var bought = left - cost.Short;
                if (bought > 0)
                {
                    lines.Add(new ShoppingLine(need, SourceKind.Market, bought) { Market = quote, MarketCost = cost, Gil = cost.Gil, Craft = recipe });
                    left = cost.Short;
                }
            }
            if (left <= 0) continue;

            lines.Add(recipe != null
                ? new ShoppingLine(need, SourceKind.Craft, left) { Craft = recipe }
                : new ShoppingLine(need, SourceKind.Unknown, left));
        }
        return new ShoppingPlan(lines);
    }

    /// <summary>
    /// The needs of a plan: each missing piece's item, counted; and each dye to buy (from
    /// <see cref="DyePlanner.ToBuy"/>) as its dye item. A dye with no item is left out.
    /// </summary>
    public static List<Need> NeedsOf(Plan plan, IEnumerable<(byte Stain, int Count)> dyesToBuy, Func<byte, uint> dyeItem)
    {
        var needs = plan.Entries.Where(e => e.State == PieceState.Missing)
            .GroupBy(e => e.Target.ItemId)
            .OrderBy(g => g.Key)
            .Select(g => new Need(g.Key, g.Count(), NeedKind.Piece))
            .ToList();
        foreach (var (stain, count) in dyesToBuy)
        {
            var item = dyeItem(stain);
            if (item != 0 && count > 0) needs.Add(new Need(item, count, NeedKind.Dye, stain));
        }
        return needs;
    }

    /// <summary>The items worth asking the market about: those no retainer or vendor covers.</summary>
    public static List<uint> ToPrice(IReadOnlyList<Need> needs, IReadOnlyList<RetainerStock> retainers, Func<uint, IReadOnlyList<VendorOffer>> offers) =>
        needs.Where(n => n.Count > retainers.Sum(r => r.Count(n.ItemId)) && offers(n.ItemId).Count == 0)
            .Select(n => n.ItemId).Distinct().OrderBy(i => i).ToList();
}

/// <summary>Putting together what retainers hold from the plugin's own record and Allagan Tools.</summary>
public static class RetainerStocks
{
    /// <summary>
    /// One stock per retainer: what this plugin saw itself (it knows when), else what Allagan Tools says,
    /// named from the plugin's record or <paramref name="names"/> (the game's retainer list) when it can be.
    /// A retainer that holds nothing is left out.
    /// </summary>
    public static List<RetainerStock> Merge(IEnumerable<RetainerStock> seen, IEnumerable<RetainerStock> allagan, IReadOnlyDictionary<ulong, string>? names = null)
    {
        var byId = new Dictionary<ulong, RetainerStock>();
        foreach (var s in seen)
            if (!byId.TryGetValue(s.RetainerId, out var had) || (s.SeenAt ?? DateTime.MinValue) > (had.SeenAt ?? DateTime.MinValue))
                byId[s.RetainerId] = s;
        foreach (var a in allagan)
        {
            if (byId.ContainsKey(a.RetainerId)) continue;
            var name = names != null && names.TryGetValue(a.RetainerId, out var n) && n.Length > 0 ? n : a.Name;
            byId[a.RetainerId] = a with { Name = name };
        }
        return byId.Values.Where(r => r.Items.Count > 0).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RetainerId).ToList();
    }

    /// <summary>Counts by item from (item, quantity) pairs, HQ and collectable ids counted as their item.</summary>
    public static Dictionary<uint, int> Count(IEnumerable<(uint ItemId, long Quantity)> slots)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var (raw, qty) in slots)
        {
            // HQ copies are kept as 1,000,000 + the item, collectables as 500,000 + it.
            var id = raw >= 1_000_000 ? raw - 1_000_000 : raw >= 500_000 ? raw - 500_000 : raw;
            if (id == 0 || qty <= 0) continue;
            counts[id] = (int)Math.Min(int.MaxValue, counts.GetValueOrDefault(id) + qty);
        }
        return counts;
    }
}
