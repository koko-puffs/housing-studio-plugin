using System.Linq;
using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class ClearOutTests
{
    private static PlacedPiece At(int key, float x, float y) => new(key, 100, new Vector3(x, y, 0), 0, 0);

    [Fact]
    public void PicksEveryExtraWithNoFilters()
    {
        var extras = new[] { At(1, 0, 0), At(2, 0, 8), At(3, 0, -3) };
        Assert.Equal(3, ClearOut.Pick(extras, Side.Interior, "Medium", _ => true, null, 0).Count);
    }

    [Fact]
    public void LeavesOutTheFloorsSwitchedOff()
    {
        var extras = new[] { At(1, 0, 0), At(2, 0, 8), At(3, 0, -3) };
        var picked = ClearOut.Pick(extras, Side.Interior, "Medium", f => f != Floor.Upper, null, 0);
        Assert.Equal(new[] { 1, 3 }, picked.Select(p => p.Key));
    }

    [Fact]
    public void ASmallHouseHasNoUpperFloorToLeaveOut()
    {
        // Above 7 yalms in a cottage is still its ground floor (a loft), as for the design's pieces.
        var picked = ClearOut.Pick(new[] { At(2, 0, 8) }, Side.Interior, "Small", f => f != Floor.Upper, null, 0);
        Assert.Single(picked);
    }

    [Fact]
    public void NearMeKeepsWhatsWithinReach()
    {
        var extras = new[] { At(1, 1, 0), At(2, 20, 0) };
        var picked = ClearOut.Pick(extras, Side.Interior, "Medium", _ => true, Vector3.Zero, 8);
        Assert.Equal(1, Assert.Single(picked).Key);
        // No position (or no reach) leaves nothing out, as for the design's pieces.
        Assert.Equal(2, ClearOut.Pick(extras, Side.Interior, "Medium", _ => true, null, 8).Count);
        Assert.Equal(2, ClearOut.Pick(extras, Side.Interior, "Medium", _ => true, Vector3.Zero, 0).Count);
    }

    [Fact]
    public void AGardensPiecesAreOnTheGarden()
    {
        var picked = ClearOut.Pick(new[] { At(1, 0, -5), At(2, 0, 30) }, Side.Garden, "Large", f => f == Floor.Garden, null, 0);
        Assert.Equal(2, picked.Count);
    }

    [Theory]
    [InlineData(Side.Interior, "Medium", "Mist", 12, "Medium house in Mist, plot 12")]
    [InlineData(Side.Interior, "Apartment", "the Goblet", 3, "Apartment in the Goblet")]
    [InlineData(Side.Garden, "Small", "the Lavender Beds", 7, "Garden in the Lavender Beds, plot 7")]
    [InlineData(Side.Interior, null, null, null, "House")]
    public void NamesTheHome(Side side, string? size, string? district, int? plot, string expected) =>
        Assert.Equal(expected, SwapNames.Home(side, size, district, plot));

    [Fact]
    public void KeepsWhatWasThereBeforeTheDesign()
    {
        Assert.Equal("Medium house in Mist before Winter Lodge", SwapNames.KeepAs("Medium house in Mist", " Winter Lodge "));
        Assert.Equal("House before a swap", SwapNames.KeepAs("House", ""));
    }

    [Fact]
    public void FileStemsHoldNoCharactersAFileNameCant()
    {
        Assert.Equal("Medium house before Winter Lodge 2026", SwapNames.FileStem("Medium house before Winter: Lodge? 2026"));
        Assert.Equal("a b", SwapNames.FileStem("a/\\b."));
        Assert.Equal("Home before a swap", SwapNames.FileStem("???"));
        Assert.True(SwapNames.FileStem(new string('x', 200)).Length <= 80);
    }

    [Fact]
    public void AKeptNameIsNeverOneTheBackupsArePrunedBy()
    {
        // Backups' own names are "interior-20261030-091500"; a kept name starts with the home.
        var stem = SwapNames.FileStem(SwapNames.KeepAs(SwapNames.Home(Side.Interior, "Large", "Shirogane", 1), "interior-20261030-091500"));
        Assert.StartsWith("Large house", stem);
    }

    [Fact]
    public void WhatWasChosenIsClearedOutOnlyWhileItsStillThere()
    {
        var chosen = new[] { At(1, 1, 0), At(2, 5, 0), At(3, 9, 0) };
        // Read again on coming home: slot 2's piece is now slot 7 at the same spot; slot 3's was moved away; one more is new.
        var now = new[] { At(1, 1, 0), At(7, 5, 0), At(3, 20, 0), At(8, 30, 0) };
        var still = ClearOut.StillHere(chosen, now);
        Assert.Equal(new[] { 1, 7 }, still.Select(p => p.Key));
        // Never more than chosen, and each piece once.
        var twice = ClearOut.StillHere(new[] { At(1, 1, 0), At(1, 1, 0) }, new[] { At(1, 1, 0) });
        Assert.Single(twice);
        Assert.Empty(ClearOut.StillHere(chosen, Array.Empty<PlacedPiece>()));
        // Another item at the spot isn't it.
        Assert.Empty(ClearOut.StillHere(new[] { At(1, 1, 0) }, new[] { new PlacedPiece(1, 999, new Vector3(1, 0, 0), 0, 0) }));
    }
}

