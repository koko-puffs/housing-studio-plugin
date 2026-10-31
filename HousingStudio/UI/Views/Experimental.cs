namespace HousingStudio.UI;

/// <summary>
/// Settings → Experimental: one switch that lets the features not yet tried in the game (or that act
/// further on your behalf) be used, and each such feature's own switch under it. A feature checks
/// <see cref="Configuration.Experimental"/> and its own switch both.
/// </summary>
public sealed partial class MainWindow
{
    private string ExperimentalSummary() => config.Experimental ? "On: the features below can be used" : "Features not yet tried in the game";

    private void DrawExperimental()
    {
        var i = 1;
        if (SwitchRow(i++, "##experimental", "Experimental features",
                "Features not yet tried in the game, or that act further on your behalf. Keep the backup on, and send the log if anything goes wrong.",
                config.Experimental))
        {
            config.Experimental = !config.Experimental;
            config.Save();
        }
        if (!config.Experimental) return;
        // Each experimental feature adds its switch here (index i++).
        if (SwitchRow(i++, "##set-fixtures", "Set the fixtures for me",
                "Ready's fixtures get \"Set them for me\": the game's fixtures window is opened and each fixture put in from your bags, step by step. You press its apply.",
                config.SetFixturesForYou))
        {
            config.SetFixturesForYou = !config.SetFixturesForYou;
            config.Save();
        }
        _ = i;
    }
}
