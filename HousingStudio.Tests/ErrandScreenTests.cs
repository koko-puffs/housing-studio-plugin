using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>The errands' screens' words in 0.9.0's pass: the plan, the running journey, the summary.</summary>
public class ErrandScreenTests
{
    private static VendorOffer Merchant(uint item, uint price, string npc = "Housing Merchant", string place = "The Lavender Beds") =>
        new(item, price, 7, npc, 262151, "Shop", 339, place, 72, null, new System.Numerics.Vector3(1, 2, 3), "lavender");

    private static ErrandItem Item(uint id, int count, int slots = 1, long gil = 0) => new(new Need(id, count, NeedKind.Piece), count, count, slots, gil);

    private static ErrandStop Vendor(uint id, string npc = "Housing Merchant") =>
        new(StopKind.Vendor, $"v:{npc}", new[] { Item(id, 1, 1, 1500) }) { Vendor = Merchant(id, 1500, npc) };

    private static ErrandStop Retainer(uint id) => new(StopKind.Retainer, "r:1", new[] { Item(id, 1) });

    private static ErrandStop Board(uint id, string world) => new(StopKind.Market, $"m:{world}", new[] { Item(id, 1, 1, 5000) }) { World = world };

    [Fact]
    public void AlreadyHereIsFourFigures()
    {
        var cells = ErrandWords.HaveFigures(placed: 0, storeroom: 45, bags: 1, onRetainers: 1);
        Assert.Equal(new[] { (45, "in storeroom"), (1, "in your bags"), (1, "on a retainer"), (0, "placed") }, cells);
        Assert.Equal("on retainers", ErrandWords.HaveFigures(0, 0, 0, 3)[2].Label);
    }

    [Fact]
    public void BagSlotsSayWhatsLeftSpare()
    {
        var trip = new ErrandTrip(1, Enumerable.Range(1, 102).Select(i => Vendor((uint)i)).ToList());
        Assert.Equal("fills 102 bag slots, 2 spare", ErrandWords.TripSlots(trip, freeSlots: 102, keptFree: 2));
        Assert.Equal("fills 102 bag slots, 6 spare", ErrandWords.TripSlots(trip, freeSlots: 106, keptFree: 2));
        Assert.Equal("worked out again after trip 1", ErrandWords.TripSlots(new ErrandTrip(2, new[] { Vendor(1) }), 10, 2));
    }

    [Fact]
    public void TheHintLinksTheExpectedGilToTheCap()
    {
        Assert.Equal("Expected about 2,098,460 by the shopping list; it never spends more than “up to”. You can stop at any time.",
            ErrandWords.SetOutHint(2_098_460, 2_179_172, market: true));
        // Only shops: their prices are fixed.
        Assert.Equal("Nothing is bought above the price shown. You can stop at any time.", ErrandWords.SetOutHint(3_000, 3_000, market: false));
        Assert.Equal("Nothing is bought above the price shown. You can stop at any time.", ErrandWords.SetOutHint(0, 5_000, market: true));
    }

    [Fact]
    public void ExpectedIsTheShopsAndTheListsEstimateForWhatsBoughtOnTheBoard()
    {
        var onBoard = new Need(300, 1, NeedKind.Piece);
        var leftOut = new Need(400, 1, NeedKind.Piece);
        var trip = new ErrandTrip(1, new[]
        {
            Vendor(200),
            new ErrandStop(StopKind.Market, "m:Lich", new[] { new ErrandItem(onBoard, 1, 1, 1, 5_250) }) { World = "Lich" },
        });
        var plan = new ErrandPlan(new List<ErrandTrip> { trip }, new List<ErrandLeft> { new(leftOut, 1, LeftReason.OverCeiling) }, 10, 100_000);
        var shop = new ShoppingPlan(new List<ShoppingLine>
        {
            new(new Need(200, 1, NeedKind.Piece), SourceKind.Vendor, 1) { Gil = 1_500 },
            new(onBoard, SourceKind.Market, 1) { Gil = 5_000 },
            new(leftOut, SourceKind.Market, 1) { Gil = 900_000 },
        });
        Assert.Equal(1_500 + 5_000, ErrandWords.Expected(shop, plan));
    }

