using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class ErrandPlannerTests
{
    private static readonly DateTime Noon = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static VendorOffer Offer(uint item, uint price, uint npc = 1, string? district = "mist", bool located = true, uint territory = 339) =>
        new(item, price, npc, $"NPC {npc}", 262144 + npc, "Shop", located ? territory : 0, located ? "Mist" : string.Empty, located ? 72u : 0,
            located ? new Vector2(10.9f, 11.4f) : null, located ? new Vector3(1, 2, 3) : null, district);

    private static RetainerStock Retainer(ulong id, string name, params (uint Item, int Count)[] items) =>
        new(id, name, Noon, items.ToDictionary(i => i.Item, i => i.Count), HoldingOrigin.Seen);

    private static MarketQuote Quote(uint item, params (uint Price, int Qty, string World)[] listings) =>
        new(item, "Light", Noon, listings.Select(l => new MarketListing(l.Price, l.Qty, l.World, false)).ToList());

    private static Func<uint, IReadOnlyList<VendorOffer>> Offers(params VendorOffer[] offers) =>
        id => offers.Where(o => o.ItemId == id).ToList();

    private static readonly Func<uint, CraftOption?> NoCraft = _ => null;

    private static ShoppingPlan Shop(IEnumerable<Need> needs, RetainerStock[]? retainers = null, VendorOffer[]? offers = null, MarketQuote[]? quotes = null,
        Func<uint, CraftOption?>? craft = null) =>
        ShoppingPlanner.Build(needs.ToList(), retainers ?? Array.Empty<RetainerStock>(), Offers(offers ?? Array.Empty<VendorOffer>()),
            (quotes ?? Array.Empty<MarketQuote>()).ToDictionary(q => q.ItemId), craft ?? NoCraft, "mist");

    private static ErrandOptions Options(int free = 100, long ceiling = 1_000_000, int stack = 1) =>
        new() { FreeSlots = free, MarketCeiling = ceiling, StackSize = _ => stack };

    [Fact]
    public void OneTripInOrderVendorsRetainersMarket()
    {
        // Vendors first (a trip starts at home, where your district's are), then the bell and the
        // boards, which are in the same city (0.8.0 went to the bell, home, then back to a city).
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece), new Need(200, 2, NeedKind.Piece), new Need(100, 1, NeedKind.Piece) },
            new[] { Retainer(1, "Momo", (100, 1)) }, new[] { Offer(200, 1500) }, new[] { Quote(300, (5000, 1, "Lich")) });
        var plan = ErrandPlanner.Build(shop, Options());
        var trip = Assert.Single(plan.Trips);
        Assert.Equal(new[] { StopKind.Vendor, StopKind.Retainer, StopKind.Market }, trip.Stops.Select(s => s.Kind));
        Assert.Equal(3000, plan.VendorGil);
        Assert.Equal(5250, plan.MarketGil);
        Assert.Equal(4, plan.Count);
        Assert.Equal("Lich", trip.Stops[2].World);
        Assert.Equal(5000u, trip.Stops[2].Items[0].MaxUnitPrice);
        Assert.Empty(plan.Left);
    }

    [Fact]
    public void OneNpcsShopsAreOneStopEachItemAtItsOwnPrice()
    {
        // The Housing Merchant's chairs and partitions are two shops of one NPC: one visit (0.8.0 made
        // a stop per shop, and kept the first item's offer for the stop, so a Stage Panel was checked
        // against an Eorzean Map's price).
        var chair = Offer(200, 5000) with { ShopId = 1, ShopName = "Purchase Furnishings (Chairs, Beds)" };
        var map = Offer(201, 1000) with { ShopId = 2, ShopName = "Purchase Furnishings (Partitions, Wall Decorations)" };
        var panel = Offer(202, 4000) with { ShopId = 2, ShopName = "Purchase Furnishings (Partitions, Wall Decorations)" };
        var shop = Shop(new[] { new Need(200, 1, NeedKind.Piece), new Need(201, 1, NeedKind.Piece), new Need(202, 2, NeedKind.Piece) },
            offers: new[] { chair, map, panel });
        var plan = ErrandPlanner.Build(shop, Options());
        var stop = Assert.Single(Assert.Single(plan.Trips).Stops);
        Assert.Equal(StopKind.Vendor, stop.Kind);
        Assert.Equal(3, stop.Items.Count);
        Assert.Equal(4000u, stop.Items.Single(i => i.ItemId == 202).Vendor!.Price);
        Assert.Equal(1000u, stop.Items.Single(i => i.ItemId == 201).Vendor!.Price);
        Assert.Equal(1u, stop.Items.Single(i => i.ItemId == 200).Vendor!.ShopId);
        Assert.Equal(2u, stop.Items.Single(i => i.ItemId == 202).Vendor!.ShopId);
        Assert.Equal(5000 + 1000 + 8000, plan.VendorGil);
    }

    [Fact]
    public void YourOwnDistrictsVendorComesBeforeAnothers()
    {
        // The Goblet's merchant (only it sells one thing) after Mist's, your own, though "goblet" sorts first.
        var shop = Shop(new[] { new Need(200, 1, NeedKind.Piece), new Need(201, 1, NeedKind.Piece) },
            offers: new[] { Offer(200, 1000, npc: 1, district: "mist"), Offer(201, 1000, npc: 2, district: "goblet", territory: 341) });
        var plan = ErrandPlanner.Build(shop, Options() with { PreferDistrict = "mist" });
        Assert.Equal(new[] { "mist", "goblet" }, Assert.Single(plan.Trips).Stops.Select(s => s.Vendor!.District));
    }

    [Fact]
    public void ADistrictsCityIsItsAethernetsAetheryte()
    {
        Assert.Equal(8u, Residential.CityAetheryte("mist"));
        Assert.Equal(2u, Residential.CityAetheryte("lavender-beds"));
        Assert.Equal(9u, Residential.CityAetheryte("goblet"));
        Assert.Equal(111u, Residential.CityAetheryte("shirogane"));
        Assert.Equal(70u, Residential.CityAetheryte("empyreum"));
        Assert.Equal(0u, Residential.CityAetheryte(null));
    }

    [Fact]
    public void ABellAtHomeComesFirst()
    {
        var shop = Shop(new[] { new Need(200, 2, NeedKind.Piece), new Need(100, 1, NeedKind.Piece) },
            new[] { Retainer(1, "Momo", (100, 1)) }, new[] { Offer(200, 1500) });
        var plan = ErrandPlanner.Build(shop, Options() with { BellAtHome = true });
        Assert.Equal(new[] { StopKind.Retainer, StopKind.Vendor }, Assert.Single(plan.Trips).Stops.Select(s => s.Kind));
    }

    [Fact]
    public void YourOwnWorldsBoardComesFirst()
    {
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece), new Need(301, 1, NeedKind.Piece) },
            quotes: new[] { Quote(300, (100, 1, "Alpha")), Quote(301, (100, 1, "Zodiark")) });
        var plan = ErrandPlanner.Build(shop, Options() with { HomeWorld = "Zodiark" });
        Assert.Equal(new[] { "Zodiark", "Alpha" }, Assert.Single(plan.Trips).Stops.Select(s => s.World));
        var other = ErrandPlanner.Build(shop, Options());
        Assert.Equal(new[] { "Alpha", "Zodiark" }, Assert.Single(other.Trips).Stops.Select(s => s.World));
    }

    [Fact]
    public void TripsFillTheFreeSlotsAndSplitByStacks()
    {
        var shop = Shop(new[] { new Need(200, 5, NeedKind.Piece) }, offers: new[] { Offer(200, 100) });
        var plan = ErrandPlanner.Build(shop, Options(free: 2));
        Assert.Equal(3, plan.Trips.Count);
        Assert.Equal(new[] { 2, 2, 1 }, plan.Trips.Select(t => t.Count));
        Assert.Equal(new[] { 200L, 200, 100 }, plan.Trips.Select(t => t.Gil));
        Assert.Equal(500, plan.VendorGil);
    }

    [Fact]
    public void StackingItemsTakeOneSlotPerStack()
    {
        var shop = Shop(new[] { new Need(5729, 120, NeedKind.Dye, 1) }, offers: new[] { Offer(5729, 216) });
        var plan = ErrandPlanner.Build(shop, Options(free: 1, stack: 999));
        var trip = Assert.Single(plan.Trips);
        Assert.Equal(1, trip.Slots);
        Assert.Equal(120, trip.Count);
    }

    [Fact]
    public void AMarketListingIsNeverSplitAndCountsItsWholeStack()
    {
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece) }, quotes: new[] { Quote(300, (100, 3, "Lich")) });
        var plan = ErrandPlanner.Build(shop, Options(free: 2));
        Assert.Empty(plan.Trips);
        var left = Assert.Single(plan.Left);
        Assert.Equal(LeftReason.NoRoom, left.Reason);

        var roomy = ErrandPlanner.Build(shop, Options(free: 3));
        var item = roomy.Trips.Single().Stops.Single().Items.Single();
        Assert.Equal(1, item.Count);
        Assert.Equal(3, item.Bought);
        Assert.Equal(3, item.Slots);
    }

    [Fact]
    public void TheMarketCeilingLeavesOutWhatGoesPastIt()
    {
        var shop = Shop(new[] { new Need(300, 3, NeedKind.Piece) }, quotes: new[] { Quote(300, (1000, 1, "Lich"), (2000, 1, "Lich"), (4000, 1, "Odin")) });
        var plan = ErrandPlanner.Build(shop, Options(ceiling: 3500));
        Assert.Equal(1050 + 2100, plan.MarketGil);
        Assert.True(plan.MarketGil <= plan.MarketCeiling);
        var left = Assert.Single(plan.Left);
        Assert.Equal(LeftReason.OverCeiling, left.Reason);
        Assert.Equal(1, left.Count);

        var none = ErrandPlanner.Build(shop, Options(ceiling: 0));
        Assert.Equal(0, none.MarketGil);
        Assert.Empty(none.Trips);
    }

    [Fact]
    public void OnlyTheWorldsAllowedAreBoughtOn()
    {
        var shop = Shop(new[] { new Need(300, 2, NeedKind.Piece) }, quotes: new[] { Quote(300, (1000, 1, "Odin"), (1500, 1, "Lich"), (3000, 1, "Lich")) });
        var plan = ErrandPlanner.Build(shop, Options() with { WorldAllowed = w => w == "Lich" });
        var stop = plan.Stops(StopKind.Market).Single();
        Assert.Equal("Lich", stop.World);
        Assert.Equal(1575 + 3150, stop.Gil);
        Assert.Equal(3000u, stop.Items[0].MaxUnitPrice);

        var thin = Shop(new[] { new Need(300, 2, NeedKind.Piece) }, quotes: new[] { Quote(300, (1000, 1, "Odin"), (1500, 1, "Lich")) });
        var some = ErrandPlanner.Build(thin, Options() with { WorldAllowed = w => w == "Lich" });
        Assert.Equal(1, some.Count);
        Assert.Equal(LeftReason.NotListedHere, Assert.Single(some.Left).Reason);
    }

    [Fact]
    public void MarketStopsAreGroupedByWorld()
    {
        var shop = Shop(new[] { new Need(300, 2, NeedKind.Piece), new Need(301, 1, NeedKind.Piece) },
            quotes: new[] { Quote(300, (1000, 1, "Odin"), (1100, 1, "Lich")), Quote(301, (500, 1, "Odin")) });
        var plan = ErrandPlanner.Build(shop, Options());
        var stops = plan.Stops(StopKind.Market).ToList();
        Assert.Equal(new[] { "Lich", "Odin" }, stops.Select(s => s.World));
        Assert.Equal(2, stops[1].Items.Count);
    }

    [Fact]
    public void AnUnreachableVendorIsSwappedForAReachableOneOrLeftOut()
    {
        var far = Offer(200, 1000, npc: 1, district: "goblet");
        var near = Offer(200, 1200, npc: 2, district: null, territory: 129);
        var shop = Shop(new[] { new Need(200, 1, NeedKind.Piece) }, offers: new[] { far, near });
        Assert.Equal(1u, shop.Lines.Single().Vendor!.NpcId);
        var o = Options() with { Reachable = v => v.District == null || v.District == "mist", Offers = Offers(far, near) };
        var plan = ErrandPlanner.Build(shop, o);
        Assert.Equal(2u, plan.Stops(StopKind.Vendor).Single().Vendor!.NpcId);
        Assert.Equal(1200, plan.VendorGil);

        var none = ErrandPlanner.Build(shop, o with { Offers = Offers(far) });
        Assert.Equal(LeftReason.NoVendorReachable, Assert.Single(none.Left).Reason);
    }

    [Fact]
    public void CraftAndUnknownAreLeftOut()
    {
        var shop = Shop(new[] { new Need(400, 2, NeedKind.Piece), new Need(401, 1, NeedKind.Piece) },
            craft: id => id == 400 ? new CraftOption(400, 9, "Carpenter", 50) : null);
        var plan = ErrandPlanner.Build(shop, Options());
        Assert.True(plan.IsEmpty);
        Assert.Equal(new[] { LeftReason.Craft, LeftReason.Unknown }, plan.Left.Select(l => l.Reason));
    }

    [Fact]
    public void FullBagsLeaveEverythingOut()
    {
        var shop = Shop(new[] { new Need(200, 1, NeedKind.Piece) }, offers: new[] { Offer(200, 100) });
        var plan = ErrandPlanner.Build(shop, Options(free: 0));
        Assert.True(plan.IsEmpty);
        Assert.Equal(LeftReason.NoRoom, Assert.Single(plan.Left).Reason);
    }

    [Fact]
    public void VendorsAreGroupedByPlaceThenNpc()
    {
        var shop = Shop(new[] { new Need(200, 1, NeedKind.Piece), new Need(201, 1, NeedKind.Piece), new Need(202, 1, NeedKind.Piece) },
            offers: new[] { Offer(200, 10, npc: 2), Offer(201, 10, npc: 1), Offer(202, 10, npc: 2) });
        var plan = ErrandPlanner.Build(shop, Options());
        var stops = plan.Stops(StopKind.Vendor).ToList();
        Assert.Equal(new[] { 1u, 2u }, stops.Select(s => s.Vendor!.NpcId));
        Assert.Equal(2, stops[1].Items.Count);
    }

    // 0.7.13: a world's board in as few trips as it can (0.7.12 went back to Shiva trip after trip).

    [Fact]
    public void EachWorldsBoardIsVisitedOnceWhenItFitsATrip()
    {
        var listings = new List<(uint, int, string)>();
        for (var i = 0; i < 4; i++) listings.Add((1000u + (uint)i, 1, "Lich"));
        for (var i = 0; i < 8; i++) listings.Add((2000u + (uint)i, 1, "Shiva"));
        for (var i = 0; i < 3; i++) listings.Add((3000u + (uint)i, 1, "Twintania"));
        var quotes = new[] { Quote(300, listings.Take(4).ToArray()), Quote(301, listings.Skip(4).Take(8).ToArray()), Quote(302, listings.Skip(12).ToArray()) };
        var shop = Shop(new[] { new Need(300, 4, NeedKind.Piece), new Need(301, 8, NeedKind.Piece), new Need(302, 3, NeedKind.Piece) }, quotes: quotes);
        var plan = ErrandPlanner.Build(shop, Options(free: 10));
        Assert.Equal(2, plan.Trips.Count);
        foreach (var world in new[] { "Lich", "Shiva", "Twintania" })
            Assert.Single(plan.Trips, t => t.Stops.Any(s => s.World == world));
        Assert.Equal(15, plan.Trips.Sum(t => t.Count));
        Assert.All(plan.Trips, t => Assert.True(t.Slots <= 10));
    }

    [Fact]
    public void AWorldTooBigForOneTripIsSplitOnlyAsMuchAsItMust()
    {
        var listings = Enumerable.Range(0, 13).Select(i => (1000u + (uint)i, 1, "Shiva")).ToArray();
        var shop = Shop(new[] { new Need(300, 13, NeedKind.Piece) }, quotes: new[] { Quote(300, listings) });
        var plan = ErrandPlanner.Build(shop, Options(free: 10));
        Assert.Equal(2, plan.Trips.Count);
        Assert.Equal(new[] { 10, 3 }, plan.Trips.Select(t => t.Count));
    }

    // 0.7.12: listings Universalis hasn't seen lately are put after fresh ones; a world whose board had
    // none is left out.

    [Fact]
    public void FreshListingsAreChosenOverOldCheaperOnes()
    {
        var old = Noon - TimeSpan.FromDays(3);
        var quote = new MarketQuote(300, "Light", Noon, new List<MarketListing>
        {
            new(1000, 1, "Lich", false, 0, old),
            new(1500, 1, "Odin", false, 0, Noon - TimeSpan.FromHours(1)),
        });
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece) }, quotes: new[] { quote });
        var plan = ErrandPlanner.Build(shop, Options() with { Now = Noon });
        Assert.Equal("Odin", Assert.Single(plan.Trips).Stops.Single(s => s.Kind == StopKind.Market).World);
    }

    [Fact]
    public void OldListingsStillCountWhenTheFreshOnesAreTooFew()
    {
        var old = Noon - TimeSpan.FromDays(3);
        var quote = new MarketQuote(300, "Light", Noon, new List<MarketListing> { new(1000, 1, "Lich", false, 0, old) });
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece) }, quotes: new[] { quote });
        var plan = ErrandPlanner.Build(shop, Options() with { Now = Noon });
        Assert.Equal("Lich", Assert.Single(plan.Trips).Stops.Single(s => s.Kind == StopKind.Market).World);
    }

    [Fact]
    public void AWorldWhoseBoardHadNoneIsLeftOut()
    {
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece) }, quotes: new[] { Quote(300, (1000, 1, "Lich"), (2000, 1, "Odin")) });
        var plan = ErrandPlanner.Build(shop, Options() with { ListingAllowed = (item, world) => !(item == 300 && world == "Lich") });
        Assert.Equal("Odin", Assert.Single(plan.Trips).Stops.Single(s => s.Kind == StopKind.Market).World);
    }
}

