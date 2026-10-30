using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class StepQueueTests
{
    private DateTime now = new(2026, 1, 1);
    private StepQueue Queue() => new(() => now);
    private void Advance(double ms) => now = now.AddMilliseconds(ms);

    [Fact]
    public void RunsStepsInOrderOnePerFrameAtMost()
    {
        var log = new List<string>();
        var q = Queue();
        q.Begin(new[]
        {
            Step.Act("a", () => { log.Add("a"); return StepOutcome.Done; }),
            Step.Act("b", () => { log.Add("b"); return StepOutcome.Done; }),
        });
        q.Tick();
        Assert.Equal(new[] { "a" }, log);
        q.Tick();
        Assert.Equal(new[] { "a", "b" }, log);
        Assert.Equal(QueueState.Finished, q.State);
    }

    [Fact]
    public void WaitsUntilAConditionAndStopsAtItsTimeout()
    {
        var ready = false;
        var cleaned = false;
        var q = Queue();
        q.Begin(new[] { Step.Wait("the dye window", () => ready, TimeSpan.FromSeconds(2)) }, new[] { Step.Act("close", () => { cleaned = true; return StepOutcome.Done; }) });
        q.Tick();
        Advance(1000);
        q.Tick();
        Assert.Equal(QueueState.Running, q.State);
        Advance(1500);
        q.Tick();
        Assert.Equal(QueueState.Stopped, q.State);
        Assert.Contains("Timed out: the dye window", q.Message);
        Assert.True(cleaned);
    }

    [Fact]
    public void AWaitThatComesTrueGoesOn()
    {
        var ready = false;
        var q = Queue();
        q.Begin(new[] { Step.Wait("menu", () => ready, TimeSpan.FromSeconds(2)) });
        q.Tick();
        ready = true;
        Advance(100);
        q.Tick();
        Assert.Equal(QueueState.Finished, q.State);
    }

    [Fact]
    public void ATimeoutCanSkipJustThisPiece()
    {
        var q = Queue();
        q.Begin(new[] { Step.Wait("landed", () => false, TimeSpan.FromSeconds(1), StepStatus.SkipJob) });
        q.Tick();
        Advance(1500);
        q.Tick();
        Assert.Equal(QueueState.Skipped, q.State);
    }

    [Fact]
    public void StepByStepHoldsBeforeEveryActionButNotWaits()
    {
        var acted = 0;
        var q = Queue();
        q.StepByStep = true;
        q.Begin(new[]
        {
            Step.Wait("ready", () => true, TimeSpan.FromSeconds(1)),
            Step.Act("click", () => { acted++; return StepOutcome.Done; }),
        });
        q.Tick();
        Assert.Equal(QueueState.Running, q.State);
        q.Tick();
        Assert.Equal(QueueState.AwaitingContinue, q.State);
        q.Tick();
        Assert.Equal(0, acted);
        Assert.Equal("click", q.Current!.Label);
        q.Continue();
        q.Tick();
        Assert.Equal(1, acted);
        Assert.Equal(QueueState.Finished, q.State);
    }

    [Fact]
    public void HonoursAStepsDelay()
    {
        var ran = false;
        var q = Queue();
        q.Begin(new[] { Step.Act("x", () => { ran = true; return StepOutcome.Done; }, TimeSpan.FromMilliseconds(300)) });
        q.Tick();
        Assert.False(ran);
        Advance(350);
        q.Tick();
        Assert.True(ran);
    }

    [Fact]
    public void AStepThatThrowsStopsWithItsMessage()
    {
        var q = Queue();
        q.Begin(new[] { Step.Act("boom", () => throw new InvalidOperationException("no addon")) });
        q.Tick();
        Assert.Equal(QueueState.Stopped, q.State);
        Assert.Contains("no addon", q.Message);
    }

    [Fact]
    public void KeepsAGapBetweenClicks()
    {
        var clicks = 0;
        var q = Queue();
        q.ActionGap = TimeSpan.FromMilliseconds(300);
        q.Begin(new[]
        {
            Step.Act("one", () => { clicks++; return StepOutcome.Done; }),
            Step.Wait("quick wait", () => true, TimeSpan.FromSeconds(1)),
            Step.Act("two", () => { clicks++; return StepOutcome.Done; }),
        });
        q.Tick();
        q.Tick();
        q.Tick();
        Assert.Equal(1, clicks);
        Advance(310);
        q.Tick();
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void AStepCanFinishThePieceEarly()
    {
        var ran = new List<string>();
        var q = Queue();
        q.Begin(new[]
        {
            Step.Act("check", () => { ran.Add("check"); q.FinishAfterThis(); return StepOutcome.Done; }),
            Step.Act("never", () => { ran.Add("never"); return StepOutcome.Done; }),
        });
        q.Tick();
        q.Tick();
        Assert.Equal(new[] { "check" }, ran);
        Assert.Equal(QueueState.Finished, q.State);
    }

    [Fact]
    public void StepsCanAddMoreAfterThemselves()
    {
        var log = new List<string>();
        var q = Queue();
        q.Begin(new[]
        {
            Step.Act("find", () =>
            {
                log.Add("find");
                q.InsertNext(new[] { Step.Act("found", () => { log.Add("found"); return StepOutcome.Done; }) });
                return StepOutcome.Done;
            }),
            Step.Act("last", () => { log.Add("last"); return StepOutcome.Done; }),
        });
        for (var i = 0; i < 5; i++) q.Tick();
        Assert.Equal(new[] { "find", "found", "last" }, log);
    }
}

public class RunSequenceTests
{
    private static PlanEntry E(int key, PieceState state, bool late = false, Floor floor = Floor.Ground, float y = 0) =>
        new()
        {
            Target = new Target(key, 10, new Vector3(0, y, 0), 0, 0, floor, late),
            State = state,
            Placed = state is PieceState.Move or PieceState.InPlace ? new PlacedPiece(100 + key, 10, Vector3.One, 0, 0) : null,
            Stock = state is PieceState.FromInventory or PieceState.FromStoreroom ? new StockSlot(state == PieceState.FromInventory ? StockSource.Inventory : StockSource.Storeroom, 0, key, 10, 0, 1) : null,
        };

    [Fact]
    public void MovesThenPlacesFloorPiecesBeforeWhatSitsOnThem()
    {
        var jobs = RunSequence.Build(new[]
        {
            E(0, PieceState.FromInventory, late: true),
            E(1, PieceState.Move, late: true),
            E(2, PieceState.FromStoreroom),
            E(3, PieceState.Move),
            E(4, PieceState.InPlace),
            E(5, PieceState.Missing),
        });
        Assert.Equal(new[] { 3, 2, 1, 0 }, jobs.Select(j => j.Target.Key));
        Assert.Equal(new[] { JobKind.Move, JobKind.PlaceFromStoreroom, JobKind.Move, JobKind.PlaceFromBags }, jobs.Select(j => j.Kind));
        Assert.Equal(103, jobs[0].Key);
        Assert.Null(jobs[1].Key);
    }

    [Fact]
    public void GoesFloorByFloorWithinEachTier()
    {
        var jobs = RunSequence.Build(new[]
        {
            E(0, PieceState.Move, floor: Floor.Upper, y: 8),
            E(1, PieceState.Move, floor: Floor.Basement, y: -5),
            E(2, PieceState.Move, floor: Floor.Ground),
        });
        Assert.Equal(new[] { 1, 2, 0 }, jobs.Select(j => j.Target.Key));
    }
}

public class DyePlannerTests
{
    private static DyeNeed N(int key, byte want, byte has, float y = 0) => new(key, new Target(key, 10, new Vector3(0, y, 0), 0, want, Floor.Ground, false), has);

    [Fact]
    public void DyesWhatItHasDyesForAndListsTheRest()
    {
        var owned = new Dictionary<byte, int> { [5] = 1, [7] = 3 };
        var plan = DyePlanner.Plan(new[] { N(0, 5, 0), N(1, 5, 0), N(2, 7, 0), N(3, 9, 0), N(4, 5, 5) }, owned);
        Assert.Equal(new[] { 0, 2 }, plan.Jobs.Select(j => j.Key!.Value));
        Assert.Equal(new[] { 1, 3 }, plan.Skipped.Select(s => s.Key));
        Assert.Contains(((byte)5, 2, 1), plan.Short);
        Assert.Contains(((byte)9, 1, 0), plan.Short);
        Assert.DoesNotContain(plan.Short, s => s.Stain == 7);
    }

    [Fact]
    public void LeavesUndyeingToYou()
    {
        var plan = DyePlanner.Plan(new[] { N(0, 0, 4) }, new Dictionary<byte, int>());
        Assert.Empty(plan.Jobs);
        Assert.Single(plan.Undye);
    }

    [Fact]
    public void DyesLowPiecesFirst()
    {
        var plan = DyePlanner.Plan(new[] { N(0, 5, 0, y: 3), N(1, 5, 0, y: 0) }, new Dictionary<byte, int> { [5] = 5 });
        Assert.Equal(new[] { 1, 0 }, plan.Jobs.Select(j => j.Key!.Value));
    }

    [Fact]
    public void CountsDyesToBuyForTheWholeDesign()
    {
        var targets = new[]
        {
            new Target(0, 10, Vector3.Zero, 0, 5, Floor.Ground, false),
            new Target(1, 10, Vector3.One, 0, 5, Floor.Ground, false),
            new Target(2, 11, Vector3.One * 2, 0, 6, Floor.Ground, false),
            new Target(3, 12, Vector3.One * 3, 0, 6, Floor.Ground, false),
        };
        var placed = new[] { new PlacedPiece(50, 11, Vector3.One * 2, 0, 6) };
        var stock = new[] { new StockSlot(StockSource.Inventory, 0, 0, 10, 0, 2) };
        var plan = Matcher.Build(targets, placed, stock);
        var buy = DyePlanner.ToBuy(plan, new Dictionary<byte, int> { [5] = 1 });
        // Two pieces from the bags want dye 5 (one owned); the missing piece wants dye 6.
        Assert.Equal(new[] { ((byte)5, 1), ((byte)6, 1) }, buy);
    }
}

public class UiCallTests
{
    [Fact]
    public void ReadsTypedValuesAndBlanks()
    {
        var call = new UiCall("HousingGoods", "i:1 i:{index}", true, "test");
        Assert.Equal(new[] { new UiArg(ArgKind.Int, 1, null), new UiArg(ArgKind.Int, 0, "index") }, call.Args);
        Assert.Equal(new[] { (ArgKind.Int, 1L), (ArgKind.Int, 12L) }, call.Resolve(UiCallValues.Of(("index", 12))));
        Assert.Equal("HousingGoods ← [i:1, i:12] (closes)", call.Describe(UiCallValues.Of(("index", 12))));
    }

    [Fact]
    public void ReadsUnsignedAndBooleans()
    {
        var args = UiCall.Parse("u:7, b:true i:-1");
        Assert.Equal(new[] { new UiArg(ArgKind.UInt, 7, null), new UiArg(ArgKind.Bool, 1, null), new UiArg(ArgKind.Int, -1, null) }, args);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("x:1")]
    [InlineData("i:one")]
    public void RefusesWhatIsntAValue(string spec) => Assert.Throws<FormatException>(() => UiCall.Parse(spec));

    [Fact]
    public void SaysWhichBlankIsMissing()
    {
        var call = new UiCall("ContextMenu", "i:0 i:{entry} i:0", true, "test");
        Assert.Throws<InvalidOperationException>(() => call.Resolve());
        Assert.Contains("{entry}", call.Describe());
    }
}

public class MenuTests
{
    [Fact]
    public void FindsAnEntryByItsWordsExactFirst()
    {
        var entries = new[] { "Place Furnishing", "Place", "Discard" };
        Assert.Equal(1, Menus.FindEntry(entries, new[] { "place" }));
        Assert.Equal(0, Menus.FindEntry(new[] { "Place Furnishing", "Discard" }, new[] { "Place" }));
        Assert.Equal(-1, Menus.FindEntry(entries, new[] { "Dye" }));
    }

    [Fact]
    public void IgnoresTheGamesFormattingCharacters()
    {
        Assert.Equal(0, Menus.FindEntry(new[] { "\u0002Place\u0003 " }, new[] { "Place" }));
    }
}
