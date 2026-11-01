using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>A shot taken and not yet sent (or just sent): the JPEG, where the camera was, its preview.</summary>
public sealed class Shot
{
    public Shot(string designId, string designName, ShotPose pose, byte[] jpeg, int width, int height, float quality, IDalamudTextureWrap? thumb)
    {
        DesignId = designId;
        DesignName = designName;
        Pose = pose;
        Jpeg = jpeg;
        Width = width;
        Height = height;
        Quality = quality;
        Bytes = jpeg.Length;
        Thumb = thumb;
    }

    public string DesignId { get; }

    public string DesignName { get; }

    public ShotPose Pose { get; }

    /// <summary>The picture as sent; let go of once it's sent.</summary>
    public byte[]? Jpeg { get; set; }

    public int Width { get; }

    public int Height { get; }

    public float Quality { get; }

    public int Bytes { get; }

    /// <summary>The preview, small (the shot's to let go of).</summary>
    public IDalamudTextureWrap? Thumb { get; set; }

    public bool Sending { get; set; }

    public bool Sent { get; set; }

    /// <summary>How sending went, in words (bad: it didn't).</summary>
    public (string Text, bool Bad)? Note { get; set; }
}

/// <summary>
/// Shots from the game: in your own home verified as one of your designs (inside, or its garden),
/// This house's Take a shot or <c>/hstudio shot</c> reads the camera (HomeReader.Camera) and grabs
/// the game's frame before Dalamud's windows are drawn over it; once the picture is in, the camera is
/// read again and must not have moved. On a worker the picture is made at most 2560 on its long side,
/// written as a JPEG at about 0.9 (lower until it's at most 3.8 MB), and a small preview made; the
/// shot panel (UI/ShotPanel.cs) shows it with Send to … and Discard. Sending posts the form to the
/// edge function. Every texture is let go of as soon as it's done with.
/// </summary>
public sealed partial class GameLink
{
    /// <summary>WIC's JPEG container (GUID_ContainerFormatJpeg), if the encoders' list doesn't name it by its MIME type.</summary>
    private static readonly Guid JpegContainer = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");

    /// <summary>DXGI_FORMAT_B8G8R8A8_UNORM: what the picture is turned into before it's written (the game's own format may be another).</summary>
    private const int Bgra = 87;

    private Task<IDalamudTextureWrap>? grab;
    private (ShotPose Pose, string DesignId, string DesignName, Side Side, PlotSpot? Plot, int Generation)? grabFor;
    private int shotGeneration;

    /// <summary>The shot taken, until it's sent and closed, or discarded.</summary>
    public Shot? Taken { get; private set; }

    /// <summary>A picture being taken or made.</summary>
    public bool Capturing { get; private set; }

    /// <summary>Why the last try didn't make a shot, in words.</summary>
    public string? ShotNote { get; private set; }

    /// <summary>The design this home is verified as, while you're in it (inside or its garden) and may decorate.</summary>
    public MyDesign? ShotDesign(HousingContext ctx) => Home is { } h && ctx.Side == h.Side ? h.Verified : null;

    /// <summary>Why Take a shot can't go now, or null.</summary>
    public string? CantShoot(HousingContext ctx)
    {
        if (!Usable) return "Link your Housing Studio account in Settings → Help first";
        if (runActive()) return "Not while a run is going";
        if (Home is not { } h || ctx.Side != h.Side) return "Go to your own home first";
        if (h.Verified == null) return h.Designs == null ? "Still looking for this home on Housing Studio" : "Verify this home as one of your designs first";
        if (Capturing) return "Taking one…";
        if (Taken is { Sent: false }) return "Send or discard the last shot first";
        if (ctx.Busy) return "Not now";
        return null;
    }

