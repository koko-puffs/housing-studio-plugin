using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class ModeRouteAfterOpeningTests
{
    /// <summary>Walks a route the way the runner does: bar buttons only when needed; Indoor Furnishings opens in <paramref name="opensIn"/>.</summary>
    private static (EditorMode End, List<ModeClick> Pressed) Walk(EditorMode from, EditorMode want, bool menuOpen, EditorMode opensIn)
    {
        var mode = from;
        var pressed = new List<ModeClick>();
        foreach (var click in ModeRoute.Plan(from, want, menuOpen)!)
        {
            if (!ModeRoute.Needed(click, mode)) continue;
            pressed.Add(click);
            mode = click switch
            {
                ModeClick.OpenFurnishings => opensIn,
                ModeClick.OpenHousingMenu or ModeClick.CloseEditor or ModeClick.CloseDyeList => EditorMode.None,
                _ => ModeRoute.After(click),
            };
            Assert.True(ModeRoute.Reached(click, mode), $"{click} should count as gone through in {mode}");
        }
        return (mode, pressed);
    }

    [Fact]
    public void FromNothingTheEditorOpeningInMoveGetsRotatePressed()
    {
        // 0.3.0: HousingMenu [i:0], the editor opened in Move, and the run waited 5 s for Rotate and stopped.
        var (end, pressed) = Walk(EditorMode.None, EditorMode.Rotate, false, opensIn: EditorMode.Move);
        Assert.Equal(EditorMode.Rotate, end);
        Assert.Equal(new[] { ModeClick.OpenHousingMenu, ModeClick.OpenFurnishings, ModeClick.Rotate }, pressed);
    }

    [Fact]
    public void FromNothingTheEditorOpeningInRotateNeedsNoMoreClicks()
    {
        var (end, pressed) = Walk(EditorMode.None, EditorMode.Rotate, false, opensIn: EditorMode.Rotate);
        Assert.Equal(EditorMode.Rotate, end);
        Assert.Equal(new[] { ModeClick.OpenHousingMenu, ModeClick.OpenFurnishings }, pressed);
    }

    [Fact]
    public void FromDyeModeEitherWayEndsInTheModeWanted()
    {
        foreach (var opensIn in new[] { EditorMode.Move, EditorMode.Rotate })
        {
            Assert.Equal(EditorMode.Rotate, Walk(EditorMode.Dye, EditorMode.Rotate, false, opensIn).End);
            Assert.Equal(EditorMode.Move, Walk(EditorMode.Dye, EditorMode.Move, false, opensIn).End);
        }
    }

    [Fact]
    public void IndoorFurnishingsHasGoneThroughInAnyLayoutModeOnly()
    {
        Assert.True(ModeRoute.Reached(ModeClick.OpenFurnishings, EditorMode.Move));
        Assert.True(ModeRoute.Reached(ModeClick.OpenFurnishings, EditorMode.Rotate));
        Assert.False(ModeRoute.Reached(ModeClick.OpenFurnishings, EditorMode.None));
        Assert.False(ModeRoute.Reached(ModeClick.OpenFurnishings, EditorMode.Dye));
        Assert.True(ModeRoute.Needed(ModeClick.Rotate, EditorMode.Move));
        Assert.False(ModeRoute.Needed(ModeClick.Rotate, EditorMode.Rotate));
        Assert.True(ModeRoute.Needed(ModeClick.OpenFurnishings, EditorMode.None));
    }
}

public class BagRouteTests
{
    [Fact]
    public void InsideABagPieceIsStoredThenPlacedFromTheStoreroom()
    {
        Assert.Equal(BagWay.Store, BagRoute.First(interior: true));
        Assert.Equal(BagWay.FromStoreroom, BagRoute.After(BagWay.Store, worked: true));
        Assert.Null(BagRoute.After(BagWay.FromStoreroom, worked: true));
    }

