using System.Numerics;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>The Shopping list's and the Pieces footer's words (0.9.0): totals, sections, shops by place.</summary>
public class ShoppingScreenTests
{
    private static readonly DateTime Noon = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static VendorOffer Offer(uint item, uint price, uint npc = 1, string place = "The Lavender Beds", string? district = "lavender-beds",
        bool located = true, bool unlock = false, string? name = null) =>
        new(item, price, npc, name ?? $"NPC {npc}", 262144 + npc, "Shop", located ? 340u : 0, place, located ? 72u : 0,
            located ? new Vector2(10.9f, 11.4f) : null, located ? new Vector3(1, 2, 3) : null, district, unlock);

    private static ShoppingLine Vendor(uint item, int count, VendorOffer offer, NeedKind kind = NeedKind.Piece) =>
        new(new Need(item, count, kind), SourceKind.Vendor, count) { Vendor = offer, Gil = (long)offer.Price * count };

    private static ShoppingLine Market(uint item, int count, uint each, NeedKind kind = NeedKind.Piece)
    {
        var listing = new MarketListing(each, count, "Lich", false);
        var quote = new MarketQuote(item, "Light", Noon, new[] { listing });
        var gil = (long)each * count;
        return new ShoppingLine(new Need(item, count, kind), SourceKind.Market, count)
            { Market = quote, MarketCost = new MarketCost(gil, count, 0, new[] { listing }), Gil = gil };
    }

    private static ShoppingLine Retainer(uint item, int count) =>
        new(new Need(item, count, NeedKind.Piece), SourceKind.Retainer, count)
            { Retainer = new RetainerStock(7, "Momo", Noon, new Dictionary<uint, int> { [item] = count }, HoldingOrigin.Seen) };

    private static ShoppingLine Exchange(uint item, int count, ExchangeOffer x) =>
        new(new Need(item, count, NeedKind.Piece), SourceKind.Exchange, count) { Exchange = x, CurrencyCost = x.CostFor(count) };

    private static string Name(uint id) => id switch
    {
        1 => "Showcase",
        2 => "Glade Lantern",
        3 => "Oasis Interior Wall",
        4 => "Bathroom Floor Tiles",
        5 => "Ahriman Vase",
        6 => "Snow White Dye",
        _ => $"Item {id}",
    };

    private static ShoppingPlan Sample()
    {
        var merchant = Offer(0, 0, npc: 1, name: "Housing Merchant");
        return new ShoppingPlan(new List<ShoppingLine>
        {
            Retainer(5, 1),
            Vendor(1, 5, merchant with { ItemId = 1, Price = 12000 }),
            Vendor(2, 10, merchant with { ItemId = 2, Price = 3000 }),
            Vendor(3, 1, merchant with { ItemId = 3, Price = 5000 }, NeedKind.Fixture),
            Vendor(9, 2, Offer(9, 100, npc: 2, place: "Ul'dah - Steps of Thal", district: null, name: "Material Supplier")),
            Market(4, 2, 50400),
        });
    }

    [Fact]
    public void TitleCountsEveryThingToGet()
    {
        Assert.Equal("21 to get", ShoppingScreen.Title(Sample()));
        Assert.Equal("Nothing to get", ShoppingScreen.Title(ShoppingPlan.Empty));
    }

    [Fact]
    public void TotalSaysAboutWhenTheBoardIsInIt()
    {
        Assert.Equal("about 196,000 gil in all", ShoppingScreen.Total(Sample()));
        var shops = new ShoppingPlan(new List<ShoppingLine> { Vendor(1, 2, Offer(1, 4000)) });
        Assert.Equal("8,000 gil in all", ShoppingScreen.Total(shops));
        Assert.Null(ShoppingScreen.Total(new ShoppingPlan(new List<ShoppingLine> { Retainer(5, 1) })));
        var mgp = new ExchangeOffer(Offer(8, 0, npc: 3, place: "The Gold Saucer", district: null), CurrencyItems.Mgp, "MGP", 15000);
        var both = new ShoppingPlan(new List<ShoppingLine> { Vendor(1, 2, Offer(1, 4000)), Exchange(8, 3, mgp) });
        Assert.Equal("8,000 gil and 45,000 MGP in all", ShoppingScreen.Total(both));
    }

    [Fact]
    public void PiecesFootAgreesWithTheTitle()
    {
        var shop = new ShoppingPlan(new List<ShoppingLine>
        {
            Vendor(1, 99, Offer(1, 10)),
            Vendor(3, 3, Offer(3, 10), NeedKind.Fixture),
        });
        var (pieces, fixtures, dyes) = ShoppingScreen.Counts(shop);
        Assert.Equal((99, 3, 0), (pieces, fixtures, dyes));
        Assert.Equal("102 to get", ShoppingScreen.Title(shop));
        Assert.Equal("99 pieces and 3 fixtures to get · no dyes to buy", ShoppingScreen.PiecesFoot(pieces, fixtures, dyes));
        Assert.Equal("1 piece to get · 2 dyes to buy", ShoppingScreen.PiecesFoot(1, 0, 2));
        Assert.Equal("3 fixtures to get · no dyes to buy", ShoppingScreen.PiecesFoot(0, 3, 0));
        Assert.Equal("No pieces to get · 1 dye to buy", ShoppingScreen.PiecesFoot(0, 0, 1));
        Assert.Equal("Nothing to get", ShoppingScreen.PiecesFoot(0, 0, 0));
    }

