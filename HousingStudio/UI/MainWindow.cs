using System;
using System.Collections.Generic;
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

/// <summary>
/// The plugin's one window. No tabs: the main view follows the session (Start with nothing loaded,
/// Ready, Running while a run or preview goes, Done until you close its result), and Pieces and
/// Settings open from the header with a back arrow. While a run goes they're hidden (the header
/// has no buttons then), so nothing that steers the run changes under it.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string DropLabel = "HousingStudioLayoutDrop";

    private enum Sub
    {
        None,
        Pieces,
        Settings,
        Shopping,
    }

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly Fonts fonts;
    private readonly Automation.UiRecorder recorder;
    private readonly Overlay overlay;
    private readonly ViewModel vm;
    private readonly FileDialogManager dialogs = new();
    private readonly List<Action> later = new();
    private Sub sub;

    public MainWindow(Configuration config, LayoutSession session, PlacementRunner runner, GameData data, ActivityLog log, Fonts fonts,
        Automation.UiRecorder recorder, Overlay overlay)
        : base("Housing Studio###HousingStudioMain")
    {
        this.config = config;
        this.session = session;
        this.runner = runner;
        this.data = data;
        this.log = log;
        this.fonts = fonts;
        this.recorder = recorder;
        this.overlay = overlay;
        vm = new ViewModel(config, session, runner, data);
        Txt.Fonts = fonts;
        if (config.Migrate()) config.Save();

        Size = new Vector2(640, 980);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 600), MaximumSize = new Vector2(1600, 2400) };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        dialogs.AddedWindowFlags = ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Opens the window on its main view.</summary>
    public void ShowMain()
    {
        sub = Sub.None;
        IsOpen = true;
    }

    public override void PreDraw() => Theme.PushFrame();

    public override void PostDraw()
    {
        Theme.Pop();
        // The file dialog is its own window, in ImGui's usual style.
        dialogs.Draw();
    }

    public override void Draw()
    {
        if (runner.TakeNotice() is { } notice) Notify(notice);
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        // If drawing throws (Dalamud catches it and logs it), the style pushed above still comes off:
        // left on, every frame would push more.
        try
        {
            DrawBody();
        }
        finally
        {
            Theme.Pop();
        }

        // Whatever changes what a view shows (opening, closing a design) waits until the frame is drawn.
        if (later.Count > 0)
        {
            var actions = later.ToArray();
            later.Clear();
            // Each on its own: one that throws doesn't drop the rest, nor escape the window (0.9.9).
            foreach (var a in actions)
            {
                try
                {
                    a();
                }
                catch (Exception ex)
                {
                    Services.Log.Error(ex, "Housing Studio action failed");
                    Notify("That didn't work (see /xllog)");
                }
            }
        }
    }

    private void DrawBody()
    {
        vm.Observe();
        using (fonts.Push(Face.Sans, 14))
        {
            // (Setting the fixtures: its Continue and Stop are in Ready's Finish stage and on Done.)
            if (runner.Active || Plugin.FixtureRun.Active || Plugin.Errands.Active) sub = Sub.None;
            // With a question up, nothing under it reacts (its own window takes the clicks too).
            Kit.Blocked = confirm != null;
            try
            {
            if (sub == Sub.Settings && !Plugin.Errands.Active && !Plugin.Errands.ResultWaiting)
            {
                // Settings over the errands' estimate too (its limits are in Getting); back goes back to it.
                DrawSettings();
            }
            else if (ErrandsShown())
            {
                // Errands (experimental): their estimate, run (its placing too) and summary.
                DrawErrands();
            }
            else if (sub == Sub.Shopping && vm.View is MainView.Ready or MainView.Done)
            {
                DrawShopping();
            }
            else
            {
                switch (vm.View)
                {
                    case MainView.Start:
                        sub = Sub.None;
                        DrawStart();
                        break;
                    case MainView.Running:
                        DrawRunning();
                        break;
                    case MainView.Done:
                        DrawDone();
                        break;
                    default:
                        if (sub == Sub.Pieces) DrawPieces();
                        else DrawReady();
                        break;
                }
            }
            }
            finally
            {
                // Even when a view throws, the question is drawn (to be answered) and nothing stays blocked.
                Frame.DrawToast();
                Kit.Blocked = false;
                DrawConfirm();
            }
        }

        // Dragging a file in from outside the game: the drop zone (Start) and the body (Ready) take it.
        Services.DragDrop.CreateImGuiSource(DropLabel, m => m.Files.Any(IsLayoutFile), m =>
        {
            ImGui.TextUnformatted($"Load {Path.GetFileName(m.Files.First(IsLayoutFile))}");
            return true;
        });
    }

    /// <summary>Runs after this frame's drawing is finished.</summary>
    private void Later(Action action) => later.Add(action);

    /// <summary>On the framework thread (the runner and the game are read there).</summary>
    /// <summary>
    /// On the framework thread (the runner and the game are read there). What it runs can't throw
    /// into nothing: Dalamud hands an exception back as a Task no one looks at, so it's logged here
    /// and you're told.
    /// </summary>
    private static void OnFramework(Action action) => Services.Framework.RunOnFrameworkThread(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Housing Studio action failed");
            Notify("Something went wrong (see /xllog)");
        }
    });

    private static void Notify(string text) => Frame.Toast(text);

    // ---------------------------------------------------------------------------------------------
    // The pieces a run works on: the plan, less the floors switched off and what "Near me" leaves out.

    private IEnumerable<PlanEntry> Filtered()
    {
        var plan = session.Plan;
        if (plan == null) return Enumerable.Empty<PlanEntry>();
        var me = Services.Objects.LocalPlayer?.Position;
        return plan.Entries.Where(e =>
            config.FloorShown(e.Target.Floor)
            && (!config.NearMe || config.NearMeRadius <= 0 || me == null || Vector3.Distance(me.Value, e.Target.Position) <= config.NearMeRadius));
    }

    /// <summary>Do the floor switches or "Near me" leave pieces out?</summary>
    private bool Narrowed => config.NearMe || !config.ShowBasement || !config.ShowGround || !config.ShowUpper;

    /// <param name="clearOut">
    /// The pieces put away first (Ready's Clear out or Swap), or null. Always given by the caller: only
    /// a start the player confirmed for clearing out (Ready's button, its Preview, Done's) passes them.
    /// </param>
    /// <param name="onStarted">Runs once the run has started (the Done view goes then, not when it can't start).</param>
    /// <param name="keepAs">A swap: the name its backup is kept under.</param>
    private void StartRun(bool dryRun, List<PlanEntry>? entries, IReadOnlyList<PlacedPiece>? clearOut, Action? onStarted = null, string? keepAs = null, Action? onNotStarted = null)
    {
        entries ??= Filtered().ToList();
        vm.Starting(entries);
        if (!dryRun) ClearHomeAgain();
        OnFramework(() =>
        {
            runner.Start(dryRun, entries, clearOut, keepAs);
            if (runner.Active)
            {
                onStarted?.Invoke();
                return;
            }
            vm.NotStarted();
            onNotStarted?.Invoke();
            if (runner.StopReason != null) Notify(runner.StopReason);
        });
    }

    private void ShowWhere(IEnumerable<Target> targets)
    {
        var list = targets.Select(t => (t, data.ItemName(t.ItemId))).ToList();
        if (list.Count == 0) return;
        overlay.Highlight(list);
        Notify(list.Count == 1 ? "Its spot is lit in your home" : $"{list.Count} spots are lit in your home");
    }

    // ---------------------------------------------------------------------------------------------
    // Files.

    private static bool IsLayoutFile(string path) =>
        path.EndsWith(".build", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Makes the last item a drop target for layout files dragged in from outside the game.</summary>
    private void DropTarget()
    {
        if (!Services.DragDrop.CreateImGuiTarget(DropLabel, out var files, out _)) return;
        var file = files.FirstOrDefault(IsLayoutFile);
        if (file != null) Open(file);
    }

    private bool DraggingLayout => Services.DragDrop.IsDragging && Services.DragDrop.Files.Any(IsLayoutFile);

    /// <summary>Opens a layout once this frame is drawn.</summary>
    private void Open(string path) => Later(() => OpenNow(path));

    private void OpenNow(string path)
    {
        if (runner.Active)
        {
            Notify("Stop placing first");
            return;
        }
        // Loading another design would stop these with no question.
        if (Plugin.FixtureRun.Active || Plugin.Errands.Active)
        {
            Notify(Plugin.FixtureRun.Active ? "Stop the fixtures first" : "Stop the errands first");
            return;
        }
        if (session.Load(path))
        {
            config.AddRecent(path);
            vm.Dismiss();
            sub = Sub.None;
            // A question or the errands' screen up for the design before isn't for this one (0.9.9).
            confirm = null;
            errandsOpen = false;
            Notify($"Loaded {session.Title}");
            OnFramework(() => session.Refresh(HousingContext.Read(Plugin.Plots)));
        }
        else
        {
            Notify("Couldn't load that file");
        }
    }

    private void OpenDialog()
    {
        var start = config.RecentFiles.Select(Path.GetDirectoryName).FirstOrDefault(Directory.Exists)
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        dialogs.OpenFileDialog("Choose a build or MakePlace layout", "Layouts{.build,.json},.build,.json", (ok, paths) =>
        {
            if (ok && paths.Count > 0) Open(paths[0]);
        }, 1, start, true);
    }

    /// <summary>
    /// Saves what's placed in the home you're in as a .build (or a MakePlace .json): any home you may
    /// decorate, with or without a design loaded, the editor open or not.
    /// </summary>
    private void SaveDialog()
    {
        var ctx = session.Context;
        if (!session.ReadPlacedNow(out var pieces, out _, out var why))
        {
            Notify(why);
            return;
        }
        if (pieces.Count == 0)
        {
            Notify("Nothing placed here to save");
            return;
        }
        var name = $"{(ctx.Side == Side.Garden ? "garden" : "house")}-{DateTime.Now:yyyy-MM-dd}";
        dialogs.SaveFileDialog("Save what's placed as a build", "Build{.build},MakePlace layout{.json}", name, ".build", (ok, path) =>
        {
            if (!ok) return;
            // The dialog leaves the extension off when it's typed without one; the site only opens a .build.
            path = LayoutWriter.WithExtension(path);
            OnFramework(() =>
            {
                if (Backups.SaveTo(path, session, data, Path.GetFileNameWithoutExtension(path), out var failed))
                {
                    log.Done($"Saved what's placed to {path}.");
                    Notify($"Saved {Path.GetFileName(path)}");
                }
                else Notify(failed.Length > 0 ? failed : "Couldn't save it");
            });
        }, Backups.Folder, true);
    }

    // ---------------------------------------------------------------------------------------------
    // The confirmation dialog: a glass-solid card over the dimmed game, its answers saying what happens.

    /// <summary>
    /// A question for you. With <paramref name="Choices"/>, one of them is picked first (the brass answer
    /// stays greyed until one is) and <paramref name="OnChoice"/> gets its index instead of OnYes.
    /// </summary>
    private sealed record Confirmation(string Title, string Body, string Yes, string No, Action OnYes,
        IReadOnlyList<string>? Choices = null, Action<int>? OnChoice = null);

    private Confirmation? confirm;
    private int confirmPick = -1;

    private void Ask(string title, string body, string yes, string no, Action onYes)
    {
        confirm = new Confirmation(title, body, yes, no, onYes);
        confirmPick = -1;
    }

    /// <summary>A question with a list to pick one from (Replace a pin?).</summary>
    private void AskChoice(string title, string body, IReadOnlyList<string> choices, string yes, string no, Action<int> onChoice)
    {
        confirm = new Confirmation(title, body, yes, no, () => { }, choices, onChoice);
        confirmPick = -1;
    }

    private float confirmCardH;

    /// <summary>
    /// The question over the plugin's window (0.9.0): a window of its own the size of the plugin's, kept
    /// in front, which darkens only the plugin's window and takes every click on it; the card in its
    /// middle. 0.8 used ImGui's modal popup, whose dim covered the whole screen in the game style's pale
    /// colour, over the card too.
    /// </summary>
    private void DrawConfirm()
    {
        if (confirm == null) return;
        var c = confirm;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        ImGui.SetNextWindowPos(winPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(winSize, ImGuiCond.Always);
        ImGui.SetNextWindowFocus();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, S(10));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        var shown = ImGui.Begin("##hs-confirm-over", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
        try
        {
            if (!shown) return;
            var dl = ImGui.GetWindowDrawList();
            // The plugin's window darkened (only it), and nothing under it reacts.
            dl.AddRectFilled(winPos, winPos + winSize, Palette.U32(Palette.WithAlpha(Palette.Ink, 0.72f)), S(10));
            var width = MathF.Min(S(440), winSize.X - S(56));
            var pad = S(28);
            var inner = width - pad * 2;
            var cardH = confirmCardH > 0 ? confirmCardH : S(220);
            var cardPos = new Vector2(winPos.X + (winSize.X - width) / 2, winPos.Y + MathF.Max(S(40), (winSize.Y - cardH) / 2));
            var r = S(Palette.Radius2Xl);
            dl.AddRectFilled(cardPos, cardPos + new Vector2(width, cardH), Palette.U32(Palette.Over(Palette.Float, Palette.Ink)), r);
            dl.AddRect(cardPos, cardPos + new Vector2(width, cardH), Palette.U32(Palette.Line), r);

            var at = cardPos + new Vector2(pad, pad);
            ImGui.SetCursorScreenPos(at);
            Kit.PushBox(at.X, inner);
            var titleH = Txt.LineBox(Face.Serif, 26, 26 * 1.1f);
            Txt.Line(dl, at, c.Title, Face.Serif, 26, Palette.Ivory, 26 * 1.1f);
            Kit.Reserve(at, inner, titleH);
            Kit.Gap(12);
            Txt.Para(new[] { new Run(c.Body, Face.Sans, 15, Palette.Text) }, 24);
            if (c.Choices is { Count: > 0 } choices)
            {
                Kit.Gap(14);
                for (var i = 0; i < choices.Count; i++)
                {
                    var row = Kit.At;
                    var h = S(40);
                    var (clicked, hovered, _) = Kit.Hit($"##choice{i}", row, new Vector2(inner, h));
                    if (clicked) confirmPick = i;
                    var picked = confirmPick == i;
                    if (picked || hovered)
                        dl.AddRectFilled(row, row + new Vector2(inner, h), Palette.U32(picked ? Palette.BrassPicked : Palette.RowHover), S(Palette.RadiusMd));
                    if (picked) dl.AddRect(row, row + new Vector2(inner, h), Palette.U32(Palette.BrassEdge), S(Palette.RadiusMd));
                    var text = Txt.Fit(choices[i], Face.Sans, 14, inner - S(28));
                    Txt.Line(dl, row + new Vector2(S(14), (h - Txt.Natural(Face.Sans, 14)) / 2), text, Face.Sans, 14, picked ? Palette.Ivory : Palette.Text);
                    Kit.Reserve(row, inner, h);
                    Kit.Gap(6);
                }
            }
            Kit.Gap(18);
            var buttons = Kit.At;
            var yesW = Kit.ButtonWidth(c.Yes, BtnSize.Md, null, Btn.Primary);
            var noW = Kit.ButtonWidth(c.No);
            var close = false;
            var needPick = c.Choices is { Count: > 0 } && confirmPick < 0;
            if (Kit.Button("##no", new Vector2(buttons.X + inner - yesW - S(8) - noW, buttons.Y), c.No)) close = true;
            if (Kit.Button("##yes", new Vector2(buttons.X + inner - yesW, buttons.Y), c.Yes, Btn.Primary, BtnSize.Md, null, 0, needPick, needPick ? "Pick one first" : null))
            {
                close = true;
                // Run after the frame (it may open a design, or ask again): not inside this window.
                var pick = confirmPick;
                Later(() =>
                {
                    if (c.OnChoice != null) c.OnChoice(pick);
                    else c.OnYes();
                });
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) close = true;
            Kit.Reserve(buttons, inner, Kit.ButtonHeight(BtnSize.Md));
            Kit.PopBox();
            confirmCardH = Kit.At.Y - cardPos.Y + pad;
            if (close && ReferenceEquals(confirm, c)) confirm = null;
        }
        finally
        {
            ImGui.End();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Pieces shared by the views.

    /// <summary>A header's back arrow (Pieces, Settings).</summary>
    private static bool BackButton(Vector2 pos, string tip) => Kit.IconButton("##back", pos, Icon.ArrowLeft, tip);

    /// <summary>The header's Settings button.</summary>
    private void SettingsButton(Vector2 pos)
    {
        if (Kit.IconButton("##settings", pos, Icon.Sliders, "Settings"))
        {
            // Its tabs (not More options, if a run or a design closed Settings while it was open).
            moreOpen = false;
            sub = Sub.Settings;
        }
    }

    /// <summary>
    /// A serif title on one line ("kokomed2", "Housing <em>Studio</em>"), line-height 1.1, cut with an
    /// ellipsis to fit; the italic part in brass-soft at weight 400. Returns its line's height.
    /// </summary>
    private static float Title(Vector2 pos, string plain, string? italic, float maxWidth, int px = 32)
    {
        var dl = Kit.Dl;
        var line = px * 1.1f;
        var italicW = italic != null ? Txt.Measure(" " + italic, Face.SerifItalic, px).X : 0;
        var text = Txt.Fit(plain, Face.Serif, px, MathF.Max(S(40), maxWidth - italicW));
        var w = Txt.Line(dl, pos, text, Face.Serif, px, Palette.Ivory, line);
        // Both cuts share their vertical metrics, so the same line box keeps them on one baseline.
        if (italic != null) Txt.Line(dl, pos + new Vector2(w, 0), " " + italic, Face.SerifItalic, px, Palette.BrassSoft, line);
        return S(line);
    }

    private static string Clock(DateTime t) => t.ToString("HH:mm");
}
