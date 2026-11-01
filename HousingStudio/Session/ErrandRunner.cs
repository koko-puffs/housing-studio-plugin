using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// "Get everything for me" (experimental): fetches what the design is missing and places it, trip by
/// trip. A trip goes to its stops in order (a summoning bell for each retainer, each vendor, a market
/// board on each world), gets what fits in your bags, goes home (Lifestream), goes in, and runs the
/// usual placing (<see cref="PlacementRunner"/>) on what's now in the bags; then what's still missing
/// is worked out again (the house matched afresh) and the next trip planned, until nothing is left
/// that the errands can get.
///
/// The same pattern as the placing run: a list of small steps (<see cref="StepQueue"/>) on the
/// framework thread, every wait with a timeout, every action logged, Pause, Stop and step by step;
/// clicks not yet seen working (<see cref="ErrandTable"/>) always wait for Continue until you trust
/// them. It stops, and says why, on anything it didn't expect: a window that doesn't open, a price
/// above the plan, more gil than you agreed to, combat, a duty, a cutscene, a zone change no travel
/// asked for, full bags, too little gil, a path vnavmesh can't find, a plugin that stops answering.
/// </summary>
public sealed partial class ErrandRunner : IDisposable
{
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan YouWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan WalkWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan TravelWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan WorldWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PlaceWait = TimeSpan.FromHours(2);

    /// <summary>How close is close enough to talk to someone or use something.</summary>
    private const float Reach = 3f;

    /// <summary>How near an NPC the errands walk before talking to them.</summary>
    private const float NpcReach = 2f;

    /// <summary>A gil shop sells at most this many in one purchase.</summary>
    private const int MaxPerPurchase = 99;

    private const string Guess = "guess";

    /// <summary>The pace chosen (Settings → Pace): the errands' clicks and some of their waits follow it.</summary>
    private PaceProfile Speed => Pacing.Profile(config.Pace);

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly Shopping shop;

    /// <summary>Items a world's board had none of (or none to buy) this run: not planned there again.</summary>
    private readonly HashSet<(uint Item, string World)> emptyBoards = new();

    /// <summary>
    /// Why a thing couldn't be bought on a board, the first time it couldn't ("Twintania: sold to someone
    /// else first"): the summary's Not got says it rather than "not listed on the worlds the errands buy on".
    /// </summary>
    private readonly Dictionary<uint, string> boardWhy = new();

    public IReadOnlyDictionary<uint, string> BoardWhy => boardWhy;
    private readonly StepQueue queue = new(() => DateTime.UtcNow);

    /// <summary>
    /// A part of a trip: its words, its steps, and where it is (for the window's journey and title);
    /// <see cref="Stop"/> is the trip's stop it belongs to (its index in the trip, -1 for home and placing).
    /// </summary>
    private readonly record struct Segment(string Title, Func<List<Step>> Steps, Func<List<Step>> Cleanup, ErrandLeg Leg, bool Going, string? Who, ErrandItem? Item,
        int Stop = -1);

    private readonly List<Segment> segments = new();
    private int segment = -1;

    /// <summary>The trip's stop whose segments are being added (-1: none).</summary>
    private int addingStop = -1;
    private ErrandTrip? pendingTrip;
    private bool finishPending;
    private ErrandTrip? trip;

    /// <summary>Ready's choice, for the first placing run only: the pieces to clear out first, and a swap's name for what's here.</summary>
    private IReadOnlyList<PlacedPiece>? clearOutFirst;
    private string? keepAsFirst;

    private HomeRef? home;
    private HomeKind homeKind;
    private string homeWorld = string.Empty;
    private bool houseBell;
    private long agreedGil;
    private long ceiling;
    private int tripsAtStart;
    private int cycles;
    private int gotThisTrip;

    /// <summary>Boards found without what was wanted this trip: a trip that got nothing but those isn't "something's wrong" (the next one goes elsewhere).</summary>
    private int emptiedThisTrip;
    private bool travelling;
    private uint lastTerritory;
    private DateTime? pausedAt;
    private Vector3? walkTarget;

    /// <summary>The recorded way being followed to your door (so a pause's walk carries on along it), or null.</summary>
    private IReadOnlyList<Vector3>? followWay;
    private DateTime readAfter;
    private readonly Dictionary<uint, int> got = new();

    public ErrandRunner(Configuration config, LayoutSession session, PlacementRunner runner, GameData data, ActivityLog log, Shopping shop)
    {
        this.config = config;
        this.session = session;
        this.runner = runner;
        this.data = data;
        this.log = log;
        this.shop = shop;
        Rehearsals.Register(rehearsing);
        queue.StepsPerTick = 4;
        queue.Trace += (step, what) =>
        {
            // Steps that wait for you say so themselves, once they have to wait (You).
            if (what != "start" && what != "done") log.Warn($"  ✕ {what}");
        };
        queue.Timed += (step, took) =>
        {
            if (step.Label.StartsWith("You:", StringComparison.Ordinal))
            {
                if (took > TimeSpan.FromMilliseconds(250)) log.Info($"  … done, after {took.TotalSeconds:0.#} s.");
            }
            else log.Info($"  {(step.IsAction ? "→" : "…")} {step.Label} ({took.TotalMilliseconds:0} ms)");
        };
    }

    public Vnavmesh Nav { get; } = new();
    public Lifestream Travel { get; } = new();

    /// <summary>Another run (setting fixtures): errands don't start while it goes.</summary>
    public Func<bool>? OtherRunActive { get; set; }

    // ---------------------------------------------------------------------------------------------
    // What the window shows.

    /// <summary>Always (0.7.13: no longer a Beta feature to switch on); Ready's "Get them for me first" decides whether its button gets first.</summary>
    public bool Enabled => true;

    public RunState State { get; private set; } = RunState.Idle;
    public bool Active => State is RunState.Running or RunState.Paused;
    public string? StopReason { get; private set; }
    public string? StatusText { get; private set; }

    /// <summary>The errands have started the placing run (it may run while they're active; nothing else may).</summary>
    public bool LetsPlace { get; private set; }

    /// <summary>The placing run going now is the errands'.</summary>
    public bool Placing => LetsPlace && runner.Active;

    /// <summary>The plan the run is on (the last one worked out).</summary>
    public ErrandPlan Plan { get; private set; } = ErrandPlan.Empty;

    /// <summary>The trip being made (1-based), and how many the plan at the start had.</summary>
    public int Trip { get; private set; }

    public int TripsAtStart => tripsAtStart;

    /// <summary>How many trips there are, as planned when this one began ("trip 2 of 3").</summary>
    public int TripsPlanned { get; private set; }

    /// <summary>How many things the plan agreed to at the start had.</summary>
    public int CountAtStart { get; private set; }

    /// <summary>The trip being made (its stops), or null between trips.</summary>
    public ErrandTrip? CurrentTrip => Active ? trip : null;

    /// <summary>Where the run is on its trip (the journey's stop), whether it's on the way there, and the vendor's name there.</summary>
    public ErrandLeg Leg => Active && segment >= 0 && segment < segments.Count ? segments[segment].Leg : ErrandLeg.Bell;

    public bool Going => Active && segment >= 0 && segment < segments.Count && segments[segment].Going;

    public string? Who => Active && segment >= 0 && segment < segments.Count ? segments[segment].Who : null;

    /// <summary>The thing being taken or bought now, or null.</summary>
    public ErrandItem? CurrentItem => Active && segment >= 0 && segment < segments.Count ? segments[segment].Item : null;

    /// <summary>The trip's stop it's at or on its way to (its index in <see cref="CurrentTrip"/>'s stops), or -1 (home, placing, between trips).</summary>
    public int StopIndex => Active && segment >= 0 && segment < segments.Count ? segments[segment].Stop : -1;

    /// <summary>The run's home has a summoning bell (its retainers are visited there).</summary>
    public bool HouseBell => houseBell;

    /// <summary>The segment in hand (it changes as the run moves on; the window caches by it).</summary>
    public int SegmentIndex => Active ? segment : -1;

    /// <summary>After placing: working out what's still missing.</summary>
    public bool Replanning => Active && segment >= 0 && segment == segments.Count - 1;

    /// <summary>The first placing run clears out (Ready's Clear out or Swap): how many, and the swap's name.</summary>
    public int ClearOutFirst => clearOutFirst?.Count ?? 0;

    public string? KeepAsFirst => keepAsFirst;

    /// <summary>What the run is doing ("Going to Material Supplier, Mist").</summary>
    public string? Doing => Active && segment >= 0 && segment < segments.Count ? segments[segment].Title : null;

    public string? StepLabel => Active && queue.State is QueueState.Running or QueueState.AwaitingContinue ? queue.Current?.Label : null;

    public bool AwaitingContinue => Active && queue.State == QueueState.AwaitingContinue;

    public bool WaitingForYou => StepLabel is { } l && l.StartsWith("You:", StringComparison.Ordinal);

    /// <summary>The click being held for is one not yet seen working (from <see cref="ErrandTable"/>).</summary>
    public bool HoldingAGuess => AwaitingContinue && queue.Current?.Key == Guess;

    public long VendorSpent { get; private set; }
    public long MarketSpent { get; private set; }
    public long Spent => VendorSpent + MarketSpent;
    public long AgreedGil => agreedGil;
    public long Ceiling => ceiling;

    /// <summary>What's been got, by item.</summary>
    public IReadOnlyDictionary<uint, int> Got => got;

    public int GotCount => got.Values.Sum();

    /// <summary>The run ended and its summary hasn't been closed.</summary>
    public bool ResultWaiting { get; private set; }

    public DateTime StartedAt { get; private set; }

    public void Dismiss() => ResultWaiting = false;

    // ---------------------------------------------------------------------------------------------
    // Planning.

    /// <summary>Which home "go home" goes to: the one chosen in the errands' screen, else the one you're in.</summary>
    /// <summary>
    /// Which home "go home" is: the one you're in when the game says it's yours (0.7.13 took Settings'
    /// choice over it: from company chambers with "My house" chosen it went to your house's ward with the
    /// chambers' district's merchants planned); Settings' choice only for a home the game doesn't say is.
    /// </summary>
    public HomeKind KindOf(HomeRef at) => !at.Owned && config.ErrandsHome is >= 0 and <= 2 ? (HomeKind)config.ErrandsHome : at.Kind;

    /// <summary>
    /// The errands for a shopping plan, as things stand here: your free bag slots (less the spare ones),
    /// stack sizes, which vendors can be got to from this home, the market ceiling, and which worlds.
    /// </summary>
    public ErrandPlan Estimate(ShoppingPlan shopping, HomeRef? at)
    {
        if (at == null || shopping.IsEmpty) return ErrandPlan.Empty;
        var world = ErrandGame.CurrentWorld();
        var kind = KindOf(at);
        var options = new ErrandOptions
        {
            FreeSlots = Math.Max(0, ErrandGame.FreeSlots() - Math.Max(0, config.ErrandsSpareSlots)),
            MarketCeiling = Math.Max(0, config.ErrandsMarketCeiling),
            StackSize = ErrandGame.StackSize,
            Held = ErrandGame.NqCount,
            Reachable = v => Reachable(v, at, kind),
            Offers = shop.Offers,
            PreferDistrict = at.District,
            WorldAllowed = config.ErrandsOtherWorlds ? w => w == world || Travel.CanVisit(w) != false : w => w == world,
            ListingAllowed = (item, w) => !emptyBoards.Contains((item, w)),
            HomeWorld = ErrandGame.HomeWorld(),
            BellAtHome = Active ? houseBell : BellNearby(),
        };
        return ErrandPlanner.Build(shopping, options);
    }

    /// <summary>
    /// A vendor the errands can get to: in your home's own district (its teleport lands you in your
    /// ward; not from an apartment, whose building stands apart), in another district (0.8.3: through its
    /// city's Residential District Aethernet, when you're attuned to that city's aetheryte), or in a zone
    /// with an aetheryte you're attuned to.
    /// </summary>
    private static bool Reachable(VendorOffer v, HomeRef at, HomeKind kind)
    {
        if (!v.Located) return false;
        // A shop that offers the item only after a quest you haven't done (0.7.13): another vendor, or the board.
        if (v.UnlockQuests.Count > 0 && !ErrandGame.QuestsDone(v.UnlockQuests)) return false;
        if (v.District != null)
        {
            if (v.District == at.District && kind != HomeKind.Apartment) return true;
            var city = Residential.CityAetheryte(v.District);
            return city != 0 && ErrandGame.Attuned(city);
        }
        var aetheryte = ErrandGame.AetheryteOf(v.TerritoryId);
        return aetheryte != 0 && ErrandGame.Attuned(aetheryte);
    }

    /// <summary>Why the errands can't start now with this plan, or null.</summary>
    /// <param name="rehearse">For a rehearsal: too little gil doesn't stop it (it's noted as where a real run would stop).</param>
    public string? CanStart(ErrandPlan plan, HousingContext ctx, bool rehearse = false)
    {
        if (Active) return "Already on errands.";
        if (runner.Active || OtherRunActive?.Invoke() == true) return "Wait until the run is done.";
        if (ErrandGame.Blocked(false) is { } blocked) return blocked;
        if (ctx.Busy) return "Wait until you're free (not in combat, a cutscene or between areas).";
        var mine = ErrandGame.HomeHere(ctx);
        if (mine == null || session.Plan == null) return "Go inside your home: what's missing is worked out there.";
        if (!ctx.HasPermission) return "You can only place furniture in a home you may decorate.";
        if (!mine.Owned && config.ErrandsHome < 0) return mine.Chamber
            ? "These chambers aren't in your company's house as the game has it: choose which home \"go home\" is under Limits → Change → Home is."
            : "The game doesn't say this home is yours: choose which home it is under Limits → Change → Home is.";
        var world = ErrandGame.CurrentWorld();
        if (world.Length == 0 || world != ErrandGame.HomeWorld()) return "Go back to your home world first.";
        if (!Nav.Installed) return "vnavmesh isn't installed (or is turned off).";
        if (!Travel.Installed) return "Lifestream isn't installed (or is turned off).";
        // What's fetched is placed by the usual run: it has to be able to start here.
        if (runner.CanStart(false) is { } placing) return placing;
        if (plan.IsEmpty) return "Nothing the errands can get.";
        var gil = ErrandGame.Gil();
        if (gil < plan.TotalGil && !rehearse) return $"You have {gil:N0} gil; the errands need about {plan.TotalGil:N0}.";
        if (plan.Needs(StopKind.Market) || (plan.Needs(StopKind.Retainer) && !BellNearby()))
        {
            var city = ErrandGame.Hub(config.ErrandsHub);
            var hub = ErrandGame.AetheryteOf(city.AetheryteTerritory);
            if (hub == 0 || !ErrandGame.Attuned(hub)) return $"The market board and the summoning bells are reached through {city.Name}'s aetheryte, which you haven't attuned to: attune to it, or pick the other in Settings → Getting → Market city.";
        }
        return null;
    }

    private static readonly List<string> BellNames = new();
    private static readonly List<string> BoardNames = new();

    private static IReadOnlyCollection<string> Bells()
    {
        if (BellNames.Count == 0) BellNames.AddRange(ErrandTable.SummoningBellWords.Append(ErrandGame.EObjName(ErrandTable.SummoningBellRow)).Where(n => n.Length > 0).Distinct());
        return BellNames;
    }

    private static IReadOnlyCollection<string> Boards()
    {
        if (BoardNames.Count == 0) BoardNames.AddRange(ErrandTable.MarketBoardWords.Append(ErrandGame.EObjName(ErrandTable.MarketBoardRow)).Where(n => n.Length > 0).Distinct());
        return BoardNames;
    }

    private static readonly ObjectKind[] UsableKinds = { ObjectKind.EventObj, ObjectKind.HousingEventObject };

    /// <summary>A summoning bell placed in the home you're in (looked for within 60 yalms).</summary>
    public static bool BellNearby() => ErrandGame.Me is { } me && ErrandGame.Nearest(Bells(), me, 60, UsableKinds) != null;

    // ---------------------------------------------------------------------------------------------
    // Starting, pausing, stopping.