    /// <summary>
    /// Takes a shot (framework thread): the camera's pose now, and the game's frame grabbed before
    /// ImGui draws. With the HUD showing, it's hidden first (as Scroll Lock does) and shown again once
    /// the picture is in (0.9.10: hiding it yourself hid the plugin's button and the chat too). False,
    /// with why, when it can't.
    /// </summary>
    public bool TakeShot(HousingContext ctx, out string why)
    {
        why = CantShoot(ctx) ?? string.Empty;
        if (why.Length > 0) return false;
        var h = Home!;
        var design = h.Verified!;
        PlotSpot? spot = null;
        if (h.Side == Side.Garden)
        {
            if (h.Address is not { Plot: { } n } home || !plots.TryGet(home.District, n, out var s))
            {
                why = "Couldn't tell which plot this is.";
                return false;
            }
            spot = s;
        }
        if (!Hud.Hidden && !Services.ClientState.IsGPosing)
        {
            // A shot sent and still shown goes for the new one.
            if (Taken is { Sent: true }) Discard();
            if (Hud.Set(false))
            {
                hidHud = true;
                hudWait = (h, design, spot, DateTime.UtcNow);
                hudWaitFrames = 0;
                Capturing = true;
                ShotNote = null;
                return true;
            }
            // (Couldn't hide it: the picture is taken with it.)
        }
        return Grab(h, design, spot, out why);
    }

    /// <summary>The home and design a shot is for, waiting for the HUD to go (the frames it takes to hide).</summary>
    private (HomeLook Home, MyDesign Design, PlotSpot? Plot, DateTime At)? hudWait;
    private int hudWaitFrames;

    /// <summary>This plugin hid the HUD for a shot, and shows it again when the picture is in.</summary>
    private bool hidHud;

    /// <summary>Shows the HUD again if a shot hid it (and it's still hidden).</summary>
    private void ShowHudAgain()
    {
        hudWait = null;
        if (!hidHud) return;
        hidHud = false;
        if (Hud.Hidden) Hud.Set(true);
    }

    /// <summary>The camera read and the game's next frame grabbed for a shot.</summary>
    private bool Grab(HomeLook h, MyDesign design, PlotSpot? spot, out string why)
    {
        var pose = HomeReader.Camera(h.Side, spot, out why, out var detail);
        if (pose is not { } p)
        {
            // (What the camera held, for /xllog: what to look at when it can't be read.)
            Services.Log.Warning($"Housing Studio: couldn't read the camera for a shot ({why}): {detail}");
            return false;
        }
        try
        {
            grab = Services.Textures.CreateFromImGuiViewportAsync(
                new ImGuiViewportTextureArgs { ViewportId = ImGui.GetMainViewport().ID, TakeBeforeImGuiRender = true },
                "Housing Studio shot", stop.Token);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Housing Studio couldn't grab the game's picture");
            why = "Couldn't take the picture (see /xllog).";
            return false;
        }
        // A shot sent and still shown goes for the new one.
        if (Taken is { Sent: true }) Discard();
        Capturing = true;
        ShotNote = null;
        grabFor = (p, design.Id, design.Name, h.Side, spot, ++shotGeneration);
        Services.Log.Information($"Housing Studio: taking a shot for “{design.Name}”: {p}; {detail}");
        return true;
    }

    private sealed record MadeShot(byte[]? Jpeg, int Width, int Height, float Quality, IDalamudTextureWrap? Thumb, string? Why);

