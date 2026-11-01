using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>Start (mockup 1): nothing loaded. The title and where you are, the drop zone, This house, From the site, Recent, and Browse designs.</summary>
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
        // Header (0.9.3): the title at the top, the pill for where you are under it, Settings on the right.
        // 20 on every screen (0.9.3): the round buttons at the top sit at one height from screen to screen.
        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var titleH = Title(at, "Housing", "Studio", w - S(56));
        SettingsButton(new Vector2(at.X + w - S(40), at.Y));
        var (where, tone) = WherePill();
        var pillY = at.Y + MathF.Max(titleH, S(40)) + S(8);
        Kit.StatusPill(new Vector2(at.X, pillY), where, tone, maxWidth: w);
        Kit.Reserve(at, w, pillY + S(28) - at.Y);
        Frame.EndHeader();

        Frame.BeginBody("##start", null, 20, 22);
        CareNotice();
        // 0.9.0: pinned designs first, then a one-row Load, This house (with its backups), and a short
        // Recent at the bottom. On a first start (nothing pinned or recent) the load row is the big one.
        var pins = vm.Pinned();
        var firstStart = pins.Count == 0 && vm.RecentCount == 0;
        // 0.9.1: no Pinned at all until something is pinned (Recent's ☆ says how).
        if (pins.Count > 0)
        {
            DrawPins(pins);
            Kit.Gap(14);
        }
        DrawDropZone(firstStart);
        if (session.LoadError != null && session.Layout == null)
        {
            Kit.Gap(12);
            AlertNote(session.LoadError, Palette.Danger);
        }
        backupsDrawn = false;
        DrawThisHouse();
        if (session.Context.Side == null || !session.Context.HasPermission)
        {
            // (The mockup's line: where This house went.)
            Kit.Gap(14);
            Txt.Note("This house shows here when you're in a home you may decorate.", Palette.Dim);
        }
        if (!backupsDrawn && vm.BackupRows().Count > 0)
        {
            Kit.Gap(14);
            Kit.BeginPanel("##backups-panel", 10, 14, 10);
            DrawBackups();
            Kit.EndPanel();
        }
        DrawFromSite();
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
    private void DrawDropZone(bool big = false)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var pad = S(big ? 34 : 14);
        var well = S(big ? 52 : 40);
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

    private (Side Side, string? Size, int? Placed, int? Limit, int? Light, string Line) houseLine = (Side.Interior, null, null, null, null, string.Empty);

    private (DriftCheck? Check, string Label) changedChip = (null, string.Empty);

    /// <summary>
    /// In a home you may decorate: the card for it. Its name, the home and how full it is ("Medium
    /// house · 312 of 450 pieces placed inside · lighting 3 of 5", read every 2 s while Start shows) with a meter; a chip
    /// for what changed since your last run here (with a snapshot), which checks and opens the changes
    /// under the card; the home on Housing Studio (verified, Verify as …, or Post it); Save as a
    /// build, Send to my builds, and Take a shot when it's verified as one of your designs.
    /// </summary>
    private void DrawThisHouse()
    {
        var ctx = session.Context;
        if (ctx.Side is not { } side || !ctx.HasPermission) return;
        care?.WantCount();
        int? placed = null, light = null;
        if (care?.Count is { } count && count.Home == care.CurrentHome && count.Side == side)
            (placed, light) = (count.Placed, count.Light);
        var limit = ItemLimits.Of(side, ctx.HouseSize);
        var hl = houseLine;
        if (hl.Line.Length == 0 || hl.Side != side || hl.Size != ctx.HouseSize || hl.Placed != placed || hl.Limit != limit || hl.Light != light)
            hl = houseLine = (side, ctx.HouseSize, placed, limit, light, CareWords.HouseLine(side, ctx.HouseSize, placed, limit, light));

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

        // On Housing Studio: verified, Verify as …, or Post it (Views/GameLink.cs).
        DrawSiteHome();

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
        ShotButton(ref row, bh);
        // 0.9.1: everything placed here put away, whatever design is loaded (or none).
        const string clear = "Clear out";
        var nothing = placed == 0;
        if (Kit.Button("##clear-home", row.Next(Kit.ButtonWidth(clear, BtnSize.Tiny, Icon.Inbox), bh), clear, Btn.Glass, BtnSize.Tiny, Icon.Inbox, 0,
                nothing || runner.Active, nothing ? "Nothing is placed here." : $"Puts away everything placed {(side == Side.Garden ? "in this garden" : "in this house")}: to the storeroom, your bags, or both (it asks)"))
            AskClearHome(side, placed);
        row.End();
        DrawSendState();
        DrawShotHint();
        if (vm.BackupRows().Count > 0)
        {
            Kit.Gap(12);
            Kit.Divider(Kit.At.X, Kit.At.X + Kit.Width, Kit.At.Y);
            Kit.Gap(6);
            DrawBackups();
        }
        Kit.EndPanel();

        if (changesOpen && kept != null) DrawChanges(check);
    }

    private static readonly Run[] HouseLineRun = new Run[1];

    // ---------------------------------------------------------------------------------------------
    // Recent and Browse.

    private string? recentMenuFor;

    /// <summary>Recent shows its three newest designs until "Show all" is pressed.</summary>
    private bool recentAll;

    private const int RecentShown = 3;

    /// <summary>
    /// Recent, at the bottom (0.9.0): designs only (backups are in This house, pinned designs at the top),
    /// the newest three with "Show all N" for the rest. Each row: house badge, name, "447 pieces · House
    /// (medium) · today 05.24", kind and chevron; hovering shows a ☆ that pins it, and right-click has Pin
    /// and Take it off this list.
    /// </summary>
    private void DrawRecent()
    {
        var total = vm.RecentCount;
        if (total == 0) return;
        if (total <= RecentShown) recentAll = false;
        var rows = vm.Recent(recentAll ? total : RecentShown);
        Kit.Gap(18);
        var link = total > RecentShown ? recentAll ? "Show fewer" : $"Show all {total}" : null;
        if (SectionLink("##recent-all", "Recent", link)) recentAll = !recentAll;
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

            var chevronX = at.X + w - S(14) - S(16);
            var kindW = Txt.SpacedWidth(row.Kind, Face.Mono, 11, 0.1f);
            var kindX = chevronX - S(14) - kindW;
            // The ☆ goes in first, so it (not the row under it) answers the mouse there.
            var starSize = S(28);
            var starAt = new Vector2(kindX - S(8) - starSize, at.Y + (h - starSize) / 2);
            var rowHovered = !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, at + new Vector2(w, h));
            var overStar = rowHovered && ImGui.IsMouseHoveringRect(starAt, starAt + new Vector2(starSize));
            if (rowHovered && row.Exists && StarButton($"##recent-pin{i}", starAt, starSize, false, "Pin to the top of Home"))
                TogglePin(row.Path, row.Name);

            // A file that's gone can't be opened: no hand over it (its menu still forgets it).
            var (clicked, hovered, _) = Kit.Hit($"##recent{i}", at, new Vector2(w, h), disabled: !row.Exists);
            if (clicked && row.Exists && !overStar) Open(row.Path);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && !Kit.Blocked)
            {
                recentMenuFor = row.Path;
                ImGui.OpenPopup("##recent-menu");
            }
            if (hovered || rowHovered) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd));
            if (hovered && !row.Exists) Kit.Tooltip("This file isn't there any more. Right-click to take it off the list.");
            if (rowHovered && row.Exists) StarButton($"##recent-pin-draw{i}", starAt, starSize, false, null, drawOnly: true);

            var x = at.X + S(14);
            var badge = new Vector2(x, at.Y + (h - S(36)) / 2);
            dl.AddRectFilled(badge, badge + new Vector2(S(36)), Palette.U32(Palette.Photo), S(Palette.RadiusSm));
            Icons.Draw(dl, Icon.House, badge + new Vector2(S(10)), S(16), Palette.Brass);
            x += S(36) + S(14);

            var textW = (rowHovered ? starAt.X : kindX) - S(14) - x;
            var textTop = at.Y + (h - (nameH + S(2) + S(19.5f))) / 2;
            Txt.Line(dl, new Vector2(x, textTop), Txt.Fit(row.Name, Face.SansMedium, 14, textW), Face.SansMedium, 14, row.Exists ? Palette.Ivory : Palette.Dim);
            Txt.Line(dl, new Vector2(x, textTop + nameH + S(2)), Txt.Fit(row.Note, Face.Sans, 13, textW), Face.Sans, 13, Palette.Ash, 19.5f);
            Txt.Spaced(dl, new Vector2(kindX, at.Y + (h - Txt.Natural(Face.Mono, 11)) / 2), row.Kind, Face.Mono, 11, Palette.Dim, 0.1f);
            Icons.Draw(dl, Icon.ChevronRight, new Vector2(chevronX, at.Y + (h - S(16)) / 2), S(16), Palette.Dim);
            Kit.Reserve(at, w, h);
        }
        if (Kit.BeginMenu("##recent-menu"))
        {
            if (recentMenuFor != null && File.Exists(recentMenuFor) && Kit.MenuItem("##pin", "Pin to the top of Home", Icon.Bookmark))
            {
                TogglePin(recentMenuFor);
                ImGui.CloseCurrentPopup();
            }
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

    // ---------------------------------------------------------------------------------------------
    // Pinned (0.9.0): three tiles at the top.

    private string? pinMenuFor;

    /// <summary>
    /// "Pinned · N of 3" and a row of three tiles: a picture well, the name on up to two lines, and
    /// "145 pieces · Apartment". A tile opens its design; its ★ unpins, as does right-click's Remove. An
    /// empty slot is a dashed tile saying how to pin. Narrow windows stack the tiles.
    /// </summary>
    private void DrawPins(List<RecentRow> pins)
    {
        SectionLink("##pins-count", "Pinned", null, $"{pins.Count} of {PinsMost}");
        Kit.Gap(10);
        var at = Kit.At;
        var w = Kit.Width;
        var gap = S(10);
        var columns = (w - gap * 2) / 3 >= S(112) ? 3 : 1;
        var tw = columns == 3 ? (w - gap * 2) / 3 : w;
        var pad = S(10);
        var shotH = columns == 3 ? S(54) : S(40);
        var nameH = Txt.Natural(Face.SansMedium, 13);
        var metaH = Txt.Natural(Face.Sans, 12);
        var th = columns == 3 ? pad + shotH + S(8) + nameH * 2 + S(2) + metaH + pad : pad * 2 + MathF.Max(shotH, nameH * 2 + metaH);
        var dl = Kit.Dl;
        var r = S(Palette.RadiusLg);
        const string hint = "Pin a design from Recent";
        // Stacked (a narrow window), one empty row is enough.
        var slots = columns == 3 ? PinsMost : Math.Min(PinsMost, pins.Count + 1);
        for (var slot = 0; slot < slots; slot++)
        {
            var pos = columns == 3 ? new Vector2(at.X + slot * (tw + gap), at.Y) : new Vector2(at.X, at.Y + slot * (th + gap));
            var size = new Vector2(tw, th);
            if (slot >= pins.Count)
            {
                Kit.Dashed(pos, pos + size, r, Palette.LineDashed, S(1));
                var words = Txt.Fit(hint, Face.Sans, 12, tw - pad * 2);
                var ww = Txt.Measure(words, Face.Sans, 12).X;
                Txt.Draw(dl, pos + new Vector2((tw - ww) / 2, (th - metaH) / 2), words, Face.Sans, 12, Palette.Dim);
                continue;
            }
            var pin = pins[slot];
            var starSize = S(26);
            var starAt = columns == 3 ? pos + new Vector2(tw - pad - starSize + S(4), pad - S(4)) : pos + new Vector2(tw - pad - starSize, (th - starSize) / 2);
            var tileHovered = !Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(pos, pos + size);
            var overStar = tileHovered && ImGui.IsMouseHoveringRect(starAt, starAt + new Vector2(starSize));
            if (StarButton($"##unpin{slot}", starAt, starSize, true, "Unpin"))
                TogglePin(pin.Path, pin.Name);
            var (clicked, hovered, _) = Kit.Hit($"##pin{slot}", pos, size);
            if (clicked && pin.Exists && !overStar) Open(pin.Path);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && !Kit.Blocked)
            {
                pinMenuFor = pin.Path;
                ImGui.OpenPopup("##pin-menu");
            }
            if (hovered && !pin.Exists) Kit.Tooltip("This file isn't there any more. Right-click to remove the pin.");

            dl.AddRectFilled(pos, pos + size, Palette.U32(hovered || tileHovered ? Palette.GlassLive : Palette.Glass), r);
            dl.AddRect(pos, pos + size, Palette.U32(hovered || tileHovered ? Palette.LineHover : Palette.Line), r, ImDrawFlags.None, 1f);
            Vector2 shotAt;
            Vector2 shotSize;
            float textX, textTop, textW;
            if (columns == 3)
            {
                shotAt = pos + new Vector2(pad);
                shotSize = new Vector2(tw - pad * 2, shotH);
                textX = pos.X + pad;
                textTop = shotAt.Y + shotH + S(8);
                textW = tw - pad * 2;
            }
            else
            {
                shotAt = pos + new Vector2(pad, (th - shotH) / 2);
                shotSize = new Vector2(shotH);
                textX = shotAt.X + shotH + S(12);
                textTop = pos.Y + (th - (nameH * 2 + metaH)) / 2;
                textW = starAt.X - S(8) - textX;
            }
            dl.AddRectFilled(shotAt, shotAt + shotSize, Palette.U32(Palette.Photo), S(Palette.RadiusSm));
            Icons.Draw(dl, Icon.House, shotAt + shotSize / 2 - new Vector2(S(9)), S(18), Palette.Brass);
            StarButton($"##unpin-draw{slot}", starAt, starSize, true, null, drawOnly: true);
            var (one, two) = TwoLines(pin.Name, Face.SansMedium, 13, textW);
            var nameColor = pin.Exists ? Palette.Ivory : Palette.Dim;
            Txt.Line(dl, new Vector2(textX, textTop), one, Face.SansMedium, 13, nameColor);
            if (two.Length > 0) Txt.Line(dl, new Vector2(textX, textTop + nameH), two, Face.SansMedium, 13, nameColor);
            var meta = pin.Exists ? PinMeta(pin.Note) : "Not found";
            Txt.Line(dl, new Vector2(textX, textTop + nameH * 2 + S(2)), Txt.Fit(meta, Face.Sans, 12, textW), Face.Sans, 12, Palette.Ash);
        }
        if (Kit.BeginMenu("##pin-menu"))
        {
            if (pinMenuFor != null && File.Exists(pinMenuFor) && Kit.MenuItem("##pin-open", "Open", Icon.ChevronRight))
            {
                Open(pinMenuFor);
                ImGui.CloseCurrentPopup();
            }
            if (Kit.MenuItem("##pin-remove", "Remove from pinned", Icon.X) && pinMenuFor != null)
            {
                TogglePin(pinMenuFor);
                ImGui.CloseCurrentPopup();
            }
            Kit.EndMenu();
        }
        var rows = columns == 3 ? 1 : slots;
        Kit.Reserve(at, w, rows * th + (rows - 1) * gap);
    }

    /// <summary>A pin's note without its "when" ("145 pieces · Apartment · yesterday" → "145 pieces · Apartment").</summary>
    private static string PinMeta(string note)
    {
        var parts = note.Split(" · ");
        return parts.Length >= 3 ? string.Join(" · ", parts.Take(2)) : note;
    }

    /// <summary>A name on at most two lines: as many words as fit on the first, the rest cut to fit on the second.</summary>
    private static (string One, string Two) TwoLines(string text, Face face, int px, float width)
    {
        if (Txt.Measure(text, face, px).X <= width) return (text, string.Empty);
        var words = text.Split(' ');
        var first = words[0];
        var n = 1;
        while (n < words.Length && Txt.Measure(first + " " + words[n], face, px).X <= width)
        {
            first += " " + words[n];
            n++;
        }
        if (Txt.Measure(first, face, px).X > width) return (Txt.Fit(text, face, px, width), string.Empty);
        return (first, Txt.Fit(string.Join(' ', words.Skip(n)), face, px, width));
    }

    // ---------------------------------------------------------------------------------------------
    // Backups (0.9.0): one row in This house, opening the list.

    private bool backupsOpen;
    private bool backupsDrawn;
    private (List<RecentRow>? Rows, string Words) backupsWords = (null, string.Empty);

    /// <summary>
    /// "Backups · 3 · newest yesterday" with a chevron; pressed, the backups the runs saved (and homes kept
    /// by a swap) are listed under it, newest first, each opening as a design.
    /// </summary>
    private void DrawBackups()
    {
        backupsDrawn = true;
        var rows = vm.BackupRows();
        if (rows.Count == 0) return;
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var h = S(36);
        var (clicked, hovered, _) = Kit.Hit("##backups", at, new Vector2(w, h));
        if (clicked) backupsOpen = !backupsOpen;
        if (hovered) dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd));
        Icons.Draw(dl, Icon.Undo, new Vector2(at.X + S(6), at.Y + (h - S(16)) / 2), S(16), Palette.Ash);
        if (!ReferenceEquals(backupsWords.Rows, rows))
        {
            // The newest by when it was saved (or opened), not the last words of its note (which can be "Not found").
            var newestAt = rows.Max(r => r.At);
            var newest = newestAt == DateTime.MinValue ? null : When.Relative(newestAt, DateTime.Now, t => t.ToString("HH:mm"));
            var total = Math.Max(rows.Count, vm.BackupsTotal);
            backupsWords = (rows, $"Backups · {total}{(newest != null ? $" · newest {newest}" : string.Empty)}");
        }
        var words = backupsWords.Words;
        var textX = at.X + S(6) + S(16) + S(10);
        var chevronX = at.X + w - S(6) - S(16);
        Txt.Line(dl, new Vector2(textX, at.Y + (h - Txt.Natural(Face.Sans, 13)) / 2), Txt.Fit(words, Face.Sans, 13, chevronX - S(8) - textX), Face.Sans, 13, Palette.Text);
        Icons.Draw(dl, backupsOpen ? Icon.ChevronDown : Icon.ChevronRight, new Vector2(chevronX, at.Y + (h - S(16)) / 2), S(16), Palette.Dim);
        if (hovered) Kit.Tooltip("What was here before each run: open one to put it back");
        Kit.Reserve(at, w, h);
        if (!backupsOpen) return;
        foreach (var (row, i) in rows.Select((r, i) => (r, i)))
        {
            var ra = Kit.At;
            var rh = S(40);
            var (rc, rhov, _) = Kit.Hit($"##backup{i}", ra, new Vector2(w, rh));
            if (rc && row.Exists) Open(row.Path);
            else if (rc)
            {
                // Gone from the disk: pressing it takes it off the list.
                config.RecentFiles.Remove(row.Path);
                config.RecentOpened.Remove(row.Path);
                config.Save();
            }
            if (rhov && !row.Exists) Kit.Tooltip("This file isn't there any more. Press to take it off the list.");
            if (rhov) dl.AddRectFilled(ra, ra + new Vector2(w, rh), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd));
            var kindW = Txt.SpacedWidth(row.Kind, Face.Mono, 11, 0.1f);
            var tx = textX;
            var tw = at.X + w - S(6) - kindW - S(12) - tx;
            var top = ra.Y + (rh - Txt.Natural(Face.Sans, 13) - Txt.Natural(Face.Sans, 12)) / 2;
            Txt.Line(dl, new Vector2(tx, top), Txt.Fit(row.Name, Face.Sans, 13, tw), Face.Sans, 13, row.Exists ? Palette.Ivory : Palette.Dim);
            Txt.Line(dl, new Vector2(tx, top + Txt.Natural(Face.Sans, 13)), Txt.Fit(row.Note, Face.Sans, 12, tw), Face.Sans, 12, Palette.Ash);
            Txt.Spaced(dl, new Vector2(at.X + w - S(6) - kindW, ra.Y + (rh - Txt.Natural(Face.Mono, 11)) / 2), row.Kind, Face.Mono, 11, Palette.Dim, 0.1f);
            Kit.Reserve(ra, w, rh);
        }
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
