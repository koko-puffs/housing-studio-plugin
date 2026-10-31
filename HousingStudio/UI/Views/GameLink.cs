using System;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// The window's side of the site link (0.7.0, Session/GameLink.cs): Start's "From the site" (things
/// sent with the site's Send to game, each with Open and ×), This house's line about the home on the
/// site (Verified in game with Open, Verify as …, or Post it) and its Take a shot, and Settings →
/// Getting's two switches. The visitor panel and the shot's preview are their own small windows
/// (UI/VisitorPanel.cs, UI/ShotPanel.cs).
/// </summary>
public sealed partial class MainWindow
{
    private GameLink? link;

    internal void Use(GameLink l) => link = l;

    /// <summary>How many of what's waiting Start lists (the newest).</summary>
    private const int FromSiteShown = 5;

    /// <summary>
    /// "From the site", above Recent, when anything's waiting: each with a badge, its name, "Design ·
    /// sent today 21:04" (· opened), Open (loads it as choosing a file does) and ×; how the last Open or ×
    /// went under it.
    /// </summary>
    private void DrawFromSite()
    {
        if (link is not { Usable: true } l) return;
        var items = l.Inbox;
        var note = l.InboxNote;
        if (items.Count == 0 && note == null) return;
        Kit.Gap(18);
        Kit.Section("From the site", items.Count > FromSiteShown ? $"{FromSiteShown} of {items.Count}" : null);
        Kit.Gap(10);
        if (items.Count > 0)
        {
            Kit.BeginPanel("##from-site", 6, 6, 6);
            var dl = Kit.Dl;
            var cant = l.CantOpen();
            var shown = Math.Min(items.Count, FromSiteShown);
            for (var i = 0; i < shown; i++)
            {
                var item = items[i];
                var at = Kit.At;
                var w = Kit.Width;
                var nameH = Txt.Natural(Face.SansMedium, 14);
                var h = S(12) * 2 + MathF.Max(S(36), nameH + S(2) + S(19.5f));
                if (i > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);

                var x = at.X + S(14);
                var badge = new Vector2(x, at.Y + (h - S(36)) / 2);
                dl.AddRectFilled(badge, badge + new Vector2(S(36)), Palette.U32(Palette.Photo), S(Palette.RadiusSm));
                Icons.Draw(dl, item.Kind == "build" ? Icon.File : Icon.House, badge + new Vector2(S(10)), S(16), Palette.Brass);
                x += S(36) + S(14);

                var busy = l.InboxBusy(item.Id);
                var bh = Kit.ButtonHeight(BtnSize.Tiny);
                var open = l.Opening == item.Id ? "Opening…" : "Open";
                var openW = Kit.ButtonWidth(open, BtnSize.Tiny);
                var xAt = new Vector2(at.X + w - S(10) - S(30), at.Y + (h - S(30)) / 2);
                var openAt = new Vector2(xAt.X - S(8) - openW, at.Y + (h - bh) / 2);
                var textW = openAt.X - S(14) - x;
                var textTop = at.Y + (h - (nameH + S(2) + S(19.5f))) / 2;
                var sent = When.Relative(item.CreatedAt.LocalDateTime, DateTime.Now, Clock);
                var line = $"{item.KindWord} · sent {sent}{(item.FetchedAt != null ? " · opened" : string.Empty)}";
                Txt.Line(dl, new Vector2(x, textTop), Txt.Fit(item.Name, Face.SansMedium, 14, textW), Face.SansMedium, 14, Palette.Ivory);
                Txt.Line(dl, new Vector2(x, textTop + nameH + S(2)), Txt.Fit(line, Face.Sans, 13, textW), Face.Sans, 13, Palette.Ash, 19.5f);

                if (Kit.Button($"##inbox-open{i}", openAt, open, Btn.Glass, BtnSize.Tiny, null, 0, cant != null || busy,
                        cant ?? $"Load “{item.Name}” as a design (it's kept in the plugin's folder and Recent)"))
                    l.Open(item, OpenFromSite);
                if (Kit.IconButton($"##inbox-x{i}", xAt, Icon.X, "Take it off this list (on the site too)", 30, Btn.Quiet, busy))
                    l.Remove(item);
                Kit.Reserve(at, w, h);
            }
            Kit.EndPanel();
        }
        if (note is { } n)
        {
            Kit.Gap(8);
            AlertNote(n.Text, n.Bad ? Palette.Danger : Palette.Dim, n.Bad ? Icon.Alert : Icon.Info);
        }
    }

