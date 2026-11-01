using System;
using System.Linq;
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

    /// <summary>"Set them for me": the design's fixtures, through the game's fixtures windows.</summary>
    internal static FixtureRunner FixtureRun { get; private set; } = null!;

    /// <summary>"Get everything for me": fetching what's missing, and placing it.</summary>
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

        InventoryReader.LearntSizes = config.StoreroomSizes;
        InventoryReader.SaveSizes = config.Save;
        try
        {
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
            // The errands get the fixtures not in your bags too; Set them for me puts them up after placing.
            Shop.FixturesToGet = () => FixtureRun.Plan().Where(t => t.State == FixtureState.NotOwned).Select(t => t.Fixture.ItemId).ToList();
            BoardListings.Hook();
            // One run acts at a time; the errands start the placing run themselves.
            runner.OtherRunActive = () => FixtureRun.Active || (Errands.Active && !Errands.LetsPlace);
            FixtureRun.OtherRunActive = () => runner.Active || Errands.Active;
            Errands.OtherRunActive = () => FixtureRun.Active;
            recorder.RunGoing = () => runner.Active || FixtureRun.Active || Errands.Active;
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
            // (Safely: a file that fails to load mustn't leave the plugin half set up, its hooks in the game.)
            // On the framework thread (1.0.0): the constructor can run on a worker, with the frame's update
            // (which reads the session) already going.
            if (config.RecentFiles.Count > 0 && System.IO.File.Exists(config.RecentFiles[0]))
            {
                var last = config.RecentFiles[0];
                Services.Framework.RunOnFrameworkThread(() => Safely(() => session.Load(last)));
            }
        }
        catch
        {
            // Dalamud doesn't dispose a plugin whose constructor threw: what was hooked into the game is
            // let go of here, or the game would call into an unloaded plugin.
            TearDownAfterFailedStart();
            throw;
        }
    }

    /// <summary>What a constructor that threw had set up, let go of (each part only if it was made).</summary>
    private void TearDownAfterFailedStart()
    {
        Services.Framework.Update -= OnUpdate;
        Services.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Services.ClientState.Logout -= OnLogout;
        Services.ClientState.Login -= OnLogin;
        Services.PluginInterface.UiBuilder.Draw -= Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        Safely(() => Services.Commands.RemoveHandler(Command));
        Safely(anywhere.Dispose);
        Safely(() => Errands?.Dispose());
        Safely(BoardListings.Unhook);
        Safely(windows.RemoveAllWindows);
        Safely(() => link?.Dispose());
        Safely(() => runner?.Dispose());
        Safely(() => recorder?.Dispose());
        Safely(() => fonts?.Dispose());
        Safely(() => Shop?.Dispose());
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
            // Logged at most every 10 s (1.0.0: the time was set on every failure, so one failing every
            // frame was logged once and never again).
            if (DateTime.UtcNow - drawFailedAt > TimeSpan.FromSeconds(10))
            {
                Services.Log.Error(ex, "Housing Studio couldn't draw its window");
                drawFailedAt = DateTime.UtcNow;
            }
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
        HousingContext ctx;
        try
        {
            ctx = HousingContext.Read(Plots);
        }
        catch (Exception ex)
        {
            // Where you are couldn't be read: the runs stop, to be safe, and the rest still ticks (as elsewhere).
            Throttled(ex, "Housing Studio: reading where you are failed");
            ctx = HousingContext.None;
            StopEverything("Something went wrong reading where you are; stopped to be safe.");
        }
        try
        {
            runner.Update();
            runner.Tick(ctx);
            Errands.Tick(ctx);
            FixtureRun.Tick(ctx);
        }
        catch (Exception ex)
        {
            // The runs themselves (or reading where you are): stopped, to be safe.
            Throttled(ex, "Housing Studio update failed");
            StopEverything("Something went wrong reading the game; stopped to be safe.");
        }
        // The rest each on its own: one failing (every frame, say) no longer stops every run, and its
        // error is logged at most every 10 s.
        Ticked(window.Tick, "the window's");
        Ticked(() => care.Tick(ctx), "the home check's");
        Ticked(() => link.Tick(ctx), "the site link's");
        Ticked(recorder.Tick, "the recording's");
        Ticked(Shop.Tick, "the shopping list's");
        Ticked(() =>
        {
            if (DateTime.UtcNow < nextRefresh) return;
            nextRefresh = DateTime.UtcNow + (runner.Active ? RefreshWhileRunning : RefreshEvery);
            session.Refresh(ctx);
        }, "reading the home's");
    }

    private DateTime updateFailedAt = DateTime.MinValue;

    private void StopEverything(string reason)
    {
        Safely(() => runner.Stop(reason));
        Safely(() => FixtureRun.Stop(reason));
        Safely(() => Errands.Stop(reason));
        Safely(anywhere.Disable);
    }

    private void Ticked(Action tick, string what)
    {
        try
        {
            tick();
        }
        catch (Exception ex)
        {
            Throttled(ex, $"Housing Studio: {what} update failed");
        }
    }

    private void Throttled(Exception ex, string message)
    {
        if (DateTime.UtcNow - updateFailedAt < TimeSpan.FromSeconds(10)) return;
        updateFailedAt = DateTime.UtcNow;
        Services.Log.Error(ex, message);
    }

    private void OnTerritoryChanged(uint territory)
    {
        // Each on its own: one throwing mustn't leave another going (or the placement check off).
        Safely(() => runner.Stop("You changed zones."));
        Safely(() => FixtureRun.Stop("You changed zones."));
        Safely(anywhere.Disable);
        nextRefresh = DateTime.UtcNow;
    }

    private void OnLogout(int type, int code)
    {
        Safely(() => runner.Stop("You logged out."));
        Safely(() => FixtureRun.Stop("You logged out."));
        Safely(() => Errands.Stop("You logged out."));
        Safely(anywhere.Disable);
        Safely(link.LoggedOut);
    }

    private void OnLogin() => Safely(link.LoggedIn);

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "stop":
                // Each on its own (1.0.0): one that throws doesn't leave the others going, nor place anywhere on.
                Safely(() => Errands.Stop("Stopped by you (/hstudio stop)."));
                Safely(() => runner.Stop("Stopped by you (/hstudio stop)."));
                Safely(() => FixtureRun.Stop("Stopped by you (/hstudio stop)."));
                Safely(runner.CancelPending);
                Safely(care.Cancel);
                Safely(anywhere.Disable);
                break;
            case "pause":
                Safely(() => runner.Pause("Paused by you (/hstudio pause)."));
                Safely(() => Errands.Pause("Paused by you (/hstudio pause)."));
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
        Safely(BoardListings.Unhook);
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
