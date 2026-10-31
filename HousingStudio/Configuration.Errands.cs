namespace HousingStudio;

/// <summary>"Get everything for me" (Settings → Experimental): the errands' own settings.</summary>
public sealed partial class Configuration
{
    /// <summary>
    /// Experimental: "Get everything for me" (Shopping, Ready) fetches what's missing from your retainers,
    /// vendors and the market board, and places it. Only while <see cref="Experimental"/> is on too.
    /// </summary>
    public bool ErrandsForYou { get; set; }

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

    /// <summary>Bag slots the errands leave free (a stack that doesn't join one already there, a quest item).</summary>
    public int ErrandsSpareSlots { get; set; } = 2;
}
