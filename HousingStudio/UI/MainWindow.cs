using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>The plugin's one window: a header with where you are, then Design, Pieces, Place and Settings.</summary>
public sealed partial class MainWindow : Window
{
    private static readonly string[] Tabs = { "Design", "Pieces", "Place", "Settings" };
    private const string DropLabel = "HousingStudioLayoutDrop";

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly Theme theme;
    private readonly FileDialogManager dialogs = new();
    private int tab;
    private string? notice;
    private DateTime noticeUntil;

    public MainWindow(Configuration config, LayoutSession session, PlacementRunner runner, GameData data, ActivityLog log, Theme theme)
        : base("Housing Studio###HousingStudioMain")
    {
        this.config = config;
        this.session = session;
        this.runner = runner;
        this.data = data;
        this.log = log;
        this.theme = theme;
        Size = new Vector2(660, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 460), MaximumSize = new Vector2(1600, 2000) };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        dialogs.AddedWindowFlags = ImGuiWindowFlags.NoCollapse;
    }

    public void ShowTab(int index)
    {
        tab = Math.Clamp(index, 0, Tabs.Length - 1);
        IsOpen = true;
    }

    public override void PreDraw() => theme.Push();

    public override void PostDraw() => theme.Pop();

    public override void Draw()
    {
        using var body = theme.Fonts.Body.Push();
        DrawHeader();
        Widgets.Gap(4);
        Widgets.Segmented("##tabs", Tabs, ref tab);
        Widgets.Gap(8);

        ImGui.BeginChild("##page", new Vector2(0, 0), false, ImGuiWindowFlags.None);
        switch (tab)
        {
            case 0:
                DrawDesign();
                break;
            case 1:
                DrawPieces();
                break;
            case 2:
                DrawPlace();
                break;
            default:
                DrawSettings();
                break;
        }
        ImGui.EndChild();

        // External drag & drop (from Explorer): the drop zone on the Design page is the target.
        Services.DragDrop.CreateImGuiSource(DropLabel, m => m.Files.Any(IsLayoutFile), m =>
        {
            ImGui.TextUnformatted($"Load {Path.GetFileName(m.Files.First(IsLayoutFile))}");
            return true;
        });

        dialogs.Draw();
    }

    /// <summary>Makes the last item a drop target for layout files dragged in from outside the game.</summary>
    private void DropTarget()
    {
        if (!Services.DragDrop.CreateImGuiTarget(DropLabel, out var files, out _)) return;
        var file = files.FirstOrDefault(IsLayoutFile);
        if (file != null) Open(file);
    }

    private static bool IsLayoutFile(string path) =>
        path.EndsWith(".build", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private void DrawHeader()
    {
        using (theme.Fonts.Title.Push())
            ImGui.TextColored(Palette.Ivory, "Housing Studio");
        var ctx = session.Context;
        var where = ctx.Kind switch
        {
            PlaceKind.Interior => $"Inside · {ctx.HouseSize?.ToLowerInvariant() ?? "home"}{(ctx.HouseSize is null or "Apartment" ? "" : " house")} · {Districts.Display(ctx.District)}",
            PlaceKind.Ward => ctx.Plot is { } p ? $"Garden · plot {p} · {Districts.Display(ctx.District)}" : $"Ward · {Districts.Display(ctx.District)}",
            _ => "Not at a home",
        };
        var mode = ctx.EditorOpen ? $" · editor: {ModeWord(ctx.Mode)}" : string.Empty;
        Widgets.Eyebrow(where + mode, ctx.Kind == PlaceKind.Elsewhere ? Palette.Dim : Palette.Ash);

        if (notice != null && DateTime.UtcNow < noticeUntil)
        {
            ImGui.SameLine();
            Widgets.Chip(notice, Palette.BrassSoft, Palette.BrassWash);
        }
    }

    private static string ModeWord(EditorMode m) => m switch
    {
        EditorMode.Move => "Move",
        EditorMode.Rotate => "Rotate",
        EditorMode.Store => "Store",
        EditorMode.Place => "placing",
        EditorMode.Dye => "Dye",
        EditorMode.Remove => "Remove",
        _ => "open",
    };

    private void Notify(string text)
    {
        notice = text;
        noticeUntil = DateTime.UtcNow + TimeSpan.FromSeconds(4);
    }

    private void Open(string path)
    {
        if (runner.Active)
        {
            Notify("Stop placing first");
            return;
        }
        if (session.Load(path))
        {
            config.AddRecent(path);
            Notify("Loaded");
            Services.Framework.RunOnFrameworkThread(() => session.Refresh(HousingContext.Read(Plugin.Plots)));
            tab = 0;
        }
    }

    private void OpenDialog()
    {
        var start = config.RecentFiles.Select(Path.GetDirectoryName).FirstOrDefault(Directory.Exists)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        dialogs.OpenFileDialog("Open a build or MakePlace layout", "Layouts{.build,.json},.build,.json", (ok, paths) =>
        {
            if (ok && paths.Count > 0) Open(paths[0]);
        }, 1, start, true);
    }

    private void SaveDialog()
    {
        var ctx = session.Context;
        if (ctx.Side == null || session.Live.Count == 0)
        {
            Notify("Nothing placed here to save");
            return;
        }
        var name = $"{(ctx.Side == Side.Garden ? "garden" : "house")}-{DateTime.Now:yyyy-MM-dd}";
        dialogs.SaveFileDialog("Save what's placed as a build", "Build{.build},MakePlace layout{.json}", name, ".build", (ok, path) =>
        {
            if (!ok) return;
            Services.Framework.RunOnFrameworkThread(() =>
            {
                if (Backups.SaveTo(path, session, data, Path.GetFileNameWithoutExtension(path)))
                {
                    log.Done($"Saved what's placed to {path}.");
                    Notify("Saved");
                }
            });
        }, Backups.Folder, true);
    }
}