    private void TickShot(HousingContext ctx)
    {
        // The HUD hidden for the shot: a few frames and 0.15 s for it to go (1 s at most), then the picture.
        if (hudWait is { } w)
        {
            hudWaitFrames++;
            var waited = DateTime.UtcNow - w.At;
            if ((hudWaitFrames < 3 || waited < TimeSpan.FromMilliseconds(150) || !Hud.Hidden) && waited < TimeSpan.FromSeconds(1)) return;
            hudWait = null;
            if (!Grab(w.Home, w.Design, w.Plot, out var why))
            {
                Capturing = false;
                ShowHudAgain();
                ShotFailed(why);
            }
            return;
        }
        if (grab is not { IsCompleted: true } t || grabFor is not { } f) return;
        grab = null;
        grabFor = null;
        // The picture is in: the HUD comes back (if this hid it).
        ShowHudAgain();
        if (f.Generation != shotGeneration)
        {
            if (t.IsCompletedSuccessfully) Release(t.Result);
            return;
        }
        if (!t.IsCompletedSuccessfully)
        {
            Capturing = false;
            if (t.Exception != null) Services.Log.Error(t.Exception, "Housing Studio couldn't grab the game's picture");
            ShotFailed("Couldn't take the picture (see /xllog).");
            return;
        }
        var wrap = Own(t.Result);
        // The camera again: the picture is the frame after the pose was read, so it mustn't have moved.
        var now = HomeReader.Camera(f.Side, f.Plot, out _, out _);
        if (now is not { } p || !CameraMath.Same(p, f.Pose))
        {
            Release(wrap);
            Capturing = false;
            ShotFailed("The camera moved while the picture was taken. Hold it still and take it again.");
            return;
        }
        Work("making a shot", c => Make(wrap, c), m => ShotMade(m, f.Pose, f.DesignId, f.DesignName, f.Generation),
            _ => new MadeShot(null, 0, 0, 0, null, "Couldn't make the picture (see /xllog)."));
    }