public class MarketRuleTests
{
    [Fact]
    public void BuysUpToAQuarterOverThePlan()
    {
        Assert.Null(MarketRule.WhyNot(1000, 1, 50, 1000, 1, 1050, 10_000, 100_000));
        // A little over the plan (Universalis's prices are old by the time the board is read): bought.
        Assert.Null(MarketRule.WhyNot(1001, 1, 51, 1000, 1, 1050, 10_000, 100_000));
        Assert.Null(MarketRule.WhyNot(1250, 1, 63, 1000, 1, 1050, 10_000, 100_000));
        Assert.Contains("above the planned", MarketRule.WhyNot(1251, 1, 63, 1000, 1, 5000, 10_000, 100_000));
        Assert.Contains("ceiling", MarketRule.WhyNot(1000, 1, 50, 1000, 1, 5000, 1000, 100_000));
        Assert.Contains("more than you have", MarketRule.WhyNot(1000, 1, 50, 1000, 1, 5000, 10_000, 900));
        Assert.Contains("planned for it", MarketRule.WhyNot(1000, 3, 150, 1000, 1, 1050, 10_000, 100_000));
        Assert.Equal("enough already", MarketRule.WhyNot(1000, 1, 50, 1000, 0, 5000, 10_000, 100_000));
    }

