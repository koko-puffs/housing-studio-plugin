using System;
using System.Collections.Generic;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>
/// The file's axes and turns, and the game's. These mirror Housing Studio's own reader
/// (src/features/housing-studio/makeplace.ts, <c>placement</c>, and build/model.ts, <c>entry</c>):
/// <list type="bullet">
/// <item>MakePlace stores a location as [x, z, y] of the game's (its z is up), divided by the
/// file's <c>interiorScale</c> / <c>exteriorScale</c>.</item>
/// <item>It stores the turn as a quaternion [x, y, z, w] about its own up axis, the opposite way
/// to the game's, so the game's yaw is <c>-2 * atan2(qz, qw)</c>.</item>
/// </list>
/// </summary>
public static class Transforms
{
    /// <summary>A file transform as the game's position and yaw. Anything that isn't a number reads as 0.</summary>
    public static (Vector3 Position, float Yaw) FromFile(IReadOnlyList<double>? location, IReadOnlyList<double>? rotation, float scale = 1)
    {
        if (!(scale > 0) || float.IsInfinity(scale)) scale = 1;
        double At(IReadOnlyList<double>? l, int i, double fallback) =>
            l != null && i < l.Count && double.IsFinite(l[i]) ? l[i] : fallback;

        var x = At(location, 0, 0);
        var z = At(location, 1, 0);
        var y = At(location, 2, 0);
        var qz = At(rotation, 2, 0);
        var qw = At(rotation, 3, 1);
        var yaw = TidyYaw((float)(-2 * Math.Atan2(qz, qw)));
        return (new Vector3((float)(x / scale), (float)(y / scale), (float)(z / scale)), yaw);
    }

    /// <summary>The game's position and yaw as a file writes them (Housing Studio's build/model.ts <c>entry</c>).</summary>
    public static (double[] Location, double[] Rotation) ToFile(Vector3 position, float yaw)
    {
        var half = -yaw / 2.0;
        return (
            new[] { Round(position.X), Round(position.Z), Round(position.Y) },
            new[] { 0, 0, Round(Math.Sin(half)), Round(Math.Cos(half)) });
    }

    private static double Round(double v)
    {
        var r = Math.Round(v * 1000) / 1000;
        return r == 0 ? 0 : r; // no "-0"
    }

    /// <summary>A turn kept between -pi and pi, as the game keeps it.</summary>
    public static float TidyYaw(float yaw)
    {
        if (!float.IsFinite(yaw)) return 0;
        var full = MathF.PI * 2;
        var y = ((yaw % full) + full) % full;
        if (y > MathF.PI) y -= full;
        return MathF.Abs(y) < 1e-6f ? 0 : y;
    }

    /// <summary>How far apart two turns are, ignoring whole turns (0 to pi).</summary>
    public static float AngleDiff(float a, float b)
    {
        var d = MathF.Abs(TidyYaw(a - b));
        return d;
    }

    /// <summary>The game's rotation quaternion for a yaw (a turn about +y).</summary>
    public static Quaternion YawQuaternion(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);

    /// <summary>The yaw of a quaternion that turns about +y only.</summary>
    public static float YawOf(Quaternion q) => TidyYaw(2 * MathF.Atan2(q.Y, q.W));

    /// <summary>A garden piece's plot-local place in the ward (the inverse of <see cref="WardToPlot"/>).</summary>
    public static (Vector3 Position, float Yaw) PlotToWard(Vector3 local, float yaw, PlotSpot plot)
    {
        var c = MathF.Cos(plot.Turn);
        var s = MathF.Sin(plot.Turn);
        var dx = local.X * c + local.Z * s;
        var dz = -local.X * s + local.Z * c;
        return (new Vector3(plot.X + dx, plot.Y + local.Y, plot.Z + dz), TidyYaw(yaw + plot.Turn));
    }

    /// <summary>
    /// A place in the ward in the plot's own coordinates: Housing Studio's <c>intoPlot</c>
    /// (viewer/exteriors.ts), and MakePlace's (which turns by its own plot angle, the opposite sign).
    /// </summary>
    public static (Vector3 Position, float Yaw) WardToPlot(Vector3 world, float yaw, PlotSpot plot)
    {
        var dx = world.X - plot.X;
        var dz = world.Z - plot.Z;
        var c = MathF.Cos(plot.Turn);
        var s = MathF.Sin(plot.Turn);
        return (new Vector3(dx * c - dz * s, world.Y - plot.Y, dx * s + dz * c), TidyYaw(yaw - plot.Turn));
    }
}
