using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>Start (mockup 1): nothing loaded. The title and where you are, the drop zone, Recent, and Browse designs.</summary>
public sealed partial class MainWindow
{
    private const string BrowseUrl = "https://www.kokos-tools.com/housing-studio/browse";

    /// <summary>The header pill for where you are: green when you can decorate, danger when you can't, neutral away from home.</summary>
    private (string Text, Tone Tone) WherePill()
    {
        var ctx = session.Context;
        return ctx.Kind switch
        {
            PlaceKind.Interior when ctx.HasPermission => ($"In your {Words.HomeInSentence(ctx.HouseSize)} · you can decorate", Tone.Success),
            PlaceKind.Interior => ($"In a {Words.HomeInSentence(ctx.HouseSize)} you can't decorate", Tone.Danger),
            PlaceKind.Ward when ctx.HasPermission && ctx.Plot != null => ($"In your garden (plot {ctx.Plot}) · you can decorate", Tone.Success),
            _ => ("Go to your home to place a design", Tone.Neutral),
        };
    }

    private void DrawStart()
    {
        // Header: the title and the pill on the left, Settings on the right.
        Frame.BeginHeader(24);
        var at = Kit.At;
        var right = Kit.Right;
        var titleH = Title(at, "Housing", "Studio", Kit.Width - S(56));
        var (where, tone) = WherePill();
        // Below the Settings button (40 tall), the pill has the whole width.
        var pillY = titleH + S(10);
        Kit.StatusPill(at + new Vector2(0, pillY), where, tone, maxWidth: pillY >= S(40) + S(4) ? Kit.Width : Kit.Width - S(56));
        SettingsButton(new Vector2(right - S(40), at.Y));
        Kit.Reserve(at, Kit.Width, titleH + S(10) + S(28));
        Frame.EndHeader();

        Frame.BeginBody("##start", null, 24, 24);
        DrawDropZone();
        DrawSaveHome();
        DrawHomeCare();
        if (session.LoadError != null && session.Layout == null)
        {
            Kit.Gap(12);
            AlertNote(session.LoadError, Palette.Danger);
        }
        DrawRecent();
        Frame.BeginBottom("start-bottom");
        DrawBrowse();
        Frame.EndBottom();
        Frame.EndBody();
        // The whole body takes a dropped file too.
        DropTarget();
    }

    /// <summary>
    /// In a home you can decorate: "Save this house as a build" (or garden), what's placed there as a
    /// .build you can load here, open in MakePlace or upload to Housing Studio. No design or editor needed.
    /// </summary>
    private void DrawSaveHome()
    {
        var ctx = session.Context;
        if (ctx.Side == null || !ctx.HasPermission) return;
        Kit.Gap(12);
        var at = Kit.At;
        var w = Kit.Width;
        var home = ctx.Side == Side.Garden ? "garden" : ctx.HouseSize switch
        {
            "Small" => "cottage",
            "Medium" => "house",
            "Large" => "mansion",
            "Apartment" => "apartment",
            _ => "home",
        };
        var label = $"Save this {home} as a build";
        var bw = Kit.ButtonWidth(label, BtnSize.Md, Icon.House, Btn.Glass);
        // Send to my builds beside it (SiteLink.cs), under it when the window's too narrow for both.
        const string send = "Send to my builds";
        var sw = Kit.ButtonWidth(send, BtnSize.Md, Icon.Upload, Btn.Glass);
        var side = bw + S(10) + sw <= w;
        var x = side ? at.X + (w - bw - S(10) - sw) / 2 : at.X + (w - bw) / 2;
        if (Kit.Button("##save-home", new Vector2(x, at.Y), label, Btn.Glass, BtnSize.Md, Icon.House, 0, false,
                "What's placed here, as a .build you can load again, open in MakePlace, or upload to Housing Studio"))
            SaveDialog();
        var sendAt = side ? new Vector2(x + bw + S(10), at.Y) : new Vector2(at.X + (w - sw) / 2, at.Y + S(40) + S(8));
        var cant = CantSend();
        if (Kit.Button("##send-home", sendAt, send, Btn.Glass, BtnSize.Md, Icon.Upload, 0, cant != null,
                cant ?? "What's placed here, straight into your builds on Housing Studio"))
            SendHome();
        Kit.Reserve(at, w, side ? S(40) : S(40) * 2 + S(8));
        DrawSendState();
    }

