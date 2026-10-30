using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class StoreroomRowsTests
{
    private static StoreroomRow R(int container, int slot, uint item, byte stain = 0) => new(container, slot, item, stain);

    [Fact]
    public void ScansFromTheTopWhenNothingIsKnown()
    {
        var rows = new StoreroomRows();
        rows.Reset(5);
        Assert.Null(rows.Find(10, 0));
        Assert.Equal(0, rows.NextUnknown());
        rows.Learn(0, R(27009, 4, 11));
        rows.Learn(1, R(27009, 3, 12));
        Assert.Equal(2, rows.NextUnknown());
    }

    [Fact]
    public void RemembersRowsInTheGamesOwnOrder()
    {
        // Run 1: rows 31, 32, 30 held 27008/49, 27008/48, 27009/0: not the storeroom's slot order.
        var rows = new StoreroomRows();
        rows.Reset(40);
        rows.Learn(31, R(27008, 49, 100));
        rows.Learn(32, R(27008, 48, 200));
        rows.Learn(30, R(27009, 0, 300));
        Assert.Equal(32, rows.Find(200, 0));
        Assert.Equal(30, rows.Find(300, 0));
        Assert.Equal(0, rows.NextUnknown());
    }

    [Fact]
    public void PrefersARowInTheWantedDye()
    {
        var rows = new StoreroomRows();
        rows.Reset(3);
        rows.Learn(0, R(1, 0, 10, stain: 0));
        rows.Learn(2, R(1, 2, 10, stain: 6));
        Assert.Equal(2, rows.Find(10, 6));
        Assert.Equal(0, rows.Find(10, 5));
    }

    [Fact]
    public void RowsBelowAPlacedPieceMoveUp()
    {
        var rows = new StoreroomRows();
        rows.Reset(4);
        rows.Learn(0, R(1, 0, 10));
        rows.Learn(1, R(1, 1, 11));
        rows.Learn(3, R(1, 3, 13));
        rows.Removed(1);
        Assert.Equal(3, rows.Count);
        Assert.Null(rows.Find(11, 0));
        Assert.Equal(2, rows.Find(13, 0));
        Assert.Equal(0, rows.Find(10, 0));
        Assert.Equal(1, rows.NextUnknown());
    }

    [Fact]
    public void ASlotIsInOneRowOnly()
    {
        var rows = new StoreroomRows();
        rows.Reset(5);
        rows.Learn(1, R(27001, 5, 10));
        rows.Learn(3, R(27001, 5, 10));
        Assert.False(rows.Known.ContainsKey(1));
        Assert.Equal(3, rows.Find(10, 0));
    }

    [Fact]
    public void TellsWhenAKnownRowPicksSomethingElse()
    {
        var rows = new StoreroomRows();
        rows.Reset(5);
        rows.Learn(2, R(27006, 36, 10));
        Assert.True(rows.Matches(2, R(27006, 36, 10)));
        Assert.False(rows.Matches(2, R(27006, 35, 10)));
        Assert.False(rows.Matches(4, R(27006, 36, 10)));
    }

    [Fact]
    public void KnowsWhenEveryRowHasBeenPicked()
    {
        var rows = new StoreroomRows();
        rows.Reset(2);
        rows.Learn(0, R(1, 0, 10));
        rows.Learn(1, R(1, 1, 11));
        Assert.Null(rows.NextUnknown());
        Assert.Null(rows.Find(99, 0));
    }

    [Fact]
    public void DescribesWhatItLearnt()
    {
        var rows = new StoreroomRows();
        rows.Reset(2);
        rows.Learn(1, R(27006, 36, 10));
        Assert.Equal(new[] { "1 → 27006/36 Oak Table" }, rows.Describe(_ => "Oak Table"));
    }
}

public class ModeRouteTests
{
    [Fact]
    public void NothingToDoWhenAlreadyThere() => Assert.Empty(ModeRoute.Plan(EditorMode.Rotate, EditorMode.Rotate, false)!);

    [Fact]
    public void MoveAndRotateAreOnTheBar()
    {
        Assert.Equal(new[] { ModeClick.Rotate }, ModeRoute.Plan(EditorMode.Move, EditorMode.Rotate, false));
        Assert.Equal(new[] { ModeClick.Move }, ModeRoute.Plan(EditorMode.Rotate, EditorMode.Move, false));
    }

