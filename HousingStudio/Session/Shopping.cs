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
        AutoRetainer = new AutoRetainer();
    }

    public VendorData Vendors { get; }
    public MarketPrices Market { get; }
    public RetainerRecord Record { get; }
    public AllaganTools Allagan { get; }

    /// <summary>AutoRetainer: your retainers' names (it doesn't keep what they hold).</summary>
    public AutoRetainer AutoRetainer { get; }

    /// <summary>Starts reading the vendors off the framework thread.</summary>
    public void WarmUp() => Vendors.WarmUp();

    /// <summary>Counts an open retainer's inventory; asks Allagan Tools again now and then while there's something to get.</summary>
    public void Tick()
    {
        // Nothing here may stop a run: a failure is logged (once) and the rest goes on.
        try
        {
            Record.Tick();
            if (!last.Shop.IsEmpty)
            {
                AutoRetainer.Refresh();
                Allagan.Refresh();
            }
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

    private (Plan? Plan, object? Dyes, bool Vendors, int Market, int Record, int Allagan, bool Prices, string? Scope, string? District, long Minute, int Learnt, string Fixtures, string Currencies) key;

    /// <summary>
    /// The currencies you allow and what you have of each ("29=45000"): the plan is worked out again when
    /// either changes. Balances read at most every 2 s.
    /// </summary>
    private (DateTime Until, Dictionary<uint, long> Have, string Key) currenciesNow = (DateTime.MinValue, new(), string.Empty);

    private (Dictionary<uint, long> Have, string Key) Currencies()
    {
        if (DateTime.UtcNow < currenciesNow.Until && currenciesNow.Have.Keys.ToHashSet().SetEquals(config.ErrandsCurrencies)) return (currenciesNow.Have, currenciesNow.Key);
        var have = new Dictionary<uint, long>();
        foreach (var c in config.ErrandsCurrencies.OrderBy(c => c))
        {
            try
            {
                have[c] = ErrandGame.CurrencyCount(c);
            }
            catch (Exception ex)
            {
                Services.Log.Debug(ex, $"Couldn't read how much of currency {c} you have");
                have[c] = 0;
            }
        }
        currenciesNow = (DateTime.UtcNow + TimeSpan.FromSeconds(2), have, string.Join(",", have.Select(kv => $"{kv.Key}={kv.Value}")));
        return (have, currenciesNow.Key);
    }

    /// <summary>What you have of a currency, as last read (0 when it isn't allowed).</summary>
    public long Balance(uint currency) => currenciesNow.Have.TryGetValue(currency, out var n) ? n : 0;

    /// <summary>
    /// The design's fixtures for the side you're on that aren't in your bags (an item each), for the
    /// errands to get too; set by the plugin (Set them for me's own reading). Read at most every 2 s.
    /// </summary>
    public Func<IReadOnlyList<uint>>? FixturesToGet { get; set; }

    private (DateTime Until, IReadOnlyList<uint> Items, string Key) fixturesNow = (DateTime.MinValue, Array.Empty<uint>(), string.Empty);

    private (IReadOnlyList<uint> Items, string Key) Fixtures()
    {
        if (DateTime.UtcNow < fixturesNow.Until) return (fixturesNow.Items, fixturesNow.Key);
        IReadOnlyList<uint> items;
        try
        {
            items = FixturesToGet?.Invoke() ?? Array.Empty<uint>();
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "Couldn't read the fixtures to get");
            items = Array.Empty<uint>();
        }
        var sorted = items.OrderBy(i => i).ToList();
        fixturesNow = (DateTime.UtcNow + TimeSpan.FromSeconds(2), sorted, string.Join(",", sorted));
        return (fixturesNow.Items, fixturesNow.Key);
    }

    /// <summary>Goes up when a vendor price is learnt (the plan is worked out again).</summary>
    private int learnt;

    /// <summary>
    /// Every NPC selling an item for gil, at the price a shop was seen to ask when the game's data has
    /// another (<see cref="Configuration.ErrandsVendorPrices"/>).
    /// </summary>
    public IReadOnlyList<VendorOffer> Offers(uint itemId)
    {
        var offers = Vendors.Offers(itemId);
        return offers.Count > 0 && config.ErrandsVendorPrices.TryGetValue(itemId, out var price) && price > 0 && offers[0].Price != price
            ? offers.Select(o => o with { Price = price }).ToList()
            : offers;
    }

    /// <summary>
    /// A shop asked <paramref name="unitPrice"/> for one of an item: kept, when it's not what the plan
    /// had, for every estimate after. True when it was new.
    /// </summary>
    public bool LearnPrice(uint itemId, uint unitPrice)
    {
        if (unitPrice == 0 || Offers(itemId) is not { Count: > 0 } offers || offers[0].Price == unitPrice) return false;
        config.ErrandsVendorPrices[itemId] = unitPrice;
        config.Save();
        learnt++;
        return true;
    }
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
        var (fixtures, fixturesKey) = Fixtures();
        var (have, currenciesKey) = Currencies();
        var k = (plan, (object?)dyesToBuy, Vendors.Ready, Market.Version, Record.Version, Allagan.Version, config.LookUpMarketPrices, scope, district,
            DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute, learnt, fixturesKey, currenciesKey);
        if (k == key) return last.Shop;
        var firstForPlan = !ReferenceEquals(key.Plan, plan);
        key = k;
        if (firstForPlan)
        {
            AutoRetainer.Refresh();
            Allagan.Refresh(soon: true);
        }

        // A dye's item to buy (0.9.4): one a vendor sells, else one the board trades, else the first (a dye
        // can have several items, and the first may be neither).
        var needs = ShoppingPlanner.NeedsOf(plan, dyesToBuy.Select(d => (d.Stain, d.Count)), s => data.DyeItems(s)
            .OrderByDescending(i => Offers(i).Count > 0).ThenByDescending(Vendors.Marketable).FirstOrDefault());
        // The fixtures too (0.7.13): got with the rest, then set after placing (Set them for me).
        needs.AddRange(fixtures.Where(i => i != 0).GroupBy(i => i).Select(g => new Need(g.Key, g.Count(), NeedKind.Fixture)));
        // Retainers' names: the game's list (once it's been sent this session), else AutoRetainer's.
        var names = RetainerRecord.Names();
        foreach (var (id, name) in AutoRetainer.Names) names.TryAdd(id, name);
        // With the retainers known, only they count from Allagan Tools (not a company chest).
        var allagan = names.Count > 0 ? Allagan.Stocks.Where(s => names.ContainsKey(s.RetainerId)) : Allagan.Stocks;
        var retainers = RetainerStocks.Merge(Record.Stocks(), allagan, names);

        var priced = new List<uint>();
        var quotes = new Dictionary<uint, MarketQuote>();
        if (scope != null)
        {
            priced = ShoppingPlanner.ToPrice(needs, retainers, Offers, evenSold: true).Where(Vendors.Marketable).ToList();
            // Before the vendors are read, every item looks unsold: wait for them rather than ask about all.
            if (Vendors.Ready) Market.Request(scope, priced);
            quotes = Market.Quotes(scope, priced);
        }
        // Currency vendors (0.7.14): only the currencies you allow, as far as what you have goes.
        var allowed = new HashSet<uint>(config.ErrandsCurrencies);
        var shop = ShoppingPlanner.Build(needs, retainers, Offers, quotes, Vendors.Craft, district,
            Vendors.Exchanges, c => have.TryGetValue(c, out var n) ? n : 0, allowed.Contains);
        last = (shop, needs, priced, scope);
        return shop;
    }

    /// <summary>A line about how the prices stand, or null when there's nothing to say.</summary>
    public string? Status()
    {
        if (!Vendors.Ready) return "Reading the game's vendors…";
        if (!config.LookUpMarketPrices) return last.Shop.Count(SourceKind.Unknown) + last.Shop.Count(SourceKind.Craft) > 0
            ? "Market prices are off (Settings → Getting)."
            : null;
        if (last.Priced.Count == 0) return null;
        if (last.Scope == null) return "Market prices come once you're logged in.";
        if (Market.Waiting(last.Scope, last.Priced)) return $"Asking Universalis for prices on {last.Scope}…";
        if (Market.LastError is { } e) return $"{e}: no market prices for now.";
        return null;
    }

    public void Dispose() => Market.Dispose();
}
