using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class StoreroomRowNameTests
{
    // Run 2's top of the list: grouped by item.
    private static readonly string[] Names =
    {
        "Simple Hingan Pillar", "Simple Hingan Pillar", "Simple Hingan Pillar", "Planter Partition", "Planter Partition",
        "Verdant Partition", "Verdant Partition", "White Rectangular Partition", "White Rectangular Partition", "Stage Panel",
    };

    [Fact]
    public void FindsRowsByTheListsOwnNames()
    {
        var rows = new StoreroomRows();
        rows.Reset(Names.Length);
        Assert.Equal(new[] { 9 }, rows.ByName(Names, "Stage Panel", 50));
        Assert.Equal(new[] { 5, 6 }, rows.ByName(Names, "verdant partition", 30));
        Assert.Empty(rows.ByName(Names, "Oak Table", 99));
    }

    [Fact]
    public void LeavesOutRowsKnownToHoldSomethingElse()
    {
        var rows = new StoreroomRows();
        rows.Reset(Names.Length);
        rows.Learn(5, new StoreroomRow(27002, 36, 31, 0)); // turned out to be another item with the same name
        Assert.Equal(new[] { 6 }, rows.ByName(Names, "Verdant Partition", 30));
    }

    [Fact]
    public void IgnoresTheGamesFormattingInNames()
    {
        var rows = new StoreroomRows();
        rows.Reset(1);
        Assert.Equal(new[] { 0 }, rows.ByName(new[] { "\u0002Stage Panel\u0003" }, "Stage Panel", 50));
    }
}

public class StoreroomGroupTests
{
    private static StoreroomRow R(int slot, uint item) => new(27001, slot, item, 0);

    [Fact]
    public void InfersTheRestOfAGroupFromTheStoreroomsCount()
    {
        var rows = new StoreroomRows();
        rows.Reset(10);
        rows.Learn(0, R(8, 11), groupSize: 3); // three Simple Hingan Pillars
        Assert.Equal(new[] { 1, 2 }, rows.Inferred.Keys.OrderBy(k => k));
        Assert.Equal(3, rows.NextUnknown());
        rows.Learn(3, R(44, 12), groupSize: 2);
        Assert.Equal(5, rows.NextUnknown());
        // A row actually picked comes before one only inferred, even in another dye.
        Assert.Equal(0, rows.Find(11, 0));
        Assert.Equal(3, rows.Find(12, 5));
    }

    [Fact]
    public void FindsAnInferredRowWhenNoneIsKnown()
    {
        var rows = new StoreroomRows();
        rows.Reset(6);
        rows.Learn(0, R(1, 11), groupSize: 2);
        rows.Removed(0); // that piece was placed: row 1 moves up to 0
        Assert.Equal(0, rows.Find(11, 0));
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void APieceStoredMovesTheRowsAfterItDown()
    {
        var rows = new StoreroomRows();
        rows.Reset(4);
        rows.Learn(0, R(1, 11));
        rows.Learn(3, R(4, 14));
        rows.LearnNames(0, new[] { "A", "B", "C", "D" });
        rows.Inserted(2); // a bag piece stored, its row 2 by the list's order
        Assert.Equal(5, rows.Count);
        Assert.Equal(0, rows.Find(11, 0));
        Assert.Equal(4, rows.Find(14, 0));
        Assert.Equal("D", rows.Names[4]);
        Assert.False(rows.Names.ContainsKey(2));
    }

    [Fact]
    public void OnlyARowThatStartsAGroupInfersIt()
    {
        var rows = new StoreroomRows();
        rows.Reset(10);
        rows.Learn(4, R(1, 11)); // row 3 unknown: nothing inferred from row 5's pick
        rows.Learn(5, R(2, 11), groupSize: 3);
        Assert.Empty(rows.Inferred);
    }

    [Fact]
    public void AWrongGuessClearsTheGuesses()
    {
        var rows = new StoreroomRows();
        rows.Reset(10);
        rows.Learn(0, R(1, 11), groupSize: 4);
        rows.Learn(2, R(9, 77)); // row 2 wasn't an 11 after all
        Assert.Empty(rows.Inferred);
        Assert.Equal(1, rows.NextUnknown());
    }
}

public class StoreroomPersistenceTests
{
    private static readonly StoreroomRow[] Storeroom =
    {
        new(27004, 8, 11, 0), new(27004, 7, 11, 0), new(27001, 44, 12, 0), new(27002, 36, 13, 4),
    };

