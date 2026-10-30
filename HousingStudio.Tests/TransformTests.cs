using System.Numerics;
using HousingStudio.Core;
using Xunit;

namespace HousingStudio.Tests;

public class TransformTests
{
    private static void Near(float expected, float actual, float tol = 1e-3f) =>
        Assert.True(MathF.Abs(expected - actual) <= tol, $"expected {expected}, got {actual}");

    private static void Near(Vector3 expected, Vector3 actual, float tol = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) <= tol, $"expected {expected}, got {actual}");

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(-2.5f)]
    [InlineData(3.1f)]
    public void WritingAndReadingBackKeepsThePlaceAndTurn(float yaw)
    {
        var pos = new Vector3(1.234f, -3.5f, 7.75f);
        var (location, rotation) = Transforms.ToFile(pos, yaw);
        var (back, backYaw) = Transforms.FromFile(location, rotation);
        Near(pos, back);
        Near(0, Transforms.AngleDiff(yaw, backYaw));
    }

    [Fact]
    public void WritesTheWaySitesBuildModelDoes()
    {
        // build/model.ts: location [x, z, y], rotation [0, 0, sin(-yaw/2), cos(-yaw/2)].
        var (location, rotation) = Transforms.ToFile(new Vector3(1, 2, 3), MathF.PI / 2);
        Assert.Equal(new double[] { 1, 3, 2 }, location);
        Assert.Equal(0, rotation[0]);
        Assert.Equal(0, rotation[1]);
        Assert.Equal(Math.Round(Math.Sin(-Math.PI / 4), 3), rotation[2]);
        Assert.Equal(Math.Round(Math.Cos(-Math.PI / 4), 3), rotation[3]);
    }

    [Fact]
    public void ReadsTheTurnAsTheMakePlacePluginDoes()
    {
        // The MakePlace plugin reads -ComputeZAngle(q) (its roll about z); check ours agrees.
        foreach (var yaw in new[] { 0.3f, -1.2f, 2.9f })
        {
            var q = Quaternion.CreateFromYawPitchRoll(0, 0, -yaw);
            var (_, ours) = Transforms.FromFile(new double[] { 0, 0, 0 }, new double[] { q.X, q.Y, q.Z, q.W });
            var siny = 2 * (q.W * q.Z + q.X * q.Y);
            var cosy = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            Near(-MathF.Atan2(siny, cosy), ours);
        }
    }

    [Fact]
    public void TheGamesQuaternionTurnsAboutUp()
    {
        var q = Transforms.YawQuaternion(1.1f);
        Near(1.1f, Transforms.YawOf(q));
        Near(new Vector3(MathF.Sin(1.1f), 0, MathF.Cos(1.1f)), Vector3.Transform(Vector3.UnitZ, q));
    }

    [Theory]
    [InlineData(4f, 4f - 2 * MathF.PI)]
    [InlineData(-4f, -4f + 2 * MathF.PI)]
    [InlineData(7f, 7f - 2 * MathF.PI)]
    [InlineData(0.000001f, 0f)]
    public void KeepsTurnsBetweenMinusPiAndPi(float raw, float expected)
    {
        Near(expected, Transforms.TidyYaw(raw));
    }

    [Fact]
    public void AngleDiffIgnoresWholeTurns()
    {
        Near(0, Transforms.AngleDiff(MathF.PI, -MathF.PI));
        // Just either side of half a turn: 0.2 apart, not 2pi - 0.2.
        Near(0.2f, Transforms.AngleDiff(MathF.PI - 0.1f, -MathF.PI + 0.1f));
        Near(0.5f, Transforms.AngleDiff(0.25f, -0.25f));
    }

    [Fact]
    public void PlotAndWardCoordinatesGoBothWays()
    {
        var plot = new PlotSpot(4, "s", 101, -28.5f, 78, 1.5708f);
        var local = new Vector3(3, 0.5f, -2);
        var (world, worldYaw) = Transforms.PlotToWard(local, 0.4f, plot);
        var (back, backYaw) = Transforms.WardToPlot(world, worldYaw, plot);
        Near(local, back);
        Near(0.4f, backYaw);
    }

    [Fact]
    public void GardensLandWhereTheMakePlacePluginPutsThem()
    {
        // MakePlace's own table holds Empyreum's plot 4 at (101, -28.5, 78) turned -1.570796 (its
        // sign); it places a garden piece at Transform(local, about +y by -its turn) + the plot, and
        // turns it by (yaw - its turn). Housing Studio's table has the same plot turned +1.5708.
        const float makePlaceTurn = -1.570796f;
        var plot = new PlotSpot(4, "s", 101, -28.5f, 78, 1.5708f);
        var local = new Vector3(5, 0.25f, -3);
        var expected = Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, -makePlaceTurn)) + new Vector3(101, -28.5f, 78);
        var (world, yaw) = Transforms.PlotToWard(local, 0.3f, plot);
        // MakePlace leaves y as the plot's plus the piece's.
        Near(expected, world);
        Near(0, Transforms.AngleDiff(0.3f - makePlaceTurn, yaw));
    }
}
