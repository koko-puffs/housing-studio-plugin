using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI;

namespace HousingStudio;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/hstudio";
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RefreshWhileRunning = TimeSpan.FromSeconds(2);

    /// <summary>The bundled plot table (gardens), read once.</summary>
    internal static PlotTable Plots { get; private set; } = null!;

    /// <summary>Where the missing pieces and dyes come from (retainers, vendors, the market board).</summary>
    internal static Shopping Shop { get; private set; } = null!;

    /// <summary>"Set them for me" (Settings → Beta): the design's fixtures, through the game's fixtures windows.</summary>
    internal static FixtureRunner FixtureRun { get; private set; } = null!;

    /// <summary>"Get everything for me" (Settings → Beta): fetching what's missing, and placing it.</summary>
    internal static ErrandRunner Errands { get; private set; } = null!;

    private readonly Configuration config;
    private readonly GameData data;
    private readonly ActivityLog log = new();
    private readonly HousingEditor editor;
    private readonly HousingActions actions;
    private readonly Automation.UiRecorder recorder;
    private readonly FurnitureReader furniture;
    private readonly PlaceAnywhere anywhere = new();
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly Care care;
    private readonly Fonts fonts;
    private readonly WindowSystem windows = new("HousingStudio");
    private readonly MainWindow window;
    private readonly Overlay overlay;
    private readonly GameLink link;
    private readonly VisitorPanel visitorPanel;
    private readonly ShotPanel shotPanel;
    private DateTime nextRefresh;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (config.Version < 3)
        {
            // 0.2.4: the pace presets got much quicker; the old default (1.5 s) becomes the new one (Steady, 0.4 s).
            if (config.IntervalMs == 1500) config.IntervalMs = 400;
            config.Version = 3;
            config.Save();
        }

        Plots = PlotTable.LoadEmbedded();
        data = new GameData();
        data.WarmUp();
        Shop = new Shopping(config, data);
        Shop.WarmUp();
        editor = new HousingEditor();
        actions = new HousingActions();
        recorder = new Automation.UiRecorder();
        furniture = new FurnitureReader(data);
        if (editor.Error != null) log.Warn(editor.Error);
        if (furniture.GardenError != null) log.Warn(furniture.GardenError);
        if (actions.DyeError != null) log.Warn(actions.DyeError);

        session = new LayoutSession(data, furniture, Plots, log);
        runner = new PlacementRunner(config, session, editor, actions, furniture, anywhere, data, log);
        FixtureRun = new FixtureRunner(config, session, data, log);
        Errands = new ErrandRunner(config, session, runner, data, log, Shop);
        // One run acts at a time; the errands start the placing run themselves.
        runner.OtherRunActive = () => FixtureRun.Active || (Errands.Active && !Errands.LetsPlace);
        FixtureRun.OtherRunActive = () => runner.Active || Errands.Active;
        Errands.OtherRunActive = () => FixtureRun.Active;
        care = new Care(config, session, runner, data, log);

        fonts = new Fonts();
        Txt.Fonts = fonts;
        overlay = new Overlay(config, session, runner, data);
        window = new MainWindow(config, session, runner, data, log, fonts, recorder, overlay);
        window.Use(care);
        windows.AddWindow(window);

        // The site link (0.7.0): the inbox, verified homes and visitors, shots from the game. Nothing of
        // it starts while a run goes (the visitor panel's like, save and stamp excepted).
        link = new GameLink(config, log, Plots, () => runner.Active || FixtureRun.Active || Errands.Active);
        window.Use(link);
        visitorPanel = new VisitorPanel(config, link, fonts, window.ShowSettings);
        shotPanel = new ShotPanel(link, fonts);
        windows.AddWindow(visitorPanel);
        windows.AddWindow(shotPanel);

        Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Housing Studio. /hstudio pause, resume, stop, or continue (step-by-step mode) control a run; /hstudio shot takes a shot of your verified home for its page on the site.",
        });

        Services.PluginInterface.UiBuilder.Draw += Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Services.Framework.Update += OnUpdate;
        Services.ClientState.TerritoryChanged += OnTerritoryChanged;
        Services.ClientState.Logout += OnLogout;
        Services.ClientState.Login += OnLogin;

        // Reopen the last design, so it's there when you come back to your house.
        if (config.RecentFiles.Count > 0 && System.IO.File.Exists(config.RecentFiles[0])) session.Load(config.RecentFiles[0]);
    }

    private DateTime drawFailedAt;

    private void Draw()
    {
        try
        {
            overlay.Draw();
            windows.Draw();
        }
        catch (Exception ex)
        {
            // Dalamud stops drawing a plugin whose Draw throws; a run carrying on then has no window
            // to stop it from, so it pauses, and the error is logged (not every frame).
            if (DateTime.UtcNow - drawFailedAt > TimeSpan.FromSeconds(10)) Services.Log.Error(ex, "Housing Studio couldn't draw its window");
            drawFailedAt = DateTime.UtcNow;
            Safely(() => runner.Pause("The window couldn't be drawn (see /xllog); /hstudio resume or stop."));
            // Setting fixtures waits for Continue in the window: without it, it stops.
            Safely(() => FixtureRun.Stop("The window couldn't be drawn (see /xllog)."));
            Safely(() => Errands.Pause("The window couldn't be drawn (see /xllog); /hstudio resume or stop."));
            Safely(runner.CancelPending);
            Safely(care.Cancel);
        }
    }

    /// <summary>Runs one step of shutting down or recovering, whatever the others do.</summary>
    private static void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Housing Studio clean-up step failed");
        }
    }

    private void OpenMain() => window.ShowMain();

    private void OpenSettings() => window.ShowSettings();

    private void OnUpdate(IFramework framework)
    {
        try
        {
            var ctx = HousingContext.Read(Plots);
            runner.Update();
            runner.Tick(ctx);
            Errands.Tick(ctx);
            FixtureRun.Tick(ctx);
            // "Set them for me after placing" follows its run here, whether the window is open or not.
            window.Tick();
            care.Tick(ctx);
            link.Tick(ctx);
            recorder.Tick();
            Shop.Tick();
            if (DateTime.UtcNow >= nextRefresh)
            {
                nextRefresh = DateTime.UtcNow + (runner.Active ? RefreshWhileRunning : RefreshEvery);
                session.Refresh(ctx);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Housing Studio update failed");
            Safely(() => runner.Stop("Something went wrong reading the game; stopped to be safe."));
            Safely(() => FixtureRun.Stop("Something went wrong reading the game; stopped to be safe."));
            Safely(() => Errands.Stop("Something went wrong reading the game; stopped to be safe."));
            Safely(anywhere.Disable);
        }
    }

    private void OnTerritoryChanged(uint territory)
    {
        runner.Stop("You changed zones.");
        FixtureRun.Stop("You changed zones.");
        nextRefresh = DateTime.UtcNow;
    }

    private void OnLogout(int type, int code)
    {
        runner.Stop("You logged out.");
        FixtureRun.Stop("You logged out.");
        Errands.Stop("You logged out.");
        Safely(link.LoggedOut);
    }

    private void OnLogin() => Safely(link.LoggedIn);

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "stop":
                Errands.Stop("Stopped by you (/hstudio stop).");
                runner.Stop("Stopped by you (/hstudio stop).");
                FixtureRun.Stop("Stopped by you (/hstudio stop).");
                runner.CancelPending();
                care.Cancel();
                break;
            case "pause":
                runner.Pause("Paused by you (/hstudio pause).");
                Errands.Pause("Paused by you (/hstudio pause).");
                break;
            case "resume":
                runner.Resume();
                Errands.Resume();
                break;
            case "continue":
            case "next":
                if (Errands.Active) Errands.Continue();
                else runner.Continue();
                FixtureRun.Continue();
                break;
            case "settings":
            case "config":
                OpenSettings();
                break;
            case "shot":
                // On the framework thread already (commands are run there): the camera is read now.
                if (!link.TakeShot(HousingContext.Read(Plots), out var why)) Services.Chat.PrintError($"No shot: {why}", "Housing Studio");
                break;
            default:
                window.Toggle();
                break;
        }
    }

    public void Dispose()
    {
        // The game's code is put back first, whatever else fails on the way out.
        Safely(anywhere.Dispose);
        Safely(Errands.Dispose);
        Safely(() => runner.Stop("The plugin was unloaded."));
        Safely(() => FixtureRun.Stop("The plugin was unloaded."));
        Services.Framework.Update -= OnUpdate;
        Services.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Services.ClientState.Logout -= OnLogout;
        Services.ClientState.Login -= OnLogin;
        Services.PluginInterface.UiBuilder.Draw -= Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        Safely(() => Services.Commands.RemoveHandler(Command));
        Safely(windows.RemoveAllWindows);
        // After the windows: nothing draws its textures once they're let go of.
        Safely(link.Dispose);
        Safely(runner.Dispose);
        Safely(recorder.Dispose);
        Safely(anywhere.Dispose);
        Safely(fonts.Dispose);
        Safely(Shop.Dispose);
    }
}