    /// <summary>A note with an icon before it (info in dim, alert in danger), as the mockups' notes with an "i".</summary>
    private static void AlertNote(string text, Vector4 iconColor, Icon icon = Icon.Alert, Vector4? textColor = null)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var textX = S(16 + 10);
        var h = Txt.Flow(at + new Vector2(textX, 0), w - textX, new[] { Run.Note(text, textColor) }, 19.5f, out _);
        Icons.Draw(Kit.Dl, icon, at + new Vector2(0, S(2)), S(16), iconColor);
        Kit.Reserve(at, w, h);
    }

    /// <summary>The drop zone: a dashed edge, the round brass-wash upload icon, "Load a design", and Choose a file.</summary>
    private void DrawDropZone()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var partH = S(22 * 1.15f);
        var noteH = S(19.5f);
        var h = S(40) + S(56) + S(14) + partH + S(6) + noteH + S(14) + S(40) + S(40);
        var dragging = DraggingLayout;
        var dl = Kit.Dl;

        // The button goes in first, so it (not the zone under it) answers the mouse there.
        var label = "Choose a file";
        var bw = Kit.ButtonWidth(label, BtnSize.Md, null, Btn.Primary);
        var buttonAt = new Vector2(at.X + (w - bw) / 2, at.Y + h - S(40) - S(40));
        var overButton = ImGui.IsMouseHoveringRect(buttonAt, buttonAt + new Vector2(bw, S(40)));
        var zoneHovered = !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, at + new Vector2(w, h)) && !overButton;

        var r = S(Palette.RadiusXl);
        if (dragging) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.BrassWash), r);
        Kit.Dashed(at, at + new Vector2(w, h), r, dragging || zoneHovered ? Palette.Brass : Palette.LineDashed, S(1.5f));

        var y = at.Y + S(40);
        Kit.Round(new Vector2(at.X + (w - S(56)) / 2, y), S(56), Icon.Upload, S(24), Palette.BrassWash, Palette.Brass, 1.6f);
        y += S(56) + S(14);
        var part = dragging ? "Let go to load it" : "Load a design";
        var pw = Txt.Measure(part, Face.Serif, 22).X;
        Txt.Line(dl, new Vector2(at.X + (w - pw) / 2, y), part, Face.Serif, 22, Palette.Ivory, 22 * 1.15f);
        y += partH + S(6);
        const string note = "Drop a .build or MakePlace .json file here";
        var nw = Txt.Measure(note, Face.Sans, 13).X;
        Txt.Line(dl, new Vector2(at.X + (w - nw) / 2, y), note, Face.Sans, 13, Palette.Ash, 19.5f);

        if (Kit.Button("##choose", buttonAt, label, Btn.Primary)) OpenDialog();
        var (clicked, _, _) = Kit.Hit("##dropzone", at, new Vector2(w, h));
        if (clicked) OpenDialog();
        DropTarget();
        Kit.Reserve(at, w, h);
    }

    private string? recentMenuFor;

    /// <summary>Recent: the eyebrow, then a panel of rows (house badge, name, "447 pieces · House (medium) · today 05.24", kind, chevron).</summary>
    private void DrawRecent()
    {
        var rows = vm.Recent();
        if (rows.Count == 0) return;
        Kit.Gap(18);
        Kit.EyebrowRow("Recent");
        Kit.Gap(8);
        Kit.BeginPanel("##recent", 6, 6, 6);
        var dl = Kit.Dl;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var at = Kit.At;
            var w = Kit.Width;
            var nameH = Txt.Natural(Face.SansMedium, 14);
            var h = S(12) * 2 + MathF.Max(S(36), nameH + S(2) + S(19.5f));
            if (i > 0) Kit.Divider(at.X, at.X + w, at.Y - 0.5f);

            var (clicked, hovered, _) = Kit.Hit($"##recent{i}", at, new Vector2(w, h), !row.Exists && row.Backup);
            if (clicked && row.Exists) Open(row.Path);
            if (!row.Backup && ImGui.IsItemClicked(ImGuiMouseButton.Right) && !Kit.Blocked)
            {
                recentMenuFor = row.Path;
                ImGui.OpenPopup("##recent-menu");
            }
            if (hovered) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd));
            if (hovered && !row.Exists) Kit.Tooltip("This file isn't there any more. Right-click to take it off the list.");

            var x = at.X + S(14);
            var badge = new Vector2(x, at.Y + (h - S(36)) / 2);
            dl.AddRectFilled(badge, badge + new Vector2(S(36)), Palette.U32(Palette.Photo), S(Palette.RadiusSm));
            Icons.Draw(dl, Icon.House, badge + new Vector2(S(10)), S(16), Palette.Brass);
            x += S(36) + S(14);

            var chevronX = at.X + w - S(14) - S(16);
            var kindW = Txt.SpacedWidth(row.Kind, Face.Mono, 11, 0.1f);
            var kindX = chevronX - S(14) - kindW;
            var textW = kindX - S(14) - x;
            var textTop = at.Y + (h - (nameH + S(2) + S(19.5f))) / 2;
            Txt.Line(dl, new Vector2(x, textTop), Txt.Fit(row.Name, Face.SansMedium, 14, textW), Face.SansMedium, 14, row.Exists ? Palette.Ivory : Palette.Dim);
            Txt.Line(dl, new Vector2(x, textTop + nameH + S(2)), Txt.Fit(row.Note, Face.Sans, 13, textW), Face.Sans, 13, Palette.Ash, 19.5f);
            Txt.Spaced(dl, new Vector2(kindX, at.Y + (h - Txt.Natural(Face.Mono, 11)) / 2), row.Kind, Face.Mono, 11, Palette.Dim, 0.1f);
            Icons.Draw(dl, Icon.ChevronRight, new Vector2(chevronX, at.Y + (h - S(16)) / 2), S(16), Palette.Dim);
            Kit.Reserve(at, w, h);
        }
        if (Kit.BeginMenu("##recent-menu"))
        {
            if (Kit.MenuItem("##remove", "Take it off this list", Icon.X) && recentMenuFor != null)
            {
                config.RecentFiles.Remove(recentMenuFor);
                config.RecentOpened.Remove(recentMenuFor);
                config.Save();
                ImGui.CloseCurrentPopup();
            }
            Kit.EndMenu();
        }
        Kit.EndPanel();
    }

    /// <summary>"Looking for a design?" with Browse designs, at the foot of the body.</summary>
    private void DrawBrowse()
    {
        Kit.BeginPanel("##browse", 18, 20, 18);
        var at = Kit.At;
        var w = Kit.Width;
        const string label = "Browse designs";
        var bw = Kit.ButtonWidth(label);
        var textW = w - bw - S(14);
        var titleH = Txt.Natural(Face.SansMedium, 14);
        var note = new[] { Run.Note("Homes on Housing Studio come with a build file you can load here.") };
        var noteH = Txt.Flow(at, textW, note, 19.5f, out _, draw: false);
        var h = MathF.Max(S(40), titleH + S(4) + noteH);
        var top = at.Y + (h - (titleH + S(4) + noteH)) / 2;
        Txt.Line(Kit.Dl, new Vector2(at.X, top), "Looking for a design?", Face.SansMedium, 14, Palette.Ivory);
        Txt.Flow(new Vector2(at.X, top + titleH + S(4)), textW, note, 19.5f, out _);
        if (Kit.Button("##browse-btn", new Vector2(at.X + w - bw, at.Y + (h - S(40)) / 2), label)) Dalamud.Utility.Util.OpenLink(BrowseUrl);
        Kit.Reserve(at, w, h);
        Kit.EndPanel();
    }
}
