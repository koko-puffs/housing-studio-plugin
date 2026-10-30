using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class SettleTests
{
    private DateTime now = new(2026, 1, 1);
    private DateTime Frame() => now = now.AddMilliseconds(16);

    [Fact]
    public void NeedsTheSameStateForSeveralFramesAndAMinimumTime()
    {
        var s = new Settle(3, TimeSpan.FromMilliseconds(100));
        Assert.False(s.Update("held", Frame()));
        Assert.False(s.Update("held", Frame()));
        Assert.False(s.Update("held", Frame())); // three frames, but only 32 ms
        for (var i = 0; i < 4; i++) s.Update("held", Frame());
        Assert.True(s.Update("held", Frame()));
    }

    [Fact]
    public void AChangeOrANotReadyFrameStartsOver()
    {
        var s = new Settle(3, TimeSpan.Zero);
        s.Update("a", Frame());
        s.Update("a", Frame());
        Assert.False(s.Update(null, Frame()));
        Assert.False(s.Update("a", Frame()));
        Assert.False(s.Update("b", Frame()));
        s.Update("b", Frame());
        Assert.True(s.Update("b", Frame()));
    }

    [Fact]
    public void APickUpSeenOnlyInTheFrameOfTheClickIsntHeld()
    {
        // 0.2.4: "picks it up (0 ms)" then "dropped": the game had it for a moment only.
        var s = new Settle(3, TimeSpan.FromMilliseconds(50));
        Assert.False(s.Update("0xA1", Frame()));
        Assert.False(s.Update(null, Frame()));
        Assert.False(s.Update(null, Frame()));
    }
}

public class ListReadyTests
{
    private static List<string> Rows(int n, string name = "Simple Hingan Pillar") => Enumerable.Repeat(name, n).ToList();

    [Fact]
    public void BlankRowsArentReady()
    {
        // 0.2.4: "node 20 (tree list items, 32 rows):  |  |  | …" right after the tab opened.
        Assert.Null(ListReady.State(Enumerable.Repeat("", 32).ToList(), 446));
        Assert.Null(ListReady.State(Rows(199).Append("").ToList(), 446));
        Assert.Null(ListReady.State(null, 446));
        Assert.Null(ListReady.State(new List<string>(), 446));
    }

    [Fact]
    public void TooFewRowsForTheStoreroomArentReady()
    {
        Assert.Null(ListReady.State(Rows(32), 446));
        Assert.NotNull(ListReady.State(Rows(200), 446));   // the component holds 200 at once
        Assert.NotNull(ListReady.State(Rows(150), 150));
        Assert.Null(ListReady.State(Rows(150), 0));        // the storeroom count not loaded
    }

    [Fact]
    public void TheStateChangesWithTheCountAndTheRows()
    {
        var a = ListReady.State(Rows(200), 446);
        Assert.NotEqual(a, ListReady.State(Rows(200), 445));
        Assert.NotEqual(a, ListReady.State(Rows(199).Append("White Screen").ToList(), 446));
    }

    [Fact]
    public void SettlesOnlyOnceTheListStopsFillingIn()
    {
        // While loading, the storeroom's count climbed (0.2.4 read "rows 0–199 of 150"); a plateau
        // shorter than the minimum (250 ms in the run) doesn't count; the final state does.
        var t = new DateTime(2026, 1, 1);
        var settle = new Settle(5, TimeSpan.FromMilliseconds(250));
        var phases = new (List<string> Rows, int Count, int Frames)[]
        {
            (Enumerable.Repeat("", 32).ToList(), 150, 10),
            (Rows(150), 150, 12), // 192 ms: not long enough
            (Rows(200), 446, 30),
        };
        var settledAt = -1;
        var frame = 0;
        foreach (var (rows, count, frames) in phases)
            for (var i = 0; i < frames; i++, frame++)
            {
                t = t.AddMilliseconds(16);
                if (settle.Update(ListReady.State(rows, count), t) && settledAt < 0) settledAt = frame;
            }
        Assert.True(settledAt >= 22, $"settled at frame {settledAt}");
    }
}

public class GiveUpTests
{
    [Fact]
    public void TriesGuessedRowsBeforeGivingUp()
    {
        // 0.2.4 gave up after 6 picks: the rest were guessed to be other pieces' groups and never read.
        var rows = new StoreroomRows();
        rows.Reset(6);
        rows.Learn(0, new StoreroomRow(1, 0, 11, 0), groupSize: 6); // guesses rows 1–5 are 11 too
        var tried = new HashSet<int> { 0 };
        Assert.Null(rows.NextUnknownFor("Stage Panel", tried));
        Assert.Equal(1, rows.NextCandidate("Stage Panel", 50, tried));
    }

    [Fact]
    public void RulesOutOnlyRowsReadToHoldSomethingElse()
    {
        var rows = new StoreroomRows();
        rows.Reset(4);
        rows.LearnNames(0, new[] { "Simple Hingan Pillar", "Planter Partition" });
        rows.Learn(2, new StoreroomRow(1, 2, 13, 0));
        var tried = new HashSet<int>();
        Assert.Equal(3, rows.NextCandidate("Stage Panel", 50, tried));
        tried.Add(3);
        Assert.Null(rows.NextCandidate("Stage Panel", 50, tried));
    }
}