    [Fact]
    public void TaxIsTheGamesOrFivePercent()
    {
        Assert.Equal(1050, MarketRule.Cost(1000, 1, 0));
        Assert.Equal(1030, MarketRule.Cost(1000, 1, 30));
        Assert.Equal(2102, MarketRule.Cost(667, 3, 0));
    }
}

public class ErrandWordsTests
{
    [Fact]
    public void TheCeilingStepsThroughItsValues()
    {
        Assert.Equal(250_000, HousingStudio.UI.Model.ErrandWords.StepCeiling(100_000, 1));
        Assert.Equal(50_000, HousingStudio.UI.Model.ErrandWords.StepCeiling(100_000, -1));
        Assert.Equal(0, HousingStudio.UI.Model.ErrandWords.StepCeiling(0, -1));
        Assert.Equal(25_000_000, HousingStudio.UI.Model.ErrandWords.StepCeiling(10_000_000, 1));
        Assert.Equal(HousingStudio.UI.Model.ErrandWords.GilCap, HousingStudio.UI.Model.ErrandWords.StepCeiling(HousingStudio.UI.Model.ErrandWords.GilCap, 1));
        Assert.Equal(100_000, HousingStudio.UI.Model.ErrandWords.StepCeiling(75_000, 1));
    }

    [Theory]
    [InlineData("2500000", 2_500_000L)]
    [InlineData("2,500,000 gil", 2_500_000L)]
    [InlineData("2.500.000", 2_500_000L)]
    [InlineData("2.5m", 2_500_000L)]
    [InlineData("2,5m", 2_500_000L)]
    [InlineData("750k", 750_000L)]
    [InlineData("1b", 999_999_999L)]
    [InlineData("0", 0L)]
    [InlineData("99999999999999", 999_999_999L)]
    [InlineData(" 12 000 000 ", 12_000_000L)]
    public void ACeilingCanBeTypedIn(string typed, long want) => Assert.Equal(want, HousingStudio.UI.Model.ErrandWords.ParseGil(typed));

