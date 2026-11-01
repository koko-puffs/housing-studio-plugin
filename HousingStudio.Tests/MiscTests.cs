using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class DyeTests
{
    [Theory]
    [InlineData("4F2D1FFF", "4f2d1f")]
    [InlineData("#f9f8f4", "f9f8f4")]
    [InlineData("  E4DFD0  ", "e4dfd0")]
    [InlineData("", null)]
    [InlineData("red", null)]
    [InlineData("12345", null)]
    [InlineData("GG0000", null)]
    public void ReadsAFilesColour(string raw, string? expected) => Assert.Equal(expected, Dyes.HexOf(raw));

    [Fact]
    public void FindsTheExactDyeAndOtherwiseTheNearest()
    {
        var dyes = new[] { new DyeColor(1, 0xE4DFD0, "Snow White"), new DyeColor(2, 0x2B1B13, "Soot Black"), new DyeColor(3, 0x8A2B2B, "Rust Red") };
        Assert.Equal(1, Dyes.Closest("e4dfd0", dyes)!.Value.StainId);
        Assert.Equal(2, Dyes.Closest("1f1410", dyes)!.Value.StainId);
        Assert.Equal(3, Dyes.Closest("993333", dyes)!.Value.StainId);
        Assert.Null(Dyes.Closest("ffffff", Array.Empty<DyeColor>()));
    }
}

public class PlotTableTests
{
    [Fact]
    public void HasEveryDistrictsSixtyPlots()
    {
        var table = PlotTable.LoadEmbedded();
        foreach (var d in Districts.Keys)
            for (var n = 1; n <= 60; n++)
                Assert.True(table.TryGet(d, n, out _), $"{d} plot {n}");
    }

    [Fact]
    public void KnowsWardsByTheirTerritoryName()
    {
        var table = PlotTable.LoadEmbedded();
        Assert.Equal("mist", table.DistrictOfWard("s1h1"));
        Assert.Equal("empyreum", table.DistrictOfWard("R1H1"));
        Assert.Null(table.DistrictOfWard("s1i1"));
    }

    [Fact]
    public void MatchesTheMakePlaceTablesPlots()
    {
        // Empyreum's plot 1 in MakePlace's table: (90, -18.5, 177), a small plot.
        Assert.True(PlotTable.LoadEmbedded().TryGet("empyreum", 1, out var p));
        Assert.Equal(new Vector3(90, -18.5f, 177), new Vector3(p.X, p.Y, p.Z));
        Assert.Equal("s", p.Size);
    }
}

public class FloorAndDistrictTests
{
    [Theory]
    [InlineData(-3f, "Large", Floor.Basement)]
    [InlineData(0f, "Large", Floor.Ground)]
    [InlineData(6.5f, "Medium", Floor.Ground)]
    [InlineData(7.5f, "Medium", Floor.Upper)]
    [InlineData(7.5f, "Small", Floor.Ground)]
    [InlineData(-3f, "Apartment", Floor.Ground)]
    public void SplitsFloorsAtTheMakePlaceHeights(float y, string size, Floor expected) => Assert.Equal(expected, Floors.Of(y, size));

    [Theory]
    [InlineData("s1i2", "Medium")]
    [InlineData("r1i4", "Apartment")]
    [InlineData("s1h1", null)]
    [InlineData("x", null)]
    public void ReadsTheHouseSizeFromItsInterior(string territory, string? expected) => Assert.Equal(expected, Districts.HouseSizeOfInterior(territory));

    [Theory]
    [InlineData("The Lavender Beds", "lavender-beds")]
    [InlineData("Mist", "mist")]
    [InlineData("Minimalist", null)]
    public void ReadsDistrictNames(string name, string? expected) => Assert.Equal(expected, Districts.FromName(name));
}