public class FixtureJobsTests
{
    private static LayoutFixture F(string level, string type, uint item, string? dye = null) => new(level, type, item, $"item {item}", dye);

    [Fact]
    public void SlotsFollowTheGamesOrderFloorByFloor()
    {
        Assert.Equal(0, FixtureJobs.InteriorSlot("Ground Floor", "Wall"));
        Assert.Equal(3, FixtureJobs.InteriorSlot("Ground Floor", "Floor"));
        Assert.Equal(9, FixtureJobs.InteriorSlot("Upper Floor", "Light"));
        Assert.Equal(13, FixtureJobs.InteriorSlot("basement", "Flooring"));
        Assert.Equal(13, FixtureJobs.InteriorSlot("Basement", "Floor"));
        // A floor's window and door, and the house's own ("Main"), aren't the fixtures window's.
        Assert.Null(FixtureJobs.InteriorSlot("Ground Floor", "Window"));
        Assert.Null(FixtureJobs.InteriorSlot("Main", "Door"));
        Assert.Equal(1, FixtureJobs.ExteriorSlot("Walls"));
        Assert.Equal(4, FixtureJobs.ExteriorSlot("RoofDecoration"));
        Assert.Equal(2, FixtureJobs.ExteriorSlot("Windows"));
        Assert.Null(FixtureJobs.ExteriorSlot("Chimney"));
    }

    [Fact]
    public void PlansWhatsSetWhatsToSetAndWhatIsntOwned()
    {
        var design = new[] { F("Ground Floor", "Wall", 1), F("Ground Floor", "Floor", 2), F("Upper Floor", "Floor", 2), F("Basement", "Floor", 2), F("Main", "Door", 9) };
        var current = new[] { new CurrentFixture("Ground Floor", "Wall", 1) };
        var tasks = FixtureJobs.Plan(design, current, Side.Interior, item => item == 2 ? 2 : 0, _ => false);
        Assert.Equal(FixtureState.Set, tasks[0].State);
        Assert.Equal(FixtureState.ToSet, tasks[1].State);
        Assert.Equal(3, tasks[1].Slot);
        Assert.Equal(FixtureState.ToSet, tasks[2].State);
        // Two floorings in your bags, three floors that want one.
        Assert.Equal(FixtureState.NotOwned, tasks[3].State);
        Assert.Equal(FixtureState.ByHand, tasks[4].State);
    }

    [Fact]
    public void TheOutsidesDyesAndThemesAreYours()
    {
        var design = new[] { F("Exterior", "Roof", 10, "336699"), F("Exterior", "Walls", 11, "336699"), F("Exterior", "Fence", 12), F("Exterior", "Door", 13) };
        var current = new[] { new CurrentFixture("Exterior", "Roof", 10, "ffffff") };
        var tasks = FixtureJobs.Plan(design, current, Side.Garden, _ => 1, item => item == 12);
        Assert.Equal(FixtureState.ByHand, tasks[0].State); // set, in another dye
        Assert.Equal(FixtureState.ToSet, tasks[1].State);
        Assert.Contains("dye", tasks[1].Why);
        Assert.Equal(FixtureState.ByHand, tasks[2].State); // themed
        Assert.Equal(3, tasks[3].Slot);
        // Inside, the outside's fixtures aren't planned (and the other way round).
        Assert.Empty(FixtureJobs.Plan(design, current, Side.Interior, _ => 1, _ => false));
    }

