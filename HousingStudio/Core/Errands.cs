using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>Where an errand stop is: at a summoning bell (a retainer), an NPC (a vendor) or a market board.</summary>
public enum StopKind
{
    Retainer,
    Vendor,
    Market,
}

/// <summary>
/// One thing to get at a stop: how many of a need (<see cref="Count"/>), the bag slots it takes, and the
/// gil it should cost (a vendor's price times the count; the market's planned listings with the board's
/// tax, which is also the most the run spends on it there).
/// </summary>
/// <param name="Bought">How many come in all: more than <see cref="Count"/> when a market listing (sold whole) holds more than needed.</param>
public sealed record ErrandItem(Need Need, int Count, int Bought, int Slots, long Gil)
{
    public uint ItemId => Need.ItemId;

    /// <summary>Market: the dearest price for one among the listings planned; a listing above it isn't bought.</summary>
    public uint MaxUnitPrice { get; init; }

    /// <summary>Market: the listings planned (Universalis's, so they may have gone by the time the board is read).</summary>
    public IReadOnlyList<MarketListing> Listings { get; init; } = Array.Empty<MarketListing>();
}

/// <summary>One stop of a trip: a retainer, a vendor or a world's market board, and what to get there.</summary>
/// <param name="Key">The place, so stops at the same one are put together ("r:123", "v:339:1025800:262144", "m:Lich").</param>
public sealed record ErrandStop(StopKind Kind, string Key, IReadOnlyList<ErrandItem> Items)
{
    /// <summary><see cref="StopKind.Retainer"/>: the retainer to take it from.</summary>
    public RetainerStock? Retainer { get; init; }

    /// <summary><see cref="StopKind.Vendor"/>: the NPC to buy from.</summary>
    public VendorOffer? Vendor { get; init; }

    /// <summary><see cref="StopKind.Market"/>: the world whose board the listings are on.</summary>
    public string? World { get; init; }

    public int Slots => Items.Sum(i => i.Slots);
    public long Gil => Items.Sum(i => i.Gil);
    public int Count => Items.Sum(i => i.Count);
}

/// <summary>One trip: its stops in order (retainers, then vendors by place and NPC, then the market by world), then home to place what was got.</summary>
public sealed record ErrandTrip(int Number, IReadOnlyList<ErrandStop> Stops)
{
    public int Slots => Stops.Sum(s => s.Slots);
    public int Count => Stops.Sum(s => s.Count);
    public long VendorGil => Stops.Where(s => s.Kind == StopKind.Vendor).Sum(s => s.Gil);
    public long MarketGil => Stops.Where(s => s.Kind == StopKind.Market).Sum(s => s.Gil);
    public long Gil => VendorGil + MarketGil;
}

/// <summary>Why something on the shopping list isn't fetched.</summary>
public enum LeftReason
{
    /// <summary>Nobody sells it and none is listed: it's crafted.</summary>
    Craft,

    /// <summary>No way to get it is known.</summary>
    Unknown,

    /// <summary>Sold only by vendors the errands can't get to (another residential district, a spot not known).</summary>
    NoVendorReachable,

    /// <summary>The market would cost more than the ceiling you set.</summary>
    OverCeiling,

    /// <summary>Listed only on other worlds (buying there is off), or not enough listed.</summary>
    NotListedHere,

    /// <summary>Your bags have no room for it (no free slot, or a listing bigger than your free slots).</summary>
    NoRoom,
}

/// <summary>Something left out of the errands, with why.</summary>
public sealed record ErrandLeft(Need Need, int Count, LeftReason Reason);

/// <summary>What the errand planner is told about you and the game.</summary>
public sealed record ErrandOptions
{
    /// <summary>Free slots in your four bags: a trip takes no more.</summary>
    public int FreeSlots { get; init; }

    /// <summary>The most the market board may cost in all (gil); 0 buys nothing there.</summary>
    public long MarketCeiling { get; init; }

    /// <summary>How many of an item make one stack (the Item sheet's StackSize).</summary>
    public Func<uint, int> StackSize { get; init; } = _ => 1;

    /// <summary>Whether the errands can get to a vendor (walk in your ward, or teleport to its zone).</summary>
    public Func<VendorOffer, bool> Reachable { get; init; } = o => o.Located;

