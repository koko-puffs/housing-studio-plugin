using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>Ready's words in the 0.6 flow: the brass button, the Place stage's choice, the estimate, the chips.</summary>
public class ReadyWordsTests
{
    [Theory]
    [InlineData(ReadyMode.Keep, 12, 14, true, "Get 12, then place")]
    [InlineData(ReadyMode.ClearOut, 12, 14, true, "Get 12, then clear out 14")]
    [InlineData(ReadyMode.Swap, 12, 14, true, "Get 12, then swap")]
    [InlineData(ReadyMode.Keep, 12, 14, false, "Start placing")]
    [InlineData(ReadyMode.ClearOut, 12, 14, false, "Clear out 14, then place")]
    [InlineData(ReadyMode.Swap, 0, 14, false, "Swap and place")]
    // Getting first with nothing to get: the button says what placing does.
    [InlineData(ReadyMode.Keep, 0, 14, true, "Start placing")]
    [InlineData(ReadyMode.ClearOut, 0, 3, true, "Clear out 3, then place")]
    // Clear out with nothing to clear is Keep.
    [InlineData(ReadyMode.ClearOut, 0, 0, false, "Start placing")]
    [InlineData(ReadyMode.ClearOut, 5, 0, true, "Get 5, then place")]
    public void TheButtonSaysWhatItDoes(ReadyMode mode, int toGet, int clearOut, bool getFirst, string expected) =>
        Assert.Equal(expected, ReadyWords.Go(mode, toGet, clearOut, getFirst));

    [Fact]
    public void FixturesAfterPlacingAreNamedToo()
    {
        Assert.Equal("Get 12, then place · fixtures", ReadyWords.Go(ReadyMode.Keep, 12, 0, true, fixturesAfter: true));
        Assert.Equal("Start placing · fixtures", ReadyWords.Go(ReadyMode.Keep, 0, 0, false, fixturesAfter: true));
    }

    [Fact]
    public void TheNoteUnderTheChoice()
    {
        Assert.Equal("The 14 pieces not in the design stay where they are.", ReadyWords.ModeNote(ReadyMode.Keep, 20, 14, null));
        Assert.Equal("The 1 piece not in the design stays where it is.", ReadyWords.ModeNote(ReadyMode.Keep, 1, 1, null));
        Assert.Equal("First, the 14 pieces not in the design go to your storeroom.", ReadyWords.ModeNote(ReadyMode.ClearOut, 14, 14, null));
        Assert.Equal("First, the 1 piece not in the design goes to your storeroom.", ReadyWords.ModeNote(ReadyMode.ClearOut, 1, 1, null));
        Assert.Equal("What's here is kept as “Winter Lodge” so you can swap back. Then it clears out and places.",
            ReadyWords.ModeNote(ReadyMode.Swap, 14, 14, "Winter Lodge"));
        Assert.Equal("3 pieces here aren't in the design, none on the floors placed or near you, so nothing is put away.",
            ReadyWords.ModeNote(ReadyMode.ClearOut, 3, 0, null));
        Assert.Equal("Everything placed here is in the design.", ReadyWords.ModeNote(ReadyMode.Keep, 0, 0, null));
        Assert.Equal("Clear out 14", ReadyWords.ClearOutLabel(14));
        Assert.Equal("Clear out", ReadyWords.ClearOutLabel(0));
    }

    [Fact]
    public void TheEstimateSaysTheTripsComeFirst()
    {
        Assert.Equal("About 25 minutes at a quick pace", ReadyWords.Hint("about 25 minutes", "quick", false, 2));
        Assert.Equal("2 trips first, then about 25 minutes at a quick pace", ReadyWords.Hint("about 25 minutes", "quick", true, 2));
        Assert.Equal("1 trip first, then under a minute at a steady pace", ReadyWords.Hint("under a minute", "steady", true, 1));
        Assert.Equal("The errands first, then about an hour at a quick pace", ReadyWords.Hint("about an hour", "quick", true));
    }

    [Fact]
    public void TheEstimateSaysWhenGetFirstCantGo()
    {
        Assert.Equal("Not getting first: Nothing the errands can get. About 25 minutes at a quick pace",
            ReadyWords.Hint("about 25 minutes", "quick", false, 0, "Nothing the errands can get."));
        Assert.Equal("Not getting first: vnavmesh isn't installed. About 25 minutes at a quick pace",
            ReadyWords.Hint("about 25 minutes", "quick", false, 0, "vnavmesh isn't installed"));
        // Getting first after all: the trips, as before.
        Assert.Equal("2 trips first, then about 25 minutes at a quick pace", ReadyWords.Hint("about 25 minutes", "quick", true, 2, "ignored"));
    }

    [Fact]
    public void StageAndChipWords()
    {
        Assert.Equal("443 to do", ReadyWords.PlaceBig(443, 87));
        Assert.Equal("87 to dye", ReadyWords.PlaceBig(0, 87));
        Assert.Equal("Nothing to do", ReadyWords.PlaceBig(0, 0));
        Assert.Equal("Basement left out", ReadyWords.FloorsLeftOut(new[] { "basement" }));
        Assert.Equal("2 floors left out", ReadyWords.FloorsLeftOut(new[] { "basement", "upper floor" }));
        Assert.Equal("3 dyes short", ReadyWords.DyesShort(3));
        Assert.Equal("1 dye short", ReadyWords.DyesShort(1));
    }

    [Fact]
    public void TheLimitChipsDetail()
    {
        var fits = ItemLimits.Check(Side.Interior, "Medium", design: 447, placedNow: 30, others: 14, toPlace: 417)!.Value;
        Assert.Equal("447 of the 450 pieces a house (medium) holds inside, once Start is done. The 14 pieces here that aren't in the design count too, unless Clear out puts them away first.",
            CareWords.LimitDetail(fits, Side.Interior, "Medium", clearing: false));
        var cleared = ItemLimits.Check(Side.Interior, "Medium", design: 447, placedNow: 16, others: 0, toPlace: 417)!.Value;
        Assert.Equal("433 of the 450 pieces a house (medium) holds inside, once Start is done. The pieces Clear out puts away don't count.",
            CareWords.LimitDetail(cleared, Side.Interior, "Medium", clearing: true));
        var over = ItemLimits.Check(Side.Interior, "Small", design: 280, placedNow: 100, others: 60, toPlace: 240)!.Value;
        Assert.Equal(CareWords.Limit(over, Side.Interior, "Small"), CareWords.LimitDetail(over, Side.Interior, "Small", clearing: false));
    }

    [Fact]
    public void TheBannerSaysWhereARunWasCutOff()
    {
        var p = new RunProgress(@"C:\designs\cosy.build", "Cosy", "interior-w73-t341-0000000000000ABC", Side.Interior,
            new List<(int, uint)> { (0, 100) }, 211, 447, RunPhase.Place, DateTime.MinValue);
        Assert.Equal("A run was cut off at piece 212 of 447.", CareWords.CutOffShort(p));
    }
}
