using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

public class ReadyWordsGetTests
{
    [Fact]
    public void TheGetLineSaysWhatTheCountIs()
    {
        Assert.Equal("99 pieces and 3 fixtures", ReadyWords.GetLine(99, 3, 0));
        Assert.Equal("1 piece, 1 fixture and 2 dyes", ReadyWords.GetLine(1, 1, 2));
        Assert.Equal("4 dyes", ReadyWords.GetLine(0, 0, 4));
        Assert.Null(ReadyWords.GetLine(0, 0, 0));
    }

    [Fact]
    public void SourceChipsCountThings()
    {
        Assert.Equal("on a retainer", ReadyWords.SourceLabel(SourceKind.Retainer, 1));
        Assert.Equal("on retainers", ReadyWords.SourceLabel(SourceKind.Retainer, 2));
        Assert.Equal("from shops", ReadyWords.SourceLabel(SourceKind.Vendor, 30));
        Assert.Equal("on the board", ReadyWords.SourceLabel(SourceKind.Market, 71));
    }

    [Fact]
    public void TheChipsSayWhatTheyMeasure()
    {
        Assert.Equal("46 of 150 item limit", ReadyWords.LimitChip(46, 150));
        Assert.Equal("All 145 pieces", ReadyWords.PiecesChip(145));
        Assert.Equal("1 piece", ReadyWords.PiecesChip(1));
    }
}