    [Theory]
    [InlineData("")]
    [InlineData("gil")]
    [InlineData("lots")]
    public void NoNumberIsNoCeiling(string typed) => Assert.Null(HousingStudio.UI.Model.ErrandWords.ParseGil(typed));

    private static readonly Func<uint, string> Names = id => id switch { 100 => "Glade Shelf", 200 => "Riviera Table", 300 => "Oasis Rug", _ => $"Item {id}" };

    private static VendorOffer Merchant(uint item, uint price) =>
        new(item, price, 7, "Material Supplier", 262151, "Shop", 339, "Mist", 72, null, new System.Numerics.Vector3(1, 2, 3), "mist");

    private static ErrandPlan Plan(int free = 10, long ceiling = 50_000, params Need[] needs)
    {
        var shop = ShoppingPlanner.Build(needs, new[] { new RetainerStock(1, "Momo", DateTime.UtcNow, new Dictionary<uint, int> { [100] = 2 }, HoldingOrigin.Seen) },
            id => id == 200 ? new[] { Merchant(200, 1500) } : Array.Empty<VendorOffer>(),
            new Dictionary<uint, MarketQuote> { [300] = new(300, "Light", DateTime.UtcNow, new List<MarketListing> { new(5000, 1, "Lich", false) }) },
            _ => null, "mist");
        return ErrandPlanner.Build(shop, new ErrandOptions { FreeSlots = free, MarketCeiling = ceiling });
    }

    private static ErrandPlan Three(int free = 10, long ceiling = 50_000) =>
        Plan(free, ceiling, new Need(100, 2, NeedKind.Piece), new Need(200, 2, NeedKind.Piece), new Need(300, 1, NeedKind.Piece));

    [Fact]
    public void ATripIsItsLegsInOrderEndingAtHome()
    {
        var plan = Three();
        var route = HousingStudio.UI.Model.ErrandWords.Route(plan, Names, houseBell: true, homeWorld: "Phoenix");
        var trip = Assert.Single(route);
        Assert.Equal("TRIP 1", trip.Label);
        Assert.Equal("5 of 10 bag slots", trip.Rest);
        Assert.Equal(new[] { ErrandLeg.Vendor, ErrandLeg.Bell, ErrandLeg.Market, ErrandLeg.Home }, trip.Legs.Select(l => l.Kind));
        Assert.Equal("Mist · Material Supplier", trip.Legs[0].Name);
        Assert.Equal("Riviera Table ×2", trip.Legs[0].What);
        Assert.Equal("3,000", trip.Legs[0].Gil);
        Assert.Equal("Bell in your house", trip.Legs[1].Name);
        Assert.Equal("Momo · Glade Shelf ×2", trip.Legs[1].What);
        Assert.Equal("free", trip.Legs[1].Gil);
        // Another world's board says which.
        Assert.Equal("Limsa · Market board · Lich", trip.Legs[2].Name);
        Assert.Equal("≤ 5,250", trip.Legs[2].Gil);
        Assert.Equal("Home, and place", trip.Legs[3].Name);
        Assert.Equal("5 pieces go in", trip.Legs[3].What);
    }

    [Fact]
    public void LaterTripsAreWorkedOutAgain()
    {
        var plan = Three(free: 2);
        var route = HousingStudio.UI.Model.ErrandWords.Route(plan, Names, houseBell: false, homeWorld: "Lich");
        Assert.True(route.Count >= 2);
        Assert.Equal("2 of 2 bag slots", route[0].Rest);
        Assert.Equal("worked out again after trip 1", route[1].Rest);
        Assert.Contains(route.SelectMany(t => t.Legs), l => l.Name == "Limsa · Summoning bell");
        // The home world's board doesn't name it.
        Assert.Contains(route.SelectMany(t => t.Legs), l => l.Name == "Limsa · Market board");
    }

