using System.Numerics;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

public class ItemLimitTests
{
    [Theory]
    [InlineData("Apartment", 150)]
    [InlineData("Small", 300)]
    [InlineData("Medium", 450)]
    [InlineData("Large", 600)]
    public void InsideAsTheSiteHasIt(string size, int limit) => Assert.Equal(limit, ItemLimits.Inside(size));

    [Fact]
    public void GardensBySizeAndNoneForAnApartment()
    {
        Assert.Equal(40, ItemLimits.Garden("Small"));
        Assert.Equal(60, ItemLimits.Garden("Medium"));
        Assert.Equal(80, ItemLimits.Garden("Large"));
        Assert.Null(ItemLimits.Garden("Apartment"));
        Assert.Null(ItemLimits.Of(Side.Interior, null));
    }

    [Fact]
    public void ADesignTooBigForTheHouse()
    {
        var c = ItemLimits.Check(Side.Interior, "Medium", design: 480, placedNow: 0, others: 0, toPlace: 480)!.Value;
        Assert.True(c.DesignOver);
        Assert.Equal(30, c.Over);
        var words = CareWords.Limit(c, Side.Interior, "Medium")!;
        Assert.StartsWith("This design has 480 pieces inside; a house (medium) holds 450.", words);
        Assert.Contains("refuses the last 30 pieces", words);
    }

    [Fact]
    public void ADesignThatFitsButNotWithWhatsThere()
    {
        var c = ItemLimits.Check(Side.Interior, "Small", design: 280, placedNow: 100, others: 60, toPlace: 240)!.Value;
        Assert.False(c.DesignOver);
        Assert.Equal(340, c.After);
        Assert.Equal(40, c.Over);
        var words = CareWords.Limit(c, Side.Interior, "Small")!;
        Assert.StartsWith("With the 60 pieces here that aren't in it, that makes 340; a cottage (small) holds 300 inside.", words);
        Assert.Contains("Put some pieces away first", words);
    }

    [Fact]
    public void FitsSaysNothing()
    {
        var c = ItemLimits.Check(Side.Garden, "Large", 50, 10, 0, 40)!.Value;
        Assert.Equal(0, c.Over);
        Assert.Null(CareWords.Limit(c, Side.Garden, "Large"));
        Assert.Equal("10 of 80 pieces placed in the garden", CareWords.HowFull(10, 80, Side.Garden));
        Assert.Equal("1 piece placed inside", CareWords.HowFull(1, null, Side.Interior));
    }
}

public class ClashTests
{
    private static Target T(int key, uint item, float x, float y, float z) => new(key, item, new Vector3(x, y, z), 0, 0, Floor.Ground, false);

    [Fact]
    public void StackedCopiesOfOneItem()
    {
        var targets = new[] { T(0, 10, 1, 0, 1), T(1, 10, 1.005f, 0, 1), T(2, 10, 1, 0, 1), T(3, 11, 1, 0, 1), T(4, 10, 3, 0, 3) };
        var clashes = Clashes.Find(targets, Side.Interior, null, null);
        var stacked = Assert.Single(clashes);
        Assert.Equal(ClashKind.Stacked, stacked.Kind);
        Assert.Equal(new[] { 1, 2 }, stacked.Pieces.Select(p => p.Key));
    }

    [Fact]
    public void HeightsByFloorAndHouse()
    {
        var targets = new[]
        {
            T(0, 1, 0, 0, 0),          // on the ground floor
            T(1, 2, 0, -7.5f, 0),      // below the basement's floor
            T(2, 3, 0, -0.3f, 0),      // above the basement's ceiling (in the ground floor's floor)
            T(3, 4, 0, 6.8f, 0),       // above the ground floor's ceiling
            T(4, 5, 0, 13.6f, 0),      // above the upper floor's ceiling
            T(5, 6, 0, 13.4f, 0),      // just under it
        };
        var clashes = Clashes.Find(targets, Side.Interior, "Medium", "mist");
        Assert.Equal(new[] { 2 }, Keys(clashes, ClashKind.UnderFloor));
        Assert.Equal(new[] { 3, 4, 5 }, Keys(clashes, ClashKind.AboveCeiling));

        // Shirogane's ceilings are lower: 13.4 is above its upper floor's (13.2).
        var shirogane = Clashes.Find(targets, Side.Interior, "Medium", "shirogane");
        Assert.Contains(6, Keys(shirogane, ClashKind.AboveCeiling));
    }

    [Fact]
    public void ACottageHasNoUpperFloorAndAnApartmentNoBasement()
    {
        var cottage = Clashes.Find(new[] { T(0, 1, 0, 8, 0) }, Side.Interior, "Small", null);
        Assert.Equal(new[] { 1 }, Keys(cottage, ClashKind.AboveCeiling));
        var apartment = Clashes.Find(new[] { T(0, 1, 0, -1, 0), T(1, 2, 0, 3, 0) }, Side.Interior, "Apartment", null);
        Assert.Equal(new[] { 1 }, Keys(apartment, ClashKind.UnderFloor));
    }

