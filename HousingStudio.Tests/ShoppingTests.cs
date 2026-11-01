using System.Numerics;
using System.Text;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class ShoppingPlannerTests
{
    private static readonly DateTime Noon = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static VendorOffer Offer(uint item, uint price, uint npc = 1, string? district = null, bool located = true, bool unlock = false) =>
        new(item, price, npc, $"NPC {npc}", 262144 + npc, "Shop", located ? 339u : 0, located ? "Mist" : string.Empty, located ? 72u : 0,
            located ? new Vector2(10.9f, 11.4f) : null, located ? new Vector3(1, 2, 3) : null, district, unlock);

    private static RetainerStock Retainer(ulong id, string name, DateTime? seen, params (uint Item, int Count)[] items) =>
        new(id, name, seen, items.ToDictionary(i => i.Item, i => i.Count), HoldingOrigin.Seen);

    private static MarketQuote Quote(uint item, params (uint Price, int Qty)[] listings) =>
        new(item, "Light", Noon, listings.Select(l => new MarketListing(l.Price, l.Qty, "Lich", false)).ToList());

    private static Func<uint, IReadOnlyList<VendorOffer>> Offers(params VendorOffer[] offers) =>
        id => offers.Where(o => o.ItemId == id).ToList();

    private static readonly Func<uint, CraftOption?> NoCraft = _ => null;

    [Fact]
    public void TakesFromRetainersFirstThenAVendor()
    {
        var needs = new[] { new Need(100, 5, NeedKind.Piece) };
        var retainers = new[] { Retainer(1, "Momo", Noon, (100, 2)) };
        var shop = ShoppingPlanner.Build(needs, retainers, Offers(Offer(100, 1500)), new Dictionary<uint, MarketQuote>(), NoCraft);
        Assert.Equal(2, shop.Lines.Count);
        var r = shop.Lines[0];
        Assert.Equal(SourceKind.Retainer, r.Source);
        Assert.Equal(2, r.Count);
        Assert.Equal("Momo", r.Retainer!.Name);
        var v = shop.Lines[1];
        Assert.Equal(SourceKind.Vendor, v.Source);
        Assert.Equal(3, v.Count);
        Assert.Equal(4500, v.Gil);
        Assert.Equal(4500, shop.VendorGil);
        Assert.Equal(0, shop.MarketGil);
    }

    [Fact]
    public void ARetainerHoldingEnoughCoversAllOfIt()
    {
        var needs = new[] { new Need(100, 2, NeedKind.Piece) };
        var retainers = new[] { Retainer(1, "Momo", Noon, (100, 1)), Retainer(2, "Kiki", Noon.AddDays(-1), (100, 3)) };
        var shop = ShoppingPlanner.Build(needs, retainers, Offers(Offer(100, 1500)), new Dictionary<uint, MarketQuote>(), NoCraft);
        var line = Assert.Single(shop.Lines);
        Assert.Equal("Kiki", line.Retainer!.Name);
        Assert.Equal(0, shop.TotalGil);
    }

    [Fact]
    public void RetainerCountsAreSharedBetweenNeedsOfOneItem()
    {
        var needs = new[] { new Need(100, 2, NeedKind.Piece), new Need(100, 2, NeedKind.Piece) };
        var retainers = new[] { Retainer(1, "Momo", Noon, (100, 3)) };
        var shop = ShoppingPlanner.Build(needs, retainers, Offers(), new Dictionary<uint, MarketQuote>(), NoCraft);
        Assert.Equal(3, shop.Count(SourceKind.Retainer));
        Assert.Equal(1, shop.Count(SourceKind.Unknown));
    }

    [Fact]
    public void FallsToTheMarketThenCraftThenUnknown()
    {
        var needs = new[]
        {
            new Need(200, 3, NeedKind.Piece),
            new Need(300, 1, NeedKind.Piece),
            new Need(400, 1, NeedKind.Piece),
        };
        var quotes = new Dictionary<uint, MarketQuote> { [200] = Quote(200, (1000, 1), (2000, 5)) };
        var craft = new Func<uint, CraftOption?>(id => id == 300 ? new CraftOption(300, 9, "Carpenter", 50) : null);
        var shop = ShoppingPlanner.Build(needs, Array.Empty<RetainerStock>(), Offers(), quotes, craft);
        var m = Assert.Single(shop.Of(SourceKind.Market));
        Assert.Equal(3, m.Count);
        Assert.True(m.Estimated);
        Assert.Equal(shop.MarketGil, m.Gil);
        Assert.Equal(SourceKind.Craft, Assert.Single(shop.Lines, l => l.ItemId == 300).Source);
        Assert.Equal(SourceKind.Unknown, Assert.Single(shop.Lines, l => l.ItemId == 400).Source);
    }

    [Fact]
    public void TooFewListedLeavesTheRestToCraft()
    {
        var needs = new[] { new Need(200, 5, NeedKind.Piece) };
        var quotes = new Dictionary<uint, MarketQuote> { [200] = Quote(200, (1000, 2)) };
        var shop = ShoppingPlanner.Build(needs, Array.Empty<RetainerStock>(), Offers(), quotes, _ => new CraftOption(200, 1, "Carpenter", 10));
        Assert.Equal(2, shop.Count(SourceKind.Market));
        Assert.Equal(3, shop.Count(SourceKind.Craft));
        Assert.Equal(2000, shop.MarketGil);
    }

    [Fact]
    public void AVendorBeatsTheMarket()
    {
        var needs = new[] { new Need(100, 1, NeedKind.Dye, 5) };
        var quotes = new Dictionary<uint, MarketQuote> { [100] = Quote(100, (1, 99)) };
        var shop = ShoppingPlanner.Build(needs, Array.Empty<RetainerStock>(), Offers(Offer(100, 216)), quotes, NoCraft);
        Assert.Equal(SourceKind.Vendor, Assert.Single(shop.Lines).Source);
    }

    [Fact]
    public void NeedsOfAPlanCountMissingPiecesAndDyes()
    {
        var plan = new Plan();
        plan.Entries.Add(new PlanEntry { Target = new Target(0, 100, Vector3.Zero, 0, 0, Floor.Ground, false), State = PieceState.Missing });
        plan.Entries.Add(new PlanEntry { Target = new Target(1, 100, Vector3.Zero, 0, 3, Floor.Ground, false), State = PieceState.Missing });
        plan.Entries.Add(new PlanEntry { Target = new Target(2, 101, Vector3.Zero, 0, 0, Floor.Ground, false), State = PieceState.InPlace });
        var needs = ShoppingPlanner.NeedsOf(plan, new[] { ((byte)3, 1), ((byte)9, 2) }, s => s == 3 ? 5729u : 0);
        Assert.Equal(2, needs.Count);
        Assert.Equal(new Need(100, 2, NeedKind.Piece), needs[0]);
        Assert.Equal(new Need(5729, 1, NeedKind.Dye, 3), needs[1]);
    }

    [Fact]
    public void ToPriceAsksOnlyAboutWhatNothingElseCovers()
    {
        var needs = new[] { new Need(100, 2, NeedKind.Piece), new Need(200, 1, NeedKind.Piece), new Need(300, 1, NeedKind.Piece) };
        var retainers = new[] { Retainer(1, "Momo", Noon, (100, 2)) };
        Assert.Equal(new uint[] { 300 }, ShoppingPlanner.ToPrice(needs, retainers, Offers(Offer(200, 10))));
        // Vendors' things too, for the board when the vendor is out of reach (0.9.4).
        Assert.Equal(new uint[] { 200, 300 }, ShoppingPlanner.ToPrice(needs, retainers, Offers(Offer(200, 10)), evenSold: true));
    }

    [Fact]
    public void MergePrefersWhatWasSeenAndNamesAllagansRetainers()
    {
        var seen = new[] { Retainer(1, "Momo", Noon, (100, 1)) };
        var allagan = new[]
        {
            new RetainerStock(1, "Retainer", null, new Dictionary<uint, int> { [100] = 9 }, HoldingOrigin.AllaganTools),
            new RetainerStock(2, "Retainer", null, new Dictionary<uint, int> { [200] = 1 }, HoldingOrigin.AllaganTools),
            new RetainerStock(3, "Retainer", null, new Dictionary<uint, int>(), HoldingOrigin.AllaganTools),
        };
        var merged = RetainerStocks.Merge(seen, allagan, new Dictionary<ulong, string> { [2] = "Kiki" });
        Assert.Equal(2, merged.Count);
        Assert.Equal(1, merged.Single(r => r.RetainerId == 1).Count(100));
        Assert.Equal("Kiki", merged.Single(r => r.RetainerId == 2).Name);
    }

    [Fact]
    public void CountsFoldHqAndCollectablesIntoTheirItem()
    {
        var counts = RetainerStocks.Count(new[] { (100u, 2L), (1_000_100u, 3L), (500_100u, 1L), (0u, 5L), (7u, 0L) });
        Assert.Equal(6, counts[100]);
        Assert.Single(counts);
    }
}

