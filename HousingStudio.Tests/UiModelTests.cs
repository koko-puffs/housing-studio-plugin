using System.Numerics;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

public class UiWordsTests
{
    [Theory]
    [InlineData(0.4, "under a minute")]
    [InlineData(1.2, "about a minute")]
    [InlineData(12.4, "about 12 minutes")]
    [InlineData(19.4, "about 19 minutes")]
    [InlineData(23, "about 25 minutes")]
    [InlineData(27.3, "about 25 minutes")]
    [InlineData(52, "about 50 minutes")]
    [InlineData(61, "about an hour")]
    [InlineData(95, "about 1½ hours")]
    [InlineData(118, "about 2 hours")]
    public void SaysHowLongInPlainWords(double minutes, string expected) =>
        Assert.Equal(expected, Words.About(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData("Small", "Cottage (small)")]
    [InlineData("Medium", "House (medium)")]
    [InlineData("Large", "Mansion (large)")]
    [InlineData("Apartment", "Apartment")]
    [InlineData(null, "Any home")]
    public void NamesHomesTheGamesWay(string? size, string expected) => Assert.Equal(expected, Words.Home(size));

    [Fact]
    public void CountsListsAndShopping()
    {
        Assert.Equal("1 piece", Words.Count(1, "piece"));
        Assert.Equal("3 pieces", Words.Count(3, "piece"));
        Assert.Equal("A, B and C", Words.List(new[] { "A", "B", "C" }));
        Assert.Equal("A and B", Words.List(new[] { "A", "B" }));
        Assert.Equal("A, B and 2 more", Words.Some(new[] { "A", "B", "C", "D" }));
        Assert.Equal("A, B and C", Words.Some(new[] { "A", "B", "C" }));
        Assert.Equal("2 dyes to buy, no pieces", Words.ToBuy(0, 2));
        Assert.Equal("1 piece to buy, no dyes", Words.ToBuy(1, 0));
        Assert.Equal("3 pieces and 1 dye to buy", Words.ToBuy(3, 1));
        Assert.Equal("Nothing to buy", Words.ToBuy(0, 0));
        Assert.Equal("Walls, floors and lights", Words.Kinds(new[] { "Wall", "Floor", "Light", "Wall" }, true));
        Assert.Equal("window and door", Words.Kinds(new[] { "Window", "Door" }, false, plural: false));
    }

    [Fact]
    public void SaysWhenRelativeToToday()
    {
        var now = new DateTime(2026, 10, 30, 9, 0, 0);
        string Clock(DateTime t) => t.ToString("HH.mm");
        Assert.Equal("today 05.24", When.Relative(new DateTime(2026, 10, 30, 5, 24, 0), now, Clock));
        Assert.Equal("yesterday", When.Relative(new DateTime(2026, 10, 29, 23, 0, 0), now, Clock));
        Assert.Equal("Monday", When.Relative(new DateTime(2026, 10, 26, 12, 0, 0), now, Clock));
        Assert.Equal("3 Oct", When.Relative(new DateTime(2026, 10, 3), now, Clock));
        Assert.Equal("3 Oct 2025", When.Relative(new DateTime(2025, 10, 3), now, Clock));
    }
}

public class UiPaceTests
{
    [Theory]
    [InlineData(Pace.Careful, 1500)]
    [InlineData(Pace.Steady, 400)]
    [InlineData(Pace.Quick, 150)]
    public void EachChoiceIsAPauseAboveTheFloor(Pace pace, int ms)
    {
        Assert.Equal(ms, PaceSetting.Ms(pace));
        Assert.True(PaceSetting.Ms(pace) >= Pacing.MinimumMs);
        Assert.Equal(pace, PaceSetting.Of(ms));
    }

    [Theory]
    [InlineData(150, Pace.Quick)]
    [InlineData(275, Pace.Quick)]
    [InlineData(400, Pace.Steady)]
    [InlineData(900, Pace.Steady)]
    [InlineData(1500, Pace.Careful)]
    [InlineData(2500, Pace.Careful)]
    public void OlderPausesShowAsTheNearestChoice(int ms, Pace expected) => Assert.Equal(expected, PaceSetting.Of(ms));