    [Fact]
    public void AStoreroomThatWontTakeItFallsBackToTheBagsWayThenByHand()
    {
        Assert.Equal(BagWay.Held, BagRoute.After(BagWay.Store, worked: false));
        Assert.Equal(BagWay.ByHand, BagRoute.After(BagWay.Held, worked: false));
        Assert.Null(BagRoute.After(BagWay.Held, worked: true));
    }

    [Fact]
    public void GardensHaveNoStoreroomTabSoTheBagsWayComesFirst() => Assert.Equal(BagWay.Held, BagRoute.First(interior: false));

    [Fact]
    public void StoredMeansOneMoreInTheStoreroomAndOneFewerInTheBags()
    {
        Assert.True(BagRoute.Stored(storedBefore: 0, storedNow: 1, inBagsBefore: 1, inBagsNow: 0));
        Assert.False(BagRoute.Stored(0, 1, 1, 1)); // the bag still has it: not yet
        Assert.False(BagRoute.Stored(0, 0, 1, 0)); // gone from the bag but not in the storeroom (placed? discarded?): not stored
    }
}

public class DyeCountConsistencyTests
{
    private static PlanEntry E(int key, PieceState state, byte want, byte has)
    {
        var t = new Target(key, 100u + (uint)key, Vector3.Zero, 0, want, Floor.Ground, false);
        return new PlanEntry
        {
            Target = t,
            State = state,
            Placed = state is PieceState.Move or PieceState.InPlace ? new PlacedPiece(key, 100u + (uint)key, Vector3.Zero, 0, has) : null,
            Stock = state is PieceState.FromStoreroom or PieceState.FromInventory ? new StockSlot(state == PieceState.FromStoreroom ? StockSource.Storeroom : StockSource.Inventory, 27001, key, 100u + (uint)key, has, 1) : null,
        };
    }

    /// <summary>A design like the user's 0.3.0 dry run: bag pieces the design dyes, a few placed ones.</summary>
    private static List<PlanEntry> Design() => new()
    {
        E(1, PieceState.Move, 5, 2),
        E(2, PieceState.InPlace, 5, 5),
        E(3, PieceState.InPlace, 7, 0),
        E(4, PieceState.FromStoreroom, 6, 6),
        E(5, PieceState.FromStoreroom, 9, 0),
        E(6, PieceState.FromInventory, 3, 0),
        E(7, PieceState.FromInventory, 3, 0),
        E(8, PieceState.FromInventory, 0, 0),
        E(9, PieceState.Missing, 4, 0),
    };

    private static int? PlacedKey(PlanEntry e) => e.State is PieceState.Move or PieceState.InPlace ? e.Placed!.Key : null;

    [Fact]
    public void TheStartLineAndADryRunsDyePhaseAgree()
    {
        var design = Design();
        var start = DyeCounts.ToApply(design);
        // A dry run places nothing: the dye phase sees the placed pieces live and predicts the rest.
        var live = design.Where(e => e.Placed != null).ToDictionary(e => e.Placed!.Key, e => e.Placed!.Stain);
        var dry = DyePlanner.Plan(DyeCounts.Needs(design, PlacedKey, k => live.TryGetValue(k, out var s) ? s : null, predict: true), new Dictionary<byte, int> { [3] = 99, [5] = 99, [6] = 99, [7] = 99, [9] = 99 });
        Assert.Equal(5, start); // 1 (Move 2→5), 3 (InPlace 0→7), 5 (storeroom 0→9), 6 and 7 (bags 0→3)
        Assert.Equal(start, dry.Jobs.Count);
        Assert.Equal(start, DyeCounts.ToDye(DyeCounts.Needs(design, PlacedKey, k => live.TryGetValue(k, out var s) ? s : null, predict: true)));
    }