    [Fact]
    public void OutsideTheWallsAndTheOtherSide()
    {
        var targets = new[] { T(0, 1, 30, 0, 0), T(1, 2, 0, 0, 0) };
        var clashes = Clashes.Find(targets, Side.Interior, "Medium", null, item => item != 2);
        Assert.Equal(new[] { 1 }, Keys(clashes, ClashKind.OutsideWalls));
        Assert.Equal(new[] { 2 }, Keys(clashes, ClashKind.WrongSide));
        Assert.Equal(2, Clashes.PieceCount(clashes));
    }

    [Fact]
    public void GardensOnlyStackAndSide()
    {
        var clashes = Clashes.Find(new[] { T(0, 1, 0, -50, 0), T(1, 1, 0, -50, 0) }, Side.Garden, "Small", null);
        Assert.Equal(ClashKind.Stacked, Assert.Single(clashes).Kind);
    }

    [Fact]
    public void Wording()
    {
        Assert.Equal("is below the floor", CareWords.Reason(ClashKind.UnderFloor, 1));
        Assert.Equal("Place anywhere is off, so the game will likely refuse 3 pieces:", CareWords.ClashLead(3, false, false));
        Assert.Contains("placed anyway", CareWords.ClashLead(1, true, false));
        Assert.Equal("Oak Table ×2 and Bed", CareWords.Names(new uint[] { 1, 2, 1 }, i => i == 1 ? "Oak Table" : "Bed"));
    }

    private static int[] Keys(IEnumerable<Clash> clashes, ClashKind kind) =>
        clashes.Where(c => c.Kind == kind).SelectMany(c => c.Pieces).Select(p => (int)p.ItemId).OrderBy(i => i).ToArray();
}

public class DriftTests
{
    private static Target T(int key, uint item, float x, byte stain = 0) => new(key, item, new Vector3(x, 0, 0), 0, stain, Floor.Ground, false);
    private static PlacedPiece P(int key, uint item, float x, byte stain = 0) => new(key, item, new Vector3(x, 0, 0), 0, stain);

    [Fact]
    public void MovedAddedGoneAndRedyed()
    {
        var was = new[] { T(0, 1, 0), T(1, 2, 5), T(2, 3, 8), T(3, 4, 10, 6) };
        var now = new[] { P(10, 1, 0.001f), P(11, 2, 7), P(12, 5, 3), P(13, 4, 10, 1) };
        var r = Drift.Compare(was, now);
        Assert.Equal(1, r.Same);
        Assert.Equal(2u, Assert.Single(r.Moved).Was.ItemId);
        Assert.Equal(5u, Assert.Single(r.Added).ItemId);
        Assert.Equal(3u, Assert.Single(r.Gone).ItemId);
        Assert.Equal(4u, Assert.Single(r.Redyed).Was.ItemId);
        Assert.True(r.CanPutBack);
        Assert.Equal("1 moved, 1 added, 1 taken away and 1 in another dye", CareWords.Drift(r));
    }

    [Fact]
    public void RoundingIsNoChange()
    {
        var r = Drift.Compare(new[] { T(0, 1, 2) }, new[] { P(7, 1, 2.004f) });
        Assert.True(r.Unchanged);
        Assert.Equal("Nothing has changed", CareWords.Drift(r));
        Assert.False(Drift.Compare(new[] { T(0, 1, 2) }, new[] { P(7, 1, 2), P(8, 1, 3) }).CanPutBack);
    }
}

public class RunProgressTests
{
    private static RunProgress Sample() => new(@"C:\designs\cosy.build", "Cosy", "interior-w73-t341-0000000000000ABC", Side.Interior,
        new List<(int, uint)> { (0, 100), (1, 200), (5, 100) }, 37, 120, RunPhase.Place, new DateTime(2026, 10, 30, 21, 14, 0, DateTimeKind.Utc));