    /// <summary>
    /// Starts the errands on a plan you've seen, having agreed to its gil. Ready's choice for what's
    /// here goes to the first placing run only: <paramref name="clearOut"/> (Clear out or Swap) is put
    /// away first, as far as those pieces are still there when it comes home, and with
    /// <paramref name="keepAsName"/> (Swap) what's here is kept under that name, as Ready's own start does.
    /// </summary>
    /// <param name="rehearse">A rehearsal: every trip walked as planned, each purchase asked for and cancelled, nothing taken; then placing rehearsed.</param>
    public void Start(ErrandPlan plan, HousingContext ctx, IReadOnlyList<PlacedPiece>? clearOut = null, string? keepAsName = null, bool rehearse = false)
    {
        if (CanStart(plan, ctx, rehearse) is { } why)
        {
            StopReason = why;
            log.Warn($"Errands didn't start: {why}");
            return;
        }
        Rehearsal = rehearse;
        rehearsalFailures = 0;
        emptyBoards.Clear();
        boardWhy.Clear();
        // Nothing of a run before (a stop mid-way could leave its last plan's ending or segments).
        finishPending = false;
        pendingTrip = null;
        segments.Clear();
        segment = -1;
        pausedAt = null;
        currencyAgreed.Clear();
        currencySpent.Clear();
        foreach (var (_, currencyItem, currencyCost) in plan.CurrencyCosts) currencyAgreed[currencyItem] = currencyAgreed.GetValueOrDefault(currencyItem) + currencyCost;
        home = ErrandGame.HomeHere(ctx)!;
        homeKind = KindOf(home);
        homeWorld = ErrandGame.HomeWorld();
        houseBell = BellNearby();
        Plan = plan;
        agreedGil = plan.TotalGil;
        ceiling = Math.Max(0, config.ErrandsMarketCeiling);
        tripsAtStart = plan.Trips.Count;
        CountAtStart = plan.Count;
        clearOutFirst = clearOut is { Count: > 0 } ? clearOut.ToList() : null;
        keepAsFirst = keepAsName;
        trip = null;
        cycles = 0;
        Trip = 0;
        TripsPlanned = 0;
        VendorSpent = MarketSpent = 0;
        got.Clear();
        travelling = false;
        LetsPlace = false;
        walkTarget = null;
        followWay = null;
        lastTerritory = ErrandGame.Territory;
        StopReason = null;
        ResultWaiting = false;
        StartedAt = DateTime.Now;
        // The errands' clicks by the pace (0.7.14: Daring 0.15 s apart, Quick 0.3 s, Steady 0.4 s).
        queue.ActionGap = TimeSpan.FromMilliseconds(Math.Max(Speed.ErrandGapMs, Speed.ClickGapMs));
        queue.Begin(Array.Empty<Step>());
        if (plan.Needs(StopKind.Retainer))
            log.Info(shop.AutoRetainer.KeepOut() ? "AutoRetainer is asked to keep out of the way while the errands use a bell." : "AutoRetainer didn't answer (not installed?); carrying on.");
        log.Info($"Errands started: {plan.Count} things in {Words(plan.Trips.Count, "trip")}, about {plan.TotalGil:N0} gil agreed "
                 + $"({plan.VendorGil:N0} at vendors, {plan.MarketGil:N0} on the market board, under a ceiling of {ceiling:N0}). Home: {homeKind}, "
                 + $"{(houseBell ? "a summoning bell in it" : "no summoning bell in it")}.{(config.ErrandsTrustGuesses ? string.Empty : " Clicks not yet seen working wait for Continue.")}"
                 + (keepAsFirst != null ? $" The first placing swaps: what's here is kept as “{keepAsFirst}”, and {ClearOutFirst} pieces not in the design are put away first."
                     : clearOutFirst != null ? $" The first placing clears out {ClearOutFirst} pieces not in the design first." : string.Empty));
        if (!HasWayFor(home))
            log.Info($"No way to your front door is known for plot {home.Plot} yet: the first time home, you're asked to walk to your door once, and that walk is remembered.");
        if (plan.CurrencyCosts.Count > 0)
            log.Info($"Currency vendors: {string.Join(", ", plan.CurrencyCosts.Select(c => $"{c.Cost:N0} {c.Currency} (you have {ErrandGame.CurrencyCount(c.CurrencyItem):N0})"))} agreed, no more.");
        if (rehearse) RehearsalStarted(plan);
        State = RunState.Running;
        StatusText = "Starting…";
        pendingTrip = plan.Trips[0];
    }

    public void Pause(string why)
    {
        if (State != RunState.Running) return;
        if (Placing)
        {
            runner.Pause(why);
            return;
        }
        State = RunState.Paused;
        pausedAt = DateTime.UtcNow;
        StatusText = why;
        Nav.Stop();
        log.Warn($"Errands paused: {why}");
        if (config.ChatMessages) Services.Chat.Print($"Errands paused: {why}", "Housing Studio");
    }

    public void Resume()
    {
        if (Placing)
        {
            runner.Resume();
            return;
        }
        if (State != RunState.Paused) return;
        State = RunState.Running;
        if (pausedAt is { } at) queue.Hold(DateTime.UtcNow - at);
        pausedAt = null;
        // A walk the pause stopped carries on (a recorded way from where you are along it).
        if (followWay is { } w && ErrandGame.Me is { } me) Nav.Follow(DoorWays.From(w, me).Left);
        else if (walkTarget is { } to) Nav.MoveTo(to);
        log.Info("Errands resumed.");
    }

    public void Continue()
    {
        if (Placing) runner.Continue();
        else queue.Continue();
    }

    public void Stop(string reason)
    {
        if (!Active) return;
        if (queue.State is QueueState.Running or QueueState.AwaitingContinue) queue.Abort(reason);
        if (LetsPlace && runner.Active) runner.Stop(reason);
        LetsPlace = false;
        Nav.Stop();
        walkTarget = null;
        followWay = null;
        if (travelling || Travel.Busy) Travel.Abort();
        travelling = false;
        CloseWindows();
        shop.AutoRetainer.LetBe();
        clearOutFirst = null;
        keepAsFirst = null;
        State = RunState.Stopped;
        StopReason = reason;
        StatusText = reason;
        ResultWaiting = true;
        if (Rehearsal && !reason.StartsWith("Stopped by you", StringComparison.Ordinal)) Rehearsals.Report.Issue("The errands", $"Stopped: {reason}");
        log.Warn($"Errands {(Rehearsal ? "rehearsal " : string.Empty)}stopped: {reason} ({Summary()})");
        if (config.ChatMessages) Services.Chat.PrintError($"Errands stopped: {reason}", "Housing Studio");
    }

    private void Finish(string why)
    {
        State = RunState.Finished;
        LetsPlace = false;
        clearOutFirst = null;
        keepAsFirst = null;
        Nav.Stop();
        walkTarget = null;
        followWay = null;
        shop.AutoRetainer.LetBe();
        StopReason = null;
        StatusText = why;
        ResultWaiting = true;
        log.Done($"Errands {(Rehearsal ? "rehearsal " : string.Empty)}finished: {why} ({Summary()})");
        if (config.ChatMessages) Services.Chat.Print($"Errands {(Rehearsal ? "rehearsal " : string.Empty)}finished: {why}", "Housing Studio");
    }

    private string Summary() => Rehearsal
        ? $"would get {GotCount}, for {Spent:N0} gil (nothing bought), {Words(Trip, "trip")}"
        : $"{GotCount} got, {Spent:N0} gil spent ({VendorSpent:N0} at vendors, {MarketSpent:N0} on the market board), {Words(Trip, "trip")}";