public class VendorTests
{
    private static VendorOffer Offer(uint price, uint npc, string? district, bool located = true, bool unlock = false) =>
        new(1, price, npc, "NPC", 1, "Shop", located ? 1u : 0, "Place", 1, null, located ? Vector3.Zero : null, district, unlock);

    [Fact]
    public void HousingVendorsComeFirstYourDistrictBeforeOthers()
    {
        var offers = new[] { Offer(10, 1, null), Offer(100, 2, "goblet"), Offer(100, 3, "mist") };
        Assert.Equal(2u, VendorChoice.Best(offers)!.NpcId);
        Assert.Equal(3u, VendorChoice.Best(offers, "mist")!.NpcId);
    }

    [Fact]
    public void AVendorWhoseSpotIsKnownBeatsOneWhoseIsnt()
    {
        var offers = new[] { Offer(10, 1, null, located: false), Offer(50, 2, null) };
        Assert.Equal(2u, VendorChoice.Best(offers)!.NpcId);
    }

    [Fact]
    public void NoUnlockBeforeCheaper()
    {
        var offers = new[] { Offer(10, 1, null, unlock: true), Offer(50, 2, null) };
        Assert.Equal(2u, VendorChoice.Best(offers)!.NpcId);
        Assert.Null(VendorChoice.Best(Array.Empty<VendorOffer>()));
    }