    [Fact]
    public void ARealRunsDyePhaseCountsOnlyWhatGotPlaced()
    {
        var design = Design();
        var live = design.Where(e => e.Placed != null).ToDictionary(e => e.Placed!.Key, e => e.Placed!.Stain);
        // Pieces 5 and 6 were placed as slots 50 and 60 (undyed); 7 didn't get placed.
        live[50] = 0;
        live[60] = 0;
        int? KeyOf(PlanEntry e) => e.Target.Key switch { 5 => 50, 6 => 60, _ => PlacedKey(e) };
        var needs = DyeCounts.Needs(design, KeyOf, k => live.TryGetValue(k, out var s) ? s : null, predict: false);
        Assert.Equal(4, DyeCounts.ToDye(needs)); // the start's 5, less piece 7
        Assert.DoesNotContain(needs, n => n.Key == DyeCounts.NotPlacedYet);
    }

    [Fact]
    public void ToApplyIsTheSameCalculationPredicting()
    {
        var design = Design();
        Assert.Equal(DyeCounts.ToDye(DyeCounts.Needs(design, _ => null, _ => null, predict: true)), DyeCounts.ToApply(design));
    }
}

public class StoreroomOrderTests
{
    /// <summary>
    /// Rows a user's 0.3.1 runs learnt, with each item's sort key from the game's sheets (ItemUICategory
    /// OrderMajor/OrderMinor, Item SubcategorySort, item id). Origenics Chair was seen at row 213 after
    /// Hyperboreia Bench, seen at 213 before it, had been placed (the rows moved up one).
    /// </summary>
    private static readonly (int Row, string Name, ListSortKey Key)[] Evidence =
    {
        (0, "Simple Hingan Pillar", new(7, 20, 8230, 48110)),
        (3, "Planter Partition", new(7, 20, 10000, 7979)),
        (5, "Verdant Partition", new(7, 20, 10000, 23892)),
        (7, "White Rectangular Partition", new(7, 20, 10000, 24514)),
        (141, "White Screen", new(7, 20, 10000, 29682)),
        (142, "Wood Slat Partition", new(7, 20, 10000, 32226)),
        (156, "Waterfall Partition", new(7, 20, 10000, 39409)),
        (158, "No-entry Door Partition", new(7, 20, 10000, 48106)),
        (165, "Blank Minimalist Partition", new(7, 20, 10000, 48134)),
        (166, "Minimalist Poplar Partition", new(7, 20, 10001, 51253)),
        (167, "Blank Riviera Partition", new(7, 20, 10200, 21106)),
        (168, "Everkeep Partition", new(7, 20, 10270, 51267)),
        (169, "Oriental Bathtub", new(7, 20, 14000, 9719)),
        (170, "Potted Maguey", new(7, 20, 16000, 6595)),
        (171, "Potted Dragon Tree", new(7, 20, 16000, 7975)),
        (172, "Washbasin", new(7, 20, 16000, 15144)),
        (173, "Stage Panel", new(7, 20, 16000, 21839)),
        (183, "Doman Dogwood", new(7, 20, 16000, 23864)),
        (200, "Dance Pole", new(7, 20, 16000, 35572)),
        (210, "Simple Sink", new(7, 20, 16000, 39414)),
        (211, "Leather Sofa", new(7, 20, 40000, 28639)),
        (213, "Hyperboreia Bench", new(7, 20, 40000, 41099)),
        (214, "Cosmic Storage Bench", new(7, 20, 40001, 49848)),
        (215, "Double Feather Bed", new(7, 20, 42000, 7984)),
        (216, "Mahogany Bunk Bed", new(7, 20, 42000, 23884)),
        (217, "Eastern Canopy Bed", new(7, 20, 42000, 37355)),
        (218, "Wooden Staircase Bookshelf", new(7, 21, 2000, 30385)),
        (227, "Philosopher's Stone Table", new(7, 21, 2000, 33281)),
        (228, "Origenics Monitor Desk", new(7, 21, 2000, 44889)),
        (229, "Merchant's Shelf", new(7, 21, 4000, 17024)),
        (230, "Back Bar", new(7, 21, 4000, 33279)),
        (234, "Shoe Rack", new(7, 21, 4000, 35556)),
        (240, "Steel Locker", new(7, 21, 4000, 37369)),
        (257, "Hingan Sideboard", new(7, 21, 4230, 20732)),
        (272, "Hingan Bookshelf", new(7, 21, 4230, 20734)),
    };