    /// <summary>Every vendor for an item, when the plan's own can't be reached (null: only the plan's).</summary>
    public Func<uint, IReadOnlyList<VendorOffer>>? Offers { get; init; }

    /// <summary>The residential district to buy in first (your house's).</summary>
    public string? PreferDistrict { get; init; }

    /// <summary>Whether a listing's world may be bought on (null: any world of the data centre).</summary>
    public Func<string, bool>? WorldAllowed { get; init; }
}

/// <summary>The errands for a shopping plan: trips that fit your bags, and what's left out.</summary>
public sealed class ErrandPlan
{
    public static readonly ErrandPlan Empty = new(new List<ErrandTrip>(), new List<ErrandLeft>(), 0, 0);

    public ErrandPlan(List<ErrandTrip> trips, List<ErrandLeft> left, int freeSlots, long marketCeiling)
    {
        Trips = trips;
        Left = left;
        FreeSlots = freeSlots;
        MarketCeiling = marketCeiling;
    }

    public IReadOnlyList<ErrandTrip> Trips { get; }
    public IReadOnlyList<ErrandLeft> Left { get; }
    public int FreeSlots { get; }
    public long MarketCeiling { get; }

    public long VendorGil => Trips.Sum(t => t.VendorGil);

    /// <summary>The market board's part: an estimate, never above <see cref="MarketCeiling"/>.</summary>
    public long MarketGil => Trips.Sum(t => t.MarketGil);

    public long TotalGil => VendorGil + MarketGil;

    /// <summary>How many things the trips get, counting each one.</summary>
    public int Count => Trips.Sum(t => t.Count);

    public bool IsEmpty => Trips.Count == 0;

    public IEnumerable<ErrandStop> Stops(StopKind kind) => Trips.SelectMany(t => t.Stops).Where(s => s.Kind == kind);

    public bool Needs(StopKind kind) => Stops(kind).Any();
}

/// <summary>
/// Turns a shopping plan into trips: what to fetch from each retainer, vendor and world's market board,
/// as much as your free bag slots hold each time, in the order retainers → vendors (by place, then NPC)
/// → the market board (by world). Each trip is followed by going home and placing what it got. The
/// market keeps under a ceiling: listings past it are left out. Pure, so it's tested.
/// </summary>
public static class ErrandPlanner
{
    /// <summary>A part of a line, packed into trips whole (a market listing) or split by stacks (the rest).</summary>
    private sealed record Unit(StopKind Kind, string Key, int Order, Need Need, int Count, int Bought, int Stack, long Gil, bool Splits)
    {
        public RetainerStock? Retainer { get; init; }
        public VendorOffer? Vendor { get; init; }
        public string? World { get; init; }
        public MarketListing? Listing { get; init; }
        public int Slots => Stacks(Bought, Stack);
    }

    public static int Stacks(int count, int stack) => count <= 0 ? 0 : (count + Math.Max(1, stack) - 1) / Math.Max(1, stack);