    [Fact]
    public void TheDefaultPaceIsSteady() => Assert.Equal(Pace.Steady, PaceSetting.Of(Pacing.DefaultMs));

    [Fact]
    public void TheChoicesAreThePresets() =>
        Assert.Equal(Pacing.Presets.Select(p => p.Ms).OrderBy(x => x), new[] { Pace.Careful, Pace.Steady, Pace.Quick }.Select(PaceSetting.Ms).OrderBy(x => x));
}

public class UiPieceRowsTests
{
    private static PlanEntry E(int key, uint item, PieceState state, byte stain = 0, Floor floor = Floor.Ground) =>
        new() { Target = new Target(key, item, new Vector3(key, 0, 0), 0, stain, floor, false), State = state };

    private static string Name(uint id) => id switch
    {
        1 => "Hingan Bookshelf",
        2 => "Summoning Bell",
        3 => "White Screen",
        _ => "Oak Table",
    };

    private static List<PlanEntry> Sample()
    {
        var list = new List<PlanEntry>();
        for (var i = 0; i < 9; i++) list.Add(E(i, 1, PieceState.FromStoreroom, 7));
        for (var i = 9; i < 13; i++) list.Add(E(i, 1, PieceState.FromStoreroom, 3));
        list.Add(E(20, 2, PieceState.Move));
        list.Add(E(21, 3, PieceState.FromInventory, 5));
        list.Add(E(22, 4, PieceState.InPlace));
        list.Add(E(23, 4, PieceState.Missing, floor: Floor.Upper));
        list.Add(E(24, 1, PieceState.FromStoreroom, 7, Floor.Basement));
        return list;
    }

    [Fact]
    public void CountsEachChip()
    {
        var c = PieceRows.Counts(Sample());
        Assert.Equal(new FilterCounts(16, 1, 1, 18), c);
        Assert.Equal(16, c.Of(PieceFilter.ToDo));
        Assert.Equal(new FilterCounts(0, 0, 0, 18), PieceRows.Counts(Sample(), statesKnown: false));
    }

    [Fact]
    public void StacksTheSameItemInTheSameDyeAndState()
    {
        var groups = PieceRows.Build(Sample(), PieceFilter.All, Name, string.Empty);
        Assert.Equal(new[] { Floor.Ground, Floor.Upper, Floor.Basement }, groups.Select(g => g.Floor));
        var ground = groups[0];
        Assert.Equal(16, ground.Count);
        // Moves first, then the storeroom, bags, missing, and what's in place last.
        Assert.Equal(PieceState.Move, ground.Rows[0].State);
        var shelves = ground.Rows.Where(r => r.ItemId == 1).ToList();
        Assert.Equal(2, shelves.Count);
        Assert.Equal(9, shelves[0].Count);
        Assert.Equal(7, shelves[0].Stain);
        Assert.Equal(4, shelves[1].Count);
        Assert.Equal(PieceState.InPlace, ground.Rows[^1].State);
    }

    [Fact]
    public void FiltersBySearchAndChip()
    {
        var todo = PieceRows.Build(Sample(), PieceFilter.ToDo, Name, string.Empty);
        Assert.DoesNotContain(todo.SelectMany(g => g.Rows), r => r.State is PieceState.InPlace or PieceState.Missing);
        var missing = PieceRows.Build(Sample(), PieceFilter.Missing, Name, string.Empty);
        Assert.Single(missing);
        Assert.Equal(Floor.Upper, missing[0].Floor);
        var shelves = PieceRows.Build(Sample(), PieceFilter.All, Name, "  hingan ");
        Assert.All(shelves.SelectMany(g => g.Rows), r => Assert.Equal(1u, r.ItemId));
        Assert.Equal(14, shelves.Sum(g => g.Count));
    }