    [Fact]
    public void TheSortKeyPutsEveryLearntRowInTheListsOrder()
    {
        var byKey = Evidence.OrderBy(e => e.Key).Select(e => e.Row).ToList();
        Assert.Equal(Evidence.Select(e => e.Row), byKey);
        // Origenics Chair (44890) sorts right after Hyperboreia Bench (41099): with the bench placed it's row 213, as seen.
        var chair = new ListSortKey(7, 20, 40000, 44890);
        Assert.True(Evidence.Single(e => e.Name == "Hyperboreia Bench").Key.CompareTo(chair) < 0);
        Assert.True(chair.CompareTo(Evidence.Single(e => e.Name == "Cosmic Storage Bench").Key) < 0);
    }

    [Fact]
    public void TheRowIsHowManyPiecesSortBeforeTheItem()
    {
        // Ten Dance Poles at rows 200–209, then Simple Sink at 210 (the user's log).
        var keys = new Dictionary<uint, ListSortKey> { [1] = new(7, 20, 16000, 23864), [2] = new(7, 20, 16000, 35572), [3] = new(7, 20, 16000, 39414) };
        var storeroom = Enumerable.Repeat(1u, 200).Concat(Enumerable.Repeat(2u, 10)).Append(3u).ToList();
        Assert.Equal(200, StoreroomOrder.FirstRow(storeroom, i => keys[i], 2));
        Assert.Equal(210, StoreroomOrder.FirstRow(storeroom, i => keys[i], 3));
    }

    /// <summary>A list of about 445 pieces: items in key order, each a group of random size.</summary>
    private static List<uint> List(int seed, out Dictionary<uint, ListSortKey> keys)
    {
        var random = new Random(seed);
        keys = new Dictionary<uint, ListSortKey>();
        var pieces = new List<uint>();
        for (uint item = 1; pieces.Count < 445; item++)
        {
            keys[item] = new ListSortKey(7, 20, 1000 * (int)item, item);
            pieces.AddRange(Enumerable.Repeat(item, random.Next(1, 12)));
        }
        return pieces;
    }

    private static int PicksToFind(List<uint> list, Func<uint, ListSortKey> key, uint item, int predicted)
    {
        var seen = new Dictionary<int, uint>();
        var tried = new HashSet<int>();
        for (var picks = 1; picks <= 40; picks++)
        {
            var probe = StoreroomOrder.Next(item, key, list.Count, seen, tried, predicted, picks - 1);
            Assert.NotNull(probe);
            var row = probe!.Value.Row;
            tried.Add(row);
            if (list[row] == item) return picks;
            seen[row] = list[row];
        }
        return int.MaxValue;
    }

    [Fact]
    public void AnExactPredictionFindsThePieceInOnePick()
    {
        var list = List(1, out var keys);
        foreach (var item in list.Distinct())
            Assert.Equal(1, PicksToFind(list, i => keys[i], item, StoreroomOrder.FirstRow(list, i => keys[i], item)));
    }

    [Fact]
    public void APredictionALittleOffIsCorrectedInAFewPicks()
    {
        var list = List(3, out var keys);
        foreach (var item in list.Distinct())
        {
            var row = StoreroomOrder.FirstRow(list, i => keys[i], item);
            foreach (var off in new[] { -3, -1, 2, 5 })
                Assert.InRange(PicksToFind(list, i => keys[i], item, Math.Clamp(row + off, 0, list.Count - 1)), 1, 5);
        }
    }

    [Fact]
    public void OneRowOffTakesTwoPicks()
    {
        // 100 different pieces, one row each: 0.3.2 took three picks for a prediction one row off (49, 51, 50).
        var list = Enumerable.Range(1, 100).Select(i => (uint)i).ToList();
        ListSortKey Key(uint i) => new(7, 20, 1000 * (int)i, i);
        Assert.Equal(2, PicksToFind(list, Key, 51, predicted: 49));
        Assert.Equal(2, PicksToFind(list, Key, 51, predicted: 51));
        Assert.InRange(PicksToFind(list, Key, 51, predicted: 47), 1, 3);
        Assert.InRange(PicksToFind(list, Key, 51, predicted: 53), 1, 3);
    }

