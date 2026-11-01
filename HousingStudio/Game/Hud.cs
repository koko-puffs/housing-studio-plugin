using System;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HousingStudio.Game;

/// <summary>
/// The game's HUD shown or hidden, as Scroll Lock does (the UI module's own visibility switch), for a
/// shot without it (0.9.10). Framework thread.
/// </summary>
public static unsafe class Hud
{
    /// <summary>Is the game's HUD hidden now (Scroll Lock, a cutscene, or this plugin).</summary>
    public static bool Hidden => Services.GameGui.GameUiHidden;

    /// <summary>Shows or hides the HUD. False when the game's UI module can't be reached (nothing changed).</summary>
    public static bool Set(bool visible)
    {
        try
        {
            var ui = UIModule.Instance();
            if (ui == null) return false;
            var atk = ui->GetRaptureAtkModule();
            if (atk == null) return false;
            atk->SetUiVisibility(visible);
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, $"Housing Studio couldn't {(visible ? "show" : "hide")} the HUD");
            return false;
        }
    }
}
