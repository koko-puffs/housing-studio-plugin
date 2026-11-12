using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using SceneLight = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Light;
using SceneEnvLocation = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.EnvLocation;
using RenderLight = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Light;

namespace HousingStudio.Game;

/// <summary>
/// The lights the game is drawing where you stand, as its engine holds them (/hstudio lights): for
/// matching Housing Studio's 3D view to the game's light. The site's extractor reads each light as
/// its file states it (colour, intensity, attenuation, range rate); how the engine turns those into
/// what it draws with (its reach, its falloff, its colour) is in the game's code, not its files, so
/// it's read here from the lights themselves. Written to lights/lights-&lt;time&gt;.json in the plugin's
/// folder, to hand over in a chat.
///
/// Read only: nothing in the game is changed. Every light of the layouts loaded (the house's own,
/// its fixtures', its furniture's, down through their groups), with the group it's in (the piece's
/// or fixture's .sgb), as the render light has it (shape, flags, colour, intensity, falloff type and
/// factor, range, the box it's clipped to, its cone, its shadow planes, its fade, the texture it
/// throws) and its bytes in hex (what isn't named yet); each place's ambient (its EnvLocation: the
/// ambient set and the cube it reflects); the room's brightness (the five steps), the time of day and
/// weather, and the environment's state as bytes. And every model of the house's pieces and fixtures
/// as the engine draws it: its file, and the glow power and colour the engine gives it (its BgObject's
/// ColorIntensity and Color), which aren't in the files either.
/// </summary>
public static unsafe class LightCapture
{
    /// <summary>The most lights written (a house holds a few hundred at most).</summary>
    private const int MaxLights = 4000;

    /// <summary>The most models written (every model of the house's pieces and fixtures).</summary>
    private const int MaxModels = 12000;