    [Fact]
    public void TheRouteNamesTheMarketCityChosen()
    {
        var plan = Three(free: 2);
        var city = HousingStudio.UI.Model.ErrandWords.City(1);
        Assert.Equal("Ul'dah", city);
        Assert.Equal("Limsa", HousingStudio.UI.Model.ErrandWords.City(0));
        var route = HousingStudio.UI.Model.ErrandWords.Route(plan, Names, houseBell: false, homeWorld: "Lich", city: city);
        Assert.Contains(route.SelectMany(t => t.Legs), l => l.Name == "Ul'dah · Summoning bell");
        Assert.Contains(route.SelectMany(t => t.Legs), l => l.Name == "Ul'dah · Market board");
    }

    [Fact]
    public void TheTitleAndTheButtonNameTheThingsAndTheGil()
    {
        var plan = Three();
        Assert.Equal(("5 things,", "1 trip"), HousingStudio.UI.Model.ErrandWords.Title(plan));
        Assert.Equal(("Nothing to fetch", (string?)null), HousingStudio.UI.Model.ErrandWords.Title(ErrandPlan.Empty));
        Assert.Equal("Set out · up to 8,250 gil", HousingStudio.UI.Model.ErrandWords.SetOut(plan.TotalGil));
        Assert.Equal("Set out", HousingStudio.UI.Model.ErrandWords.SetOut(0));
    }

    [Fact]
    public void LeftIsOneRowPerReason()
    {
        var left = new[]
        {
            new ErrandLeft(new Need(1, 2, NeedKind.Piece), 2, LeftReason.OverCeiling),
            new ErrandLeft(new Need(2, 1, NeedKind.Piece), 1, LeftReason.Craft),
            new ErrandLeft(new Need(1, 1, NeedKind.Piece), 1, LeftReason.OverCeiling),
        };
        var rows = HousingStudio.UI.Model.ErrandWords.LeftRows(left, id => id == 1 ? "Bookshelf" : "Doormat");
        Assert.Equal(2, rows.Count);
        Assert.Equal("1 to craft", rows[0].Title);
        Assert.Equal("Doormat", rows[0].What);
        Assert.Equal(new[] { (2u, 1) }, rows[0].Items);
        Assert.Equal("3 past the market ceiling", rows[1].Title);
        Assert.Equal("Bookshelf ×3", rows[1].What);
        Assert.StartsWith("Past your market ceiling", rows[1].Why);
    }

    [Fact]
    public void LimitsAndReadysChoiceInOneLine()
    {
        Assert.Equal("Market up to 100,000 · home world only · 2 slots spare", HousingStudio.UI.Model.ErrandWords.Limits(100_000, false, 2));
        Assert.Equal("Nothing from the market · other worlds too · 1 slot spare", HousingStudio.UI.Model.ErrandWords.Limits(0, true, 1));
        Assert.Equal("Then clears out 14 before placing", HousingStudio.UI.Model.ErrandWords.Then(HousingStudio.UI.Model.ReadyMode.ClearOut, 14, null));
        Assert.Equal("Then swaps, keeping what's here as “Winter Lodge”", HousingStudio.UI.Model.ErrandWords.Then(HousingStudio.UI.Model.ReadyMode.Swap, 14, "Winter Lodge"));
        Assert.Null(HousingStudio.UI.Model.ErrandWords.Then(HousingStudio.UI.Model.ReadyMode.Keep, 14, null));
        Assert.Null(HousingStudio.UI.Model.ErrandWords.Then(HousingStudio.UI.Model.ReadyMode.ClearOut, 0, null));
    }

    [Fact]
    public void TheJourneyHasTheTripsStopsThenHomeAndPlace()
    {
        var plan = Three();
        var stops = HousingStudio.UI.Model.ErrandWords.Journey(plan.Trips[0]);
        Assert.Equal(new[] { ErrandLeg.Bell, ErrandLeg.Vendor, ErrandLeg.Market, ErrandLeg.Home, ErrandLeg.Place }, stops);
        Assert.Equal(new[] { ErrandLeg.Home, ErrandLeg.Place }, HousingStudio.UI.Model.ErrandWords.Journey(null));
        var onlyVendor = Plan(10, 50_000, new Need(200, 1, NeedKind.Piece));
        var two = HousingStudio.UI.Model.ErrandWords.Journey(onlyVendor.Trips[0]);
        Assert.Equal(new[] { ErrandLeg.Vendor, ErrandLeg.Home, ErrandLeg.Place }, two);
        Assert.Equal(0, HousingStudio.UI.Model.ErrandWords.JourneyAt(two, ErrandLeg.Vendor));
        Assert.Equal(0, HousingStudio.UI.Model.ErrandWords.JourneyAt(two, ErrandLeg.Bell));
        Assert.Equal(1, HousingStudio.UI.Model.ErrandWords.JourneyAt(two, ErrandLeg.Market));
        Assert.Equal(2, HousingStudio.UI.Model.ErrandWords.JourneyAt(two, ErrandLeg.Place));
    }

