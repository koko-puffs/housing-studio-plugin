using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>The words of the 0.6 flow's Start, Done and Settings (docs/mockups/flow.html).</summary>
public class FlowScreensTests
{
    [Fact]
    public void ThisHouseSaysTheHomeAndHowFull()
    {
        Assert.Equal("Medium house · 312 of 450 pieces placed inside", CareWords.HouseLine(Side.Interior, "Medium", 312, 450));
        Assert.Equal("Apartment · 12 pieces placed inside", CareWords.HouseLine(Side.Interior, "Apartment", 12, null));
        Assert.Equal("Small house", CareWords.HouseLine(Side.Interior, "Small", null, 300));
        Assert.Equal("20 of 60 pieces placed in the garden", CareWords.HouseLine(Side.Garden, "Medium", 20, 60));
        Assert.Equal("Your garden", CareWords.HouseLine(Side.Garden, null, null, null));
    }

    [Theory]
    [InlineData("Medium house in Mist, plot 12 before Winter Lodge.build", true)]
    [InlineData("Medium house in Mist before Winter Lodge 2.build", true)]
    [InlineData("interior-20261030-091500.build", false)]
    [InlineData("garden-20261030-091500-2.build", false)]
    [InlineData("house-2026-10-31.build", false)]
    [InlineData("Medium house before Winter Lodge.json", false)]
    public void TellsAKeptHomeFromABackup(string file, bool kept) => Assert.Equal(kept, SwapNames.IsKept(file));

    [Fact]
    public void KnowsTheBackupsOwnNames()
    {
        Assert.True(SwapNames.IsBackupName("interior-20261030-091500.build"));
        Assert.True(SwapNames.IsBackupName("garden-20261030-091500-3.build"));
        Assert.False(SwapNames.IsBackupName("garden-2026-10-30.build"));
        Assert.False(SwapNames.IsBackupName(SwapNames.FileStem(SwapNames.KeepAs("Garden in Mist", "x")) + ".build"));
    }

    [Fact]
    public void DoneSaysWhatHappenedInOneLine()
    {
        Assert.Equal("445 of 447 pieces", DoneWords.Line(445, 447));
        Assert.Equal("All 447 pieces", DoneWords.Line(447, 447));
        Assert.Equal("1 of 1 piece", DoneWords.Line(1, 1));
    }

    [Fact]
    public void PaceInOneLine()
    {
        Assert.Equal("0.6 s between pieces", PaceSetting.Short(Pace.Steady));
        Assert.Equal("0.15 s between pieces", PaceSetting.Short(Pace.Quick));
        Assert.Equal("0.05 s between pieces", PaceSetting.Short(Pace.Daring));
    }
}
