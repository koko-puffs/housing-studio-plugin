using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace HousingStudio.Core;

/// <summary>
/// Where the camera was for a shot from the game, as the site keeps it beside the picture
/// (<c>hs_design_shots.pose</c>): <c>{"pos": [x, y, z], "target": [x, y, z], "fov": 45.0, "side":
/// "inside" | "garden"}</c>, in the game's axes (x east, y up, z south, yalms), in the frame the
/// design's layout uses: inside, the house's own coordinates; a garden, its plot's
/// (<see cref="Transforms.WardToPlot"/>, as the garden's pieces are written).
/// </summary>
/// <param name="Fov">The vertical field of view, in degrees.</param>
public readonly record struct ShotPose(Vector3 Pos, Vector3 Target, float Fov, Side Side)
{
    /// <summary>The site's limits (<c>hs_pose_tidy</c>): every number within 5,000 yalms, the field of view 5° to 120°.</summary>
    public const float Reach = 5000;

    public const float MinFov = 5;
    public const float MaxFov = 120;

    /// <summary>Within the site's limits, every number a number.</summary>
    public bool Valid =>
        Finite(Pos) && Finite(Target) && float.IsFinite(Fov) && Fov is >= MinFov and <= MaxFov
        && Within(Pos) && Within(Target) && Vector3.DistanceSquared(Pos, Target) > 1e-6f;

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Within(Vector3 v) => MathF.Abs(v.X) <= Reach && MathF.Abs(v.Y) <= Reach && MathF.Abs(v.Z) <= Reach;

    /// <summary>The pose as the edge function's <c>pose</c> field: positions to 4 places, the field of view to 3.</summary>
    public string ToJson() => new JsonObject
    {
        ["pos"] = Array(Pos),
        ["target"] = Array(Target),
        ["fov"] = Math.Round((double)Fov, 3),
        ["side"] = Side == Side.Garden ? "garden" : "inside",
    }.ToJsonString();

    private static JsonArray Array(Vector3 v) => new(Round(v.X), Round(v.Y), Round(v.Z));

    private static JsonNode Round(float f)
    {
        var r = Math.Round((double)f, 4);
        return JsonValue.Create(r == 0 ? 0d : r)!;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"pos ({Pos.X:0.00}, {Pos.Y:0.00}, {Pos.Z:0.00}) target ({Target.X:0.00}, {Target.Y:0.00}, {Target.Z:0.00}) fov {Fov:0.0}° {Side}");
}

/// <summary>
/// The camera's matrices, turned into a <see cref="ShotPose"/>. The game's matrices are
/// row-vector (<c>v' = v · M</c>, as System.Numerics and ClientStructs' own WorldToScreen use them):
/// the view takes the world to the camera, the projection the camera to the screen.
/// </summary>
public static class CameraMath
{
    /// <summary>How far ahead of the camera the target is put (any point along the view works; the direction is what's kept).</summary>
    public const float TargetDistance = 10;

    /// <summary>
    /// The camera's place and the way it looks, from its view matrix. The camera is the view's
    /// inverse: its place that inverse's translation, its own z axis the third row. Which way along
    /// that axis it looks is the projection's to say (its M34: −1 for a right-handed one, which looks
    /// down −z as System.Numerics' CreatePerspectiveFieldOfView, +1 for a left-handed one); when the
    /// projection doesn't say (M34 near 0), the point the camera looks at does, and failing that −z.
    /// </summary>
    public static bool FromView(Matrix4x4 view, Matrix4x4 projection, Vector3? lookAt, out Vector3 eye, out Vector3 forward)
    {
        eye = default;
        forward = default;
        if (!Matrix4x4.Invert(view, out var camera)) return false;
        eye = camera.Translation;
        var axis = new Vector3(camera.M31, camera.M32, camera.M33);
        var length = axis.Length();
        if (!(length > 1e-6f) || !float.IsFinite(length) || !float.IsFinite(eye.X + eye.Y + eye.Z)) return false;
        axis /= length;
        float sign;
        if (projection.M34 < -0.5f) sign = -1;
        else if (projection.M34 > 0.5f) sign = 1;
        else if (lookAt is { } at && Vector3.DistanceSquared(at, eye) > 1e-4f) sign = Vector3.Dot(at - eye, axis) >= 0 ? 1 : -1;
        else sign = -1;
        forward = axis * sign;
        return true;
    }

    /// <summary>
    /// The vertical field of view in degrees, from the projection: its M22 is cot(fov / 2) for any
    /// perspective projection (reversed or standard depth, a finite or infinite far plane alike).
    /// Null when it isn't a perspective one.
    /// </summary>
    public static float? VerticalFov(Matrix4x4 projection)
    {
        var y = MathF.Abs(projection.M22);
        if (!float.IsFinite(y) || y < 1e-4f) return null;
        var fov = 2 * MathF.Atan(1 / y) * 180 / MathF.PI;
        return fov is > 0.5f and < 179 ? fov : null;
    }