    [Fact]
    public void ExpectedCountsOnlyThePartTheCeilingLetsTheTripsBuy()
    {
        var need = new Need(300, 1, NeedKind.Piece);
        var trip = new ErrandTrip(1, new[] { new ErrandStop(StopKind.Market, "m:Lich", new[] { new ErrandItem(need, 6, 6, 1, 6_000) }) { World = "Lich" } });
        var plan = new ErrandPlan(new List<ErrandTrip> { trip }, new List<ErrandLeft>(), 10, 100_000);
        var shop = new ShoppingPlan(new List<ShoppingLine> { new(need, SourceKind.Market, 10) { Gil = 10_000 } });
        Assert.Equal(6_000, ErrandWords.Expected(shop, plan));
    }

    [Fact]
    public void TheJourneyFollowsTheTripsOwnOrder()
    {
        // 0.8.1's order: the shops, then the bell, then the boards.
        var trip = new ErrandTrip(1, new[] { Vendor(1), Vendor(2, "Material Supplier"), Retainer(3), Board(4, "Lich"), Board(5, "Phoenix"), Board(6, "Odin") });
        var steps = ErrandWords.JourneySteps(trip);
        Assert.Equal(new[] { ErrandLeg.Vendor, ErrandLeg.Bell, ErrandLeg.Market, ErrandLeg.Home, ErrandLeg.Place }, steps.Select(s => s.Leg));
        Assert.Equal(new[] { "Shops", "Bell", "Board · 3", "Home", "Place" }, steps.Select(s => s.Label));
        Assert.Equal("Board", steps[2].Short);
        // A bell at home: the retainers first.
        var home = ErrandWords.JourneySteps(new ErrandTrip(1, new[] { Retainer(3), Vendor(1), Board(4, "Lich") }));
        Assert.Equal(new[] { "Bell", "Shops", "Board", "Home", "Place" }, home.Select(s => s.Label));
        Assert.Equal(new[] { "Home", "Place" }, ErrandWords.JourneySteps(null).Select(s => s.Label));
    }

    [Fact]
    public void WhereItIsOnTheJourneyIsTheStopItsAt()
    {
        var trip = new ErrandTrip(1, new[] { Vendor(1), Vendor(2, "Material Supplier"), Retainer(3), Board(4, "Lich") });
        var steps = ErrandWords.JourneySteps(trip);
        // On the way to the shops: Shops is the one in hand, the bell not done (the 0.8.x bar ticked it).
        Assert.Equal(0, ErrandWords.JourneyIndex(steps, ErrandLeg.Vendor, 0));
        Assert.Equal(0, ErrandWords.JourneyIndex(steps, ErrandLeg.Vendor, 1));
        Assert.Equal(1, ErrandWords.JourneyIndex(steps, ErrandLeg.Bell, 2));
        Assert.Equal(2, ErrandWords.JourneyIndex(steps, ErrandLeg.Market, 3));
        Assert.Equal(3, ErrandWords.JourneyIndex(steps, ErrandLeg.Home, -1));
        Assert.Equal(4, ErrandWords.JourneyIndex(steps, ErrandLeg.Place, -1));
    }

    [Fact]
    public void TheCardSaysWhereWithoutMapCoordinates()
    {
        Assert.Equal("Housing Merchant · The Lavender Beds", ErrandWords.Place(Vendor(1), ErrandLeg.Vendor, false, "Phoenix", "Ul'dah"));
        Assert.Equal("Bell in your house", ErrandWords.Place(Retainer(1), ErrandLeg.Bell, true, "Phoenix", "Ul'dah"));
        Assert.Equal("Ul'dah · Summoning bell", ErrandWords.Place(Retainer(1), ErrandLeg.Bell, false, "Phoenix", "Ul'dah"));
        Assert.Equal("Ul'dah · Market board", ErrandWords.Place(Board(1, "Phoenix"), ErrandLeg.Market, false, "Phoenix", "Ul'dah"));
        Assert.Equal("Ul'dah · Market board · Lich", ErrandWords.Place(Board(1, "Lich"), ErrandLeg.Market, false, "Phoenix", "Ul'dah"));
        Assert.Equal("Home", ErrandWords.Place(null, ErrandLeg.Home, false, "Phoenix", "Ul'dah"));
        Assert.Null(ErrandWords.Place(null, ErrandLeg.Place, false, "Phoenix", "Ul'dah"));
        Assert.Equal("Going to Housing Merchant, The Lavender Beds", ErrandWords.NoMapPosition("Going to Housing Merchant, The Lavender Beds (11,8, 8,4)"));
        Assert.Equal("Walk to Material Supplier, Mist", ErrandWords.NoMapPosition("Walk to Material Supplier, Mist (10.9, 11.4)"));
        Assert.Equal("Aethernet to Sapphire Avenue Exchange (Lifestream)", ErrandWords.NoMapPosition("Aethernet to Sapphire Avenue Exchange (Lifestream)"));
    }