    public static string Folder => Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, "lights");

    /// <summary>Reads the lights and writes them; the file's path, or null with why not.</summary>
    public static string? Write(out string? why)
    {
        why = null;
        var world = LayoutWorld.Instance();
        if (world == null)
        {
            why = "the game's layout isn't loaded";
            return null;
        }
        var lights = new List<object>();
        var places = new List<object>();
        var models = new List<object>();
        var seen = new HashSet<nint>();
        void Layout(LayoutManager* layout, string which)
        {
            if (layout == null) return;
            foreach (var (_, layer) in layout->Layers)
            {
                if (layer.Value == null) continue;
                foreach (var (_, inst) in layer.Value->Instances)
                    Visit(inst.Value, which, new List<string>(), 0);
            }
        }
        void Visit(ILayoutInstance* inst, string which, List<string> groups, int depth)
        {
            if (inst == null || depth > 8 || lights.Count >= MaxLights || !seen.Add((nint)inst)) return;
            switch (inst->Id.Type)
            {
                case InstanceType.SharedGroup:
                {
                    var sg = (SharedGroupLayoutInstance*)inst;
                    var path = sg->ResourceHandle != null ? sg->ResourceHandle->FileName.ToString() : "";
                    var inner = new List<string>(groups) { path };
                    foreach (var child in sg->Instances.Instances)
                        if (child.Value != null) Visit(child.Value->Instance, which, inner, depth + 1);
                    break;
                }
                case InstanceType.Light:
                    lights.Add(LightOf((LightLayoutInstance*)inst, which, groups));
                    break;
                case InstanceType.EnvLocation:
                    places.Add(PlaceOf((EnvLocationLayoutInstance*)inst, which, groups));
                    break;
                case InstanceType.BgPart:
                    // (A piece's or a fixture's models: those in a group.)
                    if (groups.Count > 0 && models.Count < MaxModels)
                    {
                        var model = ModelOf(inst, which, groups);
                        if (model != null) models.Add(model);
                    }
                    break;
            }
        }
        Layout(world->ActiveLayout, "active");
        Layout(world->GlobalLayout, "global");
        foreach (var (key, layout) in world->LoadedLayouts)
            if (layout.Value != world->ActiveLayout && layout.Value != world->GlobalLayout)
                Layout(layout.Value, $"loaded {key:x}");

        var player = Services.Objects.LocalPlayer;
        var doc = new Dictionary<string, object?>
        {
            ["plugin"] = typeof(LightCapture).Assembly.GetName().Version?.ToString(),
            ["taken"] = DateTime.UtcNow.ToString("o"),
            ["territory"] = Services.ClientState.TerritoryType,
            ["player"] = player != null ? Vec(player.Position) : null,
            ["camera"] = CameraNow(),
            ["room"] = Room(),
            ["environment"] = Environment(),
            ["places"] = places,
            ["lights"] = lights,
            ["models"] = models,
        };
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, $"lights-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        return file;
    }

    /// <summary>
    /// A model as the engine draws it (its BgObject): its file, and what it glows with that isn't in
    /// its files: its ColorIntensity (a byte, 0xCE) and Color (0xD0), which bg.shpk takes as the
    /// instance's glow power and colour (g_InstancingData's m_EmissivePower, m_EmissiveColor and
    /// m_EmissiveBlend), with the bytes round them in hex (0xC0 to 0xE0) for what isn't named. Read
    /// by offset (FFXIVClientStructs' BgObject), so an older ClientStructs reads it as well.
    /// </summary>
    private static object? ModelOf(ILayoutInstance* inst, string which, List<string> groups)
    {
        // (BgPartsLayoutInstance's GraphicsObject, at 0x30.)
        var bg = *(byte**)((byte*)inst + 0x30);
        if (bg == null) return null;
        // (BgObject's ModelResourceHandle, at 0x90.)
        var handle = *(FFXIVClientStructs.FFXIV.Client.System.Resource.Handle.ResourceHandle**)(bg + 0x90);
        var file = handle != null ? handle->FileName.ToString() : "";
        return new Dictionary<string, object?>
        {
            ["layout"] = which,
            ["groups"] = groups,
            ["model"] = file,
            ["colorIntensity"] = bg[0xCE],
            ["color"] = new[] { (int)bg[0xD0], bg[0xD1], bg[0xD2], bg[0xD3] },
            ["bytes"] = Hex(bg + 0xC0, 0x20),
        };
    }

    private static object LightOf(LightLayoutInstance* inst, string which, List<string> groups)
    {
        var o = new Dictionary<string, object?>
        {
            ["layout"] = which,
            ["groups"] = groups,
            ["id"] = inst->Id.InstanceKey,
            ["sub"] = inst->SubId,
            ["type"] = inst->LightType.ToString(),
            ["layoutColour"] = Vec(inst->Color),
            ["instanceHex"] = Hex((byte*)inst, 0x50),
        };
        SceneLight* scene = inst->GraphicsObject;
        if (scene == null) return o;
        if (scene->ProjectedTexture != null) o["throws"] = scene->ProjectedTexture->FileName.ToString();
        RenderLight* r = scene->RenderLight;
        if (r == null) return o;
        o["shape"] = r->LightShape.ToString();
        o["flags"] = r->LightFlags.ToString();
        o["colour"] = Vec(r->Color);
        o["intensity"] = r->Intensity;
        o["falloffType"] = r->FalloffType.ToString();
        o["falloffFactor"] = r->FalloffFactor;
        o["range"] = r->Range;
        o["maxRange"] = new[] { Vec(r->MaxRange.Min), Vec(r->MaxRange.Max) };
        o["spotAngle"] = r->SpotLightAngleDegrees;
        o["angularFalloff"] = r->AngularFalloffDegrees;
        o["flatSkew"] = Vec(r->FlatLightSkewAngleDegrees);
        o["shadowPlane"] = new[] { r->ShadowPlaneNear, r->ShadowPlaneFar };
        o["characterShadowRange"] = r->CharacterShadowRange;
        o["fade"] = new[] { r->LightFade, r->LightFadeLength };
        if (r->Transform != null)
        {
            o["at"] = Vec(r->Transform->Position);
            o["turn"] = new[] { r->Transform->Rotation.X, r->Transform->Rotation.Y, r->Transform->Rotation.Z, r->Transform->Rotation.W };
            o["scale"] = Vec(r->Transform->Scale);
        }
        o["renderHex"] = Hex((byte*)r, 0x130);
        return o;
    }

    private static object PlaceOf(EnvLocationLayoutInstance* inst, string which, List<string> groups)
    {
        var o = new Dictionary<string, object?>
        {
            ["layout"] = which,
            ["groups"] = groups,
            ["id"] = inst->Id.InstanceKey,
            ["instanceHex"] = Hex((byte*)inst, 0x48),
        };
        var t = inst->GetTranslationImpl();
        if (t != null) o["at"] = Vec(*t);
        SceneEnvLocation* loc = inst->GraphicsObject;
        if (loc == null) return o;
        if (loc->AmbientSetResource != null) o["ambient"] = loc->AmbientSetResource->FileName.ToString();
        if (loc->EnvironmentCubemapResource != null) o["cube"] = loc->EnvironmentCubemapResource->FileName.ToString();
        o["hex"] = Hex((byte*)loc + 0x90, 0x30);
        return o;
    }

    /// <summary>
    /// Where the game's camera is, as a shot's is read (HomeReader.Camera, inside: the interior's
    /// own frame, the site's), with the screen's size and the camera's matrices, so the site's 3D
    /// view can be put at the same spot and the two pictures compared region by region.
    /// </summary>
    private static object? CameraNow()
    {
        var o = new Dictionary<string, object?>();
        var pose = HomeReader.Camera(Core.Side.Interior, null, out var why, out var detail);
        if (pose is { } p)
        {
            o["pos"] = Vec(p.Pos);
            o["target"] = Vec(p.Target);
            o["fov"] = p.Fov;
        }
        else o["why"] = why;
        o["detail"] = detail;
        var dev = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if (dev != null) o["screen"] = new[] { dev->Width, dev->Height };
        var cm = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
        if (cm != null && cm->CameraIndex >= 0 && cm->CameraIndex < cm->Cameras.Length)
        {
            FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera* cam = cm->Cameras[cm->CameraIndex];
            if (cam != null)
            {
                o["view"] = Mat(cam->ViewMatrix);
                if (cam->RenderCamera != null) o["projection"] = Mat(cam->RenderCamera->ProjectionMatrix);
            }
        }
        return o;
    }

    private static float[] Mat(Matrix4x4 m) =>
        [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

    private static object? Room()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IndoorTerritory == null) return null;
        var t = hm->IndoorTerritory;
        return new Dictionary<string, object?>
        {
            ["brightness"] = hm->GetBrightness(),
            ["invertedBrightness"] = t->InvertedBrightness,
            ["savedInvertedBrightness"] = t->SavedInvertedBrightness,
            ["brightnessCurrent"] = t->BrightnessCurrent,
            ["brightnessTarget"] = t->BrightnessTarget,
            ["brightnessTransitioning"] = t->IsBrightnessTransitioning,
        };
    }

    private static object? Environment()
    {
        var env = EnvManager.Instance();
        if (env == null) return null;
        return new Dictionary<string, object?>
        {
            ["dayTimeSeconds"] = env->DayTimeSeconds,
            ["weather"] = env->ActiveWeather,
            ["transition"] = env->TransitionProgress,
            // (EnvState: most of it isn't named yet; its bytes, to read against the .envb.)
            ["stateHex"] = Hex((byte*)&env->EnvState, sizeof(EnvState)),
        };
    }

    private static float[] Vec(Vector2 v) => [v.X, v.Y];
    private static float[] Vec(Vector3 v) => [v.X, v.Y, v.Z];
    private static float[] Vec(Vector4 v) => [v.X, v.Y, v.Z, v.W];
    private static float[] Vec(FFXIVClientStructs.FFXIV.Common.Math.Vector3 v) => [v.X, v.Y, v.Z];

    private static string Hex(byte* p, int n) => p == null ? "" : Convert.ToHexString(new ReadOnlySpan<byte>(p, n));
}
