using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>Where an errand stop is: at a summoning bell (a retainer), an NPC (a vendor) or a market board.</summary>
public enum StopKind
{
    Retainer,
    Vendor,

    /// <summary>An NPC that takes a currency other than gil (MGP…), 0.7.14.</summary>
    Exchange,
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

    /// <summary>Exchange: the NPC's offer (the currency and what one exchange costs).</summary>
    public ExchangeOffer? Exchange { get; init; }

    /// <summary>Exchange: the currency this costs (none of it gil).</summary>
    public long CurrencyCost { get; init; }

    /// <summary>
    /// Vendor: this item's own offer (its shop in the NPC's menu, and its price). A stop is one NPC, whose
    /// items can be in several of their shops (0.8.0 kept the first item's offer for the whole stop, so a
    /// Stage Panel was checked against an Eorzean Map's 1,000 gil).
    /// </summary>
    public VendorOffer? Vendor { get; init; }
}

/// <summary>One stop of a trip: a retainer, a vendor or a world's market board, and what to get there.</summary>
/// <param name="Key">The place, so stops at the same one are put together ("r:123", "v:339:Mist:1025800", "m:Lich").</param>
public sealed record ErrandStop(StopKind Kind, string Key, IReadOnlyList<ErrandItem> Items)
{
    /// <summary><see cref="StopKind.Retainer"/>: the retainer to take it from.</summary>
    public RetainerStock? Retainer { get; init; }

    /// <summary>
    /// <see cref="StopKind.Vendor"/> and <see cref="StopKind.Exchange"/>: the NPC to buy from (and where). A
    /// vendor's items each have their own offer (<see cref="ErrandItem.Vendor"/>: the shop and the price).
    /// </summary>
    public VendorOffer? Vendor { get; init; }

    /// <summary><see cref="StopKind.Market"/>: the world whose board the listings are on.</summary>
    public string? World { get; init; }

    public int Slots => Items.Sum(i => i.Slots);
    public long Gil => Items.Sum(i => i.Gil);
    public int Count => Items.Sum(i => i.Count);
}

/// <summary>One trip: its stops in order (vendors by place and NPC, then retainers, then the market by world), then home to place what was got.</summary>
public sealed record ErrandTrip(int Number, IReadOnlyList<ErrandStop> Stops)
{
    public int Slots => Stops.Sum(s => s.Slots);
    public int Count => Stops.Sum(s => s.Count);
    public long VendorGil => Stops.Where(s => s.Kind == StopKind.Vendor).Sum(s => s.Gil);
    public long MarketGil => Stops.Where(s => s.Kind == StopKind.Market).Sum(s => s.Gil);
    public long Gil => VendorGil + MarketGil;
}

/// <summary>
/// Where the errands are on a trip, as the journey bar shows it: at a summoning bell (retainers), at
/// vendors, at the market board, going home (and in), or placing what was got.
/// </summary>
public enum ErrandLeg
{
    Bell,
    Vendor,
    Market,
    Home,
    Place,
}

/// <summary>Why something on the shopping list isn't fetched.</summary>
public enum LeftReason
{
    /// <summary>Nobody sells it and none is listed: it's crafted.</summary>
    Craft,

    /// <summary>No way to get it is known.</summary>
    Unknown,

    /// <summary>Sold only by vendors the errands can't get to (an aetheryte you haven't attuned to, a spot not known, a quest not done).</summary>
    NoVendorReachable,

    /// <summary>The market would cost more than the ceiling you set.</summary>
    OverCeiling,

    /// <summary>Listed only on other worlds (buying there is off), or not enough listed.</summary>
    NotListedHere,

    /// <summary>Your bags have no room for it (no free slot, or a listing bigger than your free slots).</summary>
    NoRoom,

    /// <summary>Sold only for a currency you don't allow the errands to spend, or haven't enough of (Wolf Marks…).</summary>
    Currency,