    [Fact]
    public void TheFingerprintFollowsTheStoreroomNotItsOrder()
    {
        var a = StoreroomRows.Fingerprint(Storeroom);
        var b = StoreroomRows.Fingerprint(Storeroom.Reverse());
        Assert.Equal(a, b);
        var changed = Storeroom.Take(3).Append(new StoreroomRow(27002, 36, 13, 5)); // re-dyed
        Assert.NotEqual(a, StoreroomRows.Fingerprint(changed));
        Assert.NotEqual(a, StoreroomRows.Fingerprint(Storeroom.Take(3)));
    }

    [Fact]
    public void SavedRowsComeBackWhileTheStoreroomIsTheSame()
    {
        var fp = StoreroomRows.Fingerprint(Storeroom);
        var rows = new StoreroomRows();
        rows.Reset(4);
        rows.Learn(2, Storeroom[2]);
        rows.Learn(0, Storeroom[0]);
        var saved = rows.Export(fp);

        var later = new StoreroomRows();
        Assert.True(later.Import(saved, fp));
        Assert.Equal(4, later.Count);
        Assert.Equal(2, later.Find(12, 0));
        Assert.True(later.Matches(0, Storeroom[0]));
    }

    [Fact]
    public void SavedRowsAreDroppedWhenTheStoreroomChanged()
    {
        var rows = new StoreroomRows();
        rows.Reset(4);
        rows.Learn(1, Storeroom[1]);
        var saved = rows.Export(StoreroomRows.Fingerprint(Storeroom));
        var later = new StoreroomRows();
        Assert.False(later.Import(saved, StoreroomRows.Fingerprint(Storeroom.Take(2))));
        Assert.Empty(later.Known);
        Assert.False(later.Import(null, "x"));
    }
}

public class DyeRowTests
{
    // Recording 2's dye window, by value index.
    private static readonly Dictionary<int, string> Window = new()
    {
        [1] = "HOUSING", [4] = "Glass Jars", [203] = "No Color", [204] = "Snow White", [205] = "Ash Grey", [206] = "Goobbue Grey",
        [207] = "Slate Grey", [208] = "Charcoal Grey", [209] = "Soot Black", [239] = "Terebinth", [240] = "Standard Spectrum Dye",
        [426] = "No Color", [432] = "Soot Black", [438] = "You do not possess this dye.", [441] = "Dye",
    };

    [Theory]
    [InlineData("Snow White", 1)] // Recording 2: [5, 1, 1, 0]
    [InlineData("Soot Black", 6)] // Recording 1: [5, 6, 6, 0]
    [InlineData("soot black", 6)]
    [InlineData("Terebinth", 36)]
    public void ReadsADyesRowFromTheWindow(string dye, int row) => Assert.Equal(row, Menus.DyeRow(Window, dye, 203));

    [Fact]
    public void NoRowForADyeNotListed() => Assert.Null(Menus.DyeRow(Window, "Rose Pink", 203));

    [Fact]
    public void NoColorIsntADye() => Assert.Null(Menus.DyeRow(Window, "No Color", 203));
}

public class RunEstimateTests
{
    private static PaceProfile Gap(int ms) => new(Pace.Quick, "Test", ms, 300, 1.0);

    private static RunJob J(RunPhase phase, JobOutcome outcome, double seconds = 0)
    {
        var kind = phase switch { RunPhase.Move => JobKind.Move, RunPhase.Place => JobKind.PlaceFromStoreroom, _ => JobKind.Dye };
        var job = new RunJob { Kind = kind, Target = new Target(0, 1, Vector3.Zero, 0, 0, Floor.Ground, false), Outcome = outcome };
        if (outcome == JobOutcome.Done)
        {
            job.StartedAt = new DateTime(2026, 1, 1);
            job.EndedAt = job.StartedAt.Value.AddSeconds(seconds);
        }
        return job;
    }

