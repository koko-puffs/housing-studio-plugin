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
