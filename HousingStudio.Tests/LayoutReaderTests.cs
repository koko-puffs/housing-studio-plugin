using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class LayoutReaderTests
{
    private static Layout Sample() => LayoutReader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.build")));

    private static void Near(float expected, float actual, float tol = 1e-3f) =>
        Assert.True(MathF.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");

    [Fact]
    public void ReadsTheHouseAndTheBuildsOwnProperties()
    {
        var l = Sample();
        Assert.Equal("Medium", l.HouseSize);
        Assert.Equal("Mist", l.DistrictName);
        Assert.Equal("mist", l.District);
        Assert.Equal("Seaside cottage", l.BuildName);
        Assert.Equal(12, l.Plot);
        Assert.False(l.IsApartment);
    }

    [Fact]
    public void ReadsEveryPieceWithAnItemIdAttachmentsIncluded()
    {
        var l = Sample();
        Assert.Equal(new uint[] { 6061, 6142, 6061, 20000, 6062 }, l.Interior.Select(p => p.ItemId));
        // The vase sits on the first table.
        Assert.Equal(0, l.Interior[1].ParentIndex);
        Assert.Null(l.Interior[0].ParentIndex);
    }

    [Fact]
    public void SwapsMakePlacesZAndYAxes()
    {
        var table = Sample().Interior[0];
        // location [1.5, -2.25, 0] is the game's x 1.5, z -2.25, y 0.
        Assert.Equal(new Vector3(1.5f, 0f, -2.25f), table.Position);
    }

    [Fact]
    public void TurnsTheOtherWayToTheGame()
    {
        var l = Sample();
        // [0, 0, -0.707, 0.707] is a quarter turn about MakePlace's up, the other way: +pi/2 in the game.
        Near(MathF.PI / 2, l.Interior[0].Yaw);
        // [0, 0, 1, 0] is half a turn.
        Near(MathF.PI, MathF.Abs(l.Interior[2].Yaw));
    }

    [Fact]
    public void ReadsDyesAndWallFixtures()
    {
        var l = Sample();
        Assert.Equal("f9f8f4", l.Interior[0].DyeHex);
        Assert.Null(l.Interior[1].DyeHex);
        Assert.Equal("4f2d1f", l.Interior[3].DyeHex);
        Assert.Equal(6497u, l.Interior[3].WallItemId);
    }

    [Fact]
    public void ReadsBadNumbersAsZeroRatherThanFailing()
    {
        var broken = Sample().Interior[4];
        // ["0,5", "x", 0]: a decimal comma reads, the rest is 0; a rotation that isn't a list is no turn.
        Assert.Equal(new Vector3(0.5f, 0, 0), broken.Position);
        Assert.Equal(0f, broken.Yaw);
    }

    [Fact]
    public void ReadsFixturesButNotTheDistrictLine()
    {
        var l = Sample();
        Assert.Equal(3, l.InteriorFixtures.Count);
        Assert.Contains(l.InteriorFixtures, f => f.Type == "Light" && f.Level == "Upper Floor");
        var roof = Assert.Single(l.ExteriorFixtures);
        Assert.Equal("Exterior", roof.Level);
        Assert.Equal("e4dfd0", roof.DyeHex);
    }

    [Fact]
    public void ReadsTheGardenInThePlotsOwnCoordinates()
    {
        var l = Sample();
        var bench = Assert.Single(l.Garden);
        Assert.Equal(Side.Garden, bench.Side);
        Assert.Equal(new Vector3(5, 0.25f, -3), bench.Position);
        Assert.False(l.GardenInWardCoordinates);
    }

    [Fact]
    public void TellsAGardenSavedInTheWardsCoordinates()
    {
        var json = """
        { "houseSize": "Small", "exteriorFurniture": [
          { "itemId": 1, "transform": { "location": [-72, -108, 40], "rotation": [0,0,0,1] } },
          { "itemId": 2, "transform": { "location": [-70, -110, 40], "rotation": [0,0,0,1] } } ] }
        """;
        Assert.True(LayoutReader.Parse(json).GardenInWardCoordinates);
    }

    [Fact]
    public void DividesByTheFilesScale()
    {
        var json = """
        { "houseSize": "Large", "interiorScale": 100, "interiorFurniture": [
          { "itemId": 5, "transform": { "location": [150, 250, 50], "rotation": [0,0,0,1] } } ] }
        """;
        Assert.Equal(new Vector3(1.5f, 0.5f, 2.5f), LayoutReader.Parse(json).Interior[0].Position);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"name\": \"something else\" }")]
    public void RefusesWhatIsntALayout(string text)
    {
        Assert.Throws<LayoutFormatException>(() => LayoutReader.Parse(text));
    }

    [Theory]
    [InlineData("Apartment", "Apartment")]
    [InlineData("Private Chambers", "Apartment")]
    [InlineData("small", "Small")]
    [InlineData("Huge", null)]
    public void ReadsTheHouseSize(string raw, string? expected)
    {
        Assert.Equal(expected, LayoutReader.Parse($"{{ \"houseSize\": \"{raw}\" }}").HouseSize);
    }
}
