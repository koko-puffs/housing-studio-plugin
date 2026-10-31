using System.Numerics;
using System.Text.Json;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>The home's lighting level: read from the game, kept in a layout's properties.light (1–5), shown on This house.</summary>
public class LightingTests
{
    private static SavedHome Inside(int? light) => new("Medium", "Mist", [], [], 12, light);

    [Theory]
    [InlineData(5, 5)]
    [InlineData(4, 4)]
    [InlineData(3, 3)]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(0, 1)] // the game's darkest step (lights out) is the darkest a layout says
    public void TheGamesLevelIsKeptOneToFive(int game, int level) => Assert.Equal<int?>(level, Lighting.FromGame(game));

    [Theory]
    [InlineData(6)]
    [InlineData(255)] // 5 - an inverted level over 5, wrapped as a byte
    [InlineData(251)]
    [InlineData(-1)]
    public void ANumberThatIsntALevelIsNone(int game) => Assert.Null(Lighting.FromGame(game));

    [Fact]
    public void AnInteriorsLevelIsWrittenAsAWholeNumber()
    {
        var json = LayoutWriter.Write(Inside(3), [], [], "My house");
        using var doc = JsonDocument.Parse(json);
        var light = doc.RootElement.GetProperty("properties").GetProperty("light");
        Assert.Equal(JsonValueKind.Number, light.ValueKind);
        Assert.Equal(3, light.GetInt32());
        // Beside the build's name and the plot, as before.
        Assert.Equal("My house", doc.RootElement.GetProperty("properties").GetProperty("build").GetString());
        Assert.Equal(12, doc.RootElement.GetProperty("properties").GetProperty("plot").GetInt32());
        Assert.Equal<int?>(3, LayoutReader.Parse(json).Light);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(6)]
    public void NoLevelOrABadOneIsLeftOut(int? light)
    {
        var json = LayoutWriter.Write(Inside(light), [], [], "My house");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("properties").TryGetProperty("light", out _));
        Assert.Null(LayoutReader.Parse(json).Light);
    }

    [Fact]
    public void AGardensSaveHasNone()
    {
        // As Backups writes a garden: no level given.
        var garden = new[] { new SavedPiece(6200, "Garden Bench", new Vector3(5, 0.25f, -3), -0.7f, null) };
        var json = LayoutWriter.Write(new SavedHome("Medium", "Mist", [], [], 12), [], garden, "My garden");
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("properties").TryGetProperty("light", out _));
        // The older overload (backups' tests) writes none either.
        var older = LayoutWriter.Write("Medium", "Mist", [], garden, "Backup", 12);
        Assert.Null(LayoutReader.Parse(older).Light);
    }

    [Theory]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"build":"Old"}}""", null)] // an older file
    [InlineData("""{"houseSize":"Small","interiorFurniture":[]}""", null)] // MakePlace's own
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":4}}""", 4)]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":"2"}}""", 2)]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":2.5}}""", null)]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":0}}""", null)]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":9}}""", null)]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":null}}""", null)]
    [InlineData("""{"houseSize":"Small","interiorFurniture":[],"properties":{"light":true}}""", null)]
    public void AFilesLevelIsReadOnlyWhenItsOne(string json, int? light) => Assert.Equal(light, LayoutReader.Parse(json).Light);

    [Fact]
    public void ThisHouseSaysTheLightingInside()
    {
        Assert.Equal("Medium house · 312 of 450 pieces placed inside · lighting 3 of 5", CareWords.HouseLine(Side.Interior, "Medium", 312, 450, 3));
        Assert.Equal("Small house · lighting 5 of 5", CareWords.HouseLine(Side.Interior, "Small", null, 300, 5));
        // Not read: as before.
        Assert.Equal("Medium house · 312 of 450 pieces placed inside", CareWords.HouseLine(Side.Interior, "Medium", 312, 450, null));
        // A garden has none.
        Assert.Equal("20 of 60 pieces placed in the garden", CareWords.HouseLine(Side.Garden, "Medium", 20, 60, 3));
    }
}
