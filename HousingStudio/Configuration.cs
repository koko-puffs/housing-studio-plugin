using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using HousingStudio.Core;

namespace HousingStudio;

[Serializable]
public sealed partial class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 8;

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
    public bool ShowPlacedMarkers { get; set; } = true;
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

    /// <summary>The shopping list asks Universalis (a request over the internet) what the market board asks for what no vendor sells.</summary>
    public bool LookUpMarketPrices { get; set; } = true;

    /// <summary>
    /// Settings → Getting, Housing Studio account: the token made on the site that lets Send to my builds put
    /// a house in your builds there (Core/SiteLink.cs); empty when not linked. Never logged.
    /// </summary>
    public string SiteToken { get; set; } = string.Empty;

    /// <summary>
    /// Settings → Getting, "Check for things sent from the site": while linked and logged in, the
    /// inbox of builds and designs sent with the site's Send to game is checked about once a minute
    /// (Session/GameLink.cs), and what's new is said in chat and a notification.
    /// </summary>
    public bool CheckInbox { get; set; } = true;

    /// <summary>The newest thing sent from the site already told of (UTC): what's sent after it is new.</summary>
    public DateTime InboxSeenAt { get; set; }

    /// <summary>
    /// Settings → Getting, "Show homes on Housing Studio when I visit": in any home's interior, the
    /// site is asked whether it's a published design verified there, and a small panel shows it.
    /// </summary>
    public bool ShowHomesWhenVisiting { get; set; } = true;

    /// <summary>
    /// Until 0.7.13, Settings → Beta's "Experimental features" (the errands, setting fixtures, putting
    /// back what changed only worked while it was on). Read once by <see cref="Migrate"/>: those features
    /// are always there now.
    /// </summary>
    public bool Experimental { get; set; }

    /// <summary>
    /// Ready's Finish stage, "Set them for me after placing": when a placing run started from Ready
    /// finishes, setting the fixtures starts by itself.
    /// </summary>
    public bool FixturesAfterPlacing { get; set; } = true;

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
    /// <remarks>Version 8 (0.7.14): the paces moved a step faster (Daring → Quick, the rest → Steady), so the speed chosen stays.</remarks>
    /// <remarks>Version 7 (0.7.13): no Beta switch; Get them for me first and Set them after placing on, unless the Beta features were in use.</remarks>
    public bool Migrate()
    {
        if (Version >= 8) return false;
        if (Version == 7)
        {
            // 0.7.14: the paces moved a step faster; the one chosen keeps its speed.
            Pace = Pacing.FromBefore0714(Pace);
            Version = 8;
            return true;
        }
        if (Version < 7 && !Experimental)
        {
            // 0.7.13: the Beta features are always there; someone who never switched them on gets
            // their own switches as a new install has them (they were never in use).
            GetFirst = true;
            FixturesAfterPlacing = true;
        }
        if (Version >= 6)
        {
            Pace = Pacing.FromBefore0714(Pace);
            Version = 8;
            return true;
        }
        if (Version < 4)
        {
            NearMe = NearMeRadius > 0;
            if (NearMeRadius <= 0) NearMeRadius = 8f;
        }
        if (Version < 5) Pace = Pacing.FromOldInterval(IntervalMs);
        PlaceAnywhere = true;
        Pace = Pacing.FromBefore0714(Pace);
        Version = 8;
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
