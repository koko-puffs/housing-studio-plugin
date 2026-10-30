using System.Numerics;
using System.Text.Json;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class FullSaveTests
{
    private static readonly SavedHome Home = new(
        "Medium",
        "Minimalist II",
        new[]
        {
            new SavedFixture("Ground Floor", "Wall", 7000, "Mahogany Interior Wall"),
            new SavedFixture("Ground Floor", "Floor", 7001, "Mahogany Flooring"),
            new SavedFixture("Upper Floor", "Light", 7002, "Crystal Chandelier"),
            new SavedFixture("Main", "Window", 6420, "Riviera Wooden Window"),
            new SavedFixture("Main", "Door", 6410, "Riviera Wooden Door"),
        },
        new[]
        {
            new SavedFixture("Exterior", "Roof", 6347, "Riviera Cottage Roof", "336699"),
            new SavedFixture("Exterior", "Fence", 6500, "Riviera Fence"),
        },
        23);

    [Fact]
    public void WritesTheHomesFixturesStyleAndPlot()
    {
        var json = LayoutWriter.Write(Home, [], [], "My house");
        var back = LayoutReader.Parse(json);
        Assert.Equal("Medium", back.HouseSize);
        Assert.Equal(23, back.Plot);
        Assert.Equal(5, back.InteriorFixtures.Count);
        Assert.Contains(back.InteriorFixtures, f => f is { Level: "Upper Floor", Type: "Light", ItemId: 7002 });
        Assert.Contains(back.InteriorFixtures, f => f is { Level: "Main", Type: "Window", ItemId: 6420 });
        var roof = Assert.Single(back.ExteriorFixtures, f => f.Type == "Roof");
        Assert.Equal("336699", roof.DyeHex);

        using var doc = JsonDocument.Parse(json);
        var district = doc.RootElement.GetProperty("interiorFixture")[0];
        Assert.Equal("District", district.GetProperty("type").GetString());
        Assert.Equal("Minimalist II", district.GetProperty("name").GetString());
        Assert.Equal("336699FF", doc.RootElement.GetProperty("exteriorFixture")[0].GetProperty("properties").GetProperty("color").GetString());
    }

    [Fact]
    public void WritesAPartitionsWallpaper()
    {
        var piece = new SavedPiece(16000, "Wooden Partition", new Vector3(1, 0, 2), 0, null, new SavedItem(7105, "Glade Interior Wall"));
        var json = LayoutWriter.Write(Home, new[] { piece }, [], null);
        var back = Assert.Single(LayoutReader.Parse(json).Interior);
        Assert.Equal(7105u, back.WallItemId);
        using var doc = JsonDocument.Parse(json);
        var material = doc.RootElement.GetProperty("interiorFurniture")[0].GetProperty("properties").GetProperty("material");
        Assert.Equal("Glade Interior Wall", material.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("s1i2", "Mist")]
    [InlineData("f1i1", "The Lavender Beds")]
    [InlineData("w1i3", "The Goblet")]
    [InlineData("e1i4", "Shirogane")]
    [InlineData("r1i2", "Empyreum")]
    [InlineData("h1i2", "Minimalist")]
    [InlineData("h2i2", "Minimalist II")]
    [InlineData("s1h1", null)]
    [InlineData("", null)]
    public void NamesTheInteriorsStyle(string territory, string? expected) => Assert.Equal(expected, Districts.StyleOfInterior(territory));

    [Theory]
    [InlineData("C:/builds/house-2026-10-30", "C:/builds/house-2026-10-30.build")]
    [InlineData("C:/builds/house.build", "C:/builds/house.build")]
    [InlineData("C:/builds/house.BUILD", "C:/builds/house.BUILD")]
    [InlineData("C:/builds/house.json", "C:/builds/house.json")]
    [InlineData("C:/builds/house.", "C:/builds/house.build")]
    [InlineData("C:/builds/v1.2 house", "C:/builds/v1.2 house.build")]
    public void EndsASavedFileInBuild(string chosen, string expected) => Assert.Equal(expected, LayoutWriter.WithExtension(chosen));
}

public class ScanFixesTests
{
    private static Target T(int key, uint item, float x, byte stain = 0) => new(key, item, new Vector3(x, 0, 0), 0, stain, Floor.Ground, false);
    private static PlacedPiece P(int key, uint item, float x, byte stain = 0) => new(key, item, new Vector3(x, 0, 0), 0, stain);
    private static StockSlot S(StockSource src, uint item, byte stain = 0) => new(src, src == StockSource.Inventory ? 0 : 27001, 0, item, stain, 1);

    [Fact]
    public void APauseDoesntCountAgainstAStepsWait()
    {
        var t = new DateTime(2026, 1, 1);
        var q = new StepQueue(() => t);
        q.Begin(new[] { Step.Wait("the preview", () => false, TimeSpan.FromSeconds(2), StepStatus.SkipJob) });
        q.Tick();
        t += TimeSpan.FromSeconds(1);
        q.Tick();
        Assert.Equal(TimeSpan.FromSeconds(1), q.Elapsed);
        // Paused for a minute: the step carries on with the second it had left.
        t += TimeSpan.FromMinutes(1);
        q.Hold(TimeSpan.FromMinutes(1));
        q.Tick();
        Assert.Equal(QueueState.Running, q.State);
        Assert.Equal(TimeSpan.FromSeconds(1), q.Elapsed);
        t += TimeSpan.FromSeconds(1.5);
        q.Tick();
        Assert.Equal(QueueState.Skipped, q.State);
    }

    [Fact]
    public void AnUndyedSpotIsntGivenADyedPieceItCantPutRight()
    {
        // U wants undyed, B wants blue (2); a red one (1) is placed by U, an undyed one is in the bags.
        var plan = Matcher.Build(new[] { T(0, 10, 0), T(1, 10, 5, stain: 2) }, new[] { P(100, 10, 0.5f, stain: 1) }, new[] { S(StockSource.Inventory, 10) });
        Assert.Equal(PieceState.FromInventory, plan.Entries[0].State);
        Assert.Equal(100, plan.Entries[1].Placed!.Key);
    }

    [Fact]
    public void BagPiecesGoToTheUndyedFirst()
    {
        // A wants red, B undyed; the storeroom has a blue one, the bags an undyed one.
        var plan = Matcher.Build(new[] { T(0, 10, 0, stain: 1), T(1, 10, 5) }, Array.Empty<PlacedPiece>(),
            new[] { S(StockSource.Storeroom, 10, stain: 3), S(StockSource.Inventory, 10) });
        Assert.Equal(PieceState.FromStoreroom, plan.Entries[0].State);
        Assert.Equal(PieceState.FromInventory, plan.Entries[1].State);
    }

    [Fact]
    public void ManyCopiesInPlaceMatchQuickly()
    {
        var targets = Enumerable.Range(0, 2000).Select(i => T(i, 10, i * 0.5f)).ToArray();
        var placed = Enumerable.Range(0, 2000).Select(i => P(1000 + i, 10, i * 0.5f)).ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = Matcher.Build(targets, placed, Array.Empty<StockSlot>());
        Assert.All(plan.Entries, e => Assert.Equal(PieceState.InPlace, e.State));
        Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void APieceAtAnUnusablePlaceIsLeftOut()
    {
        const string json = """
        { "houseSize": "Small", "interiorScale": 1,
          "interiorFurniture": [
            { "itemId": 1, "transform": { "location": [1e39, 0, 0], "rotation": [0, 0, 0, 1] } },
            { "itemId": 2, "transform": { "location": [100, 0, 0], "rotation": [0, 0, 0, 1] } } ] }
        """;
        var layout = LayoutReader.Parse(json);
        var piece = Assert.Single(layout.Interior);
        Assert.Equal(2u, piece.ItemId);
        Assert.True(float.IsFinite(piece.Position.X) && Math.Abs(piece.Position.X) <= LayoutReader.MaxReach);
    }

    [Fact]
    public void ABrokenReadingIsWrittenAsZero()
    {
        var json = LayoutWriter.Write("Small", "Mist", new[] { new SavedPiece(1, "x", new Vector3(float.NaN, 1, float.PositiveInfinity), float.NaN, null) }, [], null);
        Assert.Single(LayoutReader.Parse(json).Interior);
    }
}
