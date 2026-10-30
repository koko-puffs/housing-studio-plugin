using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using HousingStudio.Core;

namespace HousingStudio;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>Layouts opened lately, newest first.</summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>The pause between two pieces, in milliseconds (never below <see cref="Pacing.MinimumMs"/>).</summary>
    public int IntervalMs { get; set; } = Pacing.DefaultMs;

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

    /// <summary>Diagnostics: hold before every click until Continue is pressed.</summary>
    public bool StepByStep { get; set; }

    /// <summary>The words of the "place" entry in an item's menu, comma-separated; empty uses the built-in ones.</summary>
    public string PlaceMenuWords { get; set; } = string.Empty;

    /// <summary>
    /// Temporarily switch off the game's "floating / sunk / overlapping" placement check while
    /// placing (the MakePlace plugin's "place anywhere"). Off by default; always switched back.
    /// </summary>
    public bool PlaceAnywhere { get; set; }

    // Overlay
    public bool ShowMarkers { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public bool ShowPlacedMarkers { get; set; }
    public float MarkerRange { get; set; } = 40f;

    // Filters
    public bool ShowBasement { get; set; } = true;
    public bool ShowGround { get; set; } = true;
    public bool ShowUpper { get; set; } = true;

    /// <summary>Only pieces within this many yalms of you (0: everywhere), a stand-in for "this room".</summary>
    public float NearMeRadius { get; set; }

    public bool ChatMessages { get; set; } = true;

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8) RecentFiles = RecentFiles.Take(8).ToList();
        Save();
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