    /// <summary>A thing from the site, written to the plugin's folder: loaded as choosing that file does (on the framework thread, after a tick).</summary>
    private void OpenFromSite(string path)
    {
        if (runner.Active || Plugin.FixtureRun.Active || Plugin.Errands.Active)
        {
            config.AddRecent(path);
            Notify("It's in Recent: stop the run to open it");
            return;
        }
        OpenNow(path);
    }

    // ---------------------------------------------------------------------------------------------
    // This house: the home on the site, and Take a shot.

    /// <summary>
    /// This house's line about the home on Housing Studio: "On Housing Studio: Cozy Café · Verified in
    /// game" with a tick and Open; or Verify as … (several designs at this address: one button each);
    /// or "Not on Housing Studio yet · Post it"; what went wrong in words with Try again; and, not
    /// linked, a quiet "Link your account to verify this home".
    /// </summary>
    private void DrawSiteHome()
    {
        if (link == null) return;
        var l = link;
        string? clicked;
        if (!l.Linked)
        {
            Kit.Gap(12);
            clicked = IconLine(Icon.Link, Palette.Dim, Run.Note("Link your account to verify this home. ", Palette.Dim), Run.Note("Settings", Palette.BrassSoft) with { Link = "settings" });
        }
        else if (l.TokenRefused)
        {
            Kit.Gap(12);
            clicked = IconLine(Icon.Alert, Palette.Danger, Run.Note("Housing Studio didn't accept your token. ", Palette.Danger), Run.Note("Link again", Palette.BrassSoft) with { Link = "settings" });
        }
        else if (l.Home is not { } h)
        {
            return;
        }
        else if (h.Address == null)
        {
            if (h.AddressWhy == null || h.AddressTries < 10) return;
            Kit.Gap(12);
            clicked = IconLine(Icon.Info, Palette.Dim, Run.Note(h.AddressWhy + " ", Palette.Dim), Run.Note("Try again", Palette.BrassSoft) with { Link = "retry" });
        }
        else if (h.Why != null)
        {
            Kit.Gap(12);
            clicked = IconLine(Icon.Alert, Palette.Danger, Run.Note($"Couldn't ask Housing Studio about this home. {h.Why} ", Palette.Danger),
                Run.Note("Try again", Palette.BrassSoft) with { Link = "retry" });
        }
        else if (h.Designs == null)
        {
            Kit.Gap(12);
            clicked = IconLine(Icon.Refresh, Palette.Dim, Run.Note("Looking for this home on Housing Studio…", Palette.Dim));
        }
        else if (h.Verified is { } v)
        {
            Kit.Gap(12);
            clicked = IconLine(Icon.Check, Palette.Success, Run.Note("On Housing Studio: ", Palette.Ash), new Run(v.Name, Face.SansMedium, 13, Palette.Ivory),
                Run.Note(" · Verified in game · ", Palette.Success), Run.Note("Open", Palette.BrassSoft) with { Link = "open:" + v.Id });
        }
        else if (h.Designs.Count == 0)
        {
            Kit.Gap(12);
            clicked = IconLine(Icon.House, Palette.Dim, Run.Note("Not on Housing Studio yet · ", Palette.Dim), Run.Note("Post it", Palette.BrassSoft) with { Link = "post" });
        }
        else
        {
            DrawVerify(l, h);
            return;
        }
        Act(clicked);
    }

    private void Act(string? clicked)
    {
        switch (clicked)
        {
            case null:
                return;
            case "settings":
                OpenSettings(SettingsTab.Getting);
                return;
            case "retry":
                link?.RetryHome();
                return;
            case "post":
                Dalamud.Utility.Util.OpenLink(SiteLink.NewDesignUrl);
                return;
            default:
                if (clicked.StartsWith("open:", StringComparison.Ordinal)) Dalamud.Utility.Util.OpenLink(SiteLink.DesignUrl(clicked[5..]));
                return;
        }
    }