    /// <summary>A field of view given in radians (the render camera's own FoV), in degrees; null when it isn't one.</summary>
    public static float? Degrees(float radians) =>
        float.IsFinite(radians) && radians is > 0.01f and < 3.1f ? radians * 180 / MathF.PI : null;

    /// <summary>
    /// The pose for a shot: the camera's place and a point 10 yalms ahead, its vertical field of
    /// view (from the projection, else <paramref name="fovRadians"/>), and for a garden both points
    /// in the plot's own frame. Null, with why, when the camera can't be read or is out of the
    /// site's limits.
    /// </summary>
    public static ShotPose? Pose(Matrix4x4 view, Matrix4x4 projection, float fovRadians, Vector3? lookAt, Side side, PlotSpot? plot, out string why)
    {
        why = string.Empty;
        if (!FromView(view, projection, lookAt, out var eye, out var forward))
        {
            why = "Couldn't read the camera.";
            return null;
        }
        var fov = VerticalFov(projection) ?? Degrees(fovRadians);
        if (fov == null)
        {
            why = "Couldn't read the camera's field of view.";
            return null;
        }
        var target = eye + forward * TargetDistance;
        if (side == Side.Garden)
        {
            if (plot is not { } p)
            {
                why = "Couldn't tell which plot this is.";
                return null;
            }
            eye = Transforms.WardToPlot(eye, 0, p).Position;
            target = Transforms.WardToPlot(target, 0, p).Position;
        }
        var pose = new ShotPose(eye, target, Math.Clamp(fov.Value, ShotPose.MinFov, ShotPose.MaxFov), side);
        if (!pose.Valid)
        {
            why = "The camera is somewhere the site can't keep. Move it closer to the home.";
            return null;
        }
        return pose;
    }

    /// <summary>
    /// The same pose, near enough (a few hundredths of a yalm, a quarter of a degree): the camera
    /// didn't move between the pose being read and the picture being taken.
    /// </summary>
    public static bool Same(ShotPose a, ShotPose b)
    {
        if (a.Side != b.Side) return false;
        if (Vector3.Distance(a.Pos, b.Pos) > 0.05f) return false;
        if (MathF.Abs(a.Fov - b.Fov) > 0.25f) return false;
        var da = Vector3.Normalize(a.Target - a.Pos);
        var db = Vector3.Normalize(b.Target - b.Pos);
        return Vector3.Dot(da, db) >= MathF.Cos(0.25f * MathF.PI / 180);
    }
}

/// <summary>How big a shot is sent: its size, and the JPEG qualities tried until it's small enough.</summary>
public static class ShotSize
{
    /// <summary>The longest side sent.</summary>
    public const int LongEdge = 2560;

    /// <summary>A smaller try, when the picture is still too big at the lowest quality.</summary>
    public const int SmallerLongEdge = 1920;

    /// <summary>The most sent: under the edge function's 4 MB with room for the form around it.</summary>
    public const int MaxBytes = 3_984_588; // 3.8 MiB

    /// <summary>The site's own limits on a side (<c>hs_design_shots</c>).</summary>
    public const int MinSide = 64;

    public const int MaxSide = 8192;

    /// <summary>The JPEG qualities tried in turn (WIC's ImageQuality, 0 to 1): about 0.9 first.</summary>
    public static readonly float[] Qualities = { 0.9f, 0.84f, 0.78f, 0.72f, 0.65f, 0.58f, 0.5f };

    /// <summary>A picture's size with its longest side <paramref name="longEdge"/> at most, its shape kept; null when it's too small to send.</summary>
    public static (int Width, int Height)? Fit(int width, int height, int longEdge = LongEdge)
    {
        if (width < MinSide || height < MinSide) return null;
        var scale = Math.Min(1.0, (double)longEdge / Math.Max(width, height));
        var w = (int)Math.Round(width * scale);
        var h = (int)Math.Round(height * scale);
        if (w < MinSide || h < MinSide || w > MaxSide || h > MaxSide) return null;
        return (w, h);
    }

    /// <summary>A thumbnail's size for the preview, its longest side <paramref name="longEdge"/> at most.</summary>
    public static (int Width, int Height) Thumb(int width, int height, int longEdge = 480)
    {
        var scale = Math.Min(1.0, (double)longEdge / Math.Max(1, Math.Max(width, height)));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>"2.4 MB", "860 KB".</summary>
    public static string Bytes(long n) =>
        n >= 1024 * 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{n / (1024.0 * 1024.0):0.0} MB")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (n + 512) / 1024)} KB");
}
