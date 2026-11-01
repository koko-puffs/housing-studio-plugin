using System.Numerics;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>0.9.0's Settings, fixtures panel and This house's changes: the words they work out.</summary>
public class SettingsScreenTests
{
    [Fact]
    public void CurrenciesFoldedIntoOneLine()
    {
        Assert.Equal("20 of 20 allowed · only what you have is spent", SettingsWords.Currencies(20, 20));
        Assert.Equal("3 of 20 allowed · only what you have is spent", SettingsWords.Currencies(3, 20));
        Assert.Equal("None of 20 allowed · gil vendors and the board only", SettingsWords.Currencies(0, 20));
    }

    private static Target T(int key, uint item) => new(key, item, Vector3.Zero, 0, 0, Floor.Ground, false);
    private static PlacedPiece P(int key, uint item) => new(key, item, Vector3.One, 0, 0);

    [Fact]
    public void ChangesGroupedByWhatHappenedWithCounts()
    {
        var r = new DriftReport(
            new[] { (T(1, 10), P(1, 10)), (T(2, 11), P(2, 11)) },
            new[] { P(9, 20), P(8, 20), P(7, 21) },
            new[] { T(3, 12) },
            new (Target, PlacedPiece)[0],
            5);
        var groups = ChangeGroups.Of(r);
        Assert.Equal(new[] { "Moved · 2", "Taken away · 1", "Added · 3" }, groups.Select(g => g.Head));
        Assert.Equal(new[] { ChangeKind.Moved, ChangeKind.Gone, ChangeKind.Added }, groups.Select(g => g.Kind));
        // Show where: where the moved and the taken away were; none for what was added.
        Assert.Equal(new[] { 1, 2 }, groups[0].Where.Select(t => t.Key));
        Assert.Equal(new[] { 3 }, groups[1].Where.Select(t => t.Key));
        Assert.Empty(groups[2].Where);
        Assert.Equal(new uint[] { 20, 20, 21 }, groups[2].Items);
    }

    [Fact]
    public void DyedAgainHasItsOwnGroupAndNothingChangedHasNone()
    {
        var redyed = new DriftReport(new (Target, PlacedPiece)[0], new PlacedPiece[0], new Target[0], new[] { (T(4, 30), P(4, 30)) }, 0);
        var g = Assert.Single(ChangeGroups.Of(redyed));
        Assert.Equal("Dyed again · 1", g.Head);
        Assert.Empty(g.Where);
        Assert.Empty(ChangeGroups.Of(new DriftReport(new (Target, PlacedPiece)[0], new PlacedPiece[0], new Target[0], new (Target, PlacedPiece)[0], 7)));
    }

    [Fact]
    public void FixtureCountFollowsTheSteps()
    {
        var p = new FixtureProgress();
        Assert.Null(p.Text);
        p.Begin(new[] { ("Oasis Interior Wall", "ground floor's walls"), ("Oasis Flooring", "ground floor's floor"), ("Oasis Interior Wall", "basement's walls") });
        Assert.Equal("1 of 3", p.Text);
        p.See("Get to the Housing menu (or the Remodel Interior window)");
        Assert.Equal("1 of 3", p.Text);
        p.See("Put Oasis Interior Wall in the ground floor's walls slot");
        Assert.Equal("1 of 3", p.Text);
        p.See("Is Oasis Interior Wall in its slot?");
        Assert.Equal("1 of 3", p.Text);
        p.See("Put Oasis Flooring in the ground floor's floor slot");
        Assert.Equal("2 of 3", p.Text);
        // The same item again, told apart by its slot.
        p.See("You: put Oasis Interior Wall in the basement's walls slot of the Remodel Interior window");
        Assert.Equal("3 of 3", p.Text);
        // It never goes back, and the apply names none.
        p.See("Put Oasis Flooring in the ground floor's floor slot");
        p.See("You: press Apply");
        Assert.Equal("3 of 3", p.Text);
        // A new run starts over.
        p.Begin(new[] { ("Oasis Flooring", "ground floor's floor") });
        Assert.Equal("1 of 1", p.Text);
    }
}
