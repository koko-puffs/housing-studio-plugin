using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
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
        // Header: the pill for where you are above the title, Settings on the right.
        Frame.BeginHeader(24);
        var at = Kit.At;
        var w = Kit.Width;
        var (where, tone) = WherePill();
        Kit.StatusPill(at, where, tone, maxWidth: w - S(56));
        var titleY = at.Y + S(28) + S(8);
        var titleH = Title(new Vector2(at.X, titleY), "Housing", "Studio", w - S(56));
        SettingsButton(new Vector2(at.X + w - S(40), at.Y));
        Kit.Reserve(at, w, titleY + titleH - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##start", null, 20, 22);
        CareNotice();
        DrawDropZone();
        if (session.LoadError != null && session.Layout == null)
        {
            Kit.Gap(12);
            AlertNote(session.LoadError, Palette.Danger);
        }
        DrawThisHouse();
        DrawRecent();
        Frame.BeginBottom("start-bottom");
        DrawBrowse();
        Frame.EndBottom();
        Frame.EndBody();
        // The whole body takes a dropped file too.
        DropTarget();
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

    private const string DropNote = "Drop a .build or MakePlace file here";
    private static readonly Run[] DropNoteRuns = { new(DropNote, Face.Sans, 13, Palette.Dim) };

    /// <summary>
    /// The drop zone as one row (the dashed edge, 18px inside): the round brass upload well, "Load a
    /// design" and its note, and Choose a file on the right (under the words when narrow). Pressing
    /// anywhere on it chooses a file too; a file dragged over it lights it in brass.
    /// </summary>
    private void DrawDropZone()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var pad = S(18);
        var well = S(44);
        const string label = "Choose a file";
        var bw = Kit.ButtonWidth(label, BtnSize.Tiny);
        var bh = Kit.ButtonHeight(BtnSize.Tiny);
        var textX = at.X + pad + well + S(14);
        var besideW = at.X + w - pad - bw - S(14) - textX;
        var stacked = besideW < S(170);
        var textW = stacked ? at.X + w - pad - textX : besideW;
        var dragging = DraggingLayout;
        var title = dragging ? "Let go to load it" : "Load a design";
        var titleH = Txt.Natural(Face.SansMedium, 14);
        var noteH = Txt.Flow(Vector2.Zero, textW, DropNoteRuns, 19.5f, out _, draw: false);
        var wordsH = titleH + S(2) + noteH;
        var contentH = stacked ? MathF.Max(well, wordsH + S(10) + bh) : MathF.Max(well, MathF.Max(wordsH, bh));
        var h = pad * 2 + contentH;
        var dl = Kit.Dl;

        // The button goes in first, so it (not the zone under it) answers the mouse there.
        var top = at.Y + pad;
        var wordsTop = stacked ? top : top + (contentH - wordsH) / 2;
        var buttonAt = stacked ? new Vector2(textX, wordsTop + wordsH + S(10)) : new Vector2(at.X + w - pad - bw, top + (contentH - bh) / 2);
        var overButton = ImGui.IsMouseHoveringRect(buttonAt, buttonAt + new Vector2(bw, bh));
        var zoneHovered = !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, at + new Vector2(w, h)) && !overButton;

        var r = S(Palette.RadiusXl);
        if (dragging) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.BrassWash), r);
        Kit.Dashed(at, at + new Vector2(w, h), r, dragging || zoneHovered ? Palette.Brass : Palette.LineDashed, S(1));

        var wellAt = new Vector2(at.X + pad, stacked ? top : top + (contentH - well) / 2);
        var c = wellAt + new Vector2(well / 2);
        dl.AddCircleFilled(c, well / 2, Palette.U32(Palette.BrassWash), 48);
        dl.AddCircle(c, well / 2 - 0.5f, Palette.U32(Palette.BrassEdge), 48, 1f);
        Icons.Draw(dl, Icon.Upload, c - new Vector2(S(9)), S(18), Palette.Brass);
        Txt.Line(dl, new Vector2(textX, wordsTop), Txt.Fit(title, Face.SansMedium, 14, textW), Face.SansMedium, 14, dragging ? Palette.BrassSoft : Palette.Ivory);
        Txt.Flow(new Vector2(textX, wordsTop + titleH + S(2)), textW, DropNoteRuns, 19.5f, out _);

        if (Kit.Button("##choose", buttonAt, label, Btn.Glass, BtnSize.Tiny)) OpenDialog();
        var (clicked, _, _) = Kit.Hit("##dropzone", at, new Vector2(w, h));
        if (clicked && !overButton) OpenDialog();
        DropTarget();
        Kit.Reserve(at, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // This house: how full it is, what changed since your last run, Save and Send.

    /// <summary>The changes since your last run show under the card (its chip pressed).</summary>
    private bool changesOpen;

    /// <summary>The home and the run count the changes were opened for: another home, or a run since, closes them.</summary>
    private (string? Home, int Run) changesFor;

    private (Side Side, string? Size, int? Placed, int? Limit, string Line) houseLine = (Side.Interior, null, null, null, string.Empty);

    private (DriftCheck? Check, string Label) changedChip = (null, string.Empty);

    /// <summary>
    /// In a home you may decorate: the card for it. Its name, the home and how full it is ("Medium
    /// house · 312 of 450 pieces placed inside", read every 2 s while Start shows) with a meter; a chip
    /// for what changed since your last run here (with a snapshot), which checks and opens the changes
    /// under the card; Save as a build and Send to my builds.
    /// </summary>
    private void DrawThisHouse()
    {
        var ctx = session.Context;
        if (ctx.Side is not { } side || !ctx.HasPermission) return;
        care?.WantCount();
        int? placed = care?.Count is { } count && count.Home == care.CurrentHome && count.Side == side ? count.Placed : null;
        var limit = ItemLimits.Of(side, ctx.HouseSize);
        var hl = houseLine;
        if (hl.Line.Length == 0 || hl.Side != side || hl.Size != ctx.HouseSize || hl.Placed != placed || hl.Limit != limit)
            hl = houseLine = (side, ctx.HouseSize, placed, limit, CareWords.HouseLine(side, ctx.HouseSize, placed, limit));

        // Since your last run: a chip when there's a snapshot of this home.
        var home = care?.CurrentHome;
        var kept = care != null && !runner.Active ? care.SnapshotAt(home) : null;
        var check = kept != null && care?.LastCheck is { } last && last.Home == home ? last : null;
        // Open only for the home and the run it was opened for, and only while there's a check to show or
        // one under way: else the chip says "Check for changes" again and pressing it checks.
        if (changesOpen && (kept == null || changesFor.Home != home || changesFor.Run != runner.RunNumber || (check == null && !driftChecking)))
            changesOpen = false;
        string? chip = null;
        if (kept != null)
        {
            if (driftChecking) chip = "Checking…";
            else if (check == null) chip = "Check for changes";
            else
            {
                if (!ReferenceEquals(changedChip.Check, check)) changedChip = (check, CareWords.Changed(check.Report));
                chip = changedChip.Label;
            }
        }
        var chipTone = check is { Report.Unchanged: true } ? ChipTone.Normal : ChipTone.Brass;
        var chipIcon = check is { Report.Unchanged: true } ? Icon.Check : Icon.Refresh;

        Kit.Gap(14);
        Kit.BeginPanel("##this-house", 14, 16, 14);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var textX = at.X + S(34) + S(12);
        var chipW = chip != null ? Kit.CheckChipWidth(chip, chipIcon) : 0;
        var title = side == Side.Garden ? "This garden" : "This house";
        var titleH = Txt.Natural(Face.Serif, 22);
        var lineW = Txt.Measure(hl.Line, Face.Sans, 13).X;
        var chipBeside = chip == null || textX + MathF.Max(Txt.Measure(title, Face.Serif, 22).X, lineW) + S(12) + chipW <= at.X + w;
        var textW = (chip != null && chipBeside ? at.X + w - chipW - S(12) : at.X + w) - textX;
        HouseLineRun[0] = new Run(hl.Line, Face.Sans, 13, Palette.Dim);
        var lineH = Txt.Flow(Vector2.Zero, textW, HouseLineRun, 19.5f, out _, draw: false);
        var wordsH = titleH + lineH;
        var topH = MathF.Max(S(34), wordsH);
        Kit.IconThumb(new Vector2(at.X, at.Y + (topH - S(34)) / 2), Icon.House);
        var wordsTop = at.Y + (topH - wordsH) / 2;
        Txt.Draw(dl, new Vector2(textX, wordsTop), title, Face.Serif, 22, Palette.Ivory);
        Txt.Flow(new Vector2(textX, wordsTop + titleH), textW, HouseLineRun, 19.5f, out _);
        var y = at.Y + topH;
        if (chip != null)
        {
            var chipAt = chipBeside ? new Vector2(at.X + w - chipW, at.Y + (topH - S(30)) / 2) : new Vector2(textX, y + S(10));
            if (!chipBeside) y += S(10) + S(30);
            if (Kit.CheckChip("##changed", chipAt, chip, chipTone, chipIcon, null, changesOpen, ChangesTip(kept!.Value)) && !driftChecking)
            {
                if (changesOpen) changesOpen = false;
                else
                {
                    changesOpen = true;
                    changesFor = (home, runner.RunNumber);
                    CheckForChanges();
                }
            }
        }
        Kit.Reserve(at, w, y - at.Y);

        if (placed is { } p && limit is > 0)
        {
            Kit.Gap(12);
            var m = Kit.At;
            Kit.Meter(m, w, (float)p / limit.Value);
            Kit.Reserve(m, w, S(6));
        }

        Kit.Gap(12);
        var row = new Kit.Wrap(8);
        var bh = Kit.ButtonHeight(BtnSize.Tiny);
        const string save = "Save as a build";
        if (Kit.Button("##save-home", row.Next(Kit.ButtonWidth(save, BtnSize.Tiny, Icon.Save), bh), save, Btn.Glass, BtnSize.Tiny, Icon.Save, 0, false,
                "What's placed here, as a .build you can load again, open in MakePlace, or upload to Housing Studio"))
            SaveDialog();
        const string send = "Send to my builds";
        var cant = CantSend();
        if (Kit.Button("##send-home", row.Next(Kit.ButtonWidth(send, BtnSize.Tiny, Icon.Send), bh), send, Btn.Glass, BtnSize.Tiny, Icon.Send, 0, cant != null,
                cant ?? "What's placed here, straight into your builds on Housing Studio"))
            SendHome();
        row.End();
        DrawSendState();
        Kit.EndPanel();

        if (changesOpen && kept != null) DrawChanges(check);
    }

    private static readonly Run[] HouseLineRun = new Run[1];

    // ---------------------------------------------------------------------------------------------
    // Recent and Browse.

    private string? recentMenuFor;

    /// <summary>Recent: the eyebrow, then a panel of rows (house badge, name, "447 pieces · House (medium) · today 05.24", kind, chevron).</summary>
    private void DrawRecent()
    {
        var rows = vm.Recent();
        if (rows.Count == 0) return;
        Kit.Gap(18);
        Kit.Section("Recent");
        Kit.Gap(10);
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

    private static readonly Run[] BrowseRuns =
    {
        new("Looking for a design? ", Face.Sans, 13, Palette.Dim),
        new("Browse designs", Face.Sans, 13, Palette.Ivory, Link: "browse"),
    };

    /// <summary>"Looking for a design? Browse designs", centred at the foot of the body (opens Housing Studio's Browse page).</summary>
    private void DrawBrowse()
    {
        if (Txt.Para(BrowseRuns, 19.5f, center: true) == "browse") Dalamud.Utility.Util.OpenLink(BrowseUrl);
    }
}