    [Fact]
    public void UsesTheMeasuredTimePerPhasePlusThePause()
    {
        var jobs = new[] { J(RunPhase.Place, JobOutcome.Done, 3), J(RunPhase.Place, JobOutcome.Done, 5), J(RunPhase.Place, JobOutcome.Pending), J(RunPhase.Place, JobOutcome.Pending) };
        // Two left at 4 s each, plus 1 s between pieces.
        Assert.Equal(TimeSpan.FromSeconds(10), RunEstimate.TimeLeft(jobs, Gap(1000)));
    }

    [Fact]
    public void GuessesBeforeAnythingIsMeasuredAndCountsDyesToCome()
    {
        var jobs = new[] { J(RunPhase.Move, JobOutcome.Pending) };
        Assert.Equal(TimeSpan.FromSeconds(1.5 + 1.5), RunEstimate.TimeLeft(jobs, Gap(1500)));
        Assert.Equal(TimeSpan.FromSeconds(Math.Round(3 + 2 * (5 + 1.5))), RunEstimate.TimeLeft(jobs, Gap(1500), dyesStillToPlan: true, dyesExpected: 2));
    }

    [Fact]
    public void ASlowerPaceGuessesLonger()
    {
        var jobs = new[] { J(RunPhase.Place, JobOutcome.Pending) };
        Assert.True(RunEstimate.TimeLeft(jobs, Pacing.Profile(Pace.Steady)) > RunEstimate.TimeLeft(jobs, Pacing.Profile(Pace.Quick)));
        Assert.True(RunEstimate.TimeLeft(jobs, Pacing.Profile(Pace.Quick)) > RunEstimate.TimeLeft(jobs, Pacing.Profile(Pace.Daring)));
    }

    [Fact]
    public void NothingLeftIsZero() => Assert.Equal(TimeSpan.Zero, RunEstimate.TimeLeft(new[] { J(RunPhase.Move, JobOutcome.Done, 2) }, Gap(1500)));
}

public class StepQueueRound2Tests
{
    private DateTime now = new(2026, 1, 1);

    [Fact]
    public void AWaitThatMayRunOutGoesOn()
    {
        var q = new StepQueue(() => now);
        var ran = false;
        q.Begin(new[]
        {
            new Step { Label = "optional", Run = () => StepOutcome.Waiting, Timeout = TimeSpan.FromSeconds(1), OnTimeout = StepStatus.Done },
            Step.Act("next", () => { ran = true; return StepOutcome.Done; }),
        });
        q.Tick();
        now = now.AddSeconds(2);
        q.Tick();
        q.Tick();
        Assert.True(ran);
        Assert.Equal(QueueState.Finished, q.State);
    }

    [Fact]
    public void SaysWhatKindOfProblemEndedAPiece()
    {
        var q = new StepQueue(() => now);
        q.Begin(new[] { Step.Act("find", () => StepOutcome.Skip("not in the list", ProblemKind.NotFound)) });
        q.Tick();
        Assert.Equal(QueueState.Skipped, q.State);
        Assert.Equal(ProblemKind.NotFound, q.Problem);

        q.Begin(new[] { new Step { Label = "land", Run = () => StepOutcome.Waiting, Timeout = TimeSpan.FromSeconds(1), OnTimeout = StepStatus.SkipJob, TimeoutProblem = ProblemKind.DidNotLand } });
        q.Tick();
        now = now.AddSeconds(2);
        q.Tick();
        Assert.Equal(ProblemKind.DidNotLand, q.Problem);
    }
}

public class Recording2CallbackTests
{
    [Fact]
    public void TheListRowIsSentUnsigned()
    {
        // Recording 2: HousingGoods [i:1, u:3].
        var call = new UiCall("HousingGoods", "i:1 u:{index}", true, "test");
        Assert.Equal(new[] { (ArgKind.Int, 1L), (ArgKind.UInt, 3L) }, call.Resolve(UiCallValues.Of(("index", 3))));
    }

    [Fact]
    public void FindsPaintAndReturnToInventoryMenus()
    {
        Assert.Equal(0, Menus.FindEntry(new[] { "Paint", " Search in Market Board", " Search the Market" }, new[] { "Paint" }));
        Assert.Equal(0, Menus.FindEntry(new[] { "Place", "Return to Inventory", "Search for Item" }, new[] { "Place" }));
    }
}
