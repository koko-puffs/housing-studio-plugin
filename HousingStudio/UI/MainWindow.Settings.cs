using System.Text;
using Dalamud.Bindings.ImGui;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

public sealed partial class MainWindow
{
    private string? placeWords;

    private void DrawSettings()
    {
        Widgets.BeginPanel();
        Widgets.Eyebrow("Placing");
        var storeroom = config.StoreroomAutomatic;
        if (Widgets.Toggle("Place storeroom pieces for me", ref storeroom)) { config.StoreroomAutomatic = storeroom; config.Save(); }
        Widgets.Muted("Picks each piece in the furnishing list's storeroom tab, aims the game's preview at its spot and chooses Place. Off: you put them down, the plugin sets them at their spot. (Gardens: always you.)");
        var bags = config.BagsAutomatic;
        if (Widgets.Toggle("Place pieces from my bags for me", ref bags)) { config.BagsAutomatic = bags; config.Save(); }
        Widgets.Muted("Opens the item's menu, chooses Place, aims the piece you're handed and sets it down. Off: you put them down, the plugin sets them at their spot.");
        var dyes = config.DyeAfterPlacing;
        if (Widgets.Toggle("Dye pieces after placing", ref dyes)) { config.DyeAfterPlacing = dyes; config.Save(); }
        Widgets.Muted("Uses dyes from your bags, one per piece; pieces whose dye you don't have are skipped and the dye goes on the shopping list.");
        Widgets.Gap(4);
        var backup = config.BackupBeforePlacing;
        if (Widgets.Toggle("Save a backup of what's placed before starting", ref backup)) { config.BackupBeforePlacing = backup; config.Save(); }
        Widgets.Muted($"Backups are .build files in {Backups.Folder}. Load one here to put everything back where it was.");
        var anywhere = config.PlaceAnywhere;
        if (Widgets.Toggle("Place anywhere while placing", ref anywhere)) { config.PlaceAnywhere = anywhere; config.Save(); }
        Widgets.Muted("Lets floating, sunk and overlapping pieces be set (the MakePlace plugin's option): it switches off the game's own placement check while a run lasts, and always switches it back.");
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
        if (Widgets.Toggle("Say what's needed in chat", ref chat)) { config.ChatMessages = chat; config.Save(); }
        Widgets.EndPanel();
        Widgets.Gap(10);

        DrawDiagnostics();
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
        Legend(Palette.Brass, "to move", true);
        Legend(Palette.BrassSoft, "storeroom", true);
        Legend(Palette.Blueprint, "in your bags", true);
        Legend(Palette.Danger, "missing", true);
        Legend(Palette.Success, "in place", false);
        Widgets.EndPanel();
        Widgets.Gap(10);

        Widgets.BeginPanel();
        Widgets.Eyebrow("Good to know");
        Widgets.Wrapped("Placing a piece binds it to you (it can't be traded after), as placing it by hand does. Dyeing uses up the dye.", Palette.Text);
        Widgets.Wrapped("Walls, floors, lights and the outside of the house aren't changed: the Design page lists what to set by hand.", Palette.Text);
        Widgets.Wrapped("Plugins that act in the game for you are against Square Enix's terms of service. This one only works on your own furniture in a home you may decorate, through the game's own windows, one piece at a time at the pace you set, and stops the moment anything looks different, but the choice to use it is yours.", Palette.Ash);
        Widgets.Gap(4);
        if (Widgets.Button("Save what's placed as a build…", ButtonStyle.Ghost, small: true)) SaveDialog();
        Widgets.EndPanel();
    }

    private static void Legend(System.Numerics.Vector4 color, string text, bool more)
    {
        ImGui.TextColored(color, "●");
        ImGui.SameLine();
        ImGui.TextColored(Palette.Text, text);
        if (more) ImGui.SameLine(0, S(14));
    }