    [Fact]
    public void WhereItIsAndWhatItsDoingInWords()
    {
        Assert.Equal(("At the", "market board"), HousingStudio.UI.Model.ErrandWords.Where(ErrandLeg.Market, false, "Lich"));
        Assert.Equal(("On the way to", "Material Supplier"), HousingStudio.UI.Model.ErrandWords.Where(ErrandLeg.Vendor, true, "Material Supplier"));
        Assert.Equal(("On the way", "home"), HousingStudio.UI.Model.ErrandWords.Where(ErrandLeg.Home, true, null));
        Assert.Equal(("Seeing what's", "still missing"), HousingStudio.UI.Model.ErrandWords.Where(ErrandLeg.Place, false, null, replanning: true));
        var item = Three().Trips[0].Stops.Single(s => s.Kind == StopKind.Vendor).Items[0];
        Assert.Equal("Buying Riviera Table ×2", HousingStudio.UI.Model.ErrandWords.Doing(ErrandLeg.Vendor, item, "Riviera Table", "Material Supplier"));
        Assert.Equal("Taking Riviera Table ×2 from Momo", HousingStudio.UI.Model.ErrandWords.Doing(ErrandLeg.Bell, item, "Riviera Table", "Momo"));
        Assert.Null(HousingStudio.UI.Model.ErrandWords.Doing(ErrandLeg.Home, null, string.Empty, null));
        Assert.Equal("1,500 gil each", HousingStudio.UI.Model.ErrandWords.DoingNote(ErrandLeg.Vendor, item, false));
        Assert.Equal("1,500 gil each · the game asked, and the price matched", HousingStudio.UI.Model.ErrandWords.DoingNote(ErrandLeg.Vendor, item, true));
        Assert.Equal(("4,000", "of 61,400"), HousingStudio.UI.Model.ErrandWords.Of(4000, 61_400, true));
        Assert.Equal(("6", "of 12"), HousingStudio.UI.Model.ErrandWords.Of(6, 12, false));
    }

    [Fact]
    public void TheSummaryInOneLine()
    {
        Assert.Equal("12 got on 2 trips for 58,920 gil", HousingStudio.UI.Model.ErrandWords.Summary(12, 2, 58_920));
        Assert.Equal("Nothing got, 0 gil spent", HousingStudio.UI.Model.ErrandWords.Summary(0, 1, 0));
        var got = HousingStudio.UI.Model.ErrandWords.GotList(new Dictionary<uint, int> { [200] = 2, [100] = 1, [300] = 0 }, Names);
        Assert.Equal(new[] { (100u, "Glade Shelf"), (200u, "Riviera Table ×2") }, got);
    }

    [Fact]
    public void ThePlanShownIsTheOneStartedOnlyWhileItStillSaysTheSame()
    {
        var a = Three();
        var b = Three();
        Assert.NotSame(a, b);
        Assert.True(ErrandPlanner.Same(a, b));
        // Fewer free slots, a lower ceiling: another plan, so nothing starts.
        Assert.False(ErrandPlanner.Same(a, Three(free: 2)));
        Assert.False(ErrandPlanner.Same(a, Three(ceiling: 1000)));
        Assert.False(ErrandPlanner.Same(a, ErrandPlan.Empty));
        Assert.True(ErrandPlanner.Same(ErrandPlan.Empty, ErrandPlan.Empty));
    }
}

public class MarketQuestionTests
{
    [Fact]
    public void TheQuestionMustNameTheListingChosen()
    {
        // 3 at 1,000 each: 3,000, 3,150 with tax.
        Assert.True(MarketRule.QuestionMatches("Purchase Wooden Loft ×3 for 3,150 gil?", 1000, 3, 3150));
        Assert.True(MarketRule.QuestionMatches("Purchase for 3.000 gil?", 1000, 3, 3150));
        Assert.True(MarketRule.QuestionMatches("1 000 gil each", 1000, 3, 3150));
        Assert.False(MarketRule.QuestionMatches("Purchase Wooden Loft ×3 for 9,450 gil?", 1000, 3, 3150));
        Assert.False(MarketRule.QuestionMatches("Leave the estate?", 1000, 3, 3150));
        Assert.False(MarketRule.QuestionMatches(string.Empty, 1000, 3, 3150));
        Assert.False(MarketRule.QuestionMatches(null, 1000, 3, 3150));
    }

    [Fact]
    public void TheBoardsQuestionMustNameTheListingsWholeCostAndQuantity()
    {
        // The game's own words (0.8.2's rehearsal): 1 at 6,885, 7,229 with its 344 fee.
        Assert.True(MarketRule.BoardQuestionMatches("Purchase 1 Cieldalaes planter for 7,229 gil (344 fee included)?", 1, 7229, 6885));
        Assert.True(MarketRule.BoardQuestionMatches("Purchase 1 Cieldalaes planter for 7,230 gil (345 fee included)?", 1, 7229, 6885));
        Assert.True(MarketRule.BoardQuestionMatches("Purchase 3 wooden lofts for 3,150 gil (150 fee included)?", 3, 3150, 3000));
        // Another row: 20 at 500 when 1 at 500 (525) was chosen; its fee equals the price chosen.
        Assert.False(MarketRule.BoardQuestionMatches("Purchase 20 stools for 10,500 gil (500 fee included)?", 1, 525, 500));
        // The right total but another quantity.
        Assert.False(MarketRule.BoardQuestionMatches("Purchase 5 wooden lofts for 3,150 gil (150 fee included)?", 3, 3150, 3000));
        Assert.False(MarketRule.BoardQuestionMatches("Leave the estate?", 1, 525, 500));
        Assert.False(MarketRule.BoardQuestionMatches(null, 1, 525, 500));
    }

    [Fact]
    public void AShopsQuestionNamesTheItemInAnyNumber()
    {
        Assert.True(MarketRule.NamesItem("Purchase 1 stage panel for 4,000 gil?", "Stage Panel"));
        Assert.True(MarketRule.NamesItem("Purchase 3 riviera tables for 6,000 gil?", "Riviera Table"));
        Assert.True(MarketRule.NamesItem("Purchase 1 Sil'dihn side table for 2,000 gil?", "Sil'dihn Side Table"));
        Assert.False(MarketRule.NamesItem("Purchase 1 riviera chair for 2,000 gil?", "Riviera Table"));
        Assert.False(MarketRule.NamesItem(null, "Stage Panel"));
        Assert.False(MarketRule.NamesItem("Purchase 1 stage panel for 4,000 gil?", " "));
    }

    [Fact]
    public void NumbersAreReadWithTheirGrouping()
    {
        Assert.Equal(new long[] { 1234567, 3 }, MarketRule.Numbers("1,234,567 gil for 3."));
        Assert.Equal(new long[] { 12, 5 }, MarketRule.Numbers("12, 5"));
    }
}

public class RecordedQuestionTests
{
    // The game's own questions, from a recording of buying by hand.
    [Fact]
    public void TheShopsQuestionsAsTheGameAsks()
    {
        Assert.True(MarketRule.QuestionMatches("Purchase 1 lump of beeswax for 14 gil?", 14, 1, 14));
        Assert.True(MarketRule.QuestionMatches("Purchase 1 riviera table for 2,000 gil?", 2000, 1, 2000));
        Assert.False(MarketRule.QuestionMatches("Purchase 1 riviera table for 2,000 gil?", 1500, 1, 1500));
    }

