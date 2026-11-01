using System.Collections.Generic;

namespace HousingStudio;

/// <summary>"Get everything for me": the errands' own settings (their limits in Settings → Getting).</summary>
public sealed partial class Configuration
{
    /// <summary>
    /// Ready's Get stage, "Get them for me first": the brass button opens the errands (to get what's
    /// missing, then place) instead of placing. On by default.
    /// </summary>
    public bool GetFirst { get; set; } = true;

    /// <summary>The most the errands may spend on the market board in all, in gil (0: nothing bought there).</summary>
    public long ErrandsMarketCeiling { get; set; } = 1_000_000;

    /// <summary>Buy listings on the other worlds of your data centre too (Lifestream visits them); off: your home world only.</summary>
    public bool ErrandsOtherWorlds { get; set; } = true;

    /// <summary>Which home "go home" means (0 your house, 1 your free company's, 2 your apartment), or -1 to tell from the home you're in.</summary>
    public int ErrandsHome { get; set; } = -1;

    /// <summary>
    /// The errands' clicks not yet seen working (Automation/ErrandTable.cs) wait for Continue each time
    /// until this is on: switch it on once you've watched them work.
    /// </summary>
    public bool ErrandsTrustGuesses { get; set; }

    /// <summary>The city for the market board, summoning bells and world visits: 0 Limsa Lominsa, 1 Ul'dah.</summary>
    public int ErrandsHub { get; set; } = 1;

    /// <summary>The errands ride (the mount roulette) where the zone lets you, for walks long enough; Sprint otherwise.</summary>
    public bool ErrandsRide { get; set; } = true;

    /// <summary>
    /// The ways to your front door you walked once while they were recorded (Settings → Getting → Way to
    /// your door): the errands follow one instead of vnavmesh's own way, which goes through the walls.
    /// </summary>
    public List<Core.DoorWay> ErrandsDoorWays { get; set; } = new();

    /// <summary>
    /// Vendor prices the game's data has wrong, by item (gil for one): learnt when a shop's question
    /// asked another ("Purchase 1 stage panel for 4,000 gil?" with 1,000 in the data), and used in every
    /// estimate after.
    /// </summary>
    public Dictionary<uint, uint> ErrandsVendorPrices { get; set; } = new();

    /// <summary>
    /// The currencies the errands may spend at currency vendors (their Item rows; 29 is MGP), 0.7.14.
    /// Only what you have of one is planned; gil vendors come first. MGP is allowed by default.
    /// </summary>
    public HashSet<uint> ErrandsCurrencies { get; set; } = new() { Core.CurrencyItems.Mgp };

    /// <summary>Bag slots the errands leave free (a stack that doesn't join one already there, a quest item).</summary>
    public int ErrandsSpareSlots { get; set; } = 2;
}
