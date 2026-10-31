using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>"Set them for me after placing": when the fixtures follow a run, and when they never do.</summary>
public class AfterPlacingTests
{
    private const string Design = "c:/designs/cosy.build";

    private static AfterPlacingSeen Seen(bool runActive = false, int run = 1, bool finished = false, bool errands = false, bool errandsFinished = false,
        bool summary = false, bool fixtures = false, string? design = Design) =>
        new(runActive, run, finished, errands, errandsFinished, summary, fixtures, design);

    [Fact]
    public void ARunThatFinishesSetsTheFixturesOnce()
    {
        var a = new AfterPlacing();
        a.ArmRun(1, Design);
        Assert.False(a.Step(Seen(runActive: true)));
        Assert.True(a.Step(Seen(finished: true)));
        Assert.False(a.Armed);
        Assert.False(a.Step(Seen(finished: true)));
    }

    [Fact]
    public void AStoppedOrPreviewedRunNever()
    {
        var a = new AfterPlacing();
        a.ArmRun(1, Design);
        Assert.False(a.Step(Seen(runActive: true)));
        // Stopped, failed, or a preview: the runner doesn't say it finished.
        Assert.False(a.Step(Seen(finished: false)));
        Assert.False(a.Armed);
    }

    [Fact]
    public void AnotherRunIsNotTheOneArmedFor()
    {
        var a = new AfterPlacing();
        a.ArmRun(1, Design);
        // The window was closed: the armed run ended unseen and another started.
        Assert.False(a.Step(Seen(runActive: true, run: 2)));
        Assert.False(a.Armed);
        Assert.False(a.Step(Seen(run: 2, finished: true)));
    }

    [Fact]
    public void AnotherDesignNever()
    {
        var a = new AfterPlacing();
        a.ArmRun(1, Design);
        Assert.False(a.Step(Seen(finished: true, design: "c:/designs/other.build")));
        Assert.False(a.Armed);
    }

    [Fact]
    public void WaitsWhileTheFixturesAlreadyGo()
    {
        var a = new AfterPlacing();
        a.ArmRun(1, Design);
        Assert.False(a.Step(Seen(finished: true, fixtures: true)));
        Assert.True(a.Armed);
        Assert.True(a.Step(Seen(finished: true)));
    }

    [Fact]
    public void TheErrandsEveryPlacingFinishedAndTheSummaryPutAway()
    {
        var a = new AfterPlacing();
        a.ArmErrands(Design);
        Assert.False(a.Step(Seen(errands: true, run: 4)));
        Assert.False(a.Step(Seen(runActive: true, run: 5, errands: true)));
        Assert.False(a.Step(Seen(run: 5, finished: true, errands: true)));
        Assert.False(a.Step(Seen(runActive: true, run: 6, errands: true)));
        Assert.False(a.Step(Seen(run: 6, finished: true, errands: true)));
        // Finished, but their summary is on screen: not yet.
        Assert.False(a.Step(Seen(run: 6, finished: true, errandsFinished: true, summary: true)));
        Assert.True(a.Armed);
        Assert.True(a.Step(Seen(run: 6, finished: true, errandsFinished: true)));
    }

    [Fact]
    public void TheErrandsStoppedNever()
    {
        var a = new AfterPlacing();
        a.ArmErrands(Design);
        Assert.False(a.Step(Seen(runActive: true, run: 5, errands: true)));
        Assert.False(a.Step(Seen(run: 5, finished: true, errands: true)));
        // Stopped on the second trip (by you, or for safety).
        Assert.False(a.Step(Seen(run: 5, finished: true, errandsFinished: false, summary: true)));
        Assert.False(a.Armed);
    }

    [Fact]
    public void ALaterPlacingThatDidntFinishSpoilsIt()
    {
        var a = new AfterPlacing();
        a.ArmErrands(Design);
        Assert.False(a.Step(Seen(runActive: true, run: 5, errands: true)));
        Assert.False(a.Step(Seen(run: 5, finished: true, errands: true)));
        Assert.False(a.Step(Seen(runActive: true, run: 6, errands: true)));
        Assert.False(a.Step(Seen(run: 6, finished: false, errands: true)));
        Assert.False(a.Step(Seen(run: 6, errandsFinished: true)));
        Assert.False(a.Armed);
    }

    [Fact]
    public void ErrandsThatNeverPlacedNever()
    {
        var a = new AfterPlacing();
        a.ArmErrands(Design);
        Assert.False(a.Step(Seen(errands: true)));
        Assert.False(a.Step(Seen(errandsFinished: true)));
        Assert.False(a.Armed);
    }
}