    private static Guid? Jpeg()
    {
        foreach (var codec in Services.TextureReadback.GetSupportedImageEncoderInfos())
        {
            if (codec.ContainerGuid == JpegContainer || codec.MimeTypes.Any(m => m.Trim().Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)))
                return codec.ContainerGuid;
        }
        return null;
    }

    /// <summary>
    /// On a worker: the grabbed frame made 2560 at most on its long side, written as a JPEG at the
    /// first quality that's small enough (1920 if none at 2560 is), and a preview. The frame and the
    /// sized picture are let go of here whatever happens; the preview is the shot's.
    /// </summary>
    private async Task<MadeShot> Make(IDalamudTextureWrap grabbed, CancellationToken cancel)
    {
        IDalamudTextureWrap? sized = null;
        try
        {
            if (Jpeg() is not { } codec) return new MadeShot(null, 0, 0, 0, null, "This PC can't write JPEG pictures.");
            var format = Services.Textures.IsDxgiFormatSupportedForCreateFromExistingTextureAsync(Bgra) ? Bgra : 0;
            byte[]? jpeg = null;
            var quality = 0f;
            foreach (var edge in new[] { ShotSize.LongEdge, ShotSize.SmallerLongEdge })
            {
                if (ShotSize.Fit(grabbed.Width, grabbed.Height, edge) is not { } fit)
                    return new MadeShot(null, 0, 0, 0, null, "The game's picture is too small to send.");
                Release(sized);
                sized = Own(await Services.Textures.CreateFromExistingTextureAsync(grabbed,
                    new TextureModificationArgs { NewWidth = fit.Width, NewHeight = fit.Height, MakeOpaque = true, DxgiFormat = format },
                    leaveWrapOpen: true, "Housing Studio shot (sized)", cancel).ConfigureAwait(false));
                foreach (var q in ShotSize.Qualities)
                {
                    using var stream = new MemoryStream();
                    await Services.TextureReadback.SaveToStreamAsync(sized, codec, stream, new Dictionary<string, object> { ["ImageQuality"] = q },
                        leaveWrapOpen: true, leaveStreamOpen: true, cancel).ConfigureAwait(false);
                    if (stream.Length > ShotSize.MaxBytes) continue;
                    jpeg = stream.ToArray();
                    quality = q;
                    break;
                }
                if (jpeg != null) break;
            }
            if (jpeg == null || sized == null) return new MadeShot(null, 0, 0, 0, null, "The picture is too big to send, even smaller.");
            var (tw, th) = ShotSize.Thumb(sized.Width, sized.Height);
            var thumb = Own(await Services.Textures.CreateFromExistingTextureAsync(sized,
                new TextureModificationArgs { NewWidth = tw, NewHeight = th, DxgiFormat = format },
                leaveWrapOpen: true, "Housing Studio shot (preview)", cancel).ConfigureAwait(false));
            return new MadeShot(jpeg, sized.Width, sized.Height, quality, thumb, null);
        }
        finally
        {
            Release(grabbed);
            Release(sized);
        }
    }

    private void ShotMade(MadeShot m, ShotPose pose, string designId, string designName, int generation)
    {
        if (generation != shotGeneration)
        {
            Release(m.Thumb);
            return;
        }
        Capturing = false;
        if (m.Jpeg == null || m.Why != null)
        {
            Release(m.Thumb);
            ShotFailed(m.Why ?? "Couldn't make the picture.");
            return;
        }
        Taken = new Shot(designId, designName, pose, m.Jpeg, m.Width, m.Height, m.Quality, m.Thumb);
        log.Info($"Took a shot for “{designName}”: {m.Width}×{m.Height}, {ShotSize.Bytes(m.Jpeg.Length)} (quality {m.Quality:0.00}).");
        // With the HUD hidden (and in Group Pose) Dalamud hides the plugin's windows too.
        if (Services.GameGui.GameUiHidden || Services.ClientState.IsGPosing)
            Tell("Shot taken. Show the HUD again (or leave Group Pose) to see it and send it.", notification: false);
    }

    /// <summary>
    /// A shot that didn't come out, after it was asked for: This house says why, and chat does too (it
    /// may have been asked for with /hstudio shot, the HUD and so the plugin's windows hidden).
    /// </summary>
    private void ShotFailed(string why)
    {
        ShotNote = why;
        log.Warn($"No shot: {why}");
        Tell($"No shot: {why}", notification: false);
    }

    /// <summary>Why Send can't go now, or null.</summary>
    public string? CantSendShot() =>
        Taken is not { } s ? "No shot"
        : s.Sent ? "Sent"
        : s.Sending ? "Sending…"
        : !Usable ? "Link your Housing Studio account first"
        : s.Jpeg == null ? "No picture"
        : null;

    /// <summary>Send to …: the shot to its design's page, from a worker.</summary>
    public void SendShot()
    {
        if (CantSendShot() != null || Taken is not { Jpeg: { } jpeg } s) return;
        s.Sending = true;
        s.Note = null;
        var token = config.SiteToken;
        var pose = s.Pose.ToJson();
        Work("sending a shot", c => SiteLink.ShotAsync(http, token, s.DesignId, pose, jpeg, c), r =>
        {
            s.Sending = false;
            if (r.Ok)
            {
                s.Sent = true;
                s.Jpeg = null;
                s.Note = ($"Sent: it's on “{s.DesignName}”'s page.", false);
                log.Done($"Sent a shot to “{s.DesignName}” on Housing Studio.");
                return;
            }
            if (r.TokenRefused) Refused(token, r.Why);
            // Not verified (any more): This house asks the site again.
            if (r.Hint == "verify" && Home is { } h)
            {
                h.Designs = null;
                h.Why = null;
            }
            s.Note = (r.Why ?? "Couldn't send it.", true);
            log.Warn($"Couldn't send a shot to “{s.DesignName}”: {r.Why}");
        }, Fault<ShotSent>);
    }

    /// <summary>Discard (or close after sending): the shot and its preview go; a picture being made is dropped when it's done.</summary>
    public void Discard()
    {
        shotGeneration++;
        Capturing = false;
        if (hudWait != null) ShowHudAgain();
        ShotNote = null;
        if (Taken is not { } s) return;
        Release(s.Thumb);
        s.Thumb = null;
        s.Jpeg = null;
        Taken = null;
    }
}