    [Fact]
    public void WithoutStatesEveryPieceIsListedOnce()
    {
        var groups = PieceRows.Build(Sample(), PieceFilter.ToDo, Name, string.Empty, statesKnown: false);
        Assert.Equal(18, groups.Sum(g => g.Count));
        Assert.All(groups.SelectMany(g => g.Rows), r => Assert.Null(r.State));
        // Without states, the same item in the same dye is one row whatever it was matched to.
        Assert.Single(groups[0].Rows, r => r.ItemId == 4);
    }

    [Fact]
    public void SummarisesWhatStartDoes()
    {
        var entries = Sample();
        entries.Add(new PlanEntry { Target = new Target(30, 4, Vector3.Zero, 0, 9, Floor.Ground, false), State = PieceState.InPlace, Placed = new PlacedPiece(1, 4, Vector3.Zero, 0, 2) });
        var s = StartSummary.Of(entries, dyeing: true);
        Assert.Single(s.Moves);
        Assert.Equal(14, s.Storeroom);
        Assert.Equal(1, s.Bags);
        Assert.Equal(15, s.Places);
        // Every dyed storeroom piece comes undyed here, the bag piece too, and the placed one is in another dye.
        Assert.Equal(16, s.Dyes);
        Assert.Equal(2, s.InPlace);
        Assert.Equal(1, s.Missing);
        Assert.Equal(0, StartSummary.Of(entries, dyeing: false).Dyes);
        Assert.True(s.Estimate(entries, Pacing.SteadyMs) > TimeSpan.FromSeconds(30));
    }
}

public class UiFixtureTickTests
{
    [Fact]
    public void TicksAreKeptPerDesign()
    {
        var store = new Dictionary<string, List<string>>();
        var a = FixtureTicks.DesignKey(@"C:\Users\Me\kokomed2.json", "kokomed2");
        var b = FixtureTicks.DesignKey(null, "The Lantern Library");
        var wall = FixtureTicks.KeyOf("Ground Floor", "Wall", 123);
        var floor = FixtureTicks.KeyOf("Ground Floor", "Floor", 456);

        FixtureTicks.Set(store, a, wall, true);
        FixtureTicks.Set(store, a, wall, true);
        Assert.True(FixtureTicks.IsDone(store, a, wall));
        Assert.False(FixtureTicks.IsDone(store, a, floor));
        Assert.False(FixtureTicks.IsDone(store, b, wall));
        Assert.Equal(1, FixtureTicks.CountDone(store, a, new[] { wall, floor }));
        Assert.Equal(a, FixtureTicks.DesignKey("c:/users/me/KOKOMED2.json", "x"));

        FixtureTicks.Set(store, a, wall, false);
        Assert.False(store.ContainsKey(a));
        FixtureTicks.Set(store, b, floor, false);
        Assert.Empty(store);
    }
}

public class UiStepWordsTests
{
    [Theory]
    [InlineData("Set Hingan Bookshelf at its spot (the editor's set-down function)", "Setting it at its spot")]
    [InlineData("Pick up Hingan Bookshelf (the editor's pick-up function)", "Picking it up")]
    [InlineData("Open the Storeroom tab: HousingGoods ← [i:11]", "Opening the Storeroom tab")]
    [InlineData("Find Hingan Bookshelf's row in the list", "Finding its row in the list")]
    [InlineData("The game picks it up", "The game picks it up")]
    [InlineData("You: put Hingan Bookshelf down anywhere (it's in bag 1, row 2, slot 3); it'll be set at its spot", "Put it down anywhere; it'll be set at its spot")]
    public void SaysTheStepInPlainWords(string label, string expected) =>
        Assert.Equal(expected, StepWords.Friendly(label, "Hingan Bookshelf"));

    [Fact]
    public void NothingWhenThereIsNoStep() => Assert.Null(StepWords.Friendly(null, "x"));
}

public class UiSvgPathTests
{
    [Fact]
    public void ReadsLinesAndRelativeMoves()
    {
        var subs = SvgPath.Parse("M18 6 6 18m0-12 12 12");
        Assert.Equal(2, subs.Count);
        Assert.Equal(new[] { new Vector2(18, 6), new Vector2(6, 18) }, subs[0].Points);
        Assert.Equal(new[] { new Vector2(6, 6), new Vector2(18, 18) }, subs[1].Points);
    }

