using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class MatchingTests
{
    private static Target T(int key, uint item, float x, byte stain = 0, float yaw = 0, bool late = false, Floor floor = Floor.Ground) =>
        new(key, item, new Vector3(x, 0, 0), yaw, stain, floor, late);

    private static PlacedPiece P(int key, uint item, float x, byte stain = 0, float yaw = 0) => new(key, item, new Vector3(x, 0, 0), yaw, stain);

    private static StockSlot S(StockSource src, uint item, byte stain = 0, int qty = 1, int slot = 0) => new(src, src == StockSource.Inventory ? 0 : 27001, slot, item, stain, qty);

    [Fact]
    public void APieceAlreadyThereIsInPlace()
    {
        var plan = Matcher.Build(new[] { T(0, 10, 1) }, new[] { P(100, 10, 1.005f) }, Array.Empty<StockSlot>());
        Assert.Equal(PieceState.InPlace, plan.Entries[0].State);
    }

    [Fact]
    public void APieceElsewhereOrTurnedIsMoved()
    {
        var plan = Matcher.Build(new[] { T(0, 10, 1), T(1, 11, 0, yaw: 1) }, new[] { P(100, 10, 3), P(101, 11, 0, yaw: 0) }, Array.Empty<StockSlot>());
        Assert.All(plan.Entries, e => Assert.Equal(PieceState.Move, e.State));
    }

    [Fact]
    public void PairsTheNearestCopies()
    {
        var plan = Matcher.Build(
            new[] { T(0, 10, 0), T(1, 10, 10) },
            new[] { P(100, 10, 9), P(101, 10, 1) },
            Array.Empty<StockSlot>());
        Assert.Equal(101, plan.Entries[0].Placed!.Key);
        Assert.Equal(100, plan.Entries[1].Placed!.Key);
    }

    [Fact]
    public void PrefersTheRightDyeOverTheNearest()
    {
        var plan = Matcher.Build(
            new[] { T(0, 10, 0, stain: 5) },
            new[] { P(100, 10, 0.5f, stain: 0), P(101, 10, 30, stain: 5) },
            Array.Empty<StockSlot>());
        Assert.Equal(101, plan.Entries[0].Placed!.Key);
        Assert.False(plan.Entries[0].DyeDiffers);
        Assert.Single(plan.Extras);
    }

    [Fact]
    public void UsesAWronglyDyedPieceRatherThanNone()
    {
        var plan = Matcher.Build(new[] { T(0, 10, 0, stain: 5) }, new[] { P(100, 10, 0, stain: 7) }, Array.Empty<StockSlot>());
        Assert.Equal(PieceState.InPlace, plan.Entries[0].State);
        Assert.True(plan.Entries[0].DyeDiffers);
    }

    [Fact]
    public void TakesFromTheStoreroomInTheRightDyeBeforeTheBags()
    {
        var stock = new[] { S(StockSource.Inventory, 10), S(StockSource.Storeroom, 10, stain: 5) };
        var plan = Matcher.Build(new[] { T(0, 10, 0, stain: 5) }, Array.Empty<PlacedPiece>(), stock);
        Assert.Equal(PieceState.FromStoreroom, plan.Entries[0].State);
    }

    [Fact]
    public void TakesFromTheBagsBeforeAWronglyDyedStoreroomPiece()
    {
        var stock = new[] { S(StockSource.Storeroom, 10, stain: 9), S(StockSource.Inventory, 10) };
        var plan = Matcher.Build(new[] { T(0, 10, 0, stain: 5) }, Array.Empty<PlacedPiece>(), stock);
        Assert.Equal(PieceState.FromInventory, plan.Entries[0].State);
        Assert.True(plan.Entries[0].DyeDiffers);
    }

    [Fact]
    public void SharesAStackOutAndCountsWhatsMissing()
    {
        var targets = Enumerable.Range(0, 5).Select(i => T(i, 10, i)).ToArray();
        var plan = Matcher.Build(targets, new[] { P(100, 10, 0) }, new[] { S(StockSource.Inventory, 10, qty: 3) });
        Assert.Equal(1, plan.Count(PieceState.InPlace));
        Assert.Equal(3, plan.Count(PieceState.FromInventory));
        Assert.Equal(1, plan.Count(PieceState.Missing));
    }

    [Fact]
    public void LeavesPiecesTheDesignDoesntUse()
    {
        var plan = Matcher.Build(new[] { T(0, 10, 0) }, new[] { P(100, 10, 0), P(101, 99, 0) }, Array.Empty<StockSlot>());
        Assert.Equal(101, Assert.Single(plan.Extras).Key);
    }

    [Fact]
    public void WorksFloorFirstThenWhatSitsOnIt()
    {
        var plan = Matcher.Build(
            new[]
            {
                T(0, 1, 0, late: true),
                T(1, 2, 0),
                T(2, 3, 0, floor: Floor.Upper),
                T(3, 4, 0, floor: Floor.Basement),
            },
            Array.Empty<PlacedPiece>(),
            Array.Empty<StockSlot>());
        Assert.Equal(new[] { 3, 1, 0, 2 }, Matcher.WorkOrder(plan.Entries).Select(e => e.Target.Key));
    }
}
