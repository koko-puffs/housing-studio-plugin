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
    public void TheDefaultPaceIsSteady()
    {
        Assert.Equal(400, Pacing.DefaultMs);
        Assert.Equal(new[] { 150, 400, 1500 }, Pacing.Presets.Select(p => p.Ms));
        Assert.InRange(Pacing.NextDelayMs(Pacing.SteadyMs, 0.25f, 0), 300, 300);
        Assert.InRange(Pacing.NextDelayMs(Pacing.QuickMs, 0.25f, 0), 150, 150);
    }

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