    [Fact]
    public void TheBoardsQuestionAsTheGameAsks()
    {
        // 21,055 for one, 1,052 fee: 22,107 in all.
        const string q = "Purchase 1 hedge-trimmed flower bed for 22,107 gil (1,052 fee included)?";
        Assert.True(MarketRule.QuestionMatches(q, 21055, 1, MarketRule.Cost(21055, 1, 1052)));
        Assert.False(MarketRule.QuestionMatches(q, 30000, 1, MarketRule.Cost(30000, 1, 1500)));
    }
}

public class AskedCostTests
{
    [Fact]
    public void TheShopsQuestionSaysWhatItAsks()
    {
        Assert.Equal(4000, HousingStudio.Core.MarketRule.AskedCost("Purchase 1 stage panel for 4,000 gil?", 1));
        Assert.Equal(21000, HousingStudio.Core.MarketRule.AskedCost("Purchase 7 white screens for 21,000 gil?", 7));
        Assert.Equal(2000, HousingStudio.Core.MarketRule.AskedCost("2.000 Gil für 1 Stück ausgeben?", 1));
        Assert.Null(HousingStudio.Core.MarketRule.AskedCost("Purchase 2 stage panels for 8,000 gil?", 1));
        Assert.Null(HousingStudio.Core.MarketRule.AskedCost("Leave the estate hall?", 1));
        Assert.Null(HousingStudio.Core.MarketRule.AskedCost(null, 1));
    }

}

public class DoorWayTests
{
    private static readonly DateTime Noon = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static List<Vector3> Line(params (float X, float Z)[] at) => at.Select(a => new Vector3(a.X, 0, a.Z)).ToList();

    [Fact]
    public void ASpotIsKeptOnlyOnceYouveMovedFarEnough()
    {
        var spots = new List<Vector3>();
        DoorWays.Add(spots, new Vector3(0, 0, 0));
        DoorWays.Add(spots, new Vector3(0.3f, 0, 0));
        DoorWays.Add(spots, new Vector3(0.8f, 0, 0));
        Assert.Equal(2, spots.Count);
        Assert.Equal(0.8f, spots[^1].X, 3);
    }

    [Fact]
    public void ARecordingStopsGrowingPastTheMost()
    {
        var spots = new List<Vector3>();
        for (var i = 0; i < DoorWays.MostSpots; i++) Assert.True(DoorWays.Add(spots, new Vector3(i, 0, 0)));
        Assert.False(DoorWays.Add(spots, new Vector3(DoorWays.MostSpots + 5, 0, 0)));
    }

    [Fact]
    public void TheWayIsTheOneEndingAtThisDoorInThisZone()
    {
        var mine = DoorWay.From(340, Line((0, 0), (5, 0), (5, 10)), Noon);
        var neighbour = DoorWay.From(340, Line((0, 0), (-20, 0)), Noon);
        var elsewhere = DoorWay.From(341, Line((0, 0), (5, 0), (5, 10)), Noon);
        var ways = new[] { neighbour, elsewhere, mine };
        Assert.Same(mine, DoorWays.For(ways, 340, new Vector3(5, 3, 11.5f)));
        Assert.Null(DoorWays.For(ways, 340, new Vector3(30, 0, 30)));
        Assert.Null(DoorWays.For(ways, 339, new Vector3(5, 0, 10)));
    }

    [Fact]
    public void RecordingAgainReplacesTheWayToTheSameDoor()
    {
        var ways = new List<DoorWay> { DoorWay.From(340, Line((0, 0), (5, 10)), Noon), DoorWay.From(340, Line((0, 0), (40, 40)), Noon) };
        DoorWays.Keep(ways, DoorWay.From(340, Line((2, 0), (5, 9)), Noon));
        Assert.Equal(2, ways.Count);
        Assert.Contains(ways, w => w.Spots()[0].X == 2);
        Assert.DoesNotContain(ways, w => w.Spots()[0].X == 0 && w.End!.Value.Z == 10);
    }

    [Fact]
    public void FollowingStartsAfterTheNearestSpot()
    {
        var spots = Line((0, 0), (1, 0), (2, 0), (3, 0), (4, 0));
        var (left, off) = DoorWays.From(spots, new Vector3(1.1f, 0, 0.2f));
        Assert.Equal(new[] { 2f, 3f, 4f }, left.Select(s => s.X));
        Assert.True(off < 0.5f);
    }

    [Fact]
    public void FarFromTheWayItGoesToTheNearestSpotFirst()
    {
        var spots = Line((0, 0), (1, 0), (2, 0));
        var (left, off) = DoorWays.From(spots, new Vector3(1, 0, 10));
        Assert.Equal(new[] { 1f, 2f }, left.Select(s => s.X));
        Assert.True(off > 9);
    }

    [Fact]
    public void AtTheEndThereIsNothingLeft()
    {
        var (left, _) = DoorWays.From(Line((0, 0), (1, 0)), new Vector3(1, 0, 0));
        Assert.Empty(left);
    }

    [Fact]
    public void AHomeHasAWayWhenOneGoesIntoItsPlot()
    {
        var ways = new[] { DoorWay.From(340, Line((0, 0), (5, 10)), Noon, plot: 12) };
        Assert.True(DoorWays.HasFor(ways, 340, 12));
        Assert.False(DoorWays.HasFor(ways, 340, 13)); // moved to another plot: asked again
        Assert.False(DoorWays.HasFor(ways, 341, 12));
        Assert.True(DoorWays.HasFor(ways, 340, null)); // an apartment has no door to walk to
        // A way from before plots were kept counts for any plot in its zone, until it's followed in.
        Assert.True(DoorWays.HasFor(new[] { DoorWay.From(340, Line((0, 0), (5, 10)), Noon) }, 340, 13));
    }

    [Fact]
    public void AWayIsMeasuredAlongIt() => Assert.Equal(15f, DoorWays.Length(Line((0, 0), (5, 0), (5, 10))), 3);
}