    [Fact]
    public void TheWindowsSlotsMustHoldWhatsSet()
    {
        var current = new[] { new CurrentFixture("Ground Floor", "Wall", 1), new CurrentFixture("Upper Floor", "Light", 7), new CurrentFixture("Main", "Door", 9) };
        Assert.True(FixtureJobs.LayoutAgrees(new Dictionary<int, uint> { [0] = 1, [9] = 7, [20] = 9 }, current, Side.Interior, out _));
        Assert.False(FixtureJobs.LayoutAgrees(new Dictionary<int, uint> { [0] = 1, [8] = 7 }, current, Side.Interior, out var why));
        Assert.Contains("slot 9", why);
        Assert.False(FixtureJobs.LayoutAgrees(new Dictionary<int, uint> { [0] = 7, [9] = 1 }, current, Side.Interior, out _));
        // Nothing to compare: not trusted.
        Assert.False(FixtureJobs.LayoutAgrees(new Dictionary<int, uint>(), Array.Empty<CurrentFixture>(), Side.Interior, out _));
        Assert.True(FixtureJobs.LayoutAgrees(new Dictionary<int, uint> { [7] = 5 }, new[] { new CurrentFixture("Exterior", "Fence", 5) }, Side.Garden, out _));
    }

    [Fact]
    public void FindsTheMenusFixturesEntry()
    {
        var menu = new[] { "Indoor Furnishings", "Furnishing Color", "Interior Fixtures", "Exterior Fixtures" };
        Assert.Equal(2, FixtureJobs.MenuEntry(menu, new[] { "Interior Fixtures", "Fixtures" }));
        Assert.Equal(3, FixtureJobs.MenuEntry(menu, new[] { "Exterior Fixtures", "Fixtures" }));
        Assert.Equal(2, FixtureJobs.MenuEntry(menu, new[] { "Fixtures" }));
        Assert.Equal(-1, FixtureJobs.MenuEntry(menu, new[] { "Lighting" }));
        Assert.True(FixtureJobs.MenuInPlace(menu));
        Assert.Equal(0, FixtureJobs.MenuOffset(menu));
        // The game's own words (0.9.1's log): the menu's title first, every entry one along.
        var titled = new[] { "Housing", "Indoor Furnishings", "Furnishing Color", "Remodel Interior", "Lighting" };
        Assert.Equal(1, FixtureJobs.MenuOffset(titled));
        Assert.Equal(3, FixtureJobs.MenuEntry(titled, new[] { "Remodel Interior", "Interior Fixtures", "Fixtures" }));
        // Two titles, or entries not where they were recorded: not trusted.
        Assert.Equal(-1, FixtureJobs.MenuOffset(new[] { "Housing", "Menu", "Indoor Furnishings", "Furnishing Color" }));
        Assert.False(FixtureJobs.MenuInPlace(new[] { "Remodel Interior", "Indoor Furnishings" }));
    }
}

public class ClearOutRoomTests
{
    private static PlacedPiece At(int key, float x, float y) => new(key, 100, new Vector3(x, y, 0), 0, 0);

    [Fact]
    public void AStoreroomWithRoomTakesThemAllFirst()
    {
        var pieces = new[] { At(1, 0, 0), At(2, 0, 1), At(3, 0, 2) };
        var (first, later) = ClearOutSplit.Split(pieces, 3);
        Assert.Equal(3, first.Count);
        Assert.Empty(later);
        // Not known (the storeroom not sent yet): all first, and a refusal puts the rest off then.
        Assert.Equal(3, ClearOutSplit.Split(pieces, null).First.Count);
    }

    [Fact]
    public void AFullStoreroomPutsTheRestOffUntilPlaced()
    {
        var pieces = new[] { At(1, 0, 0), At(2, 0, 1), At(3, 0, 2) };
        var (first, later) = ClearOutSplit.Split(pieces, 1);
        Assert.Equal(new[] { 1 }, first.Select(p => p.Key));
        Assert.Equal(new[] { 2, 3 }, later.Select(p => p.Key));
        var (none, all) = ClearOutSplit.Split(pieces, 0);
        Assert.Empty(none);
        Assert.Equal(3, all.Count);
        Assert.Empty(ClearOutSplit.Split(pieces, -2).First);
    }

    [Fact]
    public void TheAskSaysWhenTheStoreroomIsShort()
    {
        Assert.Equal(string.Empty, HousingStudio.UI.Model.ReadyWords.StoreroomRoomNote(29, null));
        Assert.Equal(string.Empty, HousingStudio.UI.Model.ReadyWords.StoreroomRoomNote(29, 40));
        Assert.Contains("room for 1: those go first, and the other 28", HousingStudio.UI.Model.ReadyWords.StoreroomRoomNote(29, 1));
        Assert.Contains("The storeroom is full: the 29 go once the design is placed", HousingStudio.UI.Model.ReadyWords.StoreroomRoomNote(29, 0));
    }
}