    [Fact]
    public void AFarOffPredictionStillFindsItWithoutScanning()
    {
        // 0.3.1 picked up to 28 rows one by one for a piece; from the wrong end of 445 rows this takes under 20.
        var list = List(2, out var keys);
        foreach (var item in list.Distinct())
        {
            Assert.InRange(PicksToFind(list, i => keys[i], item, predicted: 0), 1, 19);
            Assert.InRange(PicksToFind(list, i => keys[i], item, predicted: list.Count - 1), 1, 19);
        }
    }

    [Fact]
    public void RowsThatContradictTheOrderMeanNoGuess()
    {
        Func<uint, ListSortKey> key = i => new ListSortKey(7, 20, 0, i);
        // Item 5 is read at row 10, item 9 at row 3: item 7 would have to be below 10 and above 3.
        var seen = new Dictionary<int, uint> { [10] = 5, [3] = 9 };
        Assert.Null(StoreroomOrder.Next(7, key, 50, seen, new HashSet<int>(), predicted: 6));
    }

    [Fact]
    public void ThePredictionIsKeptWithinWhatTheReadRowsAllow()
    {
        Func<uint, ListSortKey> key = i => new ListSortKey(7, 20, 0, i);
        // Rows 0–199 read, all items sorting before 500: it's past them, whatever was predicted.
        var seen = Enumerable.Range(0, 200).ToDictionary(r => r, r => (uint)(r + 1));
        var probe = StoreroomOrder.Next(500, key, 445, seen, new HashSet<int>(), predicted: 150);
        Assert.Equal(200, probe!.Value.Row);
        Assert.Equal(200, probe.Value.Low);
    }

    [Fact]
    public void ChecksThePredictionAgainstTheRowsKnown()
    {
        var predicted = new List<uint> { 1, 1, 2, 3 };
        Assert.Equal((2, 3), StoreroomOrder.Check(predicted, new Dictionary<int, uint> { [0] = 1, [2] = 2, [3] = 4 }));
    }

    [Fact]
    public void TheNextPieceIsTheReadableOneNearestTheTop()
    {
        var names = new Dictionary<int, string> { [0] = "Simple Hingan Pillar", [3] = "Planter Partition", [141] = "White Screen", [199] = "Dance Pole" };
        // The design's order: Hingan Bookshelf (row 272, not readable), White Screen, Planter Partition.
        var waiting = new[] { "Hingan Bookshelf", "White Screen", "Planter Partition" };
        Assert.Equal(2, StoreroomOrder.FirstReadable(waiting, names));
        Assert.Null(StoreroomOrder.FirstReadable(new[] { "Hingan Bookshelf", "Steel Locker" }, names));
    }
}

public class SwapTargetsTests
{
    [Fact]
    public void TwoPiecesOfOneItemSwapWhereAndWhichCopy()
    {
        var black = new Target(1, 7, new System.Numerics.Vector3(1, 0, 0), 0, 5, Floor.Ground, false);
        var grey = new Target(2, 7, new System.Numerics.Vector3(2, 0, 0), 0, 9, Floor.Ground, false);
        var a = new RunJob { Kind = JobKind.PlaceFromStoreroom, Target = black, Entry = new PlanEntry { Target = black, State = PieceState.FromStoreroom } };
        var b = new RunJob { Kind = JobKind.PlaceFromStoreroom, Target = grey, Entry = new PlanEntry { Target = grey, State = PieceState.FromStoreroom } };
        a.SwapTargets(b);
        Assert.Equal(grey, a.Target);
        Assert.Equal(black, b.Target);
        Assert.Same(grey, a.Entry!.Target);
        Assert.Same(black, b.Entry!.Target);
    }
}