    [Fact]
    public void DyeModeIsReachedThroughTheHousingMenu()
    {
        // Recording 1: HousingLayout [-2] closed the editor, the Housing menu came back, HousingMenu [1] chose Furnishing Color.
        Assert.Equal(new[] { ModeClick.CloseEditor, ModeClick.OpenDye }, ModeRoute.Plan(EditorMode.Rotate, EditorMode.Dye, false));
        Assert.Equal(new[] { ModeClick.OpenDye }, ModeRoute.Plan(EditorMode.None, EditorMode.Dye, true));
    }

    [Fact]
    public void BackFromDyeModeOpensIndoorFurnishingsThenTheBarsButton()
    {
        // 0.3.2: Indoor Furnishings opens in Rotate or Move, so the bar's button follows (pressed only if needed).
        Assert.Equal(new[] { ModeClick.CloseDyeList, ModeClick.OpenFurnishings, ModeClick.Rotate }, ModeRoute.Plan(EditorMode.Dye, EditorMode.Rotate, false));
        Assert.Equal(new[] { ModeClick.CloseDyeList, ModeClick.OpenFurnishings, ModeClick.Move }, ModeRoute.Plan(EditorMode.Dye, EditorMode.Move, false));
        Assert.Equal(new[] { ModeClick.OpenFurnishings, ModeClick.Rotate }, ModeRoute.Plan(EditorMode.None, EditorMode.Rotate, true));
    }

    [Fact]
    public void NoWayWhileAPieceIsHeld()
    {
        Assert.Null(ModeRoute.Plan(EditorMode.Place, EditorMode.Rotate, true));
        Assert.Null(ModeRoute.Plan(EditorMode.Place, EditorMode.Dye, false));
    }

    [Fact]
    public void FromNothingOpensTheHousingMenuFirst()
    {
        Assert.Equal(new[] { ModeClick.OpenHousingMenu, ModeClick.OpenFurnishings, ModeClick.Rotate }, ModeRoute.Plan(EditorMode.None, EditorMode.Rotate, false));
        Assert.Equal(new[] { ModeClick.OpenHousingMenu, ModeClick.OpenDye }, ModeRoute.Plan(EditorMode.None, EditorMode.Dye, false));
        Assert.True(ModeRoute.LeadsToHousingMenu(ModeClick.OpenHousingMenu));
        Assert.False(ModeRoute.LeadsToHousingMenu(ModeClick.OpenDye));
    }

    [Fact]
    public void KnowsWhereEachClickLeadsTheEditor()
    {
        Assert.True(ModeRoute.Reached(ModeClick.OpenFurnishings, EditorMode.Rotate));
        Assert.True(ModeRoute.Reached(ModeClick.OpenFurnishings, EditorMode.Move));
        Assert.Equal(EditorMode.Dye, ModeRoute.After(ModeClick.OpenDye));
        Assert.Equal(EditorMode.None, ModeRoute.After(ModeClick.CloseEditor));
    }
}

public class RecordedCallbackTests
{
    [Fact]
    public void ReadsEmptyValuesAsTheGameSendsThem()
    {
        // Recording 1: choosing "Place" was ContextMenu [i:0, i:0, u:0, empty, empty].
        var call = new UiCall("ContextMenu", "i:0 i:{entry} u:0 empty empty", true, "test");
        Assert.Equal(new[] { (ArgKind.Int, 0L), (ArgKind.Int, 0L), (ArgKind.UInt, 0L), (ArgKind.Empty, 0L), (ArgKind.Empty, 0L) }, call.Resolve(UiCallValues.Of(("entry", 0))));
        Assert.Equal("ContextMenu ← [i:0, i:0, u:0, empty, empty] (closes)", call.Describe(UiCallValues.Of(("entry", 0))));
    }

    [Fact]
    public void FindsPlaceFirstInARecordedMenu()
    {
        var menu = new[] { "Place", "Store", "Dye", "Search for Item", "Link", "Search Recipes Using This Material", "Discard", "Sort", " Search in Market Board", " Search the Market" };
        Assert.Equal(0, Menus.FindEntry(menu, new[] { "Place" }));
    }
}