    [Fact]
    public void MapCoordinatesAsTheGameShowsThem()
    {
        // The middle of a 1× map is 21.5; a 2× map's spans half the coordinates.
        Assert.Equal(21.5f, MapCoords.FromWorld(0, 0, 100), 3);
        Assert.Equal(11.25f, MapCoords.FromWorld(0, 0, 200), 3);
        Assert.Equal(1f + 41f * (1024f + 100f) / 2048f, MapCoords.FromWorld(100, 0, 100), 3);
        var v = MapCoords.FromWorld(new Vector3(100, 5, -100), 0, 0, 100);
        Assert.Equal(23.5f, v.X, 3);
        Assert.Equal(19.5f, v.Y, 3);
    }

    [Fact]
    public void TellsTheResidentialWards()
    {
        Assert.Equal("mist", Residential.DistrictOf("s1h1"));
        Assert.Equal("lavender-beds", Residential.DistrictOf("f1h1"));
        Assert.Equal("goblet", Residential.DistrictOf("w1h1"));
        Assert.Equal("shirogane", Residential.DistrictOf("e1h1"));
        Assert.Equal("empyreum", Residential.DistrictOf("R1H1"));
        Assert.Null(Residential.DistrictOf("s1i2"));
        Assert.Null(Residential.DistrictOf("s1t1"));
        Assert.Equal(NpcDataKind.GilShop, Residential.KindOf(262150));
        Assert.Equal(NpcDataKind.TopicSelect, Residential.KindOf(3276801));
        Assert.Equal(NpcDataKind.PreHandler, Residential.KindOf(3538950));
        Assert.Equal(NpcDataKind.CustomTalk, Residential.KindOf(721000));
        Assert.Equal(NpcDataKind.SpecialShop, Residential.KindOf(1769472)); // a SpecialShop (0x1B…): a currency vendor (0.7.14)
        Assert.Equal(NpcDataKind.Other, Residential.KindOf(0x0025_0000));
    }

