using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using HousingStudio.Core;

namespace HousingStudio;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;

    /// <summary>Layouts opened lately, newest first.</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>How fast a run goes (Settings → Pace): the pause between pieces, between clicks, and the waits for the game.</summary>
    public Pace Pace { get; set; } = Pacing.Default;

    /// <summary>Before 0.3.3, the pace was only the pause between two pieces, in milliseconds; read once to choose <see cref="Pace"/>.</summary>
    public int IntervalMs { get; set; } = 150;

    /// <summary>How much the pause varies, 0 to 0.5.</summary>
    public float Jitter { get; set; } = 0.25f;

    /// <summary>Stop after this many pieces in a row couldn't be moved.</summary>
    public int MaxFailuresInARow { get; set; } = 3;

    /// <summary>Save what's placed now to a .build in the plugin's folder before placing starts.</summary>
    public bool BackupBeforePlacing { get; set; } = true;

    /// <summary>Place pieces from the storeroom through the furnishing list's menus (else you put them down).</summary>
    public bool StoreroomAutomatic { get; set; } = true;

    /// <summary>Place pieces from your bags through the item's menu (else you put them down).</summary>
    public bool BagsAutomatic { get; set; } = true;

    /// <summary>After placing, dye every piece whose dye differs from the design.</summary>
    public bool DyeAfterPlacing { get; set; } = true;

    /// <summary>
    /// Something-not-working option: move the game's preview of a storeroom piece to its spot before
    /// choosing Place (Refurnish's technique). Off: in 0.2.2 it stopped pieces being placed at all; the
    /// piece is placed where the game puts it and then moved to its spot.
    /// </summary>
    public bool MovePreviewBeforePlace { get; set; }

    /// <summary>Diagnostics: hold before every click until Continue is pressed.</summary>
    public bool StepByStep { get; set; }

    /// <summary>The words of the "place" entry in an item's menu, comma-separated; empty uses the built-in ones.</summary>
    public string PlaceMenuWords { get; set; } = string.Empty;

    /// <summary>
    /// Temporarily switch off the game's "floating / sunk / overlapping" placement check while
    /// placing (the MakePlace plugin's "place anywhere"). On by default since 0.3.4 (designs from
    /// Housing Studio often float or overlap on purpose); always switched back when a run ends.
    /// </summary>
    public bool PlaceAnywhere { get; set; } = true;

    // Overlay
    public bool ShowMarkers { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public bool ShowPlacedMarkers { get; set; }
    public float MarkerRange { get; set; } = 40f;

    // Filters
    public bool ShowBasement { get; set; } = true;
    public bool ShowGround { get; set; } = true;
    public bool ShowUpper { get; set; } = true;

    /// <summary>"Near me" (Pieces): only pieces within <see cref="NearMeRadius"/> yalms of you, a stand-in for "this room". Placing follows it.</summary>
    public bool NearMe { get; set; }

    /// <summary>How far "Near me" reaches, in yalms (Settings → More options).</summary>
    public float NearMeRadius { get; set; } = 8f;

    /// <summary>When each recent file was last opened (for the Recent list's "today 05.24").</summary>
    public Dictionary<string, DateTime> RecentOpened { get; set; } = new();

    /// <summary>The fixtures ticked as set, per design (see UI/Model/FixtureTicks.cs).</summary>
    public Dictionary<string, List<string>> FixturesDone { get; set; } = new();

    public bool ChatMessages { get; set; } = true;

    /// <summary>
    /// Settings → Link to Housing Studio: the token made on the site that lets Send to my builds put
    /// a house in your builds there (Core/SiteLink.cs); empty when not linked. Never logged.
    /// </summary>
    public string SiteToken { get; set; } = string.Empty;

    /// <summary>
    /// Settings → Experimental: features not yet tried in the game (or that act further on your
    /// behalf). Off by default; each has its own switch under it, which only counts while this is on.
    /// </summary>
    public bool Experimental { get; set; }

    /// <summary>
    /// The furnishing list's storeroom rows learnt in each house (by house id), kept while the
    /// storeroom holds the same pieces, so a later run doesn't pick its way down the list again.
    /// </summary>
    public Dictionary<string, SavedRows> StoreroomRows { get; set; } = new();

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8) RecentFiles = RecentFiles.Take(8).ToList();
        foreach (var gone in RecentOpened.Keys.Where(k => !RecentFiles.Contains(k)).ToList()) RecentOpened.Remove(gone);
        RecentOpened[path] = DateTime.Now;
        Save();
    }

    /// <summary>
    /// Version 4 (0.3.0, after 0.2.4's pace change made version 3): "Near me" became an on/off
    /// button with its reach in Settings; before, a reach of 0 meant off. Returns true when something changed.
    /// </summary>
    /// <remarks>
    /// Version 5 (0.3.3): the pace became a choice of Steady, Quick (the new default) and Daring, each
    /// setting more than the pause between pieces; Careful becomes Steady, the rest Quick.
    /// </remarks>
    /// <remarks>Version 6 (0.3.4): place anywhere is on by default, so it's switched on.</remarks>
    public bool Migrate()
    {
        if (Version >= 6) return false;
        if (Version < 4)
        {
            NearMe = NearMeRadius > 0;
            if (NearMeRadius <= 0) NearMeRadius = 8f;
        }
        if (Version < 5) Pace = Pacing.FromOldInterval(IntervalMs);
        PlaceAnywhere = true;
        Version = 6;
        return true;
    }

    public bool FloorShown(Floor f) => f switch
    {
        Floor.Basement => ShowBasement,
        Floor.Ground => ShowGround,
        Floor.Upper => ShowUpper,
        _ => true,
    };

    public void Save() => Services.PluginInterface.SavePluginConfig(this);
}
