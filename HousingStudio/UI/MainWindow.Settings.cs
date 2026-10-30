using System;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

public sealed partial class MainWindow
{
    private void DrawSettings()
    {
        Widgets.BeginPanel();
        Widgets.Eyebrow("Placing");
        var backup = config.BackupBeforePlacing;
        if (Widgets.Toggle("Save a backup of what's placed before starting", ref backup)) { config.BackupBeforePlacing = backup; config.Save(); }
        Widgets.Muted($"Backups are .build files in {Backups.Folder}. Load one here to put everything back where it was.");
        Widgets.Gap(4);
        var aim = config.AimStoreroomPreview;
        if (Widgets.Toggle("Aim storeroom pieces at their spot", ref aim)) { config.AimStoreroomPreview = aim; config.Save(); }
        Widgets.Muted("When you pick a storeroom piece the design needs, the game's preview of it moves to its spot, so choosing Place puts it there.");
        Widgets.Gap(4);
        var anywhere = config.PlaceAnywhere;
        if (Widgets.Toggle("Place anywhere while placing", ref anywhere)) { config.PlaceAnywhere = anywhere; config.Save(); }
        Widgets.Muted("Lets floating, sunk and overlapping pieces be set (the MakePlace plugin's option): it switches off the game's own placement check while a run lasts, and always switches it back. Off unless the design needs it.");
        Widgets.Gap(4);
        var jitter = config.Jitter * 100;
        ImGui.SetNextItemWidth(S(240));
        if (ImGui.SliderFloat("##jitter", ref jitter, 0, 50, "Pace varies by ±%.0f%%"))
        {
            config.Jitter = jitter / 100f;
            config.Save();
        }
        var failures = config.MaxFailuresInARow;
        ImGui.SetNextItemWidth(S(240));
        if (ImGui.SliderInt("##failures", ref failures, 1, 10, "Stop after %d failures in a row"))
        {
            config.MaxFailuresInARow = failures;
            config.Save();
        }
        var chat = config.ChatMessages;
        if (Widgets.Toggle("Say what's next in chat", ref chat)) { config.ChatMessages = chat; config.Save(); }
        Widgets.EndPanel();
        Widgets.Gap(10);

        Widgets.BeginPanel();
        Widgets.Eyebrow("Markers in the world");
        var show = config.ShowMarkers;
        if (Widgets.Toggle("Show where pieces go", ref show)) { config.ShowMarkers = show; config.Save(); }
        var labels = config.ShowLabels;
        if (Widgets.Toggle("Name a marker when the mouse is over it", ref labels)) { config.ShowLabels = labels; config.Save(); }
        var placed = config.ShowPlacedMarkers;
        if (Widgets.Toggle("Mark pieces already in place too", ref placed)) { config.ShowPlacedMarkers = placed; config.Save(); }
        var range = config.MarkerRange;
        ImGui.SetNextItemWidth(S(240));
        if (ImGui.SliderFloat("##range", ref range, 5, 120, "Within %.0f yalms of you"))
        {
            config.MarkerRange = range;
            config.Save();
        }
        Widgets.Gap(4);
        ImGui.TextColored(Palette.Brass, "●");
        ImGui.SameLine();
        ImGui.TextColored(Palette.Text, "to move");
        ImGui.SameLine(0, S(14));
        ImGui.TextColored(Palette.BrassSoft, "●");
        ImGui.SameLine();
        ImGui.TextColored(Palette.Text, "storeroom");
        ImGui.SameLine(0, S(14));
        ImGui.TextColored(Palette.Blueprint, "●");
        ImGui.SameLine();
        ImGui.TextColored(Palette.Text, "in your bags");
        ImGui.SameLine(0, S(14));
        ImGui.TextColored(Palette.Danger, "●");
        ImGui.SameLine();
        ImGui.TextColored(Palette.Text, "missing");
        ImGui.SameLine(0, S(14));
        ImGui.TextColored(Palette.Success, "●");
        ImGui.SameLine();
        ImGui.TextColored(Palette.Text, "in place");
        Widgets.EndPanel();
        Widgets.Gap(10);

        Widgets.BeginPanel();
        Widgets.Eyebrow("Good to know");
        Widgets.Wrapped("Placing a piece binds it to you (it can't be traded after), as placing it by hand does.", Palette.Text);
        Widgets.Wrapped("Dyes, walls, floors, lights and the outside of the house aren't applied: the lists show what to set by hand.", Palette.Text);
        Widgets.Wrapped("Plugins that act in the game for you are against Square Enix's terms of service. This one only moves your own furniture in your own home, one piece at a time at the pace you set, and stops the moment anything looks different, but the choice to use it is yours.", Palette.Ash);
        Widgets.Gap(4);
        if (Widgets.Button("Save what's placed as a build…", ButtonStyle.Ghost, small: true)) SaveDialog();
        Widgets.EndPanel();
    }
}
