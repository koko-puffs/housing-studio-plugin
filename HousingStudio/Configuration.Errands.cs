using System.Collections.Generic;

namespace HousingStudio;

/// <summary>"Get everything for me" (Settings → Beta): the errands' own settings (their limits in Settings → Getting).</summary>
public sealed partial class Configuration
{
    /// <summary>
    /// Experimental: "Get everything for me" (Shopping, Ready) fetches what's missing from your retainers,
    /// vendors and the market board, and places it. Only while <see cref="Experimental"/> is on too.
    /// </summary>
    public bool ErrandsForYou { get; set; }

    /// <summary>
    /// Ready's Get stage, "Get them for me first": the brass button opens the errands (to get what's
    /// missing, then place) instead of placing. Only while <see cref="ErrandsForYou"/> counts.
    /// </summary>
    public bool GetFirst { get; set; }

    /// <summary>The most the errands may spend on the market board in all, in gil (0: nothing bought there).</summary>
    public long ErrandsMarketCeiling { get; set; } = 100_000;

    /// <summary>Buy listings on the other worlds of your data centre too (Lifestream visits them); off: your home world only.</summary>
    public bool ErrandsOtherWorlds { get; set; }

    /// <summary>Which home "go home" means (0 your house, 1 your free company's, 2 your apartment), or -1 to tell from the home you're in.</summary>
    public int ErrandsHome { get; set; } = -1;

    /// <summary>
    /// The errands' clicks not yet seen working (Automation/ErrandTable.cs) wait for Continue each time
    /// until this is on: switch it on once you've watched them work.
    /// </summary>
    public bool ErrandsTrustGuesses { get; set; }

    /// <summary>The city for the market board, summoning bells and world visits: 0 Limsa Lominsa, 1 Ul'dah.</summary>
    public int ErrandsHub { get; set; }

    /// <summary>The errands ride (the mount roulette) where the zone lets you, for walks long enough; Sprint otherwise.</summary>
    public bool ErrandsRide { get; set; } = true;

    /// <summary>
    /// The ways to your front door you walked once while they were recorded (Settings → Getting → Way to
    /// your door): the errands follow one instead of vnavmesh's own way, which goes through the walls.
    /// </summary>
    public List<Core.DoorWay> ErrandsDoorWays { get; set; } = new();

    /// <summary>Bag slots the errands leave free (a stack that doesn't join one already there, a quest item).</summary>
    public int ErrandsSpareSlots { get; set; } = 2;
}