    public static ErrandPlan Build(ShoppingPlan shop, ErrandOptions o)
    {
        var left = new List<ErrandLeft>();
        var units = new List<Unit>();
        long marketSoFar = 0;
        foreach (var line in shop.Lines)
        {
            if (line.Count <= 0) continue;
            var stack = Math.Max(1, o.StackSize(line.ItemId));
            switch (line.Source)
            {
                case SourceKind.Retainer when line.Retainer != null:
                    units.Add(new Unit(StopKind.Retainer, $"r:{line.Retainer.RetainerId}", 0, line.Need, line.Count, line.Count, stack, 0, true)
                        { Retainer = line.Retainer });
                    break;
                case SourceKind.Vendor when line.Vendor != null:
                    var offer = Reachable(line, o);
                    if (offer == null)
                    {
                        left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.NoVendorReachable));
                        break;
                    }
                    units.Add(new Unit(StopKind.Vendor, $"v:{offer.TerritoryId}:{offer.District}:{offer.NpcId}:{offer.ShopId}", 1, line.Need, line.Count, line.Count, stack,
                        (long)offer.Price * line.Count, true) { Vendor = offer });
                    break;
                case SourceKind.Market when line.Market != null:
                    marketSoFar = MarketUnits(line, o, stack, units, left, marketSoFar);
                    break;
                case SourceKind.Craft:
                    left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.Craft));
                    break;
                default:
                    left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.Unknown));
                    break;
            }
        }

        // Retainers by name, vendors by place and NPC, the market by world; each in the plan's order within.
        var ordered = units.Select((u, i) => (u, i))
            .OrderBy(x => x.u.Kind)
            .ThenBy(x => PlaceOrder(x.u), StringComparer.Ordinal)
            .ThenBy(x => x.i)
            .Select(x => x.u)
            .ToList();
        var trips = Pack(ordered, o.FreeSlots, left);
        return new ErrandPlan(trips, left, Math.Max(0, o.FreeSlots), Math.Max(0, o.MarketCeiling));
    }

    /// <summary>The vendor to go to: the plan's when it can be reached, else the best reachable one.</summary>
    private static VendorOffer? Reachable(ShoppingLine line, ErrandOptions o)
    {
        if (line.Vendor != null && o.Reachable(line.Vendor)) return line.Vendor;
        if (o.Offers == null) return null;
        return VendorChoice.Ranked(o.Offers(line.ItemId), o.PreferDistrict).FirstOrDefault(o.Reachable);
    }

    /// <summary>
    /// A market line's listings, on the worlds allowed, each as a unit while the ceiling allows it. The
    /// cost is worked out again from the allowed listings when some are left out (another world's).
    /// </summary>
    private static long MarketUnits(ShoppingLine line, ErrandOptions o, int stack, List<Unit> units, List<ErrandLeft> left, long soFar)
    {
        var quote = line.Market!;
        IReadOnlyList<MarketListing> chosen;
        var shortBy = 0;
        if (o.WorldAllowed == null && line.MarketCost != null)
        {
            chosen = line.MarketCost.Listings;
        }
        else
        {
            var allowed = quote.Listings.Where(l => o.WorldAllowed == null || o.WorldAllowed(l.World)).ToList();
            var cost = (quote with { Listings = allowed }).CostFor(line.Count);
            chosen = cost.Listings;
            shortBy = cost.Short;
        }
        if (shortBy > 0) left.Add(new ErrandLeft(line.Need, shortBy, LeftReason.NotListedHere));

        var wanted = line.Count - shortBy;
        var over = 0;
        foreach (var l in chosen.OrderBy(l => l.PricePerUnit).ThenBy(l => l.Quantity))
        {
            if (wanted <= 0) break;
            var count = Math.Min(wanted, l.Quantity);
            // With the board's tax (Universalis's when it says, else 5%): what buying it really costs.
            var cost = MarketRule.Cost(l.PricePerUnit, l.Quantity, l.Tax);
            if (o.MarketCeiling <= 0 || soFar + cost > o.MarketCeiling)
            {
                over += count;
                wanted -= count;
                continue;
            }
            soFar += cost;
            wanted -= count;
            units.Add(new Unit(StopKind.Market, $"m:{l.World}", 2, line.Need, count, l.Quantity, stack, cost, false) { World = l.World, Listing = l });
        }
        if (over > 0) left.Add(new ErrandLeft(line.Need, over, LeftReason.OverCeiling));
        return soFar;
    }

    private static string PlaceOrder(Unit u) => u.Kind switch
    {
        StopKind.Retainer => (u.Retainer?.Name ?? string.Empty) + "\u0001" + u.Key,
        StopKind.Vendor => $"{u.Vendor?.District ?? "~"}\u0001{u.Vendor?.TerritoryId:D6}\u0001{u.Vendor?.NpcId:D10}\u0001{u.Key}",
        _ => u.World ?? string.Empty,
    };

    /// <summary>Fills trips up to the free slots, in order; a splittable part is split by whole stacks, a listing never.</summary>
    private static List<ErrandTrip> Pack(List<Unit> units, int freeSlots, List<ErrandLeft> left)
    {
        var trips = new List<List<Unit>>();
        if (freeSlots <= 0)
        {
            foreach (var u in units) left.Add(new ErrandLeft(u.Need, u.Count, LeftReason.NoRoom));
            return new List<ErrandTrip>();
        }
        var current = new List<Unit>();
        var room = freeSlots;
        foreach (var unit in units)
        {
            var u = unit;
            while (u.Count > 0)
            {
                if (u.Slots <= room)
                {
                    current.Add(u);
                    room -= u.Slots;
                    break;
                }
                if (!u.Splits)
                {
                    if (u.Slots > freeSlots)
                    {
                        left.Add(new ErrandLeft(u.Need, u.Count, LeftReason.NoRoom));
                        break;
                    }
                }
                else if (room > 0)
                {
                    // As many whole stacks as there's room for; the rest on the next trip.
                    var fits = room * u.Stack;
                    var gilEach = u.Count > 0 ? u.Gil / u.Count : 0;
                    current.Add(u with { Count = fits, Bought = fits, Gil = gilEach * fits });
                    u = u with { Count = u.Count - fits, Bought = u.Bought - fits, Gil = u.Gil - gilEach * fits };
                    room = 0;
                }
                trips.Add(current);
                current = new List<Unit>();
                room = freeSlots;
            }
        }
        if (current.Count > 0) trips.Add(current);
        return trips.Where(t => t.Count > 0).Select((t, i) => new ErrandTrip(i + 1, Stops(t))).ToList();
    }

    /// <summary>A trip's units as stops: the same place's put together, the same item at it put together.</summary>
    private static List<ErrandStop> Stops(List<Unit> units)
    {
        var stops = new List<ErrandStop>();
        foreach (var g in units.GroupBy(u => u.Key))
        {
            var first = g.First();
            var items = g.GroupBy(u => (u.Need.ItemId, u.Need.Kind, u.Need.Stain)).Select(i =>
            {
                var need = i.First().Need;
                return new ErrandItem(need, i.Sum(u => u.Count), i.Sum(u => u.Bought), i.Sum(u => u.Slots), i.Sum(u => u.Gil))
                {
                    MaxUnitPrice = i.Where(u => u.Listing != null).Select(u => u.Listing!.PricePerUnit).DefaultIfEmpty(0u).Max(),
                    Listings = i.Where(u => u.Listing != null).Select(u => u.Listing!).ToList(),
                };
            }).ToList();
            stops.Add(new ErrandStop(first.Kind, first.Key, items) { Retainer = first.Retainer, Vendor = first.Vendor, World = first.World });
        }
        return stops;
    }
}