public class PacingTests
{
    [Fact]
    public void NeverGoesQuickerThanTheFloor()
    {
        Assert.Equal(Pacing.MinimumMs, Pacing.NextDelayMs(30, 0.5f, 0));
        Assert.True(Pacing.NextDelayMs(Pacing.MinimumMs, 0.5f, 0) >= Pacing.MinimumMs);
    }

    [Fact]
    public void VariesAroundTheInterval()
    {
        Assert.Equal(1500, Pacing.NextDelayMs(1500, 0.2f, 0.5));
        Assert.Equal(1200, Pacing.NextDelayMs(1500, 0.2f, 0));
        Assert.Equal(1800, Pacing.NextDelayMs(1500, 0.2f, 1));
        // Clamped to ±50%.
        Assert.Equal(3000, Pacing.NextDelayMs(2000, 5f, 1));
    }
}

public class ShoppingListAndWriterTests
{
    [Fact]
    public void ListsWhatsMissingWithDyes()
    {
        var targets = new[]
        {
            new Target(0, 10, Vector3.Zero, 0, 3, Floor.Ground, false),
            new Target(1, 10, Vector3.One, 0, 0, Floor.Ground, false),
            new Target(2, 11, Vector3.One, 0, 0, Floor.Ground, false),
        };
        var plan = Matcher.Build(targets, new[] { new PlacedPiece(5, 11, Vector3.One, 0, 0) }, Array.Empty<StockSlot>());
        var text = ShoppingList.Build(plan, id => id == 10 ? "Oak Table" : "Stool", s => s == 3 ? "Snow White" : $"dye {s}", "Cottage");
        Assert.Contains("Cottage: shopping list", text);
        Assert.Contains("2 × Oak Table (Snow White ×1, undyed ×1)", text);
        Assert.DoesNotContain("Stool", text);
        Assert.Contains("1 × Snow White", text);
    }

    [Fact]
    public void ListsDyesToBuyWhenItKnowsWhatYouHave()
    {
        var targets = new[] { new Target(0, 10, Vector3.Zero, 0, 3, Floor.Ground, false), new Target(1, 10, Vector3.One, 0, 3, Floor.Ground, false) };
        var plan = Matcher.Build(targets, Array.Empty<PlacedPiece>(), new[] { new StockSlot(StockSource.Inventory, 0, 0, 10, 0, 2) });
        var text = ShoppingList.Build(plan, _ => "Oak Table", _ => "Snow White", dyesOwned: _ => 1);
        Assert.Contains("Dyes to buy", text);
        Assert.Contains("1 × Snow White", text.Split("Dyes to buy")[1]);
    }

    [Fact]
    public void SaysSoWhenNothingIsMissing()
    {
        var plan = Matcher.Build(Array.Empty<Target>(), Array.Empty<PlacedPiece>(), Array.Empty<StockSlot>());
        Assert.Contains("Nothing missing", ShoppingList.Build(plan, _ => "", _ => ""));
    }

    [Fact]
    public void ABackupReadsBackAsTheSameLayout()
    {
        var inside = new[] { new SavedPiece(6061, "Oak Table", new Vector3(1.5f, 0, -2.25f), 1.2f, "f9f8f4") };
        var garden = new[] { new SavedPiece(6200, "Garden Bench", new Vector3(5, 0.25f, -3), -0.7f, null) };
        var json = LayoutWriter.Write("Medium", "Mist", inside, garden, "Backup", 12);
        var back = LayoutReader.Parse(json);
        Assert.Equal("Medium", back.HouseSize);
        Assert.Equal("mist", back.District);
        Assert.Equal(12, back.Plot);
        Assert.Equal("Backup", back.BuildName);
        var t = Assert.Single(back.Interior);
        Assert.Equal(6061u, t.ItemId);
        Assert.Equal("f9f8f4", t.DyeHex);
        Assert.True(Vector3.Distance(inside[0].Position, t.Position) < 1e-3);
        Assert.True(Transforms.AngleDiff(1.2f, t.Yaw) < 1e-2);
        Assert.Single(back.Garden);
    }
}