    [Fact]
    public void SaysWhereTheVendorIs()
    {
        var o = new VendorOffer(1, 100, 1, "Material Supplier", 1, "Shop", 339, "Mist", 72, new Vector2(10.94f, 11.36f), Vector3.Zero, "mist");
        Assert.Equal("Material Supplier, Mist (10.9, 11.4)", o.Where());
    }
}

public class MarketTests
{
    private static readonly DateTime Noon = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static MarketQuote Quote(params (uint Price, int Qty)[] listings) =>
        new(1, "Light", Noon, listings.Select(l => new MarketListing(l.Price, l.Qty, "Lich", false)).ToList());

    [Fact]
    public void BuysTheCheapestStacksWhole()
    {
        var cost = Quote((100, 2), (150, 5), (90, 1)).CostFor(3);
        Assert.Equal(90 + 200, cost.Gil);
        Assert.Equal(3, cost.Covered);
        Assert.True(cost.Enough);
    }

    [Fact]
    public void ASmallStackThatsEnoughBeatsACheaperBigOne()
    {
        // One wanted: a stack of 99 at 10 each costs 990; one at 200 is cheaper whole.
        var cost = Quote((10, 99), (200, 1)).CostFor(1);
        Assert.Equal(200, cost.Gil);
        Assert.Equal(1, cost.Covered);
    }

    [Fact]
    public void SaysHowManyAreShort()
    {
        var cost = Quote((100, 2)).CostFor(5);
        Assert.Equal(200, cost.Gil);
        Assert.Equal(3, cost.Short);
        Assert.False(cost.Enough);
        Assert.Equal(0, Quote().CostFor(1).Covered);
        Assert.Equal(0, Quote((1, 1)).CostFor(0).Gil);
    }

    [Fact]
    public void CountsTheTax()
    {
        var q = new MarketQuote(1, "Light", Noon, new[] { new MarketListing(100, 2, "Lich", false, 10) });
        Assert.Equal(210, q.CostFor(2).Gil);
    }

    [Fact]
    public void ReadsSeveralItems()
    {
        const string json = """
            {"itemIDs":[5333,5334],"items":{
              "5333":{"itemID":5333,"unitsForSale":7,"listings":[{"pricePerUnit":500,"quantity":2,"worldName":"Lich","hq":false,"tax":50},{"pricePerUnit":300,"quantity":1,"worldName":"Odin","hq":true}]},
              "5334":{"itemID":5334,"listings":[]}},
             "unresolvedItems":[9]}
            """;
        var quotes = Universalis.Parse(json, "Light", Noon);
        Assert.Equal(2, quotes.Count);
        var q = quotes[5333];
        Assert.Equal(300u, q.Cheapest);
        Assert.Equal("Odin", q.Listings[0].World);
        Assert.True(q.Listings[0].Hq);
        Assert.Equal(50u, q.Listings[1].Tax);
        Assert.Equal(7, q.UnitsForSale);
        Assert.Empty(quotes[5334].Listings);
        Assert.Null(quotes[5334].Cheapest);
    }

    [Fact]
    public void ReadsOneItem()
    {
        var quotes = Universalis.Parse("""{"itemID":5333,"listings":[{"pricePerUnit":500.0,"quantity":2,"worldName":"Lich"}]}""", "Light", Noon);
        Assert.Equal(500u, Assert.Single(quotes).Value.Listings[0].PricePerUnit);
    }

    [Fact]
    public void IgnoresWhatIsntAnAnswer()
    {
        Assert.Empty(Universalis.Parse("not json", "Light", Noon));
        Assert.Empty(Universalis.Parse("[]", "Light", Noon));
        Assert.Empty(Universalis.Parse("""{"items":{"x":{}}}""", "Light", Noon));
    }

    [Fact]
    public void AsksInBatchesOfAHundred()
    {
        var batches = Universalis.Batches(Enumerable.Range(1, 250).Select(i => (uint)i).Concat(new uint[] { 5, 5 })).ToList();
        Assert.Equal(new[] { 100, 100, 50 }, batches.Select(b => b.Count));
        Assert.Equal("https://universalis.app/api/v2/Light/2,10?listings=20&entries=0", Universalis.Url("Light", new uint[] { 10, 2, 2 }));
    }
}