public class ExchangeTests
{
    private static readonly DateTime Noon = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ExchangeOffer Mgp(uint item, uint cost, int gives = 1, uint npc = 1025000, uint territory = 144) =>
        new(new VendorOffer(item, 0, npc, "Gold Saucer Attendant", 1769800, "Prize Exchange (Furnishings)", territory, "The Gold Saucer", 196,
            null, new System.Numerics.Vector3(1, 2, 3), null), CurrencyItems.Mgp, "MGP", cost, gives);

    private static ShoppingPlan Plan(IEnumerable<Need> needs, ExchangeOffer[] exchanges, long mgp, bool allowMgp = true, VendorOffer[]? gil = null,
        MarketQuote[]? quotes = null) =>
        ShoppingPlanner.Build(needs.ToList(), Array.Empty<RetainerStock>(), id => (gil ?? Array.Empty<VendorOffer>()).Where(o => o.ItemId == id).ToList(),
            (quotes ?? Array.Empty<MarketQuote>()).ToDictionary(q => q.ItemId), _ => null, "mist",
            id => exchanges.Where(e => e.ItemId == id).ToList(), c => c == CurrencyItems.Mgp ? mgp : 0, c => allowMgp && c == CurrencyItems.Mgp);

    [Fact]
    public void AnExchangeCountsWholeExchanges()
    {
        var x = Mgp(500, 3000, gives: 5);
        Assert.Equal(1, x.Exchanges(1));
        Assert.Equal(2, x.Exchanges(6));
        Assert.Equal(6000, x.CostFor(6));
        Assert.Equal(10, x.Affords(7000));
        Assert.Equal(0, x.Affords(2999));
    }

    [Fact]
    public void WhatNoGilVendorSellsComesFromTheMgpVendor()
    {
        var shop = Plan(new[] { new Need(500, 2, NeedKind.Piece) }, new[] { Mgp(500, 30_000) }, mgp: 100_000);
        var line = Assert.Single(shop.Lines);
        Assert.Equal(SourceKind.Exchange, line.Source);
        Assert.Equal(60_000, line.CurrencyCost);
        Assert.Equal(0, shop.TotalGil);
        Assert.Equal(("MGP", 60_000L), Assert.Single(shop.CurrencyCosts));
    }

    [Fact]
    public void OnlyWhatYouHaveIsPlannedTheRestFromTheBoard()
    {
        var quote = new MarketQuote(500, "Light", Noon, new List<MarketListing> { new(9000, 1, "Lich", false), new(9500, 1, "Lich", false) });
        var shop = Plan(new[] { new Need(500, 3, NeedKind.Piece) }, new[] { Mgp(500, 30_000) }, mgp: 65_000, quotes: new[] { quote });
        Assert.Equal(2, shop.Lines.Single(l => l.Source == SourceKind.Exchange).Count);
        Assert.Equal(1, shop.Lines.Single(l => l.Source == SourceKind.Market).Count);
    }

    [Fact]
    public void ACurrencyNotAllowedIsNeverPlanned()
    {
        var shop = Plan(new[] { new Need(500, 1, NeedKind.Piece) }, new[] { Mgp(500, 30_000) }, mgp: 1_000_000, allowMgp: false);
        var line = Assert.Single(shop.Lines);
        Assert.Equal(SourceKind.Unknown, line.Source);
        // …but it says which currency would get it, and the errands leave it out for that reason.
        Assert.Equal("MGP", line.Exchangeable?.CurrencyName);
        var plan = ErrandPlanner.Build(shop, new ErrandOptions { FreeSlots = 10, MarketCeiling = 1000 });
        var left = Assert.Single(plan.Left);
        Assert.Equal(LeftReason.Currency, left.Reason);
        var row = Assert.Single(HousingStudio.UI.Model.ErrandWords.LeftRows(plan.Left, _ => "Thing"));
        Assert.Equal("1 for MGP", row.Title);
    }

    [Fact]
    public void AGilVendorComesFirst()
    {
        var gil = new VendorOffer(500, 2000, 7, "Merchant", 262144, "Shop", 130, "Ul'dah", 1, null, new System.Numerics.Vector3(), null);
        var shop = Plan(new[] { new Need(500, 1, NeedKind.Piece) }, new[] { Mgp(500, 30_000) }, mgp: 1_000_000, gil: new[] { gil });
        Assert.Equal(SourceKind.Vendor, Assert.Single(shop.Lines).Source);
    }

    [Fact]
    public void TwoNeedsShareWhatYouHave()
    {
        var shop = Plan(new[] { new Need(500, 1, NeedKind.Piece), new Need(501, 1, NeedKind.Piece) }, new[] { Mgp(500, 40_000), Mgp(501, 40_000) }, mgp: 50_000);
        Assert.Single(shop.Lines, l => l.Source == SourceKind.Exchange);
    }

    [Fact]
    public void TheErrandsGoToTheExchangeAndSayWhatItCosts()
    {
        var shop = Plan(new[] { new Need(500, 2, NeedKind.Piece) }, new[] { Mgp(500, 30_000) }, mgp: 100_000);
        var plan = ErrandPlanner.Build(shop, new ErrandOptions { FreeSlots = 10, MarketCeiling = 0, StackSize = _ => 1, Reachable = o => o.Located });
        var stop = Assert.Single(Assert.Single(plan.Trips).Stops);
        Assert.Equal(StopKind.Exchange, stop.Kind);
        Assert.Equal(60_000, Assert.Single(stop.Items).CurrencyCost);
        Assert.Equal(0, plan.TotalGil);
        Assert.Equal(("MGP", CurrencyItems.Mgp, 60_000L), Assert.Single(plan.CurrencyCosts));
        Assert.Equal("Set out · 60,000 MGP", HousingStudio.UI.Model.ErrandWords.SetOut(plan.TotalGil, plan.CurrencyCosts));
        Assert.Equal("Set out · up to 1,000 gil + 60,000 MGP", HousingStudio.UI.Model.ErrandWords.SetOut(1000, plan.CurrencyCosts));
    }

    [Fact]
    public void AnExchangeOutOfReachIsLeft()
    {
        var shop = Plan(new[] { new Need(500, 1, NeedKind.Piece) }, new[] { Mgp(500, 30_000) }, mgp: 100_000);
        var plan = ErrandPlanner.Build(shop, new ErrandOptions { FreeSlots = 10, Reachable = _ => false });
        Assert.Equal(LeftReason.NoVendorReachable, Assert.Single(plan.Left).Reason);
    }
}
