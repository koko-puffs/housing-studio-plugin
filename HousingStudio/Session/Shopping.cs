using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// Where each missing piece and dye comes from (<see cref="ShoppingPlanner"/>), from what's known: your
/// retainers (this plugin's record and Allagan Tools), the game's vendors (<see cref="VendorData"/>, read in
/// the background) and the market board (<see cref="MarketPrices"/>, asked in the background). The plan is
/// built again only when something it's built from changes. Framework thread (the window draws there too).
/// </summary>
public sealed class Shopping : IDisposable
{
    private readonly Configuration config;
    private readonly GameData data;

    public Shopping(Configuration config, GameData data)
    {
        this.config = config;
        this.data = data;
        Vendors = new VendorData(data);
        Market = new MarketPrices();
        Record = new RetainerRecord();
        Allagan = new AllaganTools();
    }

    public VendorData Vendors { get; }
    public MarketPrices Market { get; }
    public RetainerRecord Record { get; }
    public AllaganTools Allagan { get; }

    /// <summary>Starts reading the vendors off the framework thread.</summary>
    public void WarmUp() => Vendors.WarmUp();

    /// <summary>Counts an open retainer's inventory; asks Allagan Tools again now and then while there's something to get.</summary>
    public void Tick()
    {
        // Nothing here may stop a run: a failure is logged (once) and the rest goes on.
        try
        {
            Record.Tick();
            if (!last.Shop.IsEmpty) Allagan.Refresh();
        }
        catch (Exception ex)
        {
            if (!tickFailed) Services.Log.Warning(ex, "Couldn't count your retainer's inventory");
            tickFailed = true;
        }
    }

    private bool tickFailed;

    /// <summary>The data centre the market is asked about: the one you're on, or null when not known.</summary>
    public static string? DataCentre()
    {
        if (!Services.PlayerState.IsLoaded) return null;
        // Read again only when the world changes (this is asked every frame the list is shown).
        var world = Services.PlayerState.CurrentWorld.RowId;
        if (world == dataCentreOf.World) return dataCentreOf.Name;
        var dc = Services.PlayerState.CurrentWorld.ValueNullable?.DataCenter.ValueNullable?.Name.ExtractText();
        dataCentreOf = (world, string.IsNullOrEmpty(dc) ? null : dc);
        return dataCentreOf.Name;
    }

    private static (uint World, string? Name) dataCentreOf = (uint.MaxValue, null);

    private (Plan? Plan, object? Dyes, bool Vendors, int Market, int Record, int Allagan, bool Prices, string? Scope, string? District, long Minute) key;
    private (ShoppingPlan Shop, IReadOnlyList<Need> Needs, IReadOnlyList<uint> Priced, string? Scope) last = (ShoppingPlan.Empty, Array.Empty<Need>(), Array.Empty<uint>(), null);

    /// <summary>The needs the plan was last built for.</summary>
    public IReadOnlyList<Need> Needs => last.Needs;

    /// <summary>
    /// Where everything still needed for <paramref name="plan"/> comes from. <paramref name="dyesToBuy"/>
    /// is <see cref="LayoutSession.MissingDyes"/> (the same list each frame while nothing changed).
    /// </summary>
    public ShoppingPlan Plan(Plan? plan, IReadOnlyList<(byte Stain, string Name, int Count)> dyesToBuy, string? district)
    {
        if (plan == null) return ShoppingPlan.Empty;
        // It's built while the window draws: a failure here mustn't stop the window (or a run), so the
        // last plan stands and it's logged once.
        try
        {
            return Build(plan, dyesToBuy, district);
        }
        catch (Exception ex)
        {
            if (!planFailed) Services.Log.Warning(ex, "Couldn't work out the shopping list");
            planFailed = true;
            return last.Shop;
        }
    }

    private bool planFailed;

    private ShoppingPlan Build(Plan plan, IReadOnlyList<(byte Stain, string Name, int Count)> dyesToBuy, string? district)
    {
        var scope = config.LookUpMarketPrices ? DataCentre() : null;
        var k = (plan, (object?)dyesToBuy, Vendors.Ready, Market.Version, Record.Version, Allagan.Version, config.LookUpMarketPrices, scope, district,
            DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
        if (k == key) return last.Shop;
        var firstForPlan = !ReferenceEquals(key.Plan, plan);
        key = k;
        if (firstForPlan) Allagan.Refresh(soon: true);

        var needs = ShoppingPlanner.NeedsOf(plan, dyesToBuy.Select(d => (d.Stain, d.Count)), s => data.DyeItems(s).FirstOrDefault());
        var names = RetainerRecord.Names();
        // With the game's retainer list known, only its retainers count from Allagan Tools (not a company chest).
        var allagan = names.Count > 0 ? Allagan.Stocks.Where(s => names.ContainsKey(s.RetainerId)) : Allagan.Stocks;
        var retainers = RetainerStocks.Merge(Record.Stocks(), allagan, names);

        var priced = new List<uint>();
        var quotes = new Dictionary<uint, MarketQuote>();
        if (scope != null)
        {
            priced = ShoppingPlanner.ToPrice(needs, retainers, Vendors.Offers).Where(Vendors.Marketable).ToList();
            // Before the vendors are read, every item looks unsold: wait for them rather than ask about all.
            if (Vendors.Ready) Market.Request(scope, priced);
            quotes = Market.Quotes(scope, priced);
        }
        var shop = ShoppingPlanner.Build(needs, retainers, Vendors.Offers, quotes, Vendors.Craft, district);
        last = (shop, needs, priced, scope);
        return shop;
    }

    /// <summary>A line about how the prices stand, or null when there's nothing to say.</summary>
    public string? Status()
    {
        if (!Vendors.Ready) return "Reading the game's vendors…";
        if (!config.LookUpMarketPrices) return last.Shop.Count(SourceKind.Unknown) + last.Shop.Count(SourceKind.Craft) > 0
            ? "Market prices are off (Settings → More options)."
            : null;
        if (last.Priced.Count == 0) return null;
        if (last.Scope == null) return "Market prices come once you're logged in.";
        if (Market.Waiting(last.Scope, last.Priced)) return $"Asking Universalis for prices on {last.Scope}…";
        if (Market.LastError is { } e) return $"{e}: no market prices for now.";
        return null;
    }

    public void Dispose() => Market.Dispose();
}
