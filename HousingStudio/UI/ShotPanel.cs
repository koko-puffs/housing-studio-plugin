using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// A shot from the game before it's sent (Session/GameLink.Shots.cs): "Taking the shot…" while the
/// picture is made, then its preview, "For Cozy Café · 2560×1440 · 1.8 MB", Send to Cozy Café and
/// Discard; once sent, Open its page and Close. Its own small panel, so it shows with the main window
/// closed (a shot taken with /hstudio shot).
/// </summary>
public sealed class ShotPanel : SmallPanel
{
    private readonly GameLink link;

    public ShotPanel(GameLink link, Fonts fonts)
        : base("Housing Studio · shot###HousingStudioShot", fonts, new Vector2(0.97f, 0.5f))
    {
        this.link = link;
    }

    protected override bool Wanted() => link.Capturing || link.Taken != null;

    protected override void Body()
    {
        var s = link.Taken;
        if (Head("Shot from the game", s is { Sent: true } ? "Close" : "Discard this shot")) link.Discard();
        Kit.Gap(12);
        if (s == null)
        {
            Line(Run.Note("Taking the shot…", Palette.Dim));
            return;
        }
        if (s.Thumb is { } thumb)
        {
            Picture(thumb, 200);
            Kit.Gap(10);
        }
        Line(Run.Note($"For {s.DesignName} · {s.Width}×{s.Height} · {ShotSize.Bytes(s.Bytes)}", Palette.Ash));
        if (s.Note is { } note)
        {
            Kit.Gap(6);
            Line(Run.Note(note.Text, note.Bad ? Palette.Danger : Palette.Success));
        }
        Kit.Gap(12);
        var row = new Kit.Wrap(8);
        var bh = Kit.ButtonHeight(BtnSize.Sm);
        if (s.Sent)
        {
            const string open = "Open its page";
            if (Kit.Button("##shot-open", row.Next(Kit.ButtonWidth(open, BtnSize.Sm, Icon.ExternalLink), bh), open, Btn.Glass, BtnSize.Sm, Icon.ExternalLink))
                Dalamud.Utility.Util.OpenLink(SiteLink.DesignUrl(s.DesignId));
            const string close = "Close";
            if (Kit.Button("##shot-close", row.Next(Kit.ButtonWidth(close, BtnSize.Sm), bh), close, Btn.Quiet, BtnSize.Sm))
                link.Discard();
        }
        else
        {
            const string discard = "Discard";
            var discardW = Kit.ButtonWidth(discard, BtnSize.Sm, null, Btn.Quiet);
            var room = Kit.Width - discardW - S(8);
            var cant = link.CantSendShot();
            var send = s.Sending ? "Sending…" : Txt.Fit($"Send to {s.DesignName}", Face.SansSemibold, 13, room - S(14) * 2 - S(24));
            if (Kit.Button("##shot-send", row.Next(Kit.ButtonWidth(send, BtnSize.Sm, Icon.Send, Btn.Primary), bh), send, Btn.Primary, BtnSize.Sm, Icon.Send, 0,
                    cant != null, cant ?? "To its page on Housing Studio, with where the camera was, beside its 3D view from the same spot"))
                link.SendShot();
            if (Kit.Button("##shot-discard", row.Next(discardW, bh), discard, Btn.Quiet, BtnSize.Sm, null, 0, s.Sending))
                link.Discard();
        }
        row.End();
    }
}
