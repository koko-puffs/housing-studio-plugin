using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using HousingStudio.Core;
using Lumina.Excel.Sheets;
using SceneCamera = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera;
using SceneCameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;

namespace HousingStudio.Game;

/// <summary>
/// The game's side of the site link (Session/GameLink.cs): which home you're in, as the site's
/// address (Core/SiteHome.cs makes it from the numbers read here), and where the camera is for a
/// shot (Core/ShotPose.cs). Framework thread only.
/// </summary>
public static unsafe class HomeReader
{
    private static readonly Dictionary<uint, string> WorldNames = new();

    /// <summary>
    /// The world you're on, in English (the World sheet's name, as the site keeps a design's world),
    /// or null before the game says. It's yours now, not the house id's: that may not carry it
    /// (0.4.2), and a home on another world is visited from that world.
    /// </summary>
    public static string? WorldName()
    {
        if (!Services.PlayerState.IsLoaded) return null;
        var id = Services.PlayerState.CurrentWorld.RowId;
        if (id == 0) return null;
        if (!WorldNames.TryGetValue(id, out var name))
        {
            name = Services.Data.GetExcelSheet<World>(Dalamud.Game.ClientLanguage.English).TryGetRow(id, out var row) ? row.Name.ExtractText() : string.Empty;
            WorldNames[id] = name;
        }
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// The district a house id belongs to: its own territory (a ward, "s1h1"), else the house's
    /// original one (the game's GetOriginalHouseTerritoryTypeId, a renovated Minimalist interior's
    /// "h1i2" saying nothing), else the interior's own name ("s1i2").
    /// </summary>
    private static string? DistrictOf(HouseId id, HousingContext ctx)
    {
        var district = Districts.OfTerritoryName(GameData.TerritoryName(id.TerritoryTypeId));
        if (district != null) return district;
        try
        {
            district = Districts.OfTerritoryName(GameData.TerritoryName(HousingManager.GetOriginalHouseTerritoryTypeId()));
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't read the house's original territory");
        }
        return district ?? Districts.OfTerritoryName(ctx.TerritoryName) ?? ctx.District;
    }

    private static ulong reportedWard;

    /// <summary>
    /// The site's address of the interior you're in (a house, an apartment or a private chamber),
    /// from its house id; null, with why, when it can't be told (or it's a workshop).
    /// </summary>
    public static SiteHome? Interior(HousingContext ctx, out string why)
    {
        why = "You're not inside a home.";
        var hm = HousingManager.Instance();
        if (ctx.Kind != PlaceKind.Interior || hm == null || hm->IndoorTerritory == null) return null;
        var id = hm->IndoorTerritory->HouseId;
        if (id.Id is 0 or ulong.MaxValue)
        {
            why = "The game hasn't said which home this is yet.";
            return null;
        }
        // The house id's ward and the game's "current ward" should agree; noted once when they don't.
        var current = hm->GetCurrentWard();
        if (current >= 0 && current != id.WardIndex && reportedWard != id.Id)
        {
            reportedWard = id.Id;
            Services.Log.Warning($"Housing Studio: the house id's ward ({id.WardIndex}) isn't the game's current ward ({current}); the house id's is used.");
        }
        var facts = new HouseFacts(WorldName(), DistrictOf(id, ctx), id.WardIndex, id.PlotIndex, id.IsApartment, id.ApartmentDivision, id.RoomNumber, id.IsWorkshop);
        return HomeAddress.From(facts, out why);
    }

    /// <summary>The site's address of the garden you're on (the house on its plot), or null with why.</summary>
    public static SiteHome? Garden(HousingContext ctx, out string why)
    {
        why = "You're not on a plot.";
        var hm = HousingManager.Instance();
        if (ctx.Kind != PlaceKind.Ward || hm == null || hm->OutdoorTerritory == null) return null;
        return HomeAddress.Garden(WorldName(), ctx.District, hm->GetCurrentWard(), ctx.Plot, out why);
    }

    /// <summary>
    /// Where the camera that's drawing the game is, as a shot's pose (Core/CameraMath): the scene's
    /// current camera (Graphics.Scene.CameraManager's, which is Group Pose's own while it's on, not
    /// the game's usual one), its view matrix, its render camera's projection and field of view. The
    /// garden's in its plot's frame. Null, with why, when it can't be read.
    /// </summary>
    public static ShotPose? Camera(Side side, PlotSpot? plot, out string why, out string detail)
    {
        why = "Couldn't read the camera.";
        detail = string.Empty;
        var cm = SceneCameraManager.Instance();
        if (cm == null) return null;
        var index = cm->CameraIndex;
        if (index < 0 || index >= cm->Cameras.Length) return null;
        SceneCamera* cam = cm->Cameras[index];
        if (cam == null || cam->RenderCamera == null) return null;
        Matrix4x4 view = cam->ViewMatrix;
        Matrix4x4 projection = cam->RenderCamera->ProjectionMatrix;
        Vector3 lookAt = cam->LookAtVector;
        var fovField = cam->RenderCamera->FoV;
        var aspectField = cam->RenderCamera->AspectRatio;
        var pose = CameraMath.Pose(view, projection, fovField, lookAt, side, plot, out why);
        // For checking in the game: the field of view from the projection beside the camera's own
        // (radians, read as vertical), and the projection's shape beside its aspect.
        detail = FormattableString.Invariant(
            $"camera {index}: fov {CameraMath.VerticalFov(projection) ?? 0:0.00}° from the projection, {CameraMath.Degrees(fovField) ?? 0:0.00}° from its FoV ({fovField:0.0000}), aspect {(projection.M11 != 0 ? projection.M22 / projection.M11 : 0):0.000} ({aspectField:0.000}), M34 {projection.M34:0.###}");
        return pose;
    }
}
