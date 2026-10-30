using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class NamesBeyondTheReadableRowsTests
{
    private static List<string> Window(int from, int count) => Enumerable.Range(from, count).Select(i => $"Piece {i / 2}").ToList();

    [Fact]
    public void FindsRowsByNameAcrossSeveralReads()
    {
        // 0.2.2: the list held 200 of 445 rows; Stage Panel was row 173, others lie past 200.
        var rows = new StoreroomRows();
        rows.Reset(445);
        rows.LearnNames(0, Window(0, 200));
        Assert.Equal(new[] { 172, 173 }, rows.ByName("Piece 86", 86));
        Assert.Empty(rows.ByName("Piece 150", 150));
        rows.LearnNames(190, Window(190, 200));
        Assert.Equal(new[] { 300, 301 }, rows.ByName("Piece 150", 150));
    }

    [Fact]
    public void AScanSkipsRowsReadToHoldOtherPieces()
    {
        var rows = new StoreroomRows();
        rows.Reset(445);
        rows.LearnNames(0, Window(0, 200));
        // Not among the 200 names: the first row worth picking is the first one not read.
        Assert.Equal(200, rows.NextUnknownFor("Piece 300"));
        // Among them: its own rows only.
        Assert.Equal(20, rows.NextUnknownFor("Piece 10"));
        Assert.Equal(21, rows.NextUnknownFor("Piece 10", new HashSet<int> { 20 }));
    }

    [Fact]
    public void NamesMoveUpWhenAPieceLeaves()
    {
        var rows = new StoreroomRows();
        rows.Reset(10);
        rows.LearnNames(0, new[] { "A", "B", "C", "D" });
        rows.Removed(1);
        Assert.Equal(new[] { 1 }, rows.ByName("C", 3));
        Assert.Equal(9, rows.Count);
    }

    [Fact]
    public void LinesUpARereadWindowByItsOverlap()
    {
        var known = new Dictionary<int, string>();
        var first = new[] { "A", "A", "B", "C", "C", "D", "E", "F", "G", "H", "H", "I" };
        for (var i = 0; i < first.Length; i++) known[i] = first[i];
        // Scrolled: the list now holds rows 6 onwards.
        var later = new[] { "E", "F", "G", "H", "H", "I", "J", "K", "K", "L" };
        Assert.Equal(6, ListWindows.Align(known, later, minOverlap: 4));
    }

    [Fact]
    public void WontGuessWhereAWindowCouldFitTwice()
    {
        var known = new Dictionary<int, string>();
        for (var i = 0; i < 20; i++) known[i] = "Same";
        Assert.Null(ListWindows.Align(known, new[] { "Same", "Same", "Same", "Same", "Same", "Same", "Same", "Same" }, 4));
        Assert.Null(ListWindows.Align(known, new[] { "Other" }, 1));
    }
}

public class NewPieceTests
{
    [Fact]
    public void FindsTheSlotThatWasntThereBefore()
    {
        var before = new HashSet<int> { 1, 2, 3 };
        var now = new[] { new SeenPiece(1, 10, Vector3.Zero), new SeenPiece(2, 50, Vector3.Zero), new SeenPiece(7, 50, Vector3.One) };
        Assert.Equal(7, NewPieces.Identify(before, now, 50, null));
    }

    [Fact]
    public void NothingNewMeansNoPiece()
    {
        // 0.2.2 waited, then said "didn't place": the preview isn't in the furniture list, so it can't count.
        var before = new HashSet<int> { 1, 2 };
        var now = new[] { new SeenPiece(1, 50, Vector3.Zero), new SeenPiece(2, 50, Vector3.Zero) };
        Assert.Null(NewPieces.Identify(before, now, 50, new Vector3(-1.49f, 0, -0.16f)));
    }

    [Fact]
    public void OfSeveralNewOnesTakesTheOneAtThePreview()
    {
        var before = new HashSet<int>();
        var preview = new Vector3(-1.49f, 0, -0.16f);
        var now = new[] { new SeenPiece(4, 50, new Vector3(5, 0, 5)), new SeenPiece(9, 50, new Vector3(-1.5f, 0, -0.15f)), new SeenPiece(2, 60, preview) };
        Assert.Equal(9, NewPieces.Identify(before, now, 50, preview));
        Assert.Equal(4, NewPieces.Identify(before, now, 50, null));
    }
}

public class DyeCountTests
{
    private static PlanEntry E(PieceState state, byte want, byte has)
    {
        var t = new Target(0, 1, Vector3.Zero, 0, want, Floor.Ground, false);
        return new PlanEntry
        {
            Target = t,
            State = state,
            Placed = state is PieceState.Move or PieceState.InPlace ? new PlacedPiece(1, 1, Vector3.Zero, 0, has) : null,
            Stock = state is PieceState.FromStoreroom or PieceState.FromInventory ? new StockSlot(state == PieceState.FromStoreroom ? StockSource.Storeroom : StockSource.Inventory, 27001, 0, 1, has, 1) : null,
        };
    }

    [Fact]
    public void CountsOnlyPiecesThatWillNeedDyeing()
    {
        var entries = new[]
        {
            E(PieceState.FromStoreroom, 6, 6),   // Stage Panel, already Soot Black in the storeroom
            E(PieceState.FromStoreroom, 9, 0),   // undyed in the storeroom
            E(PieceState.FromInventory, 3, 0),   // from the bags: never dyed
            E(PieceState.InPlace, 5, 5),
            E(PieceState.Move, 5, 2),            // placed in another dye
            E(PieceState.Missing, 7, 0),         // missing: not counted
            E(PieceState.FromStoreroom, 0, 0),   // undyed in the design
        };
        Assert.Equal(3, DyeCounts.ToApply(entries));
        Assert.Equal(5, DyeCounts.Dyed(entries));
    }
}