    [Fact]
    public void ReadsPackedNumbersAndClosesPaths()
    {
        var subs = SvgPath.Parse("M3 10a2 2 0 0 1 .709-1.528l7-5.999h4v2H5z");
        Assert.Single(subs);
        Assert.True(subs[0].Closed);
        var end = subs[0].Points[^1];
        Assert.Equal(new Vector2(5, 4.473f), end, new Vec2Comparer());
    }

    [Fact]
    public void ArcsStayOnTheirCircle()
    {
        // A whole circle of radius 10 about (12, 12), as two arcs.
        var subs = SvgPath.Parse("M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0");
        Assert.All(subs[0].Points, p => Assert.InRange(Vector2.Distance(p, new Vector2(12, 12)), 9.99f, 10.01f));
        Assert.True(subs[0].Points.Count > 16);
    }

    [Fact]
    public void CurvesEndWhereTheySay()
    {
        var subs = SvgPath.Parse("M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5");
        Assert.Equal(new Vector2(12, 3), subs[0].Points[^1], new Vec2Comparer());
    }

    private sealed class Vec2Comparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;
        public int GetHashCode(Vector2 v) => 0;
    }
}

public class UiDashesTests
{
    /// <summary>The Start view's drop zone at the widths 0.3.0 hung on (321 of 324 sizes stalled its dash walk).</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void TheDropZonesEdgeAlwaysEnds(float scale)
    {
        var h = (40 + 56 + 14 + 22 * 1.15f + 6 + 19.5f + 14 + 40 + 40) * scale;
        var thickness = 1.5f * scale;
        for (var w = 400; w <= 1600; w++)
        {
            var min = new Vector2(100.3f, 200.7f) + new Vector2(thickness / 2);
            var max = new Vector2(100.3f + w, 200.7f + h) - new Vector2(thickness / 2);
            var outline = Dashes.RoundedRect(min, max, 16 * scale - thickness / 2);
            var dashes = Dashes.Along(outline, thickness * 6);
            var perimeter = 0f;
            for (var i = 1; i < outline.Count; i++) perimeter += Vector2.Distance(outline[i - 1], outline[i]);
            Assert.Equal((int)MathF.Round(perimeter / (thickness * 6)), dashes.Count);
            foreach (var d in dashes)
            {
                Assert.True(d.Length is >= 2 and <= 16, $"a dash of {d.Length} points at width {w}");
                var len = 0f;
                for (var i = 1; i < d.Length; i++) len += Vector2.Distance(d[i - 1], d[i]);
                Assert.InRange(len, thickness * 2.5f, thickness * 3.5f);
            }
        }
    }

    [Fact]
    public void DegenerateOutlinesGiveNoDashesOrFew()
    {
        Assert.Empty(Dashes.Along(new[] { Vector2.Zero }, 6));
        Assert.Empty(Dashes.Along(new[] { Vector2.Zero, Vector2.Zero }, 6));
        Assert.Empty(Dashes.Along(new[] { Vector2.Zero, Vector2.One }, 0));
        Assert.Empty(Dashes.Along(new[] { Vector2.Zero, Vector2.One }, float.NaN));
        Assert.Single(Dashes.Along(new[] { Vector2.Zero, new Vector2(1, 0) }, 100));
        Assert.Equal(Dashes.MaxDashes, Dashes.Along(new[] { Vector2.Zero, new Vector2(1e7f, 0) }, 1).Count);
        // A rectangle smaller than its radius still closes.
        var tiny = Dashes.RoundedRect(Vector2.Zero, new Vector2(4, 4), 16);
        Assert.Equal(tiny[0], tiny[^1]);
        Assert.NotEmpty(Dashes.Along(tiny, 3));
    }
}