    /// <summary>Sold on the board, but none listed on your data centre just now (0.9.4).</summary>
    NoneListed,
}

/// <summary>Something left out of the errands, with why.</summary>
public sealed record ErrandLeft(Need Need, int Count, LeftReason Reason)
{
    /// <summary><see cref="LeftReason.Currency"/>: the currency vendor that has it.</summary>
    public ExchangeOffer? Exchange { get; init; }
}

/// <summary>What the errand planner is told about you and the game.</summary>
public sealed record ErrandOptions
{
    /// <summary>Free slots in your four bags: a trip takes no more.</summary>
    public int FreeSlots { get; init; }

    /// <summary>
    /// How many of an item are in your bags now (null: none counted). What's bought tops up their stacks
    /// before it takes a slot (0.9.5: one dye to a stack of two needed no slot, and was left for want of one).
    /// </summary>
    public Func<uint, int>? Held { get; init; }

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

    /// <summary>
    /// Whether an item may be bought on a world (null: yes): a board the errands found without it this
    /// run isn't planned for it again.
    /// </summary>
    public Func<uint, string, bool>? ListingAllowed { get; init; }

    /// <summary>
    /// Your home has a summoning bell (you start beside it): the retainers come first then, before you go
    /// out to the vendors.
    /// </summary>
    public bool BellAtHome { get; init; }

    /// <summary>Your home world: its board comes before other worlds' (you're there after the bell).</summary>
    public string? HomeWorld { get; init; }

    /// <summary>Now, for how old Universalis's listings are (tests set it).</summary>
    public DateTime Now { get; init; } = DateTime.UtcNow;
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

    /// <summary>The currency vendors' part, by currency ("MGP" → 45,000): none of it gil, so not in <see cref="TotalGil"/>.</summary>
    public IReadOnlyList<(string Currency, uint CurrencyItem, long Cost)> CurrencyCosts =>
        Stops(StopKind.Exchange).SelectMany(s => s.Items).Where(i => i.Exchange != null)
            .GroupBy(i => (i.Exchange!.CurrencyName, i.Exchange.Currency))
            .Select(g => (g.Key.CurrencyName, g.Key.Currency, g.Sum(i => i.CurrencyCost)))
            .ToList();

    public bool Needs(StopKind kind) => Stops(kind).Any();
}