    [Fact]
    public void SectionsSayWhatTheyCount()
    {
        var sections = ShoppingScreen.Sections(Sample(), Name);
        Assert.Equal(new[] { "On your retainers", "From shops", "On the market board" }, sections.Select(s => s.Title));
        Assert.Equal("1", sections[0].Aside);
        Assert.False(sections[0].Priced);
        Assert.Equal("95,200 gil · 18", sections[1].Aside);
        Assert.True(sections[1].Priced);
        Assert.Equal("about 100,800 gil · 2", sections[2].Aside);
        Assert.Empty(sections[0].Places);
        Assert.Empty(sections[2].Places);
    }

    [Fact]
    public void ShopsAreGroupedByThePlaceYouBuyAt()
    {
        var shops = ShoppingScreen.Sections(Sample(), Name)[1];
        Assert.Equal(2, shops.Places.Count);
        var merchant = shops.Places[0];
        Assert.Equal("Housing Merchant", merchant.Vendor.NpcName);
        Assert.Equal(16, merchant.Things);
        // The rows keep the section's order (by name).
        Assert.Equal(new uint[] { 2, 3, 1 }, merchant.Lines.Select(l => l.ItemId));
        Assert.Equal("Material Supplier", shops.Places[1].Vendor.NpcName);
        Assert.Equal("The Lavender Beds · ward 1 · 16 things",
            ShoppingScreen.PlaceLine(merchant.Vendor, merchant.Things, ShoppingScreen.WardFor(merchant.Vendor, "lavender-beds", 1)));
        Assert.Equal("The Lavender Beds · (10.9, 11.4) · 16 things",
            ShoppingScreen.PlaceLine(merchant.Vendor, merchant.Things, ShoppingScreen.WardFor(merchant.Vendor, "mist", 1)));
        Assert.Null(ShoppingScreen.WardFor(shops.Places[1].Vendor, "lavender-beds", 1));
        var nowhere = Offer(1, 10, located: false, place: string.Empty, district: null);
        Assert.Equal("Somewhere not known · 1 thing", ShoppingScreen.PlaceLine(nowhere, 1, null));
    }

    [Fact]
    public void PlacesWithASpotComeFirst()
    {
        var lines = new[]
        {
            Vendor(1, 1, Offer(1, 10, npc: 5, located: false, place: "Aaa")),
            Vendor(2, 1, Offer(2, 10, npc: 6, place: "Zzz")),
        };
        var places = ShoppingScreen.Places(lines);
        Assert.Equal(new uint[] { 6, 5 }, places.Select(p => p.Vendor.NpcId));
    }

    [Fact]
    public void CurrencyVendorsAreGroupedByTheirNpcToo()
    {
        var saucer = Offer(0, 0, npc: 3, place: "The Gold Saucer", district: null, name: "Gold Saucer Attendant");
        var shop = new ShoppingPlan(new List<ShoppingLine>
        {
            Exchange(10, 1, new ExchangeOffer(saucer with { ItemId = 10 }, CurrencyItems.Mgp, "MGP", 15000)),
            Exchange(11, 3, new ExchangeOffer(saucer with { ItemId = 11 }, CurrencyItems.Mgp, "MGP", 300, 3)),
        });
        var section = Assert.Single(ShoppingScreen.Sections(shop, Name));
        Assert.Equal("15,300 MGP · 4", section.Aside);
        var place = Assert.Single(section.Places);
        Assert.Equal(4, place.Things);
        Assert.Equal("300 MGP for 3", ShoppingScreen.PlaceRowNote(place.Lines.Single(l => l.ItemId == 11)));
        Assert.Equal("15,000 MGP", ShoppingScreen.RowPrice(place.Lines.Single(l => l.ItemId == 10)));
    }

    [Fact]
    public void RowsSayGilOneWay()
    {
        var shop = Sample();
        Assert.Equal("free", ShoppingScreen.RowPrice(shop.Lines[0]));
        Assert.Equal("30,000", ShoppingScreen.RowPrice(shop.Lines[2]));
        Assert.Equal("≈ 100,800", ShoppingScreen.RowPrice(shop.Lines[5]));
        Assert.Equal("about 50,400 each · Light", ShoppingScreen.MarketNote(shop.Lines[5]));
        Assert.Equal("Fixture · about 10 each · Light", ShoppingScreen.MarketNote(Market(3, 1, 10, NeedKind.Fixture)));
    }

    [Fact]
    public void RowsUnderAPlaceOnlySayWhatTheirPlaceDoesnt()
    {
        var shop = Sample();
        Assert.Null(ShoppingScreen.PlaceRowNote(shop.Lines[1]));
        Assert.Equal("Fixture", ShoppingScreen.PlaceRowNote(shop.Lines[3]));
        Assert.Equal("After a quest", ShoppingScreen.PlaceRowNote(Vendor(1, 1, Offer(1, 10, unlock: true))));
        Assert.Equal("Dye · after a quest", ShoppingScreen.PlaceRowNote(Vendor(6, 1, Offer(6, 10, unlock: true), NeedKind.Dye)));
    }
}