    private static string Words(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    /// <summary>
    /// While a click not yet seen working waits for Continue: true once you've done it by hand (the item
    /// came to your bags), so the errands carry on by themselves.
    /// </summary>
    private Func<bool>? guessByHand;

    private bool menuReopened;

    private DateTime? vendorArrived;
    private DateTime? boardArrived;

    /// <summary>True while the gil hasn't gone yet, for up to 1.5 s from the first time it's asked.</summary>
    private static bool GilLate(ref DateTime? since)
    {
        var now = DateTime.UtcNow;
        since ??= now;
        return now - since.Value < TimeSpan.FromSeconds(1.5);
    }

    /// <summary>Closes every window the errands open, as their own close buttons do.</summary>
    private static void CloseWindows()
    {
        // A purchase's question first: said no to (closed), never left up under what comes next.
        foreach (var name in new[] { UiTable.YesNo, UiTable.ContextMenu, ErrandTable.Shop, ErrandTable.ExchangeDialog, ErrandTable.ExchangeCurrency, ErrandTable.ExchangeItem,
                     ErrandTable.ItemSearchResult, ErrandTable.ItemSearch, ErrandTable.SelectIconString })
            GameUi.Close(name);
        foreach (var name in ErrandTable.RetainerInventory) GameUi.Close(name);
        // The retainer's menu and list last (closing the menu says goodbye to the retainer).
        GameUi.Close(ErrandTable.SelectString);
        GameUi.Close(ErrandTable.RetainerList);
    }

    // ---------------------------------------------------------------------------------------------
    // Every frame.

    /// <summary>Where you are, as read this frame.</summary>
    private HousingContext here = HousingContext.None;

    public void Tick(HousingContext ctx)
    {
        here = ctx;
        try
        {
            TickWay();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Recording the way to your door failed");
            RecordingWay = false;
        }
        if (!Active) return;
        try
        {
            TickSafely(ctx);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Errands failed");
            Stop($"Something went wrong ({ex.Message}); stopped to be safe.");
        }
    }

    private void TickSafely(HousingContext ctx)
    {
        // The placing run looks after itself (its own stops); the errands only wait for it.
        if (!Placing)
        {
            if (ErrandGame.Blocked(travelling) is { } why)
            {
                Stop(why);
                return;
            }
            var t = ErrandGame.Territory;
            if (t != lastTerritory)
            {
                lastTerritory = t;
                if (!travelling)
                {
                    Stop("You changed zones when no travel was asked for.");
                    return;
                }
            }
        }
        else
        {
            // (Combat, a cutscene and zone changes stop the placing run itself; a duty pop doesn't.)
            if (ErrandGame.Blocked(true) is { } why)
            {
                Stop(why);
                return;
            }
            lastTerritory = ErrandGame.Territory;
        }
        if (State == RunState.Paused) return;

        if (pendingTrip != null && queue.State is not (QueueState.Running or QueueState.AwaitingContinue))
        {
            BeginTrip(pendingTrip);
            pendingTrip = null;
        }
        if (finishPending && queue.State is not (QueueState.Running or QueueState.AwaitingContinue))
        {
            finishPending = false;
            Finish(Rehearsal ? "every trip was walked as planned, and placing rehearsed; nothing was bought or taken."
                : Plan.Left.Count == 0 ? "everything's got and placed." : $"all the errands can get is got; {Plan.Left.Sum(l => l.Count)} left for you (see the list).");
            return;
        }

        // Clicks not yet seen working wait for Continue (step by step) until you trust them.
        queue.StepByStep = config.StepByStep || (!config.ErrandsTrustGuesses && queue.Current is { IsAction: true, Key: Guess });
        // Such a click done by hand meanwhile (the item came to your bags): carried on, as Continue would.
        if (queue.State == QueueState.AwaitingContinue && queue.Current?.Key == Guess && guessByHand?.Invoke() == true)
        {
            guessByHand = null;
            log.Info("  You did that by hand: carrying on.");
            queue.Continue();
        }
        switch (queue.State)
        {
            case QueueState.Running:
                queue.Tick();
                StatusText = queue.State == QueueState.AwaitingContinue ? $"Next: {queue.Current?.Label}" : StepLabel ?? StatusText;
                if (queue.State == QueueState.Finished) NextSegment();
                else if (queue.State is QueueState.Stopped or QueueState.Skipped)
                {
                    // A rehearsal notes what didn't work and goes on where it sensibly can (the next stop).
                    if (Rehearsal && RehearsalCarriesOn(queue.Message ?? "A step didn't work.")) NextSegment();
                    else Stop(queue.Message ?? "A step didn't work.");
                }
                return;
            case QueueState.AwaitingContinue:
                return;
            default:
                NextSegment();
                return;
        }
    }

    private void BeginTrip(ErrandTrip next)
    {
        trip = next;
        Trip++;
        // This trip and those the plan has after it (a plan made again after a trip can need more or fewer;
        // a rehearsal doesn't plan again).
        TripsPlanned = Rehearsal ? Math.Max(Trip, tripsAtStart) : Math.Max(Trip, Trip - 1 + Plan.Trips.Count);
        cycles++;
        gotThisTrip = 0;
        emptiedThisTrip = 0;
        segments.Clear();
        segment = -1;
        log.Info($"Trip {Trip}: {next.Count} things ({next.Slots} bag slots), about {next.Gil:N0} gil, "
                 + string.Join(", ", next.Stops.Select(s => s.Kind switch
                 {
                     StopKind.Retainer => $"{s.Retainer?.Name}",
                     StopKind.Vendor => s.Vendor?.Where() ?? "a vendor",
                     StopKind.Exchange => $"{s.Vendor?.Where() ?? "a vendor"} (for {string.Join(" + ", s.Items.Where(i => i.Exchange != null).GroupBy(i => i.Exchange!.CurrencyName).Select(g => $"{g.Sum(i => i.CurrencyCost):N0} {g.Key}"))})",
                     _ => $"the market board on {s.World}",
                 })) + ".");
        // Each stop's segments know which stop they are (the window's journey follows the trip's own order).
        for (var stopAt = 0; stopAt < next.Stops.Count; stopAt++)
        {
            var stop = next.Stops[stopAt];
            addingStop = stopAt;
            switch (stop.Kind)
            {
                case StopKind.Retainer:
                    AddRetainer(stop);
                    break;
                case StopKind.Vendor:
                    AddVendor(stop);
                    break;
                case StopKind.Exchange:
                    AddExchange(stop);
                    break;
                default:
                    AddMarket(stop);
                    break;
            }
        }
        addingStop = -1;
        Add("Going home", GoHomeSteps, NoCleanup, ErrandLeg.Home, true);
        Add("Going in", EnterSteps, NoCleanup, ErrandLeg.Home);
        if (Rehearsal)
        {
            // No replanning (nothing was got): the trips as planned, placing rehearsed once, after the last.
            if (Trip >= Plan.Trips.Count) Add("Rehearsing placing what's in your bags", PlaceSteps, NoCleanup, ErrandLeg.Place);
            Add(Trip >= Plan.Trips.Count ? "Done with the trips" : "On to the next trip, as planned", NextTripSteps, NoCleanup, ErrandLeg.Place);
        }
        else
        {
            Add("Placing what's in your bags", PlaceSteps, NoCleanup, ErrandLeg.Place);
            Add("Seeing what's still missing", ReplanSteps, NoCleanup, ErrandLeg.Place);
        }
        NextSegment();
    }

    private void Add(string title, Func<List<Step>> steps, Func<List<Step>> cleanup, ErrandLeg leg, bool going = false, string? who = null, ErrandItem? item = null) =>
        segments.Add(new Segment(title, steps, cleanup, leg, going, who, item, addingStop));

    private static List<Step> NoCleanup() => new();

    private List<Step> WindowCleanup() => new()
    {
        new Step
        {
            Label = "Close the windows the errands opened",
            Run = () =>
            {
                Nav.Stop();
                walkTarget = null;
                CloseWindows();
                return StepOutcome.Done;
            },
        },
    };

    private void NextSegment()
    {
        if (!Active) return;
        if (pendingTrip != null || finishPending) return;
        segment++;
        if (segment >= segments.Count)
        {
            // A trip's last segment plans the next (or the end); getting here means it didn't.
            Finish("the trips are done.");
            return;
        }
        var s = segments[segment];
        log.Info($"{s.Title}.");
        StatusText = s.Title;
        queue.Begin(s.Steps(), s.Cleanup());
    }

    // ---------------------------------------------------------------------------------------------
    // Getting about: teleports, worlds, walking.

    /// <summary>
    /// To a market city (Settings → Getting → Market city): nothing when you're in Limsa or Ul'dah with
    /// one of <paramref name="near"/> (boards, bells) in sight, or in either for a world visit (null);
    /// else a teleport to the one chosen. (0.7.6: a world visit from Limsa landed in Ul'dah, and the
    /// teleport back to Limsa was refused.)
    /// </summary>
    private List<Step> HubSteps(IReadOnlyCollection<string>? near) => new()
    {
        Step.Act("To a market city", () =>
        {
            var city = ErrandGame.Hub(config.ErrandsHub);
            if (near == null)
            {
                // A world visit leaves from a city's aetheryte (Limsa's, Ul'dah's).
                if (!ErrandGame.AtAWorldVisitAetheryte) queue.InsertNext(TeleportSteps(city.AetheryteTerritory, city.Name));
                return StepOutcome.Done;
            }
            if (ErrandGame.AtABoardCity && ErrandGame.Me is { } me && ErrandGame.Nearest(near, me, 150, UsableKinds) != null) return StepOutcome.Done;
            var steps = new List<Step>();
            if (ErrandGame.Territory != city.BoardTerritory)
            {
                if (ErrandGame.Territory != city.AetheryteTerritory) steps.AddRange(TeleportSteps(city.AetheryteTerritory, city.Name));
                if (city.Aethernet != null) steps.AddRange(AethernetSteps(city.Aethernet, city.BoardTerritory, city.BoardPlace));
            }
            queue.InsertNext(steps);
            return StepOutcome.Done;
        }),
    };

    /// <summary>
    /// The city's aethernet, from its aetheryte to a shard (Lifestream): Ul'dah's Sapphire Avenue
    /// Exchange, where its boards are. Asked again every 2 s for 20 s, as a teleport.
    /// </summary>
    private List<Step> AethernetSteps(string shard, uint territory, string where)
    {
        var firstTry = DateTime.MinValue;
        var lastTry = DateTime.MinValue;
        var asks = 0;
        return new List<Step>
        {
            new Step
            {
                Label = $"Aethernet to {shard} (Lifestream)",
                IsAction = true,
                Run = () =>
                {
                    if (ErrandGame.Territory == territory) return StepOutcome.Done;
                    var now = DateTime.UtcNow;
                    if (firstTry == DateTime.MinValue) firstTry = now;
                    if (ErrandGame.Settled && !Travel.Busy && now - lastTry > TimeSpan.FromSeconds(2))
                    {
                        lastTry = now;
                        travelling = true;
                        if (Travel.Aethernet(shard)) return StepOutcome.Done;
                        travelling = false;
                    }
                    return now - firstTry > TimeSpan.FromSeconds(20)
                        ? StepOutcome.Halt($"Lifestream wouldn't take the aethernet to {shard} (asked for 20 s).")
                        : StepOutcome.Waiting;
                },
                // Its own 20 s decide (the step's 3 s default gave up after the second ask).
                Timeout = TimeSpan.FromSeconds(25),
            },
            new Step
            {
                Label = $"Arrive at {where}",
                Run = () =>
                {
                    if (ErrandGame.Territory != territory && Again(ref lastTry, ref asks, () => Travel.Aethernet(shard)) is { } stop) return stop;
                    if (ErrandGame.Territory != territory || !ErrandGame.Settled || Travel.Busy) return StepOutcome.Waiting;
                    travelling = false;
                    lastTerritory = territory;
                    return StepOutcome.Done;
                },
                Timeout = TravelWait,
                TimeoutMessage = $"Didn't arrive at {where} within {TravelWait.TotalMinutes:0} minutes.",
            },
        };
    }

    private List<Step> TeleportSteps(uint territory, string where)
    {
        var aetheryte = ErrandGame.AetheryteOf(territory);
        var firstTry = DateTime.MinValue;
        var lastTry = DateTime.MinValue;
        var asks = 0;
        return new List<Step>
        {
            new Step
            {
                Label = $"Teleport to {where} (Lifestream, aetheryte {aetheryte})",
                IsAction = true,
                Run = () =>
                {
                    if (ErrandGame.Territory == territory) return StepOutcome.Done;
                    if (aetheryte == 0) return StepOutcome.Halt($"No aetheryte for {where} is known.");
                    var now = DateTime.UtcNow;
                    if (firstTry == DateTime.MinValue) firstTry = now;
                    // Lifestream says no while it (or you) is still busy: just after a world visit, a shop
                    // closing (0.7.6's log: refused at once, twice). Asked again every 2 s for 20 s.
                    if (ErrandGame.Settled && !Travel.Busy && now - lastTry > TimeSpan.FromSeconds(2))
                    {
                        lastTry = now;
                        travelling = true;
                        if (Travel.Teleport(aetheryte))
                        {
                            if (Rehearsal) Rehearsals.Report.Teleported();
                            return StepOutcome.Done;
                        }
                        travelling = false;
                    }
                    return now - firstTry > TimeSpan.FromSeconds(20)
                        ? StepOutcome.Halt($"Lifestream wouldn't teleport to {where} (asked for 20 s; is it installed and up to date?).")
                        : StepOutcome.Waiting;
                },
                // Its own 20 s decide (the step's 3 s default gave up after the second ask).
                Timeout = TimeSpan.FromSeconds(25),
            },
            new Step
            {
                Label = $"Arrive in {where}",
                Run = () =>
                {
                    if (ErrandGame.Territory != territory && Again(ref lastTry, ref asks, () => Travel.Teleport(aetheryte)) is { } stop) return stop;
                    if (ErrandGame.Territory != territory || !ErrandGame.Settled || Travel.Busy) return StepOutcome.Waiting;
                    travelling = false;
                    lastTerritory = territory;
                    return StepOutcome.Done;
                },
                Timeout = TravelWait,
                TimeoutMessage = $"Didn't arrive in {where} within {TravelWait.TotalMinutes:0} minutes.",
            },
        };
    }

    /// <summary>
    /// To a ward of a residential district (Lifestream's housing address: the city's aetheryte, then its
    /// Residential District Aethernet, to ward 1). Nothing when you're in one of its wards already.
    /// </summary>
    private List<Step> DistrictSteps(string district)
    {
        var city = Residential.CityAetheryte(district);
        var name = Districts.Display(district);
        var firstTry = DateTime.MinValue;
        var lastTry = DateTime.MinValue;
        var asks = 0;
        var arrivedAt = DateTime.MinValue;
        bool There() => ErrandGame.WardDistrict() == district;
        bool Ask() => Travel.GoToWard(ErrandGame.CurrentWorldId(), city);
        return new List<Step>
        {
            new Step
            {
                Label = $"To {name} (Lifestream: the city's Residential District Aethernet, ward 1)",
                IsAction = true,
                Run = () =>
                {
                    if (There()) return StepOutcome.Done;
                    if (city == 0) return StepOutcome.Halt($"No way to {name} is known.");
                    var now = DateTime.UtcNow;
                    if (firstTry == DateTime.MinValue) firstTry = now;
                    if (ErrandGame.Settled && !Travel.Busy && now - lastTry > TimeSpan.FromSeconds(2))
                    {
                        lastTry = now;
                        travelling = true;
                        if (Ask())
                        {
                            if (Rehearsal) Rehearsals.Report.Teleported();
                            return StepOutcome.Done;
                        }
                        travelling = false;
                    }
                    return now - firstTry > TimeSpan.FromSeconds(20)
                        ? StepOutcome.Halt($"Lifestream wouldn't go to {name} (asked for 20 s; is it installed and up to date?).")
                        : StepOutcome.Waiting;
                },
                Timeout = TimeSpan.FromSeconds(25),
            },
            new Step
            {
                Label = $"Arrive in {name}",
                Run = () =>
                {
                    if (!There() && Again(ref lastTry, ref asks, Ask, 20) is { } stop) return stop;
                    if (!There() || !ErrandGame.Settled)
                    {
                        arrivedAt = DateTime.MinValue;
                        return StepOutcome.Waiting;
                    }
                    // Lifestream walks on to the address's plot after the aethernet: stopped a moment after
                    // arriving (it has queued its walk by then), so the merchant is walked to instead.
                    if (arrivedAt == DateTime.MinValue) arrivedAt = DateTime.UtcNow;
                    if (DateTime.UtcNow - arrivedAt < TimeSpan.FromMilliseconds(1500)) return StepOutcome.Waiting;
                    if (Travel.Busy) Travel.Abort();
                    travelling = false;
                    lastTerritory = ErrandGame.Territory;
                    return StepOutcome.Done;
                },
                Timeout = TravelWait,
                TimeoutMessage = $"Didn't arrive in {name} within {TravelWait.TotalMinutes:0} minutes.",
            },
        };
    }

    private List<Step> WorldSteps(string world)
    {
        var lastTry = DateTime.MinValue;
        var asks = 0;
        return new List<Step>
        {
        Step.Act($"Go to {world} (Lifestream)", () =>
        {
            if (ErrandGame.CurrentWorld() == world) return StepOutcome.Done;
            travelling = true;
            lastTry = DateTime.UtcNow;
            return Travel.ChangeWorld(world) ? StepOutcome.Done : StepOutcome.Halt($"Lifestream wouldn't go to {world}.");
        }),
        new Step
        {
            Label = $"Arrive on {world}",
            Run = () =>
            {
                if (ErrandGame.CurrentWorld() != world && Again(ref lastTry, ref asks, () => Travel.ChangeWorld(world), 25) is { } stop) return stop;
                if (ErrandGame.CurrentWorld() != world || !ErrandGame.Settled || Travel.Busy) return StepOutcome.Waiting;
                travelling = false;
                lastTerritory = ErrandGame.Territory;
                return StepOutcome.Done;
            },
            Timeout = WorldWait,
            TimeoutMessage = $"Didn't arrive on {world} within {WorldWait.TotalMinutes:0} minutes.",
        },
        };
    }

    /// <summary>
    /// Travel asked for that never started: settled, Lifestream idle and still not there
    /// <paramref name="afterSeconds"/> s after the ask (a refusal its fallback hid, a cast cut short):
    /// asked again, twice, then stopped (0.7.13 waited out the arrival's 2 to 5 minutes). Null: carry on waiting.
    /// </summary>
    private StepOutcome? Again(ref DateTime lastTry, ref int asks, Func<bool> ask, int afterSeconds = 12)
    {
        var now = DateTime.UtcNow;
        if (lastTry == DateTime.MinValue) lastTry = now;
        if (!ErrandGame.Settled || Travel.Busy || ErrandGame.Moving || now - lastTry < TimeSpan.FromSeconds(afterSeconds)) return null;
        if (asks >= 2) return StepOutcome.Halt("Lifestream didn't take you there (asked three times).");
        asks++;
        lastTry = now;
        travelling = true;
        log.Info("  Not on the way yet: asking Lifestream again.");
        if (!ask()) log.Info("  Lifestream said no.");
        return null;
    }

    /// <summary>Walks (vnavmesh) to within <paramref name="within"/> yalms of a spot, found when the walk starts.</summary>
    private List<Step> WalkSteps(string what, Func<Vector3?> where, float within = Reach)
    {
        var started = DateTime.MinValue;
        var tries = 0;
        Vector3? to = null;
        var mounting = false;
        var sprintTried = DateTime.MinValue;
        var gotDown = false;
        var downAt = DateTime.MinValue;
        return new List<Step>
        {
            WaitFor("vnavmesh has this zone's map", () => Nav.Ready, TimeSpan.FromSeconds(90),
                "vnavmesh's map of this zone wasn't ready in 90 s (is vnavmesh installed and on?)."),
            // Faster: on a mount for a long way where the zone lets you ride, else Sprint.
            new Step
            {
                Label = "Mount up, or sprint",
                IsAction = true,
                Run = () =>
                {
                    mounting = false;
                    gotDown = false;
                    to = where();
                    if (to == null) return StepOutcome.Done; // said by the walk
                    var d = ErrandGame.DistanceTo(to.Value);
                    if (d <= within) return StepOutcome.Done;
                    mounting = Movement.SpeedUp(d, config.ErrandsRide, log.Info);
                    sprintTried = DateTime.UtcNow;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = "On the mount",
                Timeout = TimeSpan.FromSeconds(5),
                OnTimeout = StepStatus.Done, // on foot, then
                Run = () => !mounting || (Movement.Mounted && ErrandGame.Settled) ? StepOutcome.Done : StepOutcome.Waiting,
            },
            Step.Act($"Walk to {what} (vnavmesh)", () =>
            {
                to = where();
                if (to == null) return StepOutcome.Halt($"Couldn't find {what} here. Near you: {ErrandGame.NamesNear(40)}.");
                if (ErrandGame.DistanceTo(to.Value) <= within) return StepOutcome.Done;
                if (!Nav.MoveTo(to.Value)) return StepOutcome.Halt($"vnavmesh wouldn't walk to {what}.");
                walkTarget = to;
                started = DateTime.UtcNow;
                tries = 1;
                return StepOutcome.Done;
            }),
            new Step
            {
                Label = $"Arrive at {what}",
                Run = () =>
                {
                    if (to == null) return StepOutcome.Done;
                    var d = ErrandGame.DistanceTo(to.Value);
                    if (d <= within)
                    {
                        Nav.Stop();
                        walkTarget = null;
                        return StepOutcome.Done;
                    }
                    // On foot: Sprint again once it's off cooldown (tried every few seconds, not every frame).
                    if (!Movement.Mounted && d > 15 && DateTime.UtcNow - sprintTried > TimeSpan.FromSeconds(3))
                    {
                        sprintTried = DateTime.UtcNow;
                        Movement.TrySprint(log.Info);
                    }
                    if (Nav.Busy || DateTime.UtcNow - started < TimeSpan.FromSeconds(2)) return StepOutcome.Waiting;
                    // vnavmesh stopped short: once more, then it's no way through.
                    if (tries >= 2)
                    {
                        walkTarget = null;
                        return StepOutcome.Halt($"vnavmesh couldn't find a way to {what} (it stopped {d:0} yalms away).");
                    }
                    tries++;
                    started = DateTime.UtcNow;
                    return Nav.MoveTo(to.Value) ? StepOutcome.Waiting : StepOutcome.Halt($"vnavmesh wouldn't walk to {what}.");
                },
                Timeout = WalkWait,
                TimeoutMessage = $"Didn't get to {what} within {WalkWait.TotalMinutes:0} minutes.",
            },
            // Off the mount before talking to someone or using something, and landed: the game refuses
            // to use anything while the dismount's jump lasts ("Unable to execute command while jumping",
            // 0.7.6: the shop then took 10 s to give up on).
            new Step
            {
                Label = "Get down from the mount",
                Timeout = TimeSpan.FromSeconds(5),
                OnTimeout = StepStatus.Done,
                Run = () =>
                {
                    if (Movement.Mounted)
                    {
                        if (!gotDown && Movement.GetDown(log.Info))
                        {
                            gotDown = true;
                            downAt = DateTime.UtcNow;
                        }
                        return StepOutcome.Waiting;
                    }
                    // Not ridden, but maybe mid-jump (vnavmesh jumps): landed first all the same.
                    if (!gotDown) return Movement.Landed ? StepOutcome.Done : StepOutcome.Waiting;
                    return Movement.Landed && DateTime.UtcNow - downAt > TimeSpan.FromMilliseconds(900) ? StepOutcome.Done : StepOutcome.Waiting;
                },
            },
        };
    }

    /// <summary>
    /// You're free to use something: landed, not mounted, and the game not busy with a bell, an event or
    /// a menu still closing (0.7.13: the board used right after leaving a retainer said "Cannot execute
    /// at this time", and nothing tried again).
    /// </summary>
    private static bool FreeToUse()
    {
        var c = Services.Condition;
        return Movement.Landed && !c[ConditionFlag.OccupiedSummoningBell] && !c[ConditionFlag.OccupiedInEvent]
               && !c[ConditionFlag.OccupiedInQuestEvent] && !c[ConditionFlag.Occupied] && !c[ConditionFlag.Occupied30]
               && !c[ConditionFlag.Occupied33] && !c[ConditionFlag.Occupied38] && !c[ConditionFlag.Occupied39] && !c[ConditionFlag.Casting];
    }

    /// <summary>
    /// Uses something (a bell, a board) once you're free to, and waits for its window
    /// (<paramref name="opened"/>); used again every 2.5 s it hasn't opened, four times at most, as a
    /// person would click it again.
    /// </summary>
    private Step UseUntil(string what, Func<Dalamud.Game.ClientState.Objects.Types.IGameObject?> find, string window, Func<bool> opened)
    {
        var uses = 0;
        var usedAt = DateTime.MinValue;
        return new Step
        {
            Label = $"Use {what} ({window} opens)",
            IsAction = true,
            Timeout = TimeSpan.FromSeconds(20),
            TimeoutMessage = $"{window} didn't open.",
            Run = () =>
            {
                if (opened()) return StepOutcome.Done;
                if (uses > 0 && DateTime.UtcNow - usedAt < TimeSpan.FromSeconds(2.5)) return StepOutcome.Waiting;
                if (!FreeToUse()) return StepOutcome.Waiting;
                if (uses >= 4) return StepOutcome.Halt($"{window} didn't open (used {what} {uses} times).");
                if (find() is not { } o) return StepOutcome.Halt($"Couldn't find {what} within reach. Near you: {ErrandGame.NamesNear(15)}.");
                if (uses > 0) log.Info($"  {window} didn't open: using {what} again.");
                uses++;
                usedAt = DateTime.UtcNow;
                ErrandGame.Interact(o);
                return StepOutcome.Waiting;
            },
        };
    }

    /// <summary>Talks to (or uses) the object <paramref name="find"/> finds, near you.</summary>
    private static Step Use(string what, Func<Dalamud.Game.ClientState.Objects.Types.IGameObject?> find, string? key = null) => new()
    {
        Label = $"Use {what}",
        IsAction = true,
        Key = key,
        Run = () =>
        {
            var o = find();
            if (o == null) return StepOutcome.Halt($"Couldn't find {what} within reach. Near you: {ErrandGame.NamesNear(15)}.");
            return ErrandGame.Interact(o) ? StepOutcome.Done : StepOutcome.Halt($"Couldn't use {what}.");
        },
    };

    private static Step WaitFor(string label, Func<bool> until, TimeSpan timeout, string message, StepStatus onTimeout = StepStatus.Stop) => new()
    {
        Label = label,
        Run = () => until() ? StepOutcome.Done : StepOutcome.Waiting,
        Timeout = timeout,
        OnTimeout = onTimeout,
        TimeoutMessage = message,
    };

    /// <summary>Waits for you to do something (only if it isn't done already).</summary>
    /// <remarks>
    /// Said in the log only when it has to wait (0.8.0 said "You: Close the retainer's windows" when they
    /// were closed already, and then "done, after 0 s").
    /// </remarks>
    private Step You(string what, Func<bool> done, TimeSpan? timeout = null)
    {
        var said = false;
        return new Step
        {
            Label = $"You: {what}",
            Run = () =>
            {
                if (done()) return StepOutcome.Done;
                if (!said)
                {
                    said = true;
                    log.Info($"  … You: {what}");
                }
                return StepOutcome.Waiting;
            },
            Timeout = timeout ?? YouWait,
            TimeoutMessage = $"Waited {(timeout ?? YouWait).TotalMinutes:0} minutes for you to {what[..1].ToLowerInvariant()}{what[1..]}.",
        };
    }

    /// <summary>
    /// Clicks through an NPC's (or a retainer's) words until <paramref name="done"/>, as clicking them
    /// does (<see cref="ErrandTable.TalkNext"/>), a click every half second. If they don't move on
    /// (15 clicks, or 15 seconds with nothing), you're asked to.
    /// </summary>
    private Step ClickThrough(string who, Func<bool> done)
    {
        var clicks = 0;
        var next = DateTime.MinValue;
        DateTime? began = null;
        return new Step
        {
            Label = $"Click through what {who} says ({ErrandTable.TalkNext.Describe()})",
            Run = () =>
            {
                if (done()) return StepOutcome.Done;
                var now = DateTime.UtcNow;
                began ??= now;
                if (clicks >= 15 || now - began.Value > TimeSpan.FromSeconds(15))
                {
                    log.Info($"  {who}'s words didn't move on from the plugin's clicks: you click them through.");
                    queue.InsertNext(new[] { You($"Click through what {who} says", done, TimeSpan.FromMinutes(1)) });
                    return StepOutcome.Done;
                }
                if (!GameUi.IsOpen(ErrandTable.Talk) || now < next) return StepOutcome.Waiting;
                clicks++;
                next = now + TimeSpan.FromMilliseconds(500);
                GameUi.Fire(ErrandTable.TalkNext);
                return StepOutcome.Waiting;
            },
            Timeout = TimeSpan.FromMinutes(2),
            TimeoutMessage = $"{who}'s words didn't end.",
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Retainers.

    private void AddRetainer(ErrandStop stop)
    {
        var r = stop.Retainer!;
        Add("Going to a summoning bell", BellSteps, WindowCleanup, ErrandLeg.Bell, true);
        Add($"Opening {r.Name}'s bags", () => OpenRetainerSteps(r), WindowCleanup, ErrandLeg.Bell, who: r.Name);
        foreach (var item in stop.Items)
            Add($"Taking {Thing(item)} from {r.Name}", () => Rehearsal ? RehearseWithdraw(r, item) : WithdrawSteps(r, item), WindowCleanup, ErrandLeg.Bell, who: r.Name, item: item);
        Add($"Saying goodbye to {r.Name}", () => CloseRetainerSteps(r), WindowCleanup, ErrandLeg.Bell, who: r.Name);
    }

    private List<Step> BellSteps()
    {
        var steps = new List<Step>();
        var near = ErrandGame.Me is { } me && ErrandGame.Nearest(Bells(), me, houseBell ? 60 : 120, UsableKinds) != null;
        if (!near) steps.AddRange(HubSteps(Bells()));
        steps.AddRange(WalkSteps("a summoning bell", () => ErrandGame.Me is { } p ? ErrandGame.Nearest(Bells(), p, 150, UsableKinds)?.Position : null));
        return steps;
    }

    private List<Step> OpenRetainerSteps(RetainerStock r)
    {
        bool MenuOpen() => ErrandGame.MenuEntries()?.Addon == ErrandTable.SelectString;
        return new List<Step>
        {
            UseUntil("the summoning bell", () => ErrandGame.Me is { } me ? ErrandGame.Nearest(Bells(), me, Reach + 2, UsableKinds) : null,
                "The list of retainers", () => GameUi.IsOpen(ErrandTable.RetainerList)),
            new Step
            {
                Label = $"Choose {r.Name} ({ErrandTable.RetainerPick.Describe()})",
                IsAction = true,
                Run = () =>
                {
                    var row = ErrandGame.RetainerRow(r.RetainerId);
                    if (row < 0) return StepOutcome.Halt($"{r.Name} isn't in the bell's list.");
                    return GameUi.Fire(ErrandTable.RetainerPick, UiCallValues.Of(("index", row))) ? StepOutcome.Done : StepOutcome.Halt("The list of retainers closed.");
                },
            },
            // Their greeting (a Talk window) or their menu: 0.7.13 waited 8 s for the menu with the
            // greeting up, before clicking it through.
            Step.Wait($"{r.Name} comes", () => MenuOpen() || GameUi.IsOpen(ErrandTable.Talk), TimeSpan.FromSeconds(8), StepStatus.Done),
            ClickThrough(r.Name, MenuOpen),
            new Step
            {
                Label = $"Choose \"{ErrandTable.RetainerItemsWords[0]}\"",
                IsAction = true,
                Run = () =>
                {
                    if (ErrandGame.MenuEntries() is not { } menu) return StepOutcome.Halt($"{r.Name}'s menu isn't open.");
                    var i = ErrandGame.Find(menu.Entries, ErrandTable.RetainerItemsWords);
                    if (i < 0) return StepOutcome.Halt($"{r.Name}'s menu has no \"{ErrandTable.RetainerItemsWords[0]}\" (it shows: {string.Join(" | ", menu.Entries)}).");
                    return GameUi.Fire(ErrandTable.MenuPick, UiCallValues.Of(("index", i))) ? StepOutcome.Done : StepOutcome.Halt($"{r.Name}'s menu closed.");
                },
            },
            new Step
            {
                Label = $"{r.Name}'s bags open",
                Run = () =>
                {
                    if (!ErrandGame.RetainerBagsOpen) return StepOutcome.Waiting;
                    var active = ErrandGame.ActiveRetainer();
                    return active == r.RetainerId ? StepOutcome.Done : StepOutcome.Halt("Another retainer's bags opened.");
                },
                Timeout = WindowWait,
            },
        };
    }

    private List<Step> WithdrawSteps(RetainerStock r, ErrandItem item)
    {
        var taken = 0;
        var before = 0;
        var stack = ErrandGame.StackSize(item.ItemId);
        List<Step> Once() => new()
        {
            new Step
            {
                Label = $"Open the menu of {Thing(item)} in {r.Name}'s bags",
                IsAction = true,
                Run = () =>
                {
                    if (taken >= item.Count) return Finished();
                    if (!ErrandGame.RetainerBagsOpen) return StepOutcome.Halt($"{r.Name}'s bags closed.");
                    if (ErrandGame.RetainerSlot(item.ItemId) is not { } slot)
                    {
                        log.Warn($"  {r.Name} has no more {Name(item.ItemId)} ({taken} of {item.Count} taken): the rest is left.");
                        return Finished();
                    }
                    var room = ErrandGame.FreeSlots();
                    // (What tops up a stack already in your bags takes no slot, 0.9.5.)
                    if (room < ErrandPlanner.NewSlots(ErrandGame.NqCount(item.ItemId), slot.Quantity, stack)) return StepOutcome.Halt($"Your bags are full ({room} free slots; the stack of {slot.Quantity} needs more).");
                    before = ErrandGame.BagCount(item.ItemId);
                    menuReopened = false;
                    // Held for Continue next (a click not seen working yet): taken by hand meanwhile carries on.
                    guessByHand = () => ErrandGame.BagCount(item.ItemId) > before;
                    return ErrandGame.OpenRetainerItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
                },
            },
            Step.Wait("The item's menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), TimeSpan.FromSeconds(8)),
            new Step
            {
                Label = $"Choose \"{ErrandTable.RetrieveWords[^1]}\" (the whole stack)",
                IsAction = true,
                Key = Guess,
                Timeout = TimeSpan.FromSeconds(8),
                Run = () =>
                {
                    guessByHand = null;
                    // Taken by hand while this waited for Continue (0.8.5: you retrieved it, then pressed
                    // Continue, and the errands stopped on the closed menu): counted by the next step.
                    if (ErrandGame.BagCount(item.ItemId) > before)
                    {
                        log.Info($"  {Name(item.ItemId)} was taken by hand: carrying on.");
                        if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                        return StepOutcome.Done;
                    }
                    var entries = GameUi.MenuEntries();
                    if (entries == null)
                    {
                        // The menu closed (by hand?) with nothing taken: opened again, once.
                        if (menuReopened) return StepOutcome.Waiting;
                        menuReopened = true;
                        if (ErrandGame.RetainerSlot(item.ItemId) is not { } again) return StepOutcome.Halt("The item's menu closed, and the item isn't in the retainer's bags any more.");
                        log.Info("  The item's menu closed before the choice: opening it again.");
                        return ErrandGame.OpenRetainerItemMenu(again.Container, again.Slot) ? StepOutcome.Waiting : StepOutcome.Halt("The item's menu closed.");
                    }
                    var i = ErrandGame.Find(entries, ErrandTable.RetrieveWords, ErrandTable.RetrieveQuantityWords);
                    if (i < 0)
                    {
                        GameUi.Fire(UiTable.MenuCancel);
                        return StepOutcome.Halt($"The item's menu has no \"{ErrandTable.RetrieveWords[^1]}\" (it shows: {string.Join(" | ", entries)}).");
                    }
                    return GameUi.Fire(UiTable.MenuChoose, UiCallValues.Of(("entry", i))) ? StepOutcome.Done : StepOutcome.Halt("The item's menu closed.");
                },
            },
            new Step
            {
                Label = $"{Name(item.ItemId)} comes to your bags",
                Run = () =>
                {
                    if (GameUi.IsOpen("InputNumeric"))
                    {
                        GameUi.Close("InputNumeric");
                        return StepOutcome.Halt("The game asked how many to take: that isn't done yet (send the log).");
                    }
                    var now = ErrandGame.BagCount(item.ItemId);
                    if (now <= before) return StepOutcome.Waiting;
                    Count(item.ItemId, now - before);
                    taken += now - before;
                    if (taken < item.Count) queue.InsertNext(Once());
                    return StepOutcome.Done;
                },
                Timeout = TimeSpan.FromSeconds(12),
                TimeoutMessage = $"{Name(item.ItemId)} didn't come to your bags.",
            },
        };
        return Once();
    }

    private List<Step> CloseRetainerSteps(RetainerStock r) => new()
    {
        Step.Act($"Close {r.Name}'s bags", () =>
        {
            foreach (var name in ErrandTable.RetainerInventory) GameUi.Close(name);
            return StepOutcome.Done;
        }),
        Step.Wait($"{r.Name}'s menu", () => ErrandGame.MenuEntries() != null, TimeSpan.FromSeconds(5), StepStatus.Done),
        Step.Act($"Choose \"{ErrandTable.RetainerQuitWords[0]}\"", () =>
        {
            if (ErrandGame.MenuEntries() is not { } menu) return StepOutcome.Done;
            var i = ErrandGame.Find(menu.Entries, ErrandTable.RetainerQuitWords);
            if (i >= 0) GameUi.Fire(ErrandTable.MenuPick, UiCallValues.Of(("index", i)));
            else GameUi.Close(menu.Addon);
            return StepOutcome.Done;
        }),
        Step.Wait($"{r.Name} says goodbye", () => GameUi.IsOpen(ErrandTable.Talk) || GameUi.IsOpen(ErrandTable.RetainerList), TimeSpan.FromSeconds(5), StepStatus.Done),
        ClickThrough(r.Name, () => GameUi.IsOpen(ErrandTable.RetainerList) || !Services.Condition[ConditionFlag.OccupiedSummoningBell]),
        Step.Wait("The list of retainers", () => GameUi.IsOpen(ErrandTable.RetainerList), TimeSpan.FromSeconds(5), StepStatus.Done),
        Step.Act("Close the list of retainers", () =>
        {
            GameUi.Close(ErrandTable.RetainerList);
            return StepOutcome.Done;
        }),
        Step.Wait("Away from the bell", () => !Services.Condition[ConditionFlag.OccupiedSummoningBell], TimeSpan.FromSeconds(8), StepStatus.Done),
        You("Close the retainer's windows", () => !Services.Condition[ConditionFlag.OccupiedSummoningBell], TimeSpan.FromMinutes(1)),
    };

    // ---------------------------------------------------------------------------------------------
    // Vendors.

    private void AddVendor(ErrandStop stop)
    {
        var v = stop.Vendor!;
        Add($"Going to {v.Where()}", () => VendorTravelSteps(v), WindowCleanup, ErrandLeg.Vendor, true, v.NpcName);
        // One visit for all of this NPC's items: each shop of theirs opened in turn (the Housing Merchant's
        // chairs, then their partitions…), each item bought at its own offer's price.
        var first = true;
        foreach (var group in stop.Items.GroupBy(i => (i.Vendor ?? v).ShopId))
        {
            var shopOffer = group.First().Vendor ?? v;
            if (first) Add($"Opening {v.NpcName}'s shop", () => OpenShopSteps(shopOffer), WindowCleanup, ErrandLeg.Vendor, who: v.NpcName);
            else Add($"Opening {v.NpcName}'s \"{shopOffer.ShopName}\"", () => OpenShopSteps(shopOffer, new[] { ErrandTable.Shop }, another: true), WindowCleanup, ErrandLeg.Vendor, who: v.NpcName);
            first = false;
            foreach (var item in group)
            {
                var offer = item.Vendor ?? v;
                Add($"Buying {Thing(item)} from {v.NpcName}", () => Rehearsal ? RehearseBuy(offer, item) : BuySteps(offer, item), WindowCleanup, ErrandLeg.Vendor, who: v.NpcName, item: item);
            }
        }
        Add($"Closing {v.NpcName}'s shop", () => new List<Step>
        {
            Step.Act("Close the shop", () =>
            {
                GameUi.Close(ErrandTable.Shop);
                GameUi.Close(ErrandTable.SelectIconString);
                GameUi.Close(ErrandTable.SelectString);
                return StepOutcome.Done;
            }),
            Step.Wait("The shop closes", () => !GameUi.IsOpen(ErrandTable.Shop), TimeSpan.FromSeconds(3), StepStatus.Done),
        }, WindowCleanup, ErrandLeg.Vendor, who: v.NpcName);
    }

    private List<Step> VendorTravelSteps(VendorOffer v)
    {
        var steps = new List<Step>();
        if (v.District != null && home is { } h && v.District == h.District && h.Kind != HomeKind.Apartment)
        {
            // Your own district's: your home's teleport lands you in your ward, where they stand.
            if (!ErrandGame.InHomeWard(home!)) steps.AddRange(GoHomeSteps(leave: true));
        }
        else if (v.District != null)
        {
            // Another district's (0.8.3): its city's Residential District Aethernet, to a ward there; the
            // merchant walked to is that ward's nearest (each ward and division has its own).
            steps.AddRange(DistrictSteps(v.District));
        }
        else
        {
            steps.AddRange(TeleportSteps(v.TerritoryId, v.PlaceName.Length > 0 ? v.PlaceName : "the vendor's zone"));
        }
        var at = v.Position!.Value;
        var said = false;
        // The one in sight, else the nearest spot of an NPC of that name in this zone: a ward's subdivision
        // has its own Housing Merchant, which the offer (the main division's) doesn't name, and the two
        // divisions can't be walked between (0.7.5: from a subdivision it set off for the main one's).
        Vector3 Target()
        {
            if (ErrandGame.NpcOrNamed(v.NpcId, v.NpcName, 60) is { } seen) return seen.Position;
            var nearest = shop.Vendors.SpotsOf(v.NpcName, ErrandGame.Territory)
                .Select(p => (Vector3?)p).OrderBy(p => ErrandGame.DistanceTo(p!.Value)).FirstOrDefault();
            if (nearest is not { } n) return at;
            if (!said && Vector3.Distance(n, at) > 5)
            {
                said = true;
                log.Info($"  The nearest {v.NpcName} here is at ({n.X:0.0}, {n.Z:0.0}), {ErrandGame.DistanceTo(n):0} yalms away (not the one in the list: another division of the ward).");
            }
            return n;
        }
        // Up close: from 3 yalms the game said "Unable to execute action from this location" (0.8.0's
        // merchant), and they had to be talked to again.
        steps.AddRange(WalkSteps(v.NpcName, () => Target(), NpcReach));
        return steps;
    }

    private List<Step> OpenShopSteps(VendorOffer v) => OpenShopSteps(v, new[] { ErrandTable.Shop });

    /// <summary>
    /// Talks to the vendor and goes through their menus until one of <paramref name="windows"/> (their shop) opens.
    /// <paramref name="another"/>: another of their shops, after one: the open one is closed first, and their
    /// menu used if it comes back by itself (some NPCs' does), else they're talked to again.
    /// </summary>
    private List<Step> OpenShopSteps(VendorOffer v, string[] windows, bool another = false)
    {
        var menus = 0;
        var used = DateTime.MinValue;
        var again = 0;
        Step Opens() => new()
        {
            Label = $"{v.NpcName}'s shop opens",
            Run = () =>
            {
                if (used == DateTime.MinValue) used = DateTime.UtcNow;
                if (windows.Any(GameUi.IsOpen)) return StepOutcome.Done;
                if (ErrandGame.MenuEntries() != null)
                {
                    if (++menus > 3) return StepOutcome.Halt($"{v.NpcName}'s menus went on and on.");
                    queue.InsertNext(new[] { PickShop(), Opens() });
                    return StepOutcome.Done;
                }
                if (GameUi.IsOpen(ErrandTable.Talk))
                {
                    queue.InsertNext(new[] { ClickThrough(v.NpcName, () => !GameUi.IsOpen(ErrandTable.Talk)), Opens() });
                    return StepOutcome.Done;
                }
                // Nothing opened (the game refused, say, mid-jump): talk to them again, twice at most.
                if (DateTime.UtcNow - used > TimeSpan.FromSeconds(2.5) && again < 2 && ErrandGame.Settled && ErrandGame.NpcOrNamed(v.NpcId, v.NpcName, Reach + 3) is { } npc)
                {
                    again++;
                    used = DateTime.UtcNow;
                    log.Info($"  {v.NpcName} didn't answer: talking to them again.");
                    ErrandGame.Interact(npc);
                }
                return StepOutcome.Waiting;
            },
            Timeout = WindowWait,
            TimeoutMessage = $"{v.NpcName}'s shop didn't open.",
        };
        Step PickShop() => new()
        {
            Label = $"Choose \"{v.ShopName}\" in {v.NpcName}'s menu",
            IsAction = true,
            Run = () =>
            {
                if (ErrandGame.MenuEntries() is not { } menu) return StepOutcome.Halt($"{v.NpcName}'s menu closed.");
                var i = v.ShopName.Length == 0 ? -1 : menu.Entries.FindIndex(e => string.Equals(e.Trim(), v.ShopName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (i < 0 && v.ShopName.Length > 0)
                    i = menu.Entries.FindIndex(e => e.Trim().Length > 0
                                                    && (e.Contains(v.ShopName.Trim(), StringComparison.OrdinalIgnoreCase) || v.ShopName.Contains(e.Trim(), StringComparison.OrdinalIgnoreCase)));
                if (i < 0) return StepOutcome.Halt($"{v.NpcName}'s menu has no \"{v.ShopName}\" (it shows: {string.Join(" | ", menu.Entries)}).");
                var call = menu.Addon == ErrandTable.SelectIconString ? ErrandTable.IconMenuPick : ErrandTable.MenuPick;
                return GameUi.Fire(call, UiCallValues.Of(("index", i))) ? StepOutcome.Done : StepOutcome.Halt($"{v.NpcName}'s menu closed.");
            },
        };
        if (!another)
            return new List<Step>
            {
                Use(v.NpcName, () => ErrandGame.NpcOrNamed(v.NpcId, v.NpcName, Reach + 3)),
                Opens(),
            };
        var closedAt = DateTime.MinValue;
        return new List<Step>
        {
            Step.Act("Close the last shop", () =>
            {
                GameUi.Close(ErrandTable.Shop);
                return StepOutcome.Done;
            }),
            new Step
            {
                Label = $"The shop closes ({v.NpcName}'s menu may come back)",
                Run = () =>
                {
                    if (windows.Any(GameUi.IsOpen) || GameUi.IsOpen(ErrandTable.Shop)) return StepOutcome.Waiting;
                    if (ErrandGame.MenuEntries() != null) return StepOutcome.Done;
                    if (closedAt == DateTime.MinValue) closedAt = DateTime.UtcNow;
                    // Free to talk again (the shop's close takes a moment), or their menu came back.
                    return FreeToUse() && DateTime.UtcNow - closedAt > TimeSpan.FromMilliseconds(Math.Max(300, 600 * Speed.WaitScale)) ? StepOutcome.Done : StepOutcome.Waiting;
                },
                Timeout = TimeSpan.FromSeconds(5),
                TimeoutMessage = $"{v.NpcName}'s shop didn't close.",
            },
            new Step
            {
                Label = $"Talk to {v.NpcName} again (unless their menu is up)",
                IsAction = true,
                Run = () =>
                {
                    if (ErrandGame.MenuEntries() != null) return StepOutcome.Done;
                    if (ErrandGame.NpcOrNamed(v.NpcId, v.NpcName, Reach + 3) is not { } npc) return StepOutcome.Halt($"Couldn't find {v.NpcName} within reach. Near you: {ErrandGame.NamesNear(15)}.");
                    return ErrandGame.Interact(npc) ? StepOutcome.Done : StepOutcome.Halt($"Couldn't talk to {v.NpcName}.");
                },
            },
            Opens(),
        };
    }

    private List<Step> BuySteps(VendorOffer v, ErrandItem item)
    {
        var bought = 0;
        var before = 0;
        long gilBefore = 0;
        var n = 0;
        long allowed = 0;
        var stack = ErrandGame.StackSize(item.ItemId);
        List<Step> Once() => new()
        {
            new Step
            {
                Label = $"Buy {Name(item.ItemId)} from {v.NpcName} ({ErrandTable.ShopBuy.Describe()})",
                IsAction = true,
                Run = () =>
                {
                    if (bought >= item.Count) return Finished();
                    if (!GameUi.IsOpen(ErrandTable.Shop)) return StepOutcome.Halt($"{v.NpcName}'s shop closed.");
                    n = Math.Min(item.Count - bought, Math.Min(MaxPerPurchase, stack));
                    allowed = 0;
                    var cost = (long)v.Price * n;
                    var gil = ErrandGame.Gil();
                    if (gil < cost) return StepOutcome.Halt($"Not enough gil: {cost:N0} for {n} {Name(item.ItemId)}, and you have {gil:N0}.");
                    if (Spent + cost > agreedGil) return StepOutcome.Halt($"That would spend more than the {agreedGil:N0} gil you agreed to.");
                    var room = ErrandGame.FreeSlots();
                    if (room < ErrandPlanner.NewSlots(ErrandGame.NqCount(item.ItemId), n, stack)) return StepOutcome.Halt($"Your bags are full ({room} free slots).");
                    var row = ErrandGame.ShopRow(v.ShopId, item.ItemId);
                    if (row < 0)
                    {
                        log.Warn($"  {Name(item.ItemId)} isn't in {v.NpcName}'s list (it may need a quest first): left for now.");
                        return Finished();
                    }
                    before = ErrandGame.BagCount(item.ItemId);
                    gilBefore = gil;
                    return GameUi.Fire(ErrandTable.ShopBuy, UiCallValues.Of(("row", row), ("count", n))) ? StepOutcome.Done : StepOutcome.Halt($"{v.NpcName}'s shop closed.");
                },
            },
            Bought(),
        };
        Step Bought() => new()
        {
            Label = $"{Name(item.ItemId)} comes to your bags",
            Run = () =>
            {
                var now = ErrandGame.BagCount(item.ItemId);
                if (now < before + n)
                {
                    if (GameUi.YesNoText() is { } question && now == before)
                    {
                        // Only a question about this purchase is answered yes: it names its price ("Purchase 1
                        // riviera table for 2,000 gil?"; the item's name in it may be a plural or "lump of …").
                        if (!MarketRule.QuestionMatches(question, v.Price, n, (long)v.Price * n))
                        {
                            // Another price than the game's data has (0.7.6: "Purchase 1 stage panel for 4,000
                            // gil?" with 1,000 planned): yes when the whole run still stays within the gil you
                            // agreed to (and you have it), else no, and this item is left.
                            var askedNow = MarketRule.AskedCost(question, n);
                            // Only a question about this item (a row miscounted would buy another one).
                            var aboutIt = MarketRule.NamesItem(question, Name(item.ItemId));
                            if (!aboutIt) log.Info($"  The shop's question doesn't name {Name(item.ItemId)}: \"{question}\".");
                            if (aboutIt && askedNow is { } askedSum && askedSum > 0 && askedSum % n == 0) LearnPrice(item.ItemId, (uint)(askedSum / n), v.Price);
                            if (aboutIt && askedNow is { } asked && Spent + asked <= agreedGil && ErrandGame.Gil() >= asked)
                            {
                                log.Info($"  The shop asks {asked:N0} gil for {n} × {Name(item.ItemId)}, not the {(long)v.Price * n:N0} planned: still within the {agreedGil:N0} gil you agreed to, so yes.");
                                allowed = asked;
                                queue.InsertNext(new[] { Yes(question), Bought() });
                                return StepOutcome.Done;
                            }
                            GameUi.Close(UiTable.YesNo);
                            log.Warn($"  The shop asked \"{question}\", not {n} × {Name(item.ItemId)} for {(long)v.Price * n:N0} gil as planned ({v.NpcName}, NPC {v.NpcId}, shop {v.ShopId}), and {(aboutIt ? $"it would go past the {agreedGil:N0} gil you agreed to" : "the question isn't about it")}: said no, and {Name(item.ItemId)} is left for you.");
                            return Finished();
                        }
                        allowed = (long)v.Price * n;
                        queue.InsertNext(new[] { Yes(question), Bought() });
                        return StepOutcome.Done;
                    }
                    return StepOutcome.Waiting;
                }
                // The gil may go a frame or two after the item comes: waited for (1.5 s), so it's counted.
                if (ErrandGame.Gil() >= gilBefore && GilLate(ref vendorArrived)) return StepOutcome.Waiting;
                vendorArrived = null;
                var spent = Math.Max(0, gilBefore - ErrandGame.Gil());
                VendorSpent += spent;
                Count(item.ItemId, now - before);
                bought += now - before;
                if (spent > Math.Max(allowed, (long)v.Price * n)) return StepOutcome.Halt($"{Name(item.ItemId)} cost {spent:N0} gil, more than the {Math.Max(allowed, (long)v.Price * n):N0} agreed.");
                if (bought < item.Count) queue.InsertNext(Once());
                return StepOutcome.Done;
            },
            Timeout = TimeSpan.FromSeconds(12),
            TimeoutMessage = $"{Name(item.ItemId)} didn't come to your bags.",
        };
        return Once();
    }

    // ---------------------------------------------------------------------------------------------
    // Currency vendors (0.7.14): MGP at the Gold Saucer, marks, seals, tomestones.

    /// <summary>What each currency may cost in all, as agreed (the plan's), and what's been spent of it.</summary>
    private readonly Dictionary<uint, long> currencyAgreed = new();

    private readonly Dictionary<uint, long> currencySpent = new();

    private void AddExchange(ErrandStop stop)
    {
        var v = stop.Vendor!;
        Add($"Going to {v.Where()}", () => VendorTravelSteps(v), WindowCleanup, ErrandLeg.Vendor, true, v.NpcName);
        Add($"Opening {v.NpcName}'s exchange", () => OpenShopSteps(v, ErrandTable.ExchangeWindows), WindowCleanup, ErrandLeg.Vendor, who: v.NpcName);
        foreach (var item in stop.Items.Where(i => i.Exchange != null))
            Add($"Exchanging for {Thing(item)} at {v.NpcName}", () => Rehearsal ? RehearseExchange(item) : ExchangeSteps(item), WindowCleanup, ErrandLeg.Vendor, who: v.NpcName, item: item);
        Add($"Closing {v.NpcName}'s exchange", () => new List<Step>
        {
            Step.Act("Close the exchange", () =>
            {
                GameUi.Close(ErrandTable.ExchangeDialog);
                foreach (var w in ErrandTable.ExchangeWindows) GameUi.Close(w);
                GameUi.Close(ErrandTable.SelectIconString);
                GameUi.Close(ErrandTable.SelectString);
                return StepOutcome.Done;
            }),
            Step.Wait("The exchange closes", () => !ErrandTable.ExchangeWindows.Any(GameUi.IsOpen), TimeSpan.FromSeconds(3), StepStatus.Done),
        }, WindowCleanup, ErrandLeg.Vendor, who: v.NpcName);
    }

    /// <summary>The currency vendor's window that's open, or null.</summary>
    private static string? ExchangeWindow() => ErrandTable.ExchangeWindows.FirstOrDefault(GameUi.IsOpen);

    /// <summary>
    /// Exchanges for an item: its row found in the window by name, then Exchange with the count; what
    /// comes to your bags and what the currency went down by are checked against the plan. A question
    /// is answered yes only when it names the cost planned; the game's own confirm window (if this
    /// vendor has one) is left for you to press, and the errands carry on once the item comes.
    /// </summary>
    private List<Step> ExchangeSteps(ErrandItem item)
    {
        var ex = item.Exchange!;
        var v = ex.Vendor;
        var bought = 0;
        var before = 0;
        long haveBefore = 0;
        var n = 0;
        long cost = 0;
        var askedYou = false;
        var stack = ErrandGame.StackSize(item.ItemId);
        var handBefore = 0;
        long handHave = 0;
        List<Step> Once() => new()
        {
            new Step
            {
                // What's in your bags before the click that waits for Continue: one done by hand is counted.
                Label = $"Note your {Name(item.ItemId)} and {ex.CurrencyName}",
                Run = () =>
                {
                    handBefore = ErrandGame.BagCount(item.ItemId);
                    handHave = ErrandGame.CurrencyCount(ex.Currency);
                    guessByHand = () => ErrandGame.BagCount(item.ItemId) > handBefore;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Exchange {ex.CurrencyName} for {Name(item.ItemId)} at {v.NpcName} ({ErrandTable.ExchangeCurrencyBuy.Describe()})",
                IsAction = true,
                Key = Guess,
                Run = () =>
                {
                    guessByHand = null;
                    // Exchanged by hand while this waited for Continue: counted, never exchanged again.
                    var byHand = ErrandGame.BagCount(item.ItemId) - handBefore;
                    if (byHand > 0)
                    {
                        var paid = Math.Max(0, handHave - ErrandGame.CurrencyCount(ex.Currency));
                        currencySpent[ex.Currency] = currencySpent.GetValueOrDefault(ex.Currency) + paid;
                        Count(item.ItemId, byHand);
                        bought += byHand;
                        handBefore += byHand;
                        handHave -= paid;
                        log.Info($"  {byHand} × {Name(item.ItemId)} exchanged by hand ({paid:N0} {ex.CurrencyName}): counted.");
                    }
                    if (bought >= item.Count) return Finished();
                    if (ExchangeWindow() is not { } window) return StepOutcome.Halt($"{v.NpcName}'s exchange closed.");
                    n = Math.Min(item.Count - bought, Math.Min(MaxPerPurchase, stack));
                    var times = ex.Exchanges(n);
                    cost = ex.CostFor(n);
                    var have = ErrandGame.CurrencyCount(ex.Currency);
                    if (have < cost)
                    {
                        log.Warn($"  Not enough {ex.CurrencyName}: {cost:N0} for {n} × {Name(item.ItemId)}, and you have {have:N0}. Left for now.");
                        return Finished();
                    }
                    var agreed = currencyAgreed.GetValueOrDefault(ex.Currency);
                    if (currencySpent.GetValueOrDefault(ex.Currency) + cost > agreed)
                    {
                        log.Warn($"  That would spend more than the {agreed:N0} {ex.CurrencyName} agreed to: {Name(item.ItemId)} is left for now.");
                        return Finished();
                    }
                    var room = ErrandGame.FreeSlots();
                    if (room < ErrandPlanner.NewSlots(ErrandGame.NqCount(item.ItemId), n, stack)) return StepOutcome.Halt($"Your bags are full ({room} free slots).");
                    var row = ErrandGame.ExchangeRow(window, shop.Vendors.ExchangeShopItems(v.ShopId), item.ItemId);
                    if (row < 0)
                    {
                        log.Warn($"  {Name(item.ItemId)} isn't in {v.NpcName}'s list (it may need a quest, or another of their menus): left for now. Its window's words: {GameUi.TextSummary(window)}");
                        return Finished();
                    }
                    before = ErrandGame.BagCount(item.ItemId);
                    haveBefore = have;
                    askedYou = false;
                    log.Info($"  Exchanging {cost:N0} {ex.CurrencyName} for {n} × {Name(item.ItemId)} (row {row}, {times} exchange{(times == 1 ? string.Empty : "s")}).");
                    var call = window == ErrandTable.ExchangeItem ? ErrandTable.ExchangeItemBuy : ErrandTable.ExchangeCurrencyBuy;
                    return GameUi.Fire(call, UiCallValues.Of(("row", row), ("count", times))) ? StepOutcome.Done : StepOutcome.Halt($"{v.NpcName}'s exchange closed.");
                },
            },
            Came(),
        };
        Step Came() => new()
        {
            Label = $"{Name(item.ItemId)} comes to your bags",
            Run = () =>
            {
                var now = ErrandGame.BagCount(item.ItemId);
                if (now <= before)
                {
                    if (GameUi.YesNoText() is { } question)
                    {
                        // Only a question naming the cost planned is answered yes.
                        if (MarketRule.QuestionMatches(question, ex.Cost, ex.Exchanges(n), cost))
                        {
                            queue.InsertNext(new[] { Yes(question), Came() });
                            return StepOutcome.Done;
                        }
                        GameUi.Close(UiTable.YesNo);
                        log.Warn($"  The game asked \"{question}\", not {cost:N0} {ex.CurrencyName} for {n} × {Name(item.ItemId)} as planned: said no, and it's left for you.");
                        return Finished();
                    }
                    if (GameUi.IsOpen(ErrandTable.ExchangeDialog) && !askedYou)
                    {
                        // Its confirm window isn't known yet: you press its button (once); then it's seen.
                        askedYou = true;
                        log.Warn($"  The game asks to confirm the exchange in its own window: press Exchange there for {n} × {Name(item.ItemId)} ({cost:N0} {ex.CurrencyName}); the errands carry on once it comes.");
                    }
                    return StepOutcome.Waiting;
                }
                var spent = Math.Max(0, haveBefore - ErrandGame.CurrencyCount(ex.Currency));
                currencySpent[ex.Currency] = currencySpent.GetValueOrDefault(ex.Currency) + spent;
                Count(item.ItemId, now - before);
                bought += now - before;
                log.Info($"  Got {now - before} × {Name(item.ItemId)} for {spent:N0} {ex.CurrencyName}.");
                if (spent > cost) return StepOutcome.Halt($"{Name(item.ItemId)} cost {spent:N0} {ex.CurrencyName}, more than the {cost:N0} planned.");
                if (bought < item.Count) queue.InsertNext(Once());
                return StepOutcome.Done;
            },
            Timeout = TimeSpan.FromSeconds(90),
            TimeoutMessage = $"{Name(item.ItemId)} didn't come to your bags.",
        };
        return Once();
    }

    /// <summary>
    /// A rehearsal at a currency vendor: the window open, the item's row found and what it would cost
    /// said, with what you have; Exchange is never pressed (its click isn't confirmed by a recording yet).
    /// </summary>
    private List<Step> RehearseExchange(ErrandItem item)
    {
        var ex = item.Exchange!;
        var v = ex.Vendor;
        var where = $"Exchanging for {Thing(item)} at {v.NpcName}";
        return new List<Step>
        {
            Step.Act($"Find {Name(item.ItemId)} in {v.NpcName}'s exchange (Exchange isn't pressed in a rehearsal)", () =>
            {
                if (ExchangeWindow() is not { } window)
                {
                    Rehearsals.Report.Issue(where, $"{v.NpcName}'s exchange window didn't open (looked for {string.Join(" or ", ErrandTable.ExchangeWindows)}).", RehearsalTrouble.Failed);
                    return Finished();
                }
                var row = ErrandGame.ExchangeRow(window, shop.Vendors.ExchangeShopItems(v.ShopId), item.ItemId);
                var cost = ex.CostFor(item.Count);
                var have = ErrandGame.CurrencyCount(ex.Currency);
                if (row < 0)
                {
                    Rehearsals.Report.Issue(where, $"{Name(item.ItemId)} wasn't found in the window ({window}). Its words: {GameUi.TextSummary(window)}", RehearsalTrouble.Failed);
                    log.Warn($"  {Name(item.ItemId)} wasn't found in {window}. Its words: {GameUi.TextSummary(window)}");
                    return Finished();
                }
                if (have < cost) WouldStop(where, $"You have {have:N0} {ex.CurrencyName}; this takes {cost:N0}.");
                log.Info($"  Would exchange {cost:N0} {ex.CurrencyName} for {item.Count} × {Name(item.ItemId)}: row {row} of {window} (you have {have:N0}). A rehearsal doesn't press Exchange.");
                Rehearsals.Report.Would(RehearsalAct.Buy, item.Count, 0);
                Rehearsals.Report.Note($"{where}: found at row {row} of {window}; would spend {cost:N0} {ex.CurrencyName}.");
                return StepOutcome.Done;
            }),
        };
    }

    /// <summary>Answers the game's yes/no (OK) for a purchase checked above. [Recording 2: SelectYesno [i:0]]</summary>
    private static Step Yes(string question) => new()
    {
        Label = $"Answer yes to \"{question}\"",
        IsAction = true,
        Run = () => GameUi.Fire(UiTable.YesNoYes) ? StepOutcome.Done : StepOutcome.Halt("The yes/no closed."),
    };

    // ---------------------------------------------------------------------------------------------
    // The market board.

    private void AddMarket(ErrandStop stop)
    {
        var world = stop.World ?? ErrandGame.CurrentWorld();
        Add($"Going to a market board on {world}", () => BoardSteps(world), WindowCleanup, ErrandLeg.Market, true, world);
        foreach (var item in stop.Items)
            Add($"Buying {Thing(item)} on the market board", () => Rehearsal ? RehearseListing(item) : ListingSteps(item), WindowCleanup, ErrandLeg.Market, who: world, item: item);
        Add("Closing the market board", () => new List<Step>
        {
            Step.Act("Close the board", () =>
            {
                GameUi.Close(ErrandTable.ItemSearchResult);
                GameUi.Close(ErrandTable.ItemSearch);
                return StepOutcome.Done;
            }),
        }, WindowCleanup, ErrandLeg.Market, who: world);
    }

    private List<Step> BoardSteps(string world)
    {
        var steps = new List<Step>();
        if (ErrandGame.CurrentWorld() != world)
        {
            // A world visit leaves from a city's aetheryte: the market city's first (unless you're in
            // one already), then the board where the visit lands, if it's a market city.
            steps.AddRange(HubSteps(null));
            steps.AddRange(WorldSteps(world));
        }
        steps.AddRange(HubSteps(Boards()));
        steps.AddRange(WalkSteps("a market board", () => ErrandGame.Me is { } p ? ErrandGame.Nearest(Boards(), p, 150, UsableKinds)?.Position : null));
        steps.Add(UseUntil("the market board", () => ErrandGame.Me is { } me ? ErrandGame.Nearest(Boards(), me, Reach + 2, UsableKinds) : null,
            "The board's search", () => GameUi.IsOpen(ErrandTable.ItemSearch) || GameUi.IsOpen(ErrandTable.ItemSearchResult)));
        return steps;
    }

    private List<Step> ListingSteps(ErrandItem item)
    {
        var wanted = item.Count;
        var bought = 0;
        long itemSpent = 0;
        var before = 0;
        long gilBefore = 0;
        long cost = 0;
        uint unit = 0;
        var quantity = 0;
        var name = Name(item.ItemId);
        // Listings bought (or tried): the board's list may still show one for a moment after.
        var tried = new HashSet<ulong>();
        ulong listing = 0;
        var gone = false;
        var retryPending = false;
        var misses = 0;
        var firedAt = DateTime.MinValue;
        DateTime confirmFrom() => firedAt;
        // A listing that didn't work out (gone, or the list moved under the click): the next one, three
        // times at most, then the item is left for now (0.7.13 stopped the whole run).
        StepOutcome Missed(string why)
        {
            gone = true;
            misses++;
            BoardListings.Gone(listing);
            if (misses > 3)
            {
                log.Warn($"  {name}: {why}; tried {misses} listings, so it's left for now.");
                return Finished();
            }
            log.Info($"  {name}: {why}. Trying the next listing.");
            // The next try goes after this one's last step (its steps still to come see `gone` and pass).
            retryPending = true;
            return StepOutcome.Done;
        }
        List<Step> Once() => new()
        {
            new Step
            {
                Label = $"Buy a listing of {name} ({ErrandTable.ListingBuy.Describe()})",
                IsAction = true,
                Run = () =>
                {
                    if (bought >= wanted) return Finished();
                    // A late question from the last listing (it came after it was given up on): no, first.
                    if (GameUi.YesNoText() != null)
                    {
                        GameUi.Close(UiTable.YesNo);
                        return StepOutcome.Waiting;
                    }
                    if (BoardListings.Open(item.ItemId) is not { } listings)
                        return GameUi.IsOpen(ErrandTable.ItemSearchResult) ? StepOutcome.Waiting : StepOutcome.Halt($"{name}'s listings closed.");
                    gone = false;
                    var gil = ErrandGame.Gil();
                    string? firstWhy = null;
                    foreach (var l in listings.OrderBy(l => l.UnitPrice))
                    {
                        if (tried.Contains(l.ListingId)) continue;
                        var why = MarketRule.WhyNot(l.UnitPrice, l.Quantity, l.TotalTax, item.MaxUnitPrice, wanted - bought, item.Gil - itemSpent,
                            Math.Min(ceiling - MarketSpent, agreedGil - Spent), gil);
                        if (why != null)
                        {
                            firstWhy ??= why;
                            continue;
                        }
                        if (ErrandGame.FreeSlots() < ErrandPlanner.NewSlots(ErrandGame.NqCount(item.ItemId), l.Quantity, ErrandGame.StackSize(item.ItemId)))
                        {
                            // Too big for your bags: a smaller listing may still fit.
                            firstWhy ??= $"your bags have {ErrandGame.FreeSlots()} free slots";
                            continue;
                        }
                        cost = MarketRule.Cost(l.UnitPrice, l.Quantity, l.TotalTax);
                        unit = l.UnitPrice;
                        quantity = l.Quantity;
                        tried.Add(l.ListingId);
                        listing = l.ListingId;
                        before = ErrandGame.BagCount(item.ItemId);
                        gilBefore = gil;
                        log.Info($"  Buying {l.Quantity} × {name} at {l.UnitPrice:N0} gil each ({cost:N0} with tax), row {l.Row}"
                                 + (l.UnitPrice > item.MaxUnitPrice ? $": above the {item.MaxUnitPrice:N0} planned (the board's price has moved), within a quarter more and the gil agreed." : "."));
                        firedAt = DateTime.UtcNow;
                        return GameUi.Fire(ErrandTable.ListingBuy, UiCallValues.Of(("index", l.Row))) ? StepOutcome.Done : StepOutcome.Halt($"{name}'s listings closed.");
                    }
                    log.Warn($"  No listing of {name} to buy here ({firstWhy ?? "none listed"}): {wanted - bought} left for now; this world isn't planned for it again this run.");
                    boardWhy.TryAdd(item.ItemId, $"{ErrandGame.CurrentWorld()}: {firstWhy ?? "none listed"}");
                    if (emptyBoards.Add((item.ItemId, ErrandGame.CurrentWorld()))) emptiedThisTrip++;
                    return Finished();
                },
                Timeout = TimeSpan.FromSeconds(15),
                TimeoutMessage = $"{name}'s listings didn't come.",
            },
            new Step
            {
                // No question in 5 s: the listing has gone (bought by someone else first); the next one.
                Label = "The board asks to confirm",
                Timeout = TimeSpan.FromSeconds(8),
                Run = () =>
                {
                    if (GameUi.YesNoText() != null) return StepOutcome.Done;
                    if (DateTime.UtcNow - confirmFrom() < TimeSpan.FromSeconds(5)) return StepOutcome.Waiting;
                    return Missed("the board didn't ask to confirm (sold to someone else first?)");
                },
            },
            new Step
            {
                Label = $"Answer yes (buy {name})",
                IsAction = true,
                Run = () =>
                {
                    // Only the listing chosen: the question must name its price (another row clicked, or
                    // another question, is said no to and stops the errands).
                    if (gone) return StepOutcome.Done;
                    var question = GameUi.YesNoText();
                    if (!MarketRule.BoardQuestionMatches(question, quantity, cost, (long)unit * quantity))
                    {
                        // Another listing's question (the list moved under the click): no, and the next.
                        GameUi.Close(UiTable.YesNo);
                        return Missed($"the board asked \"{question}\", not the {cost:N0} gil listing chosen: said no");
                    }
                    return GameUi.Fire(UiTable.YesNoYes) ? StepOutcome.Done : StepOutcome.Halt("The yes/no closed.");
                },
            },
            new Step
            {
                Label = $"{name} comes to your bags",
                Run = () =>
                {
                    if (!gone)
                    {
                        var came = ErrandGame.BagCount(item.ItemId);
                        if (came <= before && DateTime.UtcNow - confirmFrom() < TimeSpan.FromSeconds(10)) return StepOutcome.Waiting;
                        // The gil has gone: it's bought and on its way (a slow server). Never another
                        // listing meanwhile; the step's own limit stops the errands if it never comes.
                        if (came <= before && ErrandGame.Gil() < gilBefore) return StepOutcome.Waiting;
                        if (came <= before && Missed("it didn't come to your bags (sold to someone else first?)").Status != StepStatus.Done) return Finished();
                    }
                    if (gone)
                    {
                        // This try is over: the next listing, if one's due (the last step of the try).
                        if (retryPending)
                        {
                            retryPending = false;
                            queue.InsertNext(Once());
                        }
                        return StepOutcome.Done;
                    }
                    // The gil may go a frame or two after the item comes: waited for (1.5 s), so it's counted.
                    if (ErrandGame.Gil() >= gilBefore && GilLate(ref boardArrived)) return StepOutcome.Waiting;
                    boardArrived = null;
                    var now = ErrandGame.BagCount(item.ItemId);
                    BoardListings.Gone(listing);
                    var spent = Math.Max(0, gilBefore - ErrandGame.Gil());
                    MarketSpent += spent;
                    itemSpent += spent;
                    Count(item.ItemId, now - before);
                    bought += now - before;
                    if (spent > cost + 1) return StepOutcome.Halt($"That listing cost {spent:N0} gil, more than the {cost:N0} it showed.");
                    if (MarketSpent > ceiling) return StepOutcome.Halt($"The market board's spending ({MarketSpent:N0} gil) went past the ceiling.");
                    if (bought < wanted) queue.InsertNext(Once());
                    return StepOutcome.Done;
                },
                Timeout = TimeSpan.FromSeconds(15),
                TimeoutMessage = $"{name} didn't come to your bags (sold to someone else first?).",
            },
        };
        var steps = SearchSteps(item.ItemId, name);
        steps.AddRange(Once());
        return steps;
    }

    /// <summary>How long a board search's results take to come (the server's, so not the pace's).</summary>
    private static readonly TimeSpan BoardResultsWait = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Opens an item's listings: the board searched for its name (as typing it does), its row found
    /// in the results, and opened; what opens is checked to be that item's before anything is bought.
    /// Whatever doesn't work is left to you (search and open it), as before.
    /// </summary>
    private List<Step> SearchSteps(uint itemId, string name)
    {
        var index = -1;
        var picks = 0;
        var searches = 1;
        var pickedAt = DateTime.MinValue;
        DateTime? redoAt = null;
        DateTime? searchedAt = null;
        // The listings window open for the item, and its listings come (or none, 6 s after it opened:
        // an item nobody sells there has none to send).
        bool Opened() => GameUi.IsOpen(ErrandTable.ItemSearchResult)
                         && (ErrandGame.Listings(itemId) != null || (DateTime.UtcNow - BoardListings.AskedAt(itemId)).TotalSeconds > 10);
        // Handed to you: the listings you open are expected (an empty answer then reads as none listed).
        Step YouOpen(string what)
        {
            BoardListings.Expect(itemId);
            return You($"{what} {name} and open its listings", Opened);
        }
        return new List<Step>
        {
            Step.Act("Close the last item's listings", () =>
            {
                if (GameUi.IsOpen(ErrandTable.ItemSearchResult) && ErrandGame.ListingsItem() != itemId) GameUi.Close(ErrandTable.ItemSearchResult);
                return StepOutcome.Done;
            }),
            Step.Wait("The board's search", () => Opened() || (GameUi.IsOpen(ErrandTable.ItemSearch) && !GameUi.IsOpen(ErrandTable.ItemSearchResult)), WindowWait, StepStatus.Done),
            new Step
            {
                Label = $"Search the board for \"{name}\" (ItemSearch ← [i:{ErrandTable.SearchByWords}, empty, empty, s:words, s:words, empty, empty, empty])",
                IsAction = true,
                Run = () =>
                {
                    if (Opened()) return StepOutcome.Done;
                    if (name.Length == 0 || GameUi.IsOpen(ErrandTable.ItemSearchResult)
                        || !GameUi.FireValues(ErrandTable.ItemSearch, true, ErrandTable.SearchByWords, null, null, name, name, null, null, null))
                    {
                        queue.InsertNext(new[] { YouOpen("Search the board for") });
                        return StepOutcome.Done;
                    }
                    searchedAt = DateTime.UtcNow;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Find {name} in the results",
                Run = () =>
                {
                    if (Opened() || searchedAt == null) return StepOutcome.Done;
                    index = ErrandGame.SearchResultIndex(name, out var seen);
                    if (index >= 0)
                    {
                        log.Info($"  {name} is result {index + 1} of the search ({string.Join(" | ", seen)}).");
                        return StepOutcome.Done;
                    }
                    // The results come from the server a moment after the search, whatever the pace: 1.5 s (0.8.0
                    // waited 1 s at Quick, and a quarter of the picks went before the results and were lost).
                    if (DateTime.UtcNow - searchedAt.Value < BoardResultsWait) return StepOutcome.Waiting;
                    // The results' names aren't in the window's values (0.7.6's log): searched by its full
                    // name, the item is the first result (as you opened it, [i:5, i:0]); whose listings
                    // open is checked next, and another item's are closed and left to you.
                    index = 0;
                    log.Info($"  {name}'s place in the results can't be read{(seen.Count == 0 ? string.Empty : $" ({string.Join(" | ", seen)})")}: the first result is opened, and checked.");
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Open {name}'s listings ({ErrandTable.SearchResultPick.Describe()})",
                IsAction = true,
                Run = () =>
                {
                    if (Opened()) return StepOutcome.Done;
                    if (index < 0) index = 0;
                    BoardListings.Expect(itemId);
                    if (!GameUi.Fire(ErrandTable.SearchResultPick, UiCallValues.Of(("index", index))))
                        queue.InsertNext(new[] { YouOpen("Search the board for") });
                    pickedAt = DateTime.UtcNow;
                    picks = 1;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"{name}'s listings come",
                Run = () =>
                {
                    if (Opened()) return StepOutcome.Done;
                    var now = DateTime.UtcNow;
                    var resultOpen = GameUi.IsOpen(ErrandTable.ItemSearchResult);
                    // The window's title is the item's name ([2] in it); the proxy's item when it isn't there yet.
                    var title = resultOpen && GameUi.TextValues(ErrandTable.ItemSearchResult).TryGetValue(2, out var t) ? t.Trim() : null;
                    var other = resultOpen ? ErrandGame.ListingsItem() : 0;
                    var wrong = resultOpen && ((title is { Length: > 0 } && !string.Equals(title, name, StringComparison.OrdinalIgnoreCase))
                                               || (other != 0 && other != itemId));
                    if (wrong)
                    {
                        // The previous search's results were still showing (0.7.13: result 1 opened the last
                        // item): closed, and the search made again, twice, before it's left to you.
                        GameUi.Close(ErrandTable.ItemSearchResult);
                        if (searches >= 3)
                        {
                            log.Info($"  Result {index + 1} opened another item's listings ({title ?? Name(other)}) again: you open {name}'s.");
                            queue.InsertNext(new[] { YouOpen("In the board's results, find") });
                            return StepOutcome.Done;
                        }
                        log.Info($"  Result {index + 1} opened another item's listings ({title ?? Name(other)}): the search's results hadn't come yet. Searching again.");
                        redoAt = now + TimeSpan.FromMilliseconds(600);
                        return StepOutcome.Waiting;
                    }
                    if (redoAt is { } r && now >= r)
                    {
                        // The search again, then (once its results have had time) the first result.
                        redoAt = null;
                        searches++;
                        if (!GameUi.FireValues(ErrandTable.ItemSearch, true, ErrandTable.SearchByWords, null, null, name, name, null, null, null))
                        {
                            queue.InsertNext(new[] { YouOpen("Search the board for") });
                            return StepOutcome.Done;
                        }
                        pickedAt = now + BoardResultsWait;
                        picks = 0;
                        return StepOutcome.Waiting;
                    }
                    if (redoAt != null || resultOpen) return StepOutcome.Waiting;
                    // Nothing opened: the pick went before the results were there (0.7.13: three items
                    // waited 15 s and were left). Picked again every 1.2 s, three times, then searched again.
                    if (now < pickedAt + TimeSpan.FromMilliseconds(picks == 0 ? 0 : 1200)) return StepOutcome.Waiting;
                    if (picks < 3 && GameUi.IsOpen(ErrandTable.ItemSearch))
                    {
                        if (picks > 0) log.Info($"  {name}'s listings didn't open: picking result {index + 1} again.");
                        BoardListings.Expect(itemId);
                        GameUi.Fire(ErrandTable.SearchResultPick, UiCallValues.Of(("index", index)));
                        picks++;
                        pickedAt = now;
                        return StepOutcome.Waiting;
                    }
                    if (searches < 3 && GameUi.IsOpen(ErrandTable.ItemSearch))
                    {
                        redoAt = now;
                        return StepOutcome.Waiting;
                    }
                    log.Info($"  {name}'s listings didn't open after {searches} searches: you open them.");
                    queue.InsertNext(new[] { YouOpen("In the board's results, find") });
                    return StepOutcome.Done;
                },
                Timeout = TimeSpan.FromSeconds(30),
                TimeoutMessage = $"{name}'s listings didn't come.",
            },
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Home, in, placing, and what's next.

    private List<Step> GoHomeSteps() => GoHomeSteps(leave: false);

    /// <summary>
    /// Home (Lifestream: the estate's own teleport, which lands outside its door). With
    /// <paramref name="leave"/>, from inside too, to be out in the ward; if the teleport doesn't take
    /// you out, you're asked to step outside.
    /// </summary>
    private List<Step> GoHomeSteps(bool leave)
    {
        var steps = new List<Step>();
        if (ErrandGame.CurrentWorld() != homeWorld) steps.AddRange(WorldSteps(homeWorld));
        // Home: inside already, or teleported to its door (even from elsewhere in the ward, so the door
        // found after is yours, not a neighbour's). Out (for your district's vendors): in the ward.
        var asked = DateTime.MinValue;
        var went = false;
        var skipped = false;
        var explained = false;
        steps.Add(Step.Act(leave ? "Go out to your ward (Lifestream, your home's teleport)" : "Go home (Lifestream)", () =>
        {
            skipped = leave ? ErrandGame.InHomeWard(home!) : Inside();
            if (skipped) return StepOutcome.Done;
            travelling = true;
            went = false;
            asked = DateTime.UtcNow;
            return Travel.GoHome(homeKind) ? StepOutcome.Done : StepOutcome.Halt("Lifestream wouldn't take you home (is it installed and up to date?).");
        }));
        steps.Add(new Step
        {
            Label = leave ? "Arrive outside your home" : "Arrive home",
            Run = () =>
            {
                if (skipped) return StepOutcome.Done;
                went |= ErrandGame.Moving || Travel.Busy;
                if (!went && DateTime.UtcNow - asked > TimeSpan.FromSeconds(15))
                {
                    if (!leave || !Inside()) return StepOutcome.Halt("Lifestream didn't take you home (it may need your home's address: see its settings).");
                    // Lifestream didn't teleport from inside the home it goes to: you step out.
                    queue.InsertNext(new[] { Arriving(You("Step outside your home", () => ErrandGame.InHomeWard(home!))) });
                    return StepOutcome.Done;
                }
                var there = ErrandGame.InHomeWard(home!) || (!leave && Inside());
                if (!explained && went && !there && ErrandGame.Settled && !Travel.Busy && DateTime.UtcNow - asked > TimeSpan.FromSeconds(20))
                {
                    // Arrived somewhere that doesn't read as home: said once, with what was compared.
                    explained = true;
                    log.Warn($"  Still not home after the teleport ({ErrandGame.WardCheck(home!)}{(leave ? string.Empty : $"; inside {here.HouseId}, your home {home!.HouseId}")}).");
                }
                if (!went || !there || !ErrandGame.Settled || Travel.Busy) return StepOutcome.Waiting;
                travelling = false;
                lastTerritory = ErrandGame.Territory;
                return StepOutcome.Done;
            },
            Timeout = TravelWait,
            TimeoutMessage = "Didn't get home within 2 minutes (Lifestream may need your home's address: see its settings).",
        });
        return steps;
    }

    /// <summary>Inside your home this frame (from where you are as read each frame).</summary>
    private bool Inside() => home != null && ErrandGame.InsideHome(home, here);

    private List<Step> EnterSteps() => EnterSteps(looked: false);

    private List<Step> EnterSteps(bool looked)
    {
        if (Inside()) return ReadAgainSteps();
        // The door may load a moment after you arrive: looked for again for up to 5 s before it's
        // called missing (it was looked for once, the moment you arrived).
        if (!looked && homeKind != HomeKind.Apartment && !(home is { Chamber: true } && ErrandGame.InHouseOf(home))
            && (ErrandGame.Me is not { } at || ErrandGame.Nearest(ErrandTable.EntranceWords, at, 30, UsableKinds) == null))
            return new List<Step>
            {
                Step.Wait("Your door comes into sight", () => ErrandGame.Me is { } p && ErrandGame.Nearest(ErrandTable.EntranceWords, p, 30, UsableKinds) != null,
                    TimeSpan.FromSeconds(5), StepStatus.Done),
                Step.Act("Go in", () =>
                {
                    queue.InsertNext(EnterSteps(looked: true));
                    return StepOutcome.Done;
                }),
            };
        // In the hall of the house your chambers are in already: up to them.
        if (home is { Chamber: true } && ErrandGame.InHouseOf(home)) return ChamberSteps().Concat(ReadAgainSteps()).ToList();
        var steps = new List<Step>();
        Vector3? door = null;
        Vector3? spot = null;
        DoorWay? way = null;
        if (homeKind != HomeKind.Apartment && ErrandGame.Me is { } me && ErrandGame.Nearest(ErrandTable.EntranceWords, me, 30, UsableKinds) is { } entrance)
        {
            door = entrance.Position;
            spot = ErrandGame.OutsideOf(entrance);
            way = DoorWays.For(config.ErrandsDoorWays, ErrandGame.Territory, entrance.Position);
            log.Info(way != null
                ? $"  Your door is at ({entrance.Position.X:0.0}, {entrance.Position.Z:0.0}); following the way you recorded to it ({DoorWays.Length(way.Spots()):0} yalms)."
                : $"  Your door is at ({entrance.Position.X:0.0}, {entrance.Position.Z:0.0}), facing {entrance.Rotation * 180 / MathF.PI:0}°; walking to ({spot.Value.X:0.0}, {spot.Value.Z:0.0}), {ErrandGame.DistanceTo(spot.Value):0} yalms away. "
                  + "No way to it is recorded: vnavmesh's map of a ward has no houses in it, so its way may run into walls (Settings → Getting → Way to your door records yours).");
        }
        // Inside (your home, or the hall your chambers are in): what going in waits for.
        bool In() => Inside() || (home is { Chamber: true } && ErrandGame.InHouseOf(home));
        if (door == null)
        {
            if (homeKind != HomeKind.Apartment) log.Warn($"  Couldn't find your door. Near you: {ErrandGame.NamesNear(30)}.");
            steps.Add(Arriving(You("Go inside your home", Inside)));
        }
        else
        {
            // Handed to you when the way there fails: you walk in, and the errands go on once you're in.
            var handed = false;
            void HandOver(string why)
            {
                handed = true;
                Nav.Stop();
                walkTarget = null;
                followWay = null;
                log.Warn($"  {why} Walk to your door and go in; the errands carry on once you're inside{(way == null ? " (recording the way once, in Settings → Getting, saves this next time)" : string.Empty)}.");
                queue.InsertNext(new[] { Arriving(You("Walk to your door and go in", In)) });
            }
            if (way == null)
            {
                // No way known to this door yet (0.8.0): you walk it once, and it's remembered as you go;
                // vnavmesh's own way runs into the walls (its map of a ward has no houses in it).
                steps.Add(Step.Act("Remember the way to your door as you walk it", () =>
                {
                    var why = StartRecordingWay(errands: true);
                    log.Info(why == null
                        ? "  This once, walk from here to your door and go in: the way is remembered, and from then on the errands walk it themselves."
                        : $"  Walk to your door and go in (the way couldn't be remembered: {why}).");
                    return StepOutcome.Done;
                }));
                steps.Add(Arriving(You("Walk to your door and go in (this once: the way is remembered)", In)));
                if (home is { Chamber: true }) steps.AddRange(ChamberSteps());
                steps.AddRange(ReadAgainSteps());
                return steps;
            }
            var end = way?.End ?? spot!.Value;
            steps.AddRange(ApproachSteps(way != null, end, door.Value, HandOver, () => handed, () =>
            {
                if (ErrandGame.Me is not { } p) return false;
                if (way == null) return Nav.MoveTo(end);
                var spots = way.Spots();
                var (rest, off) = DoorWays.From(spots, p);
                if (off > 15) log.Warn($"  You're {off:0} yalms from the way you recorded (the teleport landed elsewhere?); going straight to its nearest spot.");
                followWay = spots;
                return rest.Count == 0 || Nav.Follow(rest);
            }));
            // On your feet and landed, then the door; used again if its question doesn't come (0.7.11:
            // "Unable to execute command while jumping", and nothing more).
            var uses = 0;
            var usedAt = DateTime.MinValue;
            steps.Add(new Step
            {
                Label = "Use your door",
                IsAction = true,
                Timeout = TimeSpan.FromSeconds(15),
                TimeoutMessage = "Your door didn't ask to go in.",
                Run = () =>
                {
                    if (handed || In() || GameUi.YesNoText() != null) return StepOutcome.Done;
                    if (Movement.Mounted)
                    {
                        Movement.GetDown(log.Info);
                        return StepOutcome.Waiting;
                    }
                    if (!Movement.Landed || (uses > 0 && DateTime.UtcNow - usedAt < TimeSpan.FromSeconds(2.5))) return StepOutcome.Waiting;
                    if (uses >= 3) return StepOutcome.Halt("Your door didn't ask to go in (used 3 times).");
                    if (ErrandGame.Me is not { } p || ErrandGame.Nearest(ErrandTable.EntranceWords, p, Reach + 2, UsableKinds) is not { } o)
                    {
                        HandOver($"Couldn't reach your door from here ({ErrandGame.DistanceTo(door.Value):0} yalms away).");
                        return StepOutcome.Done;
                    }
                    travelling = true;
                    if (uses > 0) log.Info("  The door didn't ask yet: using it again.");
                    uses++;
                    usedAt = DateTime.UtcNow;
                    if (!ErrandGame.Interact(o)) log.Info("  The game didn't take using the door.");
                    return StepOutcome.Waiting;
                },
            });
            steps.Add(new Step
            {
                Label = "Answer yes (go in)",
                IsAction = true,
                Run = () =>
                {
                    if (handed || In() || GameUi.YesNoText() is not { } question) return StepOutcome.Done;
                    // Only the door's own question ("Enter the estate hall?").
                    if (!ErrandTable.EnterWords.Any(w => question.TrimStart().StartsWith(w, StringComparison.OrdinalIgnoreCase)))
                        return StepOutcome.Halt($"The game asked \"{question}\", not to go in: left for you.");
                    // Going in is part of the trip, in a rehearsal too: the guard lets this one Yes through.
                    using (Rehearsals.Allow())
                        return GameUi.Fire(UiTable.YesNoYes) ? StepOutcome.Done : StepOutcome.Halt("The yes/no closed.");
                },
            });
            steps.Add(Arriving(home is { Chamber: true }
                ? WaitFor("Inside the house", In, TimeSpan.FromSeconds(30), "Didn't get inside the house.")
                : WaitFor("Inside your home", Inside, TimeSpan.FromSeconds(30), "Didn't get inside your home.")));
            // A way recorded before ways knew their plot learns it once it's taken you in.
            if (way != null)
                steps.Add(Step.Act("Note the way's plot", () =>
                {
                    if (!handed) LearnWayPlot(way, home);
                    return StepOutcome.Done;
                }));
        }
        // Private chambers: the door goes into the house's hall; then up to the room.
        if (home is { Chamber: true }) steps.AddRange(ChamberSteps());
        steps.AddRange(ReadAgainSteps());
        return steps;
    }

    /// <summary>
    /// To your front door from outside: along the way you recorded (<paramref name="recorded"/>), else
    /// vnavmesh's own way to the spot out front. Stuck (no nearer for 4 seconds) or stopped short: once
    /// more, then handed to you (<paramref name="handOver"/>), not stopped. Close enough to use the door
    /// counts as there.
    /// </summary>
    private List<Step> ApproachSteps(bool recorded, Vector3 end, Vector3 door, Action<string> handOver, Func<bool> handed, Func<bool> go)
    {
        var tries = 0;
        var started = DateTime.MinValue;
        var best = float.MaxValue;
        var bestAt = DateTime.MinValue;
        var sprintTried = DateTime.MinValue;
        var meshAsked = DateTime.MinValue;
        bool Start()
        {
            tries++;
            started = bestAt = DateTime.UtcNow;
            best = ErrandGame.DistanceTo(end);
            return go();
        }
        return new List<Step>
        {
            new Step
            {
                Label = recorded ? "Follow your way to the door (vnavmesh)" : "Walk to your door (vnavmesh)",
                IsAction = true,
                Timeout = TimeSpan.FromSeconds(35), // its own 30 s for the mesh decide
                Run = () =>
                {
                    if (handed() || ErrandGame.DistanceTo(end) <= 1f) return StepOutcome.Done;
                    // vnavmesh's map only matters to its own way (a recorded one is walked as it is).
                    if (!recorded && !Nav.Ready)
                    {
                        if (meshAsked == DateTime.MinValue) meshAsked = DateTime.UtcNow;
                        if (DateTime.UtcNow - meshAsked < TimeSpan.FromSeconds(30)) return StepOutcome.Waiting;
                        handOver("vnavmesh has no map of the ward.");
                        return StepOutcome.Done;
                    }
                    if (!Start())
                    {
                        handOver("vnavmesh wouldn't walk to your door.");
                        return StepOutcome.Done;
                    }
                    Movement.TrySprint(log.Info);
                    sprintTried = DateTime.UtcNow;
                    if (!recorded) walkTarget = end;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = "Arrive at your door",
                Timeout = TimeSpan.FromSeconds(90),
                OnTimeout = StepStatus.Done, // the door's step decides (handed to you if it's out of reach)
                Run = () =>
                {
                    if (handed() || started == DateTime.MinValue) return StepOutcome.Done;
                    var d = ErrandGame.DistanceTo(end);
                    if (d <= 1f)
                    {
                        Nav.Stop();
                        walkTarget = null;
                        followWay = null;
                        return StepOutcome.Done;
                    }
                    if (d < best - 0.5f)
                    {
                        best = d;
                        bestAt = DateTime.UtcNow;
                    }
                    if (!Movement.Mounted && d > 15 && DateTime.UtcNow - sprintTried > TimeSpan.FromSeconds(3))
                    {
                        sprintTried = DateTime.UtcNow;
                        Movement.TrySprint(log.Info);
                    }
                    var stuck = DateTime.UtcNow - bestAt > TimeSpan.FromSeconds(4);
                    if ((Nav.Busy && !stuck) || DateTime.UtcNow - started < TimeSpan.FromSeconds(2)) return StepOutcome.Waiting;
                    Nav.Stop();
                    // Stopped (or stuck) within reach of the door: near enough to use it.
                    if (ErrandGame.DistanceTo(door) <= Reach + 1)
                    {
                        walkTarget = null;
                        followWay = null;
                        return StepOutcome.Done;
                    }
                    if (tries >= 2)
                    {
                        handOver($"{(stuck ? "Got stuck" : "Stopped")} {ErrandGame.DistanceTo(door):0} yalms from your door{(recorded ? " on the way you recorded" : " (vnavmesh's way)")}.");
                        return StepOutcome.Done;
                    }
                    log.Info($"  {(stuck ? "Stuck" : "Stopped")} {d:0} yalms short of your door: trying once more.");
                    if (!Start())
                    {
                        handOver("vnavmesh wouldn't walk to your door.");
                        return StepOutcome.Done;
                    }
                    return StepOutcome.Waiting;
                },
            },
        };
    }

    /// <summary>
    /// From the company's hall up to your private chambers, as 0.7.2's recording did by hand: the door
    /// to the chambers (found by name: <see cref="ErrandTable.ChamberDoorWords"/>), walked to and used;
    /// its menu's "Move to your private chambers"; yes to "Retire to your own chambers?". Anything not
    /// as expected (no door, no way there, another menu or question) hands the rest to you: you walk up,
    /// and the errands go on once you're in.
    /// </summary>
    private List<Step> ChamberSteps()
    {
        var handed = false;
        var answered = false;
        Vector3? door = null;
        Vector3? spot = null;
        Vector3? closer = null;
        var navAsked = DateTime.MinValue;
        var walking = DateTime.MinValue;
        var usedAt = DateTime.MinValue;
        var approachAt = DateTime.MinValue;
        var stepped = false;
        var approaching = false;
        bool Up() => handed || Inside();
        Dalamud.Game.ClientState.Objects.Types.IGameObject? Door(float within) =>
            ErrandGame.Me is { } p ? ErrandGame.NearestContaining(ErrandTable.ChamberDoorWords, p, within, UsableKinds) : null;
        StepOutcome HandOver(string why)
        {
            handed = true;
            Nav.Stop();
            log.Warn($"  {why} Going up to your chambers is left for you.");
            return StepOutcome.Done;
        }
        return new List<Step>
        {
            Step.Act("Find the door to your private chambers", () =>
            {
                if (Up()) return StepOutcome.Done;
                if (ErrandGame.Me is null) return HandOver("Couldn't read where you are.");
                if (Door(80) is not { } d) return HandOver($"Couldn't find the door to the chambers. Near you: {ErrandGame.NamesNear(40)}.");
                door = d.Position;
                // Straight out in front of it (its facing), close: 0.7.9 stopped 4 yalms off, still
                // running, and the game said "Unable to execute action from this location".
                spot = ErrandGame.InFrontOf(d, 1.5f);
                closer = ErrandGame.InFrontOf(d, 0.8f);
                return StepOutcome.Done;
            }),
            new Step
            {
                Label = "Walk to it (vnavmesh)",
                IsAction = true,
                Timeout = TimeSpan.FromSeconds(25), // its own 20 s for the mesh decide
                Run = () =>
                {
                    if (Up() || spot == null) return StepOutcome.Done;
                    if (ErrandGame.DistanceTo(spot.Value) <= 0.8f) return StepOutcome.Done;
                    if (navAsked == DateTime.MinValue) navAsked = DateTime.UtcNow;
                    if (!Nav.Ready)
                        return DateTime.UtcNow - navAsked > TimeSpan.FromSeconds(20) ? HandOver("vnavmesh has no map of the hall.") : StepOutcome.Waiting;
                    if (!Nav.MoveTo(spot.Value)) return HandOver("vnavmesh wouldn't walk to the door.");
                    Movement.TrySprint(log.Info);
                    walkTarget = spot;
                    walking = DateTime.UtcNow;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = "Arrive at the door",
                Timeout = TimeSpan.FromSeconds(60),
                OnTimeout = StepStatus.Done, // decided next
                Run = () =>
                {
                    if (Up() || spot == null || door == null) return StepOutcome.Done;
                    // All the way to the spot (not "near the door"), then stopped.
                    if (ErrandGame.DistanceTo(spot.Value) <= 0.8f)
                    {
                        Nav.Stop();
                        walkTarget = null;
                        return StepOutcome.Done;
                    }
                    if (Nav.Busy || DateTime.UtcNow - walking < TimeSpan.FromSeconds(2)) return StepOutcome.Waiting;
                    walkTarget = null;
                    // Stopped short: the door may still answer (tried, and a step closer if it doesn't).
                    if (ErrandGame.DistanceTo(door.Value) <= 8) return StepOutcome.Done;
                    return HandOver($"vnavmesh stopped {ErrandGame.DistanceTo(door.Value):0} yalms from the door.");
                },
            },
            new Step
            {
                Label = "Use the door",
                IsAction = true,
                Run = () =>
                {
                    walkTarget = null;
                    if (Up()) return StepOutcome.Done;
                    if (Door(8) is not { } o) return HandOver("The door isn't within reach.");
                    usedAt = DateTime.UtcNow;
                    return ErrandGame.Interact(o) ? StepOutcome.Done : HandOver("Couldn't use the door.");
                },
                Delay = TimeSpan.FromMilliseconds(300), // standing still first
            },
            new Step
            {
                // The menu, or (once) a step closer and the door again: the game says "Unable to execute
                // action from this location" when it's out of reach.
                Label = "Its menu opens",
                Timeout = TimeSpan.FromSeconds(12),
                OnTimeout = StepStatus.Done, // the next step says it didn't
                Run = () =>
                {
                    if (Up() || ErrandGame.MenuEntries() != null) return StepOutcome.Done;
                    var now = DateTime.UtcNow;
                    if (approaching)
                    {
                        if (closer is { } c && ErrandGame.DistanceTo(c) > 0.6f && (Nav.Busy || now - approachAt < TimeSpan.FromSeconds(1.5))) return StepOutcome.Waiting;
                        Nav.Stop();
                        walkTarget = null;
                        approaching = false;
                        usedAt = now;
                        if (Door(8) is { } o) ErrandGame.Interact(o);
                        return StepOutcome.Waiting;
                    }
                    if (now - usedAt < TimeSpan.FromSeconds(2.5)) return StepOutcome.Waiting;
                    if (stepped || closer == null) return StepOutcome.Done;
                    stepped = true;
                    approaching = true;
                    approachAt = now;
                    log.Info("  The door didn't answer from here: a step closer, and again.");
                    if (Nav.MoveTo(closer.Value)) walkTarget = closer;
                    return StepOutcome.Waiting;
                },
            },
            new Step
            {
                Label = $"Choose \"{ErrandTable.ChamberMenuWords[0]}\"",
                IsAction = true,
                Run = () =>
                {
                    if (Up()) return StepOutcome.Done;
                    if (ErrandGame.MenuEntries() is not { } menu) return HandOver("The door's menu didn't open.");
                    var i = ErrandGame.Find(menu.Entries, ErrandTable.ChamberMenuWords);
                    if (i < 0)
                    {
                        GameUi.Close(menu.Addon);
                        return HandOver($"The door's menu has no \"{ErrandTable.ChamberMenuWords[0]}\" (it shows: {string.Join(" | ", menu.Entries)}).");
                    }
                    var call = menu.Addon == ErrandTable.SelectIconString ? ErrandTable.IconMenuPick : ErrandTable.MenuPick;
                    return GameUi.Fire(call, UiCallValues.Of(("index", i))) ? StepOutcome.Done : HandOver("The door's menu closed.");
                },
            },
            // The direct entry takes you in with no question (0.7.4's recording); a question that does come
            // is answered yes only if it's the chambers' own.
            Arriving(new Step
            {
                Label = "Go up to your chambers",
                Timeout = TimeSpan.FromSeconds(30),
                OnTimeout = StepStatus.Done, // the next step waits for you
                Run = () =>
                {
                    if (Up()) return StepOutcome.Done;
                    if (answered || GameUi.YesNoText() is not { } question) return StepOutcome.Waiting;
                    if (!ErrandTable.ChamberYesWords.Any(w => question.TrimStart().StartsWith(w, StringComparison.OrdinalIgnoreCase)))
                        return HandOver($"The game asked \"{question}\".");
                    // Going up is part of the trip, in a rehearsal too: the guard lets this one Yes through.
                    using (Rehearsals.Allow())
                        if (!GameUi.Fire(UiTable.YesNoYes)) return HandOver("The yes/no closed.");
                    answered = true;
                    return StepOutcome.Waiting;
                },
            }),
            // Up already (the usual): nothing for you to do, and nothing said (0.7.12 logged "You: Go up…, done
            // after 0 s" every trip). Not up: you're asked to.
            new Step
            {
                Label = "In your chambers",
                Run = () =>
                {
                    if (!Inside())
                        queue.InsertNext(new[] { Arriving(You("Go up to your private chambers (the hall's door to them, then \"Move to your private chambers\")", Inside)) });
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>A wait that ends a zone change: travel is over once it's done.</summary>
    private Step Arriving(Step wait) => new()
    {
        Label = wait.Label,
        Timeout = wait.Timeout,
        TimeoutMessage = wait.TimeoutMessage,
        OnTimeout = wait.OnTimeout,
        Run = () =>
        {
            travelling = true;
            var r = wait.Run();
            if (r.Status == StepStatus.Done && ErrandGame.Settled)
            {
                travelling = false;
                lastTerritory = ErrandGame.Territory;
                readAfter = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
                return r;
            }
            return r.Status == StepStatus.Done ? StepOutcome.Waiting : r;
        },
    };

    /// <summary>Waits until the home has been read and matched again since you came in (or placed).</summary>
    private List<Step> ReadAgainSteps() => new()
    {
        new Step
        {
            Label = "Your home is read again",
            Run = () =>
            {
                if (readAfter < DateTime.UtcNow - TimeSpan.FromMinutes(1)) readAfter = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
                if (!(session.ReadAt > readAfter && session.Plan != null && session.Context.HouseId == home!.HouseId)) return StepOutcome.Waiting;
                // Inside, the storeroom too: read before it had come in, its pieces counted as missing and
                // weren't placed (0.8.5's chamber: "0 from the storeroom"). Given up on after 15 s.
                if (session.PlanSide == Side.Interior && !session.StoreroomLoaded)
                {
                    if (DateTime.UtcNow - readAfter < TimeSpan.FromSeconds(15)) return StepOutcome.Waiting;
                    log.Warn("  The storeroom hasn't come in after 15 s: its pieces are left for a later run (open Housing → Indoor Furnishings once, then start placing from Ready).");
                }
                return StepOutcome.Done;
            },
            Timeout = TimeSpan.FromSeconds(45),
            TimeoutMessage = "Your home couldn't be read again (open Housing → Indoor Furnishings once, and start again).",
        },
    };

    private List<Step> PlaceSteps() => new()
    {
        Step.Act("Start placing (Housing Studio's own run)", () =>
        {
            if (session.Plan == null) return StepOutcome.Halt("Your home isn't matched against the design.");
            var entries = session.Plan.Entries.Where(e => config.FloorShown(e.Target.Floor)).ToList();
            // Ready's Clear out or Swap, on the first placing only: the pieces chosen, as far as they're still here.
            IReadOnlyList<PlacedPiece>? clear = null;
            if (clearOutFirst != null)
            {
                clear = ClearOut.StillHere(clearOutFirst, session.Plan.Extras);
                if (clear.Count < clearOutFirst.Count)
                    log.Warn($"  {clearOutFirst.Count - clear.Count} of the {clearOutFirst.Count} pieces to clear out aren't where they were: they're left.");
                if (clear.Count == 0) clear = null;
            }
            var keepAs = keepAsFirst;
            if (clear != null || keepAs != null)
                log.Info(keepAs != null ? $"  Swapping: what's here is kept as “{keepAs}”, {clear?.Count ?? 0} pieces put away first." : $"  Clearing out {clear!.Count} pieces first.");
            LetsPlace = true;
            if (Rehearsal) runner.Rehearse(entries, clear, keepAs);
            else runner.Start(false, entries, clear, keepAs);
            if (runner.Active)
            {
                clearOutFirst = null;
                keepAsFirst = null;
                return StepOutcome.Done;
            }
            LetsPlace = false;
            var why = runner.StopReason ?? "it didn't say why";
            if (why.StartsWith("Nothing to do", StringComparison.Ordinal))
            {
                log.Info("  Nothing to place from your bags.");
                return StepOutcome.Done;
            }
            return StepOutcome.Halt($"Placing didn't start: {why}");
        }),
        new Step
        {
            Label = "Placing",
            Run = () =>
            {
                if (LetsPlace && runner.Active) return StepOutcome.Waiting;
                var stopped = LetsPlace && runner.State == RunState.Stopped;
                LetsPlace = false;
                readAfter = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
                return stopped ? StepOutcome.Halt($"Placing stopped: {runner.StopReason}") : StepOutcome.Done;
            },
            Timeout = PlaceWait,
        },
    };

    private List<Step> ReplanSteps()
    {
        var steps = ReadAgainSteps();
        steps.Add(new Step
        {
            Label = "Work out what's still missing",
            Run = () =>
            {
                var ctx = session.Context;
                var shopping = shop.Plan(session.Plan, session.MissingDyes(), ctx.District ?? session.Layout?.District);
                var next = Estimate(shopping, home);
                Plan = next;
                log.Info($"  Still to get: {next.Count} in {Words(next.Trips.Count, "trip")}, about {next.TotalGil:N0} gil; {next.Left.Sum(l => l.Count)} left for you.");
                if (next.IsEmpty)
                {
                    finishPending = true;
                    return StepOutcome.Done;
                }
                if (gotThisTrip == 0 && emptiedThisTrip == 0) return StepOutcome.Halt("The last trip got nothing, so something's wrong: the log says what each step saw.");
                if (cycles >= tripsAtStart + 3) return StepOutcome.Halt($"That's {cycles} trips, more than the {tripsAtStart} planned: stopped to be safe.");
                var trip = next.Trips[0];
                if (Spent + trip.Gil > agreedGil)
                    return StepOutcome.Halt($"The next trip would bring the gil spent to {Spent + trip.Gil:N0}, more than the {agreedGil:N0} you agreed to. Look at the estimate again and start again.");
                pendingTrip = trip;
                return StepOutcome.Done;
            },
            Timeout = TimeSpan.FromSeconds(5),
        });
        return steps;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>Ends the segment early, as done (nothing more to do for it).</summary>
    private StepOutcome Finished()
    {
        queue.FinishAfterThis();
        return StepOutcome.Done;
    }

    private void Count(uint itemId, int n)
    {
        if (n <= 0) return;
        got[itemId] = got.GetValueOrDefault(itemId) + n;
        gotThisTrip += n;
    }

    private string Name(uint itemId)
    {
        var n = ErrandGame.ItemName(itemId);
        return n.Length > 0 ? n : data.AnyItemName(itemId);
    }

    private string Thing(ErrandItem item) => item.Count == 1 ? Name(item.ItemId) : $"{item.Count} × {Name(item.ItemId)}";

    public void Dispose()
    {
        Stop("The plugin was unloaded.");
        Rehearsals.Unregister(rehearsing);
        shop.AutoRetainer.LetBe();
    }
}