/// <summary>
/// Turns a shopping plan into trips: what to fetch from each retainer, vendor and world's market board,
/// as much as your free bag slots hold each time, in the order vendors (by place, then NPC) → retainers
/// → the market board (by world, your own first). Each trip is followed by going home and placing what it got. The
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
        public ExchangeOffer? Exchange { get; init; }
        public long CurrencyCost => Exchange?.CostFor(Count) ?? 0;

        /// <summary>How many of those bought go onto stacks already in your bags (no slot for them).</summary>
        public int TopUp { get; init; }

        public int Slots => Stacks(Math.Max(0, Bought - TopUp), Stack);
    }

    /// <summary>
    /// Two plans say the same: the same trips with the same stops, items, counts and gil (the market's
    /// dearest price for one too), and the same left out. The errands' screen starts only the plan it
    /// showed, and only while a fresh estimate still says the same.
    /// </summary>
    public static bool Same(ErrandPlan a, ErrandPlan b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.FreeSlots != b.FreeSlots || a.MarketCeiling != b.MarketCeiling || a.Trips.Count != b.Trips.Count || a.Left.Count != b.Left.Count) return false;
        for (var t = 0; t < a.Trips.Count; t++)
        {
            var (x, y) = (a.Trips[t], b.Trips[t]);
            if (x.Stops.Count != y.Stops.Count) return false;
            for (var s = 0; s < x.Stops.Count; s++)
            {
                var (p, q) = (x.Stops[s], y.Stops[s]);
                if (p.Kind != q.Kind || p.Key != q.Key || p.World != q.World || p.Items.Count != q.Items.Count) return false;
                for (var i = 0; i < p.Items.Count; i++)
                {
                    var (m, n) = (p.Items[i], q.Items[i]);
                    if (m.Need != n.Need || m.Count != n.Count || m.Bought != n.Bought || m.Slots != n.Slots || m.Gil != n.Gil || m.MaxUnitPrice != n.MaxUnitPrice
                        || m.CurrencyCost != n.CurrencyCost)
                        return false;
                }
            }
        }
        for (var i = 0; i < a.Left.Count; i++)
            if (a.Left[i] != b.Left[i]) return false;
        return true;
    }

    public static int Stacks(int count, int stack) => count <= 0 ? 0 : (count + Math.Max(1, stack) - 1) / Math.Max(1, stack);

    /// <summary>
    /// The bag slots that adding <paramref name="adding"/> of an item takes, with <paramref name="held"/>
    /// of it in your bags already: their last stack's room first (0.9.5).
    /// </summary>
    public static int NewSlots(int held, int adding, int stack) =>
        adding <= 0 ? 0 : Stacks(Math.Max(0, held) + adding, stack) - Stacks(Math.Max(0, held), stack);

    /// <summary>The room left on the stacks of an item already in your bags.</summary>
    public static int StackRoom(int held, int stack) => held <= 0 ? 0 : Stacks(held, stack) * Math.Max(1, stack) - held;

    public static ErrandPlan Build(ShoppingPlan shop, ErrandOptions o)
    {
        var left = new List<ErrandLeft>();
        var units = new List<Unit>();
        long marketSoFar = 0;
        // Listings already planned (0.9.9): a line bought on the board in place of its vendor, and the
        // board's own line for the rest of the same thing, would otherwise both plan the same listings.
        var used = new HashSet<MarketListing>(ReferenceEqualityComparer.Instance);
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
                        // On the board instead, when it's listed there (0.9.4).
                        if (OnBoard(line)) marketSoFar = MarketUnits(FromBoard(line), o, stack, units, left, marketSoFar, used);
                        else left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.NoVendorReachable));
                        break;
                    }
                    // One stop per NPC (0.8.0 had one per shop: the Housing Merchant's three shops were three
                    // visits, in three trips); each item opens its own shop there.
                    units.Add(new Unit(StopKind.Vendor, $"v:{offer.TerritoryId}:{offer.District}:{offer.NpcId}", 1, line.Need, line.Count, line.Count, stack,
                        (long)offer.Price * line.Count, true) { Vendor = offer });
                    break;
                case SourceKind.Exchange when line.Exchange != null:
                    // A currency vendor: walked to like a vendor, so it has to be reachable as one.
                    if (!o.Reachable(line.Exchange.Vendor))
                    {
                        if (OnBoard(line)) marketSoFar = MarketUnits(FromBoard(line), o, stack, units, left, marketSoFar, used);
                        else left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.NoVendorReachable));
                        break;
                    }
                    var ev = line.Exchange.Vendor;
                    units.Add(new Unit(StopKind.Exchange, $"x:{ev.TerritoryId}:{ev.District}:{ev.NpcId}:{ev.ShopId}", 1, line.Need, line.Count, line.Count, stack, 0, true)
                        { Vendor = ev, Exchange = line.Exchange });
                    break;
                case SourceKind.Market when line.Market != null:
                    marketSoFar = MarketUnits(line, o, stack, units, left, marketSoFar, used);
                    break;
                case SourceKind.Craft or SourceKind.Unknown when OnBoard(line):
                    marketSoFar = MarketUnits(FromBoard(line), o, stack, units, left, marketSoFar, used);
                    break;
                case SourceKind.Craft or SourceKind.Unknown when line.Exchangeable != null:
                    left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.Currency) { Exchange = line.Exchangeable });
                    break;
                // Asked about, and none listed on the data centre (0.9.4: said so, rather than "craft" or
                // "not known" for something that is sold on the board, just not now).
                case SourceKind.Craft or SourceKind.Unknown when line.Board != null:
                    left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.NoneListed));
                    break;
                case SourceKind.Craft:
                    left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.Craft));
                    break;
                default:
                    left.Add(new ErrandLeft(line.Need, line.Count, LeftReason.Unknown));
                    break;
            }
        }

        // Vendors by place and NPC, retainers by name, the market by world (your own first); each in the
        // plan's order within.
        var ordered = units.Select((u, i) => (u, i))
            .OrderBy(x => KindOrder(x.u.Kind, o.BellAtHome))
            .ThenBy(x => PlaceOrder(x.u, o.HomeWorld, o.PreferDistrict), StringComparer.Ordinal)
            .ThenBy(x => x.i)
            .Select(x => x.u)
            .ToList();
        // Stacks already in your bags take the first of each item (0.9.5), in the trips' order.
        if (o.Held != null)
        {
            var room = new Dictionary<uint, int>();
            for (var i = 0; i < ordered.Count; i++)
            {
                var u = ordered[i];
                // Not a retainer's (its whole stack is taken, whatever the count) nor a high-quality
                // listing (it doesn't stack onto normal ones): those take their own slots (0.9.9).
                if (u.Kind == StopKind.Retainer || u.Listing is { Hq: true }) continue;
                if (!room.TryGetValue(u.Need.ItemId, out var r)) r = StackRoom(o.Held(u.Need.ItemId), u.Stack);
                var t = Math.Min(r, u.Bought);
                if (t > 0) ordered[i] = u with { TopUp = t };
                room[u.Need.ItemId] = r - t;
            }
        }
        var trips = Pack(ordered, o.FreeSlots, left);
        return new ErrandPlan(trips, left, Math.Max(0, o.FreeSlots), Math.Max(0, o.MarketCeiling));
    }

    /// <summary>The board lists it (its listings were asked about and there are some).</summary>
    private static bool OnBoard(ShoppingLine line) => line.Board is { Listings.Count: > 0 };

    /// <summary>A line whose own source is out of reach, as a board line (its cost worked out from the listings).</summary>
    private static ShoppingLine FromBoard(ShoppingLine line) =>
        line with { Source = SourceKind.Market, Market = line.Board, MarketCost = null, Vendor = null, Exchange = null };

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
    private static long MarketUnits(ShoppingLine line, ErrandOptions o, int stack, List<Unit> units, List<ErrandLeft> left, long soFar, HashSet<MarketListing> used)
    {
        // The listings no other line planned already.
        var quote = line.Market!;
        var taken = quote.Listings.Any(used.Contains);
        if (taken) quote = quote with { Listings = quote.Listings.Where(l => !used.Contains(l)).ToList() };
        IReadOnlyList<MarketListing> chosen;
        var shortBy = 0;
        // Listings Universalis had lately first: older ones have often gone by the time the board is read.
        var fresh = quote.Listings.Where(l => l.SeenAt == null || o.Now - l.SeenAt.Value <= MarketListing.TrustedFor).ToList();
        var stale = fresh.Count < quote.Listings.Count;
        if (o.WorldAllowed == null && o.ListingAllowed == null && line.MarketCost != null && !stale && !taken)
        {
            chosen = line.MarketCost.Listings;
        }
        else
        {
            bool Allowed(MarketListing l) => (o.WorldAllowed == null || o.WorldAllowed(l.World)) && (o.ListingAllowed == null || o.ListingAllowed(line.ItemId, l.World));
            var allowed = fresh.Where(Allowed).ToList();
            // Enough of the fresh ones, else every allowed listing (old ones too) rather than none.
            if (allowed.Sum(l => l.Quantity) < line.Count) allowed = quote.Listings.Where(Allowed).ToList();
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
            used.Add(l);
        }
        if (over > 0) left.Add(new ErrandLeft(line.Need, over, LeftReason.OverCeiling));
        return soFar;
    }

    /// <summary>
    /// The order of a trip's stops: vendors first, then the bell, then the boards. A trip starts at home,
    /// where your district's vendors are (0.8.0 went to the bell in a city first, then home to the
    /// merchant, then back to a city for the board: a teleport more each trip); the bell and the boards
    /// are in the same city. A bell in your home: the retainers first, there.
    /// </summary>
    private static int KindOrder(StopKind kind, bool bellAtHome) => kind switch
    {
        StopKind.Retainer when bellAtHome => -1,
        StopKind.Vendor => 0,
        StopKind.Exchange => 1,
        StopKind.Retainer => 2,
        _ => 3,
    };

    private static string PlaceOrder(Unit u, string? homeWorld, string? homeDistrict) => u.Kind switch
    {
        StopKind.Retainer => (u.Retainer?.Name ?? string.Empty) + "\u0001" + u.Key,
        // Your own district's first (a trip starts there), then other districts' (through their cities'
        // aethernet, 0.8.3), then the rest by zone.
        StopKind.Vendor or StopKind.Exchange => $"{(u.Vendor?.District is { } d ? (d == homeDistrict ? "\u0000" : "\u0001") + d : "~")}\u0001{u.Vendor?.TerritoryId:D6}\u0001{u.Vendor?.NpcId:D10}\u0001{u.Key}",
        // Your own world's board first (the bell's city is on it), then the others by name.
        _ => (homeWorld != null && u.World == homeWorld ? "\u0000" : "\u0001") + (u.World ?? string.Empty),
    };

    /// <summary>
    /// Fills trips up to the free slots, a splittable part split by whole stacks, a listing never. Each
    /// stop (a retainer, a vendor, a world's board) goes into as few trips as it can: its parts are cut
    /// only where they don't fit one trip's slots, and the pieces are packed biggest first into the first
    /// trip with room (0.7.12 packed in order, so a world's board came back in trip after trip). Within
    /// a trip, the plan's order (vendors by place, retainers, the board by world).
    /// </summary>
    private static List<ErrandTrip> Pack(List<Unit> units, int freeSlots, List<ErrandLeft> left)
    {
        freeSlots = Math.Max(0, freeSlots);
        // Each stop's parts, cut into pieces that fit a trip.
        var pieces = new List<List<(Unit U, int I)>>();
        foreach (var stop in units.Select((u, i) => (U: u, I: i)).GroupBy(x => x.U.Key))
        {
            var piece = new List<(Unit U, int I)>();
            var room = freeSlots;
            foreach (var (unit, i) in stop)
            {
                var u = unit;
                while (u.Count > 0)
                {
                    if (u.Slots <= room)
                    {
                        piece.Add((u, i));
                        room -= u.Slots;
                        break;
                    }
                    if (!u.Splits || freeSlots == 0)
                    {
                        if (u.Slots > freeSlots)
                        {
                            // (No free slot at all: only what tops up a stack in your bags goes.)
                            if (u.Splits && u.TopUp > 0)
                            {
                                var gilEach0 = u.Count > 0 ? u.Gil / u.Count : 0;
                                piece.Add((u with { Count = u.TopUp, Bought = u.TopUp, Gil = gilEach0 * u.TopUp }, i));
                                left.Add(new ErrandLeft(u.Need, u.Count - u.TopUp, LeftReason.NoRoom));
                            }
                            else left.Add(new ErrandLeft(u.Need, u.Count, LeftReason.NoRoom));
                            break;
                        }
                    }
                    else if (room > 0 || u.TopUp > 0)
                    {
                        // As many whole stacks as there's room for (and what tops up a stack in your bags);
                        // the rest in the next piece.
                        var fits = Math.Min(u.Count, room * u.Stack + u.TopUp);
                        var gilEach = u.Count > 0 ? u.Gil / u.Count : 0;
                        piece.Add((u with { Count = fits, Bought = fits, Gil = gilEach * fits }, i));
                        u = u with { Count = u.Count - fits, Bought = u.Bought - fits, Gil = u.Gil - gilEach * fits, TopUp = 0 };
                    }
                    if (piece.Count > 0) pieces.Add(piece);
                    piece = new List<(Unit U, int I)>();
                    room = freeSlots;
                }
            }
            if (piece.Count > 0) pieces.Add(piece);
        }
        // Biggest first, into the first trip with room.
        var trips = new List<(List<(Unit U, int I)> Units, int Room)>();
        foreach (var piece in pieces.OrderByDescending(p => p.Sum(x => x.U.Slots)).ThenBy(p => p[0].I))
        {
            var slots = piece.Sum(x => x.U.Slots);
            var t = trips.FindIndex(x => x.Room >= slots);
            if (t < 0)
            {
                trips.Add((new List<(Unit U, int I)>(piece), freeSlots - slots));
                continue;
            }
            trips[t].Units.AddRange(piece);
            trips[t] = (trips[t].Units, trips[t].Room - slots);
        }
        return trips
            .Select(t => t.Units.OrderBy(x => x.I).ToList())
            .OrderBy(t => t[0].I)
            .Select((t, n) => new ErrandTrip(n + 1, Stops(t.Select(x => x.U).ToList())))
            .ToList();
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
                    Exchange = i.Select(u => u.Exchange).FirstOrDefault(e => e != null),
                    Vendor = i.Select(u => u.Vendor).FirstOrDefault(v => v != null),
                    CurrencyCost = i.Sum(u => u.CurrencyCost),
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

    /// <summary>
    /// The board's "buy this?" question names the listing about to be bought: one of its numbers is
    /// its cost with tax, its cost without, or its price for one. When none is (another row was
    /// clicked, or the question isn't the purchase's), the purchase isn't confirmed. The numbers are
    /// read with any grouping (1,234 / 1.234 / 1 234).
    /// </summary>
    public static bool QuestionMatches(string? question, uint unitPrice, int quantity, long cost)
    {
        if (string.IsNullOrEmpty(question)) return false;
        var want = new[] { cost, (long)unitPrice * quantity, unitPrice };
        foreach (var n in Numbers(question))
            if (Array.IndexOf(want, n) >= 0) return true;
        return false;
    }

    /// <summary>
    /// The board's question for the listing chosen ("Purchase 1 Cieldalaes planter for 7,229 gil (344 fee
    /// included)?"): its largest number is the listing's whole cost (with tax, 1 gil of rounding either
    /// way, or without tax when no fee is shown), and, for more than one, its quantity is in it. A fee or
    /// a price for one that happens to equal the listing's isn't enough: another row's question
    /// ("20 … for 10,500 gil (500 fee included)" for a 1 × 500 listing) is said no to.
    /// </summary>
    public static bool BoardQuestionMatches(string? question, int quantity, long cost, long costWithoutTax)
    {
        if (string.IsNullOrEmpty(question)) return false;
        var numbers = Numbers(question);
        if (numbers.Count == 0) return false;
        var total = numbers.Max();
        if (Math.Abs(total - cost) > 1 && total != costWithoutTax) return false;
        numbers.Remove(total);
        return quantity <= 1 || numbers.Contains(quantity);
    }

    /// <summary>
    /// Whether a shop's question is about this item: each word of its name (by its first four letters, so
    /// "tables" and "Table" agree) is in the question. Used before saying yes to a price other than the
    /// plan's, where a row miscounted would buy another item.
    /// </summary>
    public static bool NamesItem(string? question, string itemName)
    {
        if (string.IsNullOrEmpty(question) || string.IsNullOrWhiteSpace(itemName)) return false;
        var asked = string.Join(' ', Words(question));
        var words = Words(itemName).Where(w => w.Length >= 3).ToList();
        if (words.Count == 0) return false;
        return words.All(w => asked.Contains(w.Length > 4 ? w[..4] : w, StringComparison.Ordinal));

        static IEnumerable<string> Words(string text) =>
            new string(text.ToLowerInvariant().Select(c => char.IsLetter(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// A looser <see cref="NamesItem"/>, for a question whose price is already the one planned (1.0.0): each
    /// of the name's words of 3 letters or more has its first 3 in the question (so "leaves" says "leaf",
    /// "knives" "knife"); a name with no such word (two kanji) says nothing against it.
    /// </summary>
    public static bool MentionsItem(string? question, string itemName)
    {
        if (string.IsNullOrEmpty(question)) return false;
        var asked = string.Join(' ', Words(question));
        var words = Words(itemName).Where(w => w.Length >= 3).ToList();
        return words.Count == 0 || words.All(w => asked.Contains(w[..3], StringComparison.Ordinal));

        static IEnumerable<string> Words(string text) =>
            new string(text.ToLowerInvariant().Select(c => char.IsLetter(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// The gil a shop's question asks for <paramref name="quantity"/> of something ("Purchase 1 stage panel
    /// for 4,000 gil?" → 4,000), or null when it doesn't read so (no quantity in it, or nothing else).
    /// The largest number besides the quantity: the price, in any order the language puts them.
    /// </summary>
    public static long? AskedCost(string? question, int quantity)
    {
        if (string.IsNullOrEmpty(question)) return null;
        var numbers = Numbers(question);
        if (!numbers.Remove(quantity) || numbers.Count == 0) return null;
        return numbers.Max();
    }

    /// <summary>The whole numbers in a text, a digit group's separators (, . space, no-break spaces) left out.</summary>
    public static List<long> Numbers(string text)
    {
        var list = new List<long>();
        long n = 0;
        var digits = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : '\0';
            if (c is >= '0' and <= '9')
            {
                if (digits < 18) n = n * 10 + (c - '0');
                digits++;
                continue;
            }
            // A separator between digit groups: the number goes on.
            if (digits > 0 && c is ',' or '.' or ' ' or '\u00a0' or '\u202f' && i + 1 < text.Length && text[i + 1] is >= '0' and <= '9') continue;
            if (digits > 0) list.Add(n);
            n = 0;
            digits = 0;
        }
        return list;
    }

    /// <summary>
    /// How far over the planned price a listing may be (a quarter): the plan's prices are Universalis's,
    /// minutes or hours old, and the board's have moved by the time it's read (0.8.1 left a Hanging Planter
    /// Branch at 9,798 gil, 1 more than planned). The run as a whole never goes past the gil agreed or the
    /// ceiling (<paramref name="ceilingLeft"/> in <see cref="WhyNot"/>).
    /// </summary>
    public const double Leeway = 0.25;

    /// <summary>The most a planned amount may become with <see cref="Leeway"/>.</summary>
    public static long WithLeeway(long planned) => planned <= 0 ? 0 : (long)Math.Ceiling(planned * (1 + Leeway));

    /// <summary>Why a listing isn't bought, or null when it may be.</summary>
    public static string? WhyNot(uint unitPrice, int quantity, uint totalTax, uint maxUnitPrice, int stillWanted, long itemGilLeft, long ceilingLeft, long gilOnHand)
    {
        if (quantity <= 0) return "nothing listed";
        if (stillWanted <= 0) return "enough already";
        if (unitPrice > WithLeeway(maxUnitPrice)) return $"{unitPrice:N0} gil each is more than a quarter above the planned {maxUnitPrice:N0}";
        var cost = Cost(unitPrice, quantity, totalTax);
        if (cost > WithLeeway(itemGilLeft)) return $"{cost:N0} gil for {quantity} is more than the {itemGilLeft:N0} gil planned for it (and a quarter)";
        if (cost > ceilingLeft) return $"{cost:N0} gil would go past the market ceiling ({ceilingLeft:N0} gil left under it)";
        if (cost > gilOnHand) return $"{cost:N0} gil is more than you have ({gilOnHand:N0})";
        return null;
    }
}