    /// <summary>Your designs at this address, none verified: Verify as … for each (three at most), inside; a garden says to verify from inside.</summary>
    private void DrawVerify(GameLink l, HomeLook h)
    {
        var designs = h.Designs!;
        Kit.Gap(12);
        var first = designs[0];
        var several = designs.Count > 1;
        var words = several
            ? $"{Words.Count(designs.Count, "design")} of yours have this address on Housing Studio, not yet verified in game."
            : h.Side == Side.Interior
                ? $"“{first.Name}” has this address on Housing Studio, not yet verified in game."
                : $"“{first.Name}” has this address on Housing Studio. Verify it from inside the house.";
        Act(IconLine(Icon.Info, Palette.Brass, Run.Note(words, Palette.Ash)));
        if (h.Side == Side.Interior)
        {
            Kit.Gap(10);
            var cant = l.CantVerify();
            var row = new Kit.Wrap(8);
            var bh = Kit.ButtonHeight(BtnSize.Tiny);
            var most = Kit.Width * (several ? 0.48f : 1f);
            for (var i = 0; i < Math.Min(designs.Count, 3); i++)
            {
                var d = designs[i];
                var label = h.Verifying ? "Verifying…" : Txt.Fit($"Verify as {d.Name}", Face.SansMedium, 13, most - S(10) * 2 - S(24));
                if (Kit.Button($"##verify{i}", row.Next(Kit.ButtonWidth(label, BtnSize.Tiny, Icon.Check), bh), label, Btn.Glass, BtnSize.Tiny, Icon.Check, 0, cant != null,
                        cant ?? $"Tell Housing Studio this home is “{d.Name}”: its page then says Verified in game, and visitors with the plugin see it here"))
                    l.Verify(d);
            }
            row.End();
        }
        if (h.VerifyNote is { } note)
        {
            Kit.Gap(8);
            AlertNote(note, Palette.Danger);
        }
    }

    /// <summary>One line of runs with an icon before it (as <see cref="AlertNote"/>); returns the link pressed.</summary>
    private static string? IconLine(Icon icon, Vector4 iconColor, params Run[] runs)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var textX = S(16 + 10);
        var h = Txt.Flow(at + new Vector2(textX, 0), w - textX, runs, 19.5f, out var clicked);
        Icons.Draw(Kit.Dl, icon, at + new Vector2(0, S(2)), S(16), iconColor);
        Kit.Reserve(at, w, h);
        return clicked;
    }

    /// <summary>This house's Take a shot, in its row of buttons, when the home is verified as one of your designs.</summary>
    private void ShotButton(ref Kit.Wrap row, float bh)
    {
        if (link?.ShotDesign(session.Context) is not { } design) return;
        const string label = "Take a shot";
        var cant = link.CantShoot(session.Context);
        if (Kit.Button("##take-shot", row.Next(Kit.ButtonWidth(label, BtnSize.Tiny, Icon.Camera), bh), label, Btn.Glass, BtnSize.Tiny, Icon.Camera, 0, cant != null,
                cant ?? $"A picture of this home from where the camera is, for “{design.Name}”'s page beside its 3D view from the same spot"))
            TakeShot();
    }

    /// <summary>Takes a shot on the framework thread, where the game and its camera are read.</summary>
    private void TakeShot() => OnFramework(() =>
    {
        if (link != null && !link.TakeShot(HousingContext.Read(Plugin.Plots), out var why)) Notify(why);
    });

    /// <summary>Under This house's buttons when a shot can be taken: the hint for a clean picture, or why the last one didn't come out.</summary>
    private void DrawShotHint()
    {
        if (link?.ShotDesign(session.Context) == null) return;
        Kit.Gap(8);
        if (link.ShotNote is { } why) AlertNote(why, Palette.Danger);
        else AlertNote("For a clean picture, hide the HUD (Scroll Lock) or use Group Pose, then type /hstudio shot.", Palette.Dim, Icon.Info, Palette.Dim);
    }

    // ---------------------------------------------------------------------------------------------
    // Settings → Getting.

    /// <summary>The site link's two switches, after the account row.</summary>
    private void DrawGameLinkRows(int i)
    {
        if (SwitchRow(i++, "##check-inbox", "Check for things sent from the site", "About once a minute while linked", config.CheckInbox,
                "Builds and designs you send to the game from Housing Studio (Send to game) wait under From the site on Start; a new one is said in chat. Needs the account linked."))
        {
            config.CheckInbox = !config.CheckInbox;
            config.Save();
        }
        if (SwitchRow(i, "##visit-homes", "Show homes on Housing Studio when I visit", "A small panel in a home that's a design", config.ShowHomesWhenVisiting,
                "In any home's interior, Housing Studio is asked whether it's a published design verified there; if it is, a small panel shows it (like, save and stamp it when linked). Off: nothing is asked."))
        {
            config.ShowHomesWhenVisiting = !config.ShowHomesWhenVisiting;
            config.Save();
        }
    }
}