    [Fact]
    public void RoundTrips()
    {
        var back = RunProgress.Parse(Sample().ToJson())!;
        Assert.Equal(@"C:\designs\cosy.build", back.Design);
        Assert.Equal(Side.Interior, back.Side);
        Assert.Equal(new (int, uint)[] { (0, 100), (1, 200), (5, 100) }, back.Pieces);
        Assert.Equal((38, 120), back.NextPiece);
        Assert.Equal(RunPhase.Place, back.Phase);
        Assert.Equal(new DateTime(2026, 10, 30, 21, 14, 0, DateTimeKind.Utc), back.SavedUtc);
        Assert.Equal("Carry on from piece 38 of 120", CareWords.CarryOn(back));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":1,\"design\":\"a\",\"home\":\"h\",\"side\":\"interior\",\"done\":5,\"total\":3,\"pieces\":[[0,1]]}")]
    [InlineData("{\"version\":1,\"design\":\"a\",\"home\":\"h\",\"side\":\"attic\",\"done\":0,\"total\":3,\"pieces\":[[0,1]]}")]
    [InlineData("{\"version\":1,\"design\":\"a\",\"home\":\"h\",\"side\":\"garden\",\"done\":0,\"total\":3,\"pieces\":[[0,-1]]}")]
    [InlineData("{\"version\":1,\"design\":\"a\",\"home\":\"h\",\"side\":\"garden\",\"done\":0,\"total\":3,\"pieces\":[]}")]
    public void ANonsenseFileIsNone(string json) => Assert.Null(RunProgress.Parse(json));

    [Fact]
    public void ACutShortFileIsNone()
    {
        var json = Sample().ToJson();
        Assert.Null(RunProgress.Parse(json[..(json.Length / 2)]));
    }

    [Fact]
    public void FitsItsDesignAndHomeOnly()
    {
        var p = Sample();
        Assert.True(p.Fits(@"c:\DESIGNS\cosy.build", "interior-w73-t341-0000000000000ABC", Side.Interior));
        Assert.False(p.Fits(@"C:\designs\other.build", "interior-w73-t341-0000000000000ABC", Side.Interior));
        Assert.False(p.Fits(@"C:\designs\cosy.build", "interior-w73-t341-0000000000000ABD", Side.Interior));
        Assert.False(p.Fits(@"C:\designs\cosy.build", "interior-w73-t341-0000000000000ABC", Side.Garden));
        Assert.False(p.Fits(null, null, null));
    }

    [Fact]
    public void ItsEntriesAsMatchedNow()
    {
        var plan = new[] { 0, 1, 2, 5 }.Select(k => new PlanEntry { Target = new Target(k, k == 1 ? 200u : 100u, Vector3.Zero, 0, 0, Floor.Ground, false) }).ToList();
        Assert.Equal(new[] { 0, 1, 5 }, Sample().EntriesOf(plan).Select(e => e.Target.Key));
    }

    [Fact]
    public void HomeKeys()
    {
        Assert.Equal("interior-w73-t341-0000000000000ABC", Homes.Key(Side.Interior, 73, 341, 0xABC, null));
        Assert.Equal("garden-w73-t339-0000000000000001-p12", Homes.Key(Side.Garden, 73, 339, 1, 12));
        Assert.Null(Homes.Key(null, 73, 341, 1, null));
    }
}

public class MenuWordTests
{
    private static readonly Dictionary<uint, string> English = new() { [1751] = "Place", [6229] = "Place", [6840] = "Store", [6272] = "Paint" };
    private static readonly Dictionary<uint, string> German = new() { [1751] = "Platzieren", [6229] = "Platzieren", [6840] = "Ins Lagerhaus legen", [6272] = "Färben" };

    [Fact]
    public void TheGamesWordsInTheClientsLanguage()
    {
        var place = MenuWords.Resolve(MenuWords.Place, r => English.GetValueOrDefault(r), r => German.GetValueOrDefault(r), new[] { "Aufstellen" }, false);
        Assert.Equal(new[] { "Platzieren" }, place);
        var store = MenuWords.Resolve(MenuWords.Store, r => English.GetValueOrDefault(r), r => German.GetValueOrDefault(r), new[] { "Einlagern" }, false);
        Assert.Equal(new[] { "Ins Lagerhaus legen" }, store);
    }

    [Fact]
    public void RowsThatMovedFallBackToTheBuiltInWords()
    {
        var moved = new Dictionary<uint, string> { [1751] = "Sell", [6229] = "Estate Settings" };
        var builtIn = new[] { "Platzieren" };
        Assert.Same(builtIn, MenuWords.Resolve(MenuWords.Place, r => moved.GetValueOrDefault(r), r => German.GetValueOrDefault(r), builtIn, false));
        Assert.Same(builtIn, MenuWords.Resolve(MenuWords.Paint, _ => throw new ArgumentException("no sheet"), _ => null, builtIn, false));
    }

    [Fact]
    public void EnglishKeepsItsOwnAndTrailingSpacesGo()
    {
        var japanese = new Dictionary<uint, string> { [6272] = "染色する " };
        Assert.Equal(new[] { "染色する" }, MenuWords.Resolve(MenuWords.Paint, r => English.GetValueOrDefault(r), r => japanese.GetValueOrDefault(r), new[] { "染色" }, false));
        Assert.Equal(new[] { "Place" }, MenuWords.Resolve(MenuWords.Place, r => English.GetValueOrDefault(r), r => English.GetValueOrDefault(r), new[] { "Place" }, true));
    }
}
