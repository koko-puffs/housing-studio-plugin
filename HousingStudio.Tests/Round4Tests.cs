using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class NameRefreshTests
{
    private static string Name(uint id) => id switch
    {
        11 => "Simple Hingan Pillar",
        12 => "Planter Partition",
        13 => "Verdant Partition",
        14 => "White Screen",
        _ => $"Item {id}",
    };

    [Fact]
    public void ForgetsRememberedRowsThatMovedWhenTheNamesAreReadAgain()
    {
        // 0.2.3: after placing, "Row 0 now picks storeroom 27004/6, not what it did": the list shifts and re-sorts.
        var rows = new StoreroomRows();
        rows.Reset(6);
        rows.LearnNames(0, new[] { "Simple Hingan Pillar", "Simple Hingan Pillar", "Planter Partition", "Verdant Partition", "White Screen", "White Screen" });
        rows.Learn(2, new StoreroomRow(27001, 45, 12, 0));
        rows.Learn(4, new StoreroomRow(27005, 16, 14, 0));
        rows.Removed(0);
        var forgotten = rows.RefreshNames(0, new[] { "Simple Hingan Pillar", "White Screen", "Planter Partition", "Verdant Partition", "White Screen" }, Name);
        // Row 1 was Planter Partition (shifted up from 2) but now reads White Screen: forgotten. Row 3 (White Screen) now reads Verdant Partition: forgotten.
        Assert.Equal(2, forgotten);
        Assert.Empty(rows.Known);
        Assert.Equal(new[] { 1, 4 }, rows.ByName("White Screen", 14));
        Assert.Equal(new[] { 2 }, rows.ByName("Planter Partition", 12));
    }

    [Fact]
    public void KeepsRememberedRowsTheNamesStillAgreeWith()
    {
        var rows = new StoreroomRows();
        rows.Reset(3);
        rows.Learn(1, new StoreroomRow(27001, 45, 12, 0));
        Assert.Equal(0, rows.RefreshNames(0, new[] { "Simple Hingan Pillar", "Planter Partition", "Verdant Partition" }, Name));
        Assert.Equal(1, rows.Find(12, 0));
    }

    [Fact]
    public void ReadNamesReplaceGroupGuesses()
    {
        var rows = new StoreroomRows();
        rows.Reset(5);
        rows.Learn(0, new StoreroomRow(1, 0, 11, 0), groupSize: 3);
        Assert.NotEmpty(rows.Inferred);
        rows.RefreshNames(0, new[] { "Simple Hingan Pillar", "Planter Partition" }, Name);
        Assert.Empty(rows.Inferred);
        // Never a row read to hold another piece: the next row worth trying for White Screen is past the names.
        Assert.Equal(2, rows.NextUnknownFor("White Screen"));
    }
}

public class CategoryTests
{
    [Theory]
    [InlineData("Furnishing", 1)]
    [InlineData("Table", 2)]
    [InlineData("Tabletop", 3)]
    [InlineData("Wall-mounted", 4)]
    [InlineData("Rug", 5)]
    [InlineData("", 1)]
    public void PicksTheListFilterForAPiecesCategory(string uiCategory, int filter)
    {
        Assert.Equal(filter, ListCategories.FilterFor(uiCategory));
        Assert.True(filter < ListCategories.Filters.Length);
    }
}

public class SpeedTests
{
    private DateTime now = new(2026, 1, 1);

    [Fact]
    public void RunsWaitsAndChecksThatAreReadyInTheSameFrame()
    {
        var q = new StepQueue(() => now) { StepsPerTick = 8 };
        var ran = new List<string>();
        q.Begin(new[]
        {
            new Step { Label = "ready", Run = () => { ran.Add("ready"); return StepOutcome.Done; } },
            new Step { Label = "find", Run = () => { ran.Add("find"); return StepOutcome.Done; } },
            Step.Act("pick", () => { ran.Add("pick"); return StepOutcome.Done; }),
            new Step { Label = "menu", Run = () => { ran.Add("menu"); return StepOutcome.Done; } },
        });
        q.Tick();
        // Both checks in one frame; a click always starts a frame of its own.
        Assert.Equal(new[] { "ready", "find" }, ran);
        q.Tick();
        Assert.Equal(new[] { "ready", "find", "pick", "menu" }, ran);
        Assert.Equal(QueueState.Finished, q.State);
    }