    private void DrawDiagnostics()
    {
        Widgets.BeginPanel();
        Widgets.Eyebrow("Diagnostics");
        Widgets.Muted("The automation clicks through the game's windows with values found by other plugins; some couldn't be checked before release. If a run stops, these help fix it.");
        Widgets.Gap(4);

        var stepMode = config.StepByStep;
        if (Widgets.Toggle("Step by step", ref stepMode)) { config.StepByStep = stepMode; config.Save(); }
        Widgets.Muted("Holds before every click, showing what it's about to do (and the window and values), until you press Continue on the Place page.");
        Widgets.Gap(6);

        ImGui.TextColored(Palette.Ivory, "Recorder");
        Widgets.Muted("While on, it writes down every game window that opens or closes, every click's values, and the housing state. Turn it on, place one piece from your bags and one from the storeroom, and dye one piece, by hand; turn it off, press Copy, and send the text along.");
        if (recorder.Recording)
        {
            if (Widgets.Button("Stop recording", ButtonStyle.Danger, small: true)) Services.Framework.RunOnFrameworkThread(recorder.Stop);
            ImGui.SameLine();
            Widgets.Chip($"Recording · {recorder.LineCount} lines", Palette.Danger, Palette.DangerWash);
        }
        else if (Widgets.Button("Start recording", ButtonStyle.Ghost, small: true))
        {
            Services.Framework.RunOnFrameworkThread(recorder.Start);
        }
        ImGui.SameLine();
        if (Widgets.Button("Copy recording + log", ButtonStyle.Brass, small: true, disabled: recorder.LineCount == 0 && log.Lines.Count == 0))
        {
            ImGui.SetClipboardText(Diagnostics());
            Notify("Diagnostics copied");
        }
        if (recorder.SetupError != null) Widgets.Muted(recorder.SetupError);
        Widgets.Gap(6);

        ImGui.TextColored(Palette.Ivory, "The \"place\" entry in an item's menu");
        Widgets.Muted($"Built in for your client: {string.Join(", ", UiTable.PlaceLabels(Services.ClientState.ClientLanguage))}. If your menu says something else, type it here (several, comma-separated).");
        placeWords ??= config.PlaceMenuWords;
        ImGui.SetNextItemWidth(S(320));
        if (ImGui.InputTextWithHint("##placewords", "e.g. Place", ref placeWords, 120))
        {
            config.PlaceMenuWords = placeWords.Trim();
            config.Save();
        }
        Widgets.Gap(6);

        if (ImGui.CollapsingHeader("Windows and clicks used"))
        {
            using var font = theme.Fonts.Small.Push();
            foreach (var call in Calls())
                ImGui.TextColored(call.Verified ? Palette.Text : Palette.BrassSoft, $"{call.Describe()}  · {call.Source}");
            ImGui.TextColored(Palette.BrassSoft, UiTable.ListStoreroomTab == null ? "Storeroom tab: you click it (values not known yet)." : UiTable.ListStoreroomTab.Describe());
            ImGui.TextColored(Palette.BrassSoft, UiTable.EditorSetMode == null ? "Editor modes (Rotate, Dye): you press them (values not known yet)." : UiTable.EditorSetMode.Describe());
        }
        Widgets.EndPanel();
    }

    private static UiCall[] Calls() => new[]
    {
        UiTable.ListSelect, UiTable.MenuChoose, UiTable.MenuCancel, UiTable.YesNoYes, UiTable.DyeShade,
        UiTable.DyePick, UiTable.DyeApply, UiTable.DyeClose, UiTable.DyeConfirmYes, UiTable.DyeConfirmNo,
    };

    private string Diagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Housing Studio {typeof(MainWindow).Assembly.GetName().Version} · client {Services.ClientState.ClientLanguage}");
        sb.AppendLine($"Where: {session.Context}");
        sb.AppendLine($"Now: {UiRecorder.DescribeState()}");
        sb.AppendLine($"Settings: storeroom auto {config.StoreroomAutomatic}, bags auto {config.BagsAutomatic}, dyes {config.DyeAfterPlacing}, step by step {config.StepByStep}, place words \"{config.PlaceMenuWords}\"");
        sb.AppendLine();
        sb.AppendLine("== Activity log ==");
        sb.AppendLine(log.AsText());
        sb.AppendLine("== Recording ==");
        sb.AppendLine(recorder.AsText());
        return sb.ToString();
    }
}