    [Fact]
    public void TheCardsSecondLineIsTheStepInWords()
    {
        Assert.Equal("Now: teleport to Mist (Lifestream, aetheryte 72)", ErrandWords.Now("Teleport to Mist (Lifestream, aetheryte 72)"));
        Assert.Equal("Now: walk to Housing Merchant, The Lavender Beds", ErrandWords.Now("Walk to Housing Merchant, The Lavender Beds (11,8, 8,4)"));
        // A name keeps its capital.
        Assert.Equal("Now: Momo's bags open", ErrandWords.Now("Momo's bags open"));
    }

    [Fact]
    public void SpentIsSetAgainstTheCap() => Assert.Equal(("2,031,940", "of up to 2,179,172"), ErrandWords.SpentOf(2_031_940, 2_179_172));

    [Fact]
    public void NotGotIsARowPerThingWithWhy()
    {
        var left = new[]
        {
            new ErrandLeft(new Need(1, 2, NeedKind.Piece), 2, LeftReason.OverCeiling),
            new ErrandLeft(new Need(2, 1, NeedKind.Piece), 1, LeftReason.Craft),
            new ErrandLeft(new Need(1, 1, NeedKind.Dye), 1, LeftReason.OverCeiling),
            new ErrandLeft(new Need(3, 1, NeedKind.Piece), 1, LeftReason.NotListedHere),
        };
        var rows = ErrandWords.NotGot(left, id => id switch { 1 => "Wine Glass", 2 => "Doormat", _ => "Tier 4 Aquarium" });
        // By reason (as the plan screen's rows), then name.
        Assert.Equal(new[] { "Doormat", "Wine Glass ×3", "Tier 4 Aquarium" }, rows.Select(r => r.Name));
        Assert.Equal(new[] { "craft", "board", "board" }, rows.Select(r => r.Kind));
        Assert.Equal(3, rows[1].Count);
        Assert.StartsWith("Past your market ceiling", rows[1].Why);
        Assert.EndsWith(".", rows[1].Why);
        Assert.Equal("1 from shops it doesn't go to",
            ErrandWords.LeftRows(new[] { new ErrandLeft(new Need(9, 1, NeedKind.Piece), 1, LeftReason.NoVendorReachable) }, _ => "Lamp")[0].Title);
    }

    [Fact]
    public void ABoardsOwnReasonIsSaidForItsRow()
    {
        var left = new[] { new ErrandLeft(new Need(3, 1, NeedKind.Piece), 1, LeftReason.NotListedHere) };
        var why = new Dictionary<uint, string> { [3] = "Twintania: sold to someone else first" };
        Assert.Equal("Twintania: sold to someone else first.", ErrandWords.NotGot(left, _ => "Lamp", why)[0].Why);
        Assert.StartsWith("Not listed", ErrandWords.NotGot(left, _ => "Lamp")[0].Why);
    }

    [Fact]
    public void TheSummarysLineSaysWhatStaysOnTheList()
    {
        Assert.Null(ErrandWords.NotGotHint(0));
        Assert.Equal("The 1 not got stays on the shopping list.", ErrandWords.NotGotHint(1));
        Assert.Equal("The 2 not got stay on the shopping list.", ErrandWords.NotGotHint(2));
    }
}