/// <summary>
/// What a market listing on the board may be bought for, against the plan: at or under the planned price
/// for one, and its whole cost within what's left of the item's planned gil, of the ceiling and of your
/// gil (a listing is sold whole: one bigger than needed only when that still fits). Pure, so it's tested.
/// </summary>
public static class MarketRule
{
    /// <summary>The board's tax on a purchase (5%), when a listing doesn't say its own.</summary>
    public const double Tax = 0.05;

    /// <summary>A listing's whole cost with tax: its own tax when the game says it, else 5% rounded up.</summary>
    public static long Cost(uint unitPrice, int quantity, uint totalTax) =>
        (long)unitPrice * quantity + (totalTax > 0 ? totalTax : (long)Math.Ceiling(unitPrice * (double)quantity * Tax));

    /// <summary>Why a listing isn't bought, or null when it may be.</summary>
    public static string? WhyNot(uint unitPrice, int quantity, uint totalTax, uint maxUnitPrice, int stillWanted, long itemGilLeft, long ceilingLeft, long gilOnHand)
    {
        if (quantity <= 0) return "nothing listed";
        if (stillWanted <= 0) return "enough already";
        if (unitPrice > maxUnitPrice) return $"{unitPrice:N0} gil each is above the planned {maxUnitPrice:N0}";
        var cost = Cost(unitPrice, quantity, totalTax);
        if (cost > itemGilLeft) return $"{cost:N0} gil for {quantity} is more than the {itemGilLeft:N0} gil planned for it";
        if (cost > ceilingLeft) return $"{cost:N0} gil would go past the market ceiling ({ceilingLeft:N0} gil left under it)";
        if (cost > gilOnHand) return $"{cost:N0} gil is more than you have ({gilOnHand:N0})";
        return null;
    }
}