    [Fact]
    public void TimesEveryStep()
    {
        var q = new StepQueue(() => now);
        var timed = new List<(string, double)>();
        q.Timed += (s, t) => timed.Add((s.Key ?? s.Label, t.TotalMilliseconds));
        var ready = false;
        q.Begin(new[] { new Step { Label = "menu opens", Key = "menu", Run = () => ready ? StepOutcome.Done : StepOutcome.Waiting } });
        q.Tick();
        now = now.AddMilliseconds(120);
        ready = true;
        q.Tick();
        Assert.Equal(new[] { ("menu", 120.0) }, timed);
    }

    [Fact]
    public void TheDefaultPaceIsQuickAndEachStepIsQuickerThanTheLast()
    {
        Assert.Equal(Pace.Quick, Pacing.Default);
        // 0.7.14: Steady is 0.3.2's pace, as tested in game; Quick what Daring was.
        var quick = Pacing.Profile(Pace.Quick);
        Assert.Equal((50, 200, 0.65), (quick.PieceGapMs, quick.ClickGapMs, quick.WaitScale));
        Assert.Equal((150, 300, 1.0), (Pacing.Profile(Pace.Steady).PieceGapMs, Pacing.Profile(Pace.Steady).ClickGapMs, Pacing.Profile(Pace.Steady).WaitScale));
        Assert.True(Pacing.Profile(Pace.Daring).ErrandGapMs < quick.ErrandGapMs);
        // A pace chosen before keeps its speed.
        Assert.Equal(Pace.Quick, Pacing.FromBefore0714(Pace.Daring));
        Assert.Equal(Pace.Steady, Pacing.FromBefore0714(Pace.Quick));
        Assert.Equal(Pace.Steady, Pacing.FromBefore0714(Pace.Steady));
        var (steady, daring) = (Pacing.Profile(Pace.Steady), Pacing.Profile(Pace.Daring));
        Assert.True(steady.PieceGapMs > quick.PieceGapMs && quick.PieceGapMs > daring.PieceGapMs);
        Assert.True(steady.ClickGapMs > quick.ClickGapMs && quick.ClickGapMs > daring.ClickGapMs);
        Assert.True(steady.WaitScale > quick.WaitScale && quick.WaitScale > daring.WaitScale);
        Assert.True(daring.PieceGapMs >= Pacing.MinimumMs);
        Assert.Equal(TimeSpan.FromMilliseconds(120), steady.Wait(120));
        Assert.Equal(TimeSpan.FromMilliseconds(78), quick.Wait(120));
        Assert.Equal(TimeSpan.FromMilliseconds(48), daring.Wait(120));
        // An unknown value (a hand-edited config) is the default's.
        Assert.Equal(quick, Pacing.Profile((Pace)7));
    }

    [Theory]
    [InlineData(1500, Pace.Steady)]
    [InlineData(400, Pace.Quick)]
    [InlineData(150, Pace.Quick)]
    public void OlderPacesMoveToTheNewOnes(int intervalMs, Pace expected) => Assert.Equal(expected, Pacing.FromOldInterval(intervalMs));

    [Fact]
    public void SummarisesWhereTheTimeWent()
    {
        var t = new StepTimings();
        t.AddPiece(RunPhase.Place, TimeSpan.FromSeconds(2));
        t.AddPiece(RunPhase.Place, TimeSpan.FromSeconds(4));
        t.AddStep("pick row", TimeSpan.FromMilliseconds(20));
        t.AddStep("placed", TimeSpan.FromMilliseconds(400));
        t.AddStep("placed", TimeSpan.FromMilliseconds(600));
        Assert.Equal(TimeSpan.FromSeconds(3), t.PerPiece(RunPhase.Place));
        Assert.Equal("placed", t.Slowest()[0].Key);
        var lines = t.Summary();
        Assert.Contains(lines, l => l.Contains("3.0 s each"));
        Assert.Contains(lines, l => l.Contains("placed: 2×, 500 ms, 600 ms"));
    }
}