public class TeamcraftTests
{
    [Fact]
    public void ImportLinkIsTheBase64OfTheList()
    {
        var url = Teamcraft.ImportUrl(new[] { (5334u, 1), (5333u, 2), (5333u, 1), (0u, 4), (7u, 0) });
        Assert.NotNull(url);
        Assert.StartsWith("https://ffxivteamcraft.com/import/", url);
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(url![Teamcraft.ImportBase.Length..]));
        Assert.Equal("5333,null,3;5334,null,1", text);
        Assert.Null(Teamcraft.ImportUrl(Array.Empty<(uint, int)>()));
    }
}

public class ShoppingTextTests
{
    [Fact]
    public void ListsEachSourceWithItsGil()
    {
        var plan = new Plan();
        plan.Entries.Add(new PlanEntry { Target = new Target(0, 100, Vector3.Zero, 0, 3, Floor.Ground, false), State = PieceState.Missing });
        plan.Entries.Add(new PlanEntry { Target = new Target(1, 100, Vector3.Zero, 0, 0, Floor.Ground, false), State = PieceState.Missing });
        plan.Entries.Add(new PlanEntry { Target = new Target(2, 200, Vector3.Zero, 0, 0, Floor.Ground, false), State = PieceState.Missing });
        var momo = new RetainerStock(1, "Momo", new DateTime(2026, 10, 31), new Dictionary<uint, int> { [200] = 1 }, HoldingOrigin.Seen);
        var offer = new VendorOffer(100, 1500, 1, "Material Supplier", 1, "Shop", 339, "Mist", 72, new Vector2(10.9f, 11.4f), Vector3.Zero, "mist");
        var shop = new ShoppingPlan(new List<ShoppingLine>
        {
            new(new Need(200, 1, NeedKind.Piece), SourceKind.Retainer, 1) { Retainer = momo },
            new(new Need(100, 2, NeedKind.Piece), SourceKind.Vendor, 2) { Vendor = offer, Gil = 3000 },
        });
        var text = ShoppingList.WithSources(plan, shop, id => id == 100 ? "Oak Table" : "Stool", s => s == 3 ? "Snow White" : "Undyed", _ => "today 05.24", "My house");
        Assert.Contains("My house: shopping list", text);
        Assert.Contains("1 × Stool: on Momo (seen today 05.24)", text);
        Assert.Contains("Buy from vendors (3,000 gil)", text);
        Assert.Contains("2 × Oak Table (Snow White ×1, undyed ×1): 1,500 gil each, 3,000 gil, from Material Supplier, Mist (10.9, 11.4)", text);
        Assert.Contains("In all: 3,000 gil at vendors", text);
    }

    [Fact]
    public void APiecesDyesAreSaidOnceWhenItComesFromTwoPlaces()
    {
        var plan = new Plan();
        for (var i = 0; i < 3; i++)
            plan.Entries.Add(new PlanEntry { Target = new Target(i, 100, Vector3.Zero, 0, 3, Floor.Ground, false), State = PieceState.Missing });
        var momo = new RetainerStock(1, "Momo", null, new Dictionary<uint, int> { [100] = 1 }, HoldingOrigin.AllaganTools);
        var need = new Need(100, 3, NeedKind.Piece);
        var shop = new ShoppingPlan(new List<ShoppingLine>
        {
            new(need, SourceKind.Retainer, 1) { Retainer = momo },
            new(need, SourceKind.Unknown, 2),
        });
        var text = ShoppingList.WithSources(plan, shop, _ => "Oak Table", _ => "Snow White", _ => null);
        Assert.Contains("1 × Oak Table (Snow White ×3): on Momo", text);
        Assert.Contains("2 × Oak Table", text);
        Assert.Equal(1, text.Split("Snow White ×3").Length - 1);
    }

    [Fact]
    public void TotalsSayWhatsEstimated()
    {
        var shop = new ShoppingPlan(new List<ShoppingLine>
        {
            new(new Need(1, 1, NeedKind.Piece), SourceKind.Vendor, 1) { Gil = 12000 },
            new(new Need(2, 1, NeedKind.Piece), SourceKind.Market, 1) { Gil = 86400 },
        });
        Assert.Equal("about 98,400 gil (12,000 gil at vendors, about 86,400 gil on the market board)", ShoppingList.TotalWords(shop));
    }

}
