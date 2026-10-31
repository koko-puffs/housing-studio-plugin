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
    public void OneTripInOrderRetainersVendorsMarket()
    {
        var shop = Shop(new[] { new Need(300, 1, NeedKind.Piece), new Need(200, 2, NeedKind.Piece), new Need(100, 1, NeedKind.Piece) },
            new[] { Retainer(1, "Momo", (100, 1)) }, new[] { Offer(200, 1500) }, new[] { Quote(300, (5000, 1, "Lich")) });
        var plan = ErrandPlanner.Build(shop, Options());
        var trip = Assert.Single(plan.Trips);
        Assert.Equal(new[] { StopKind.Retainer, StopKind.Vendor, StopKind.Market }, trip.Stops.Select(s => s.Kind));
        Assert.Equal(3000, plan.VendorGil);
        Assert.Equal(5250, plan.MarketGil);
        Assert.Equal(4, plan.Count);
        Assert.Equal("Lich", trip.Stops[2].World);
        Assert.Equal(5000u, trip.Stops[2].Items[0].MaxUnitPrice);
        Assert.Empty(plan.Left);
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
}

public class MarketRuleTests
{
    [Fact]
    public void BuysOnlyAtOrUnderThePlan()
    {
        Assert.Null(MarketRule.WhyNot(1000, 1, 50, 1000, 1, 1050, 10_000, 100_000));
        Assert.Contains("above the planned", MarketRule.WhyNot(1001, 1, 50, 1000, 1, 5000, 10_000, 100_000));
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
        Assert.Equal(10_000_000, HousingStudio.UI.Model.ErrandWords.StepCeiling(10_000_000, 1));
        Assert.Equal(100_000, HousingStudio.UI.Model.ErrandWords.StepCeiling(75_000, 1));
    }

    [Fact]
    public void LeftIsGroupedByWhy()
    {
        var left = new[]
        {
            new ErrandLeft(new Need(1, 2, NeedKind.Piece), 2, LeftReason.OverCeiling),
            new ErrandLeft(new Need(2, 1, NeedKind.Piece), 1, LeftReason.Craft),
            new ErrandLeft(new Need(1, 1, NeedKind.Piece), 1, LeftReason.OverCeiling),
        };
        var rows = HousingStudio.UI.Model.ErrandWords.Left(left, id => id == 1 ? "Bookshelf" : "Doormat");
        Assert.Equal(2, rows.Count);
        Assert.Equal("Doormat", rows[0].What);
        Assert.Equal("3 × Bookshelf", rows[1].What);
    }

    [Fact]
    public void TheConfirmationNamesTheGil()
    {
        var shop = ShoppingPlanner.Build(new[] { new Need(200, 2, NeedKind.Piece) }, Array.Empty<RetainerStock>(),
            id => new[] { new VendorOffer(200, 1500, 1, "NPC", 1, "Shop", 339, "Mist", 72, null, new System.Numerics.Vector3(1, 2, 3), "mist") },
            new Dictionary<uint, MarketQuote>(), _ => null, "mist");
        var plan = ErrandPlanner.Build(shop, new ErrandOptions { FreeSlots = 10, MarketCeiling = 50_000 });
        var words = HousingStudio.UI.Model.ErrandWords.Confirm(plan);
        Assert.Contains("3,000 gil at vendors", words);
        Assert.Contains("1 trip", words);
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
