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

    private readonly Configuration config;
    private readonly GameData data;
    private readonly ActivityLog log = new();
    private readonly HousingEditor editor;
    private readonly FurnitureReader furniture;
    private readonly PlaceAnywhere anywhere = new();
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly Fonts fonts;
    private readonly WindowSystem windows = new("HousingStudio");
    private readonly MainWindow window;
    private readonly Overlay overlay;
    private DateTime nextRefresh;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Plots = PlotTable.LoadEmbedded();
        data = new GameData();
        editor = new HousingEditor();
        furniture = new FurnitureReader(data);
        if (editor.Error != null) log.Warn(editor.Error);
        if (furniture.GardenError != null) log.Warn(furniture.GardenError);

        session = new LayoutSession(data, furniture, Plots, log);
        runner = new PlacementRunner(config, session, editor, furniture, anywhere, data, log);

        fonts = new Fonts();
        Widgets.Fonts = fonts;
        var theme = new Theme(fonts);
        window = new MainWindow(config, session, runner, data, log, theme);
        windows.AddWindow(window);
        overlay = new Overlay(config, session, runner, data);

        Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Housing Studio. /hstudio pause, /hstudio resume or /hstudio stop control a run.",
        });

        Services.PluginInterface.UiBuilder.Draw += Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Services.Framework.Update += OnUpdate;
        Services.ClientState.TerritoryChanged += OnTerritoryChanged;
        Services.ClientState.Logout += OnLogout;

        // Reopen the last design, so it's there when you come back to your house.
        if (config.RecentFiles.Count > 0 && System.IO.File.Exists(config.RecentFiles[0])) session.Load(config.RecentFiles[0]);
    }

    private void Draw()
    {
        overlay.Draw();
        windows.Draw();
    }

    private void OpenMain() => window.ShowTab(0);

    private void OpenSettings() => window.ShowTab(3);

    private void OnUpdate(IFramework framework)
    {
        try
        {
            var ctx = HousingContext.Read(Plots);
            runner.Tick(ctx);
            if (DateTime.UtcNow >= nextRefresh)
            {
                nextRefresh = DateTime.UtcNow + (runner.Active ? RefreshWhileRunning : RefreshEvery);
                session.Refresh(ctx);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Housing Studio update failed");
            runner.Stop("Something went wrong reading the game; stopped to be safe.");
        }
    }

    private void OnTerritoryChanged(uint territory)
    {
        runner.Stop("You changed zones.");
        nextRefresh = DateTime.UtcNow;
    }

    private void OnLogout(int type, int code) => runner.Stop("You logged out.");

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "stop":
                runner.Stop("Stopped by you (/hstudio stop).");
                break;
            case "pause":
                runner.Pause("Paused by you (/hstudio pause).");
                break;
            case "resume":
                runner.Resume();
                break;
            case "settings":
            case "config":
                OpenSettings();
                break;
            default:
                window.Toggle();
                break;
        }
    }

    public void Dispose()
    {
        runner.Stop("The plugin was unloaded.");
        Services.Framework.Update -= OnUpdate;
        Services.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Services.ClientState.Logout -= OnLogout;
        Services.PluginInterface.UiBuilder.Draw -= Draw;
        Services.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        Services.Commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
        runner.Dispose();
        anywhere.Dispose();
        fonts.Dispose();
    }
}
