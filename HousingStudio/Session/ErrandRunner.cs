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
public sealed class ErrandRunner : IDisposable
{
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan YouWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan WalkWait = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan TravelWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan WorldWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PlaceWait = TimeSpan.FromHours(2);

    /// <summary>How close is close enough to talk to someone or use something.</summary>
    private const float Reach = 3f;

    /// <summary>A gil shop sells at most this many in one purchase.</summary>
    private const int MaxPerPurchase = 99;

    private const string Guess = "guess";

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly Shopping shop;
    private readonly StepQueue queue = new(() => DateTime.UtcNow);

    private readonly List<(string Title, Func<List<Step>> Steps, Func<List<Step>> Cleanup)> segments = new();
    private int segment = -1;
    private ErrandTrip? pendingTrip;
    private bool finishPending;

    private HomeRef? home;
    private HomeKind homeKind;
    private string homeWorld = string.Empty;
    private bool houseBell;
    private long agreedGil;
    private long ceiling;
    private int tripsAtStart;
    private int cycles;
    private int gotThisTrip;
    private bool travelling;
    private uint lastTerritory;
    private DateTime? pausedAt;
    private Vector3? walkTarget;
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
        queue.StepsPerTick = 4;
        queue.Trace += (step, what) =>
        {
            if (what == "start" && step.Label.StartsWith("You:", StringComparison.Ordinal)) log.Info($"  … {step.Label}");
            else if (what != "start" && what != "done") log.Warn($"  ✕ {what}");
        };
        queue.Timed += (step, took) =>
        {
            if (step.Label.StartsWith("You:", StringComparison.Ordinal)) log.Info($"  … done, after {took.TotalSeconds:0.#} s.");
            else log.Info($"  {(step.IsAction ? "→" : "…")} {step.Label} ({took.TotalMilliseconds:0} ms)");
        };
    }

    public Vnavmesh Nav { get; } = new();
    public Lifestream Travel { get; } = new();

    /// <summary>Another run (setting fixtures): errands don't start while it goes.</summary>
    public Func<bool>? OtherRunActive { get; set; }

    // ---------------------------------------------------------------------------------------------
    // What the window shows.

    /// <summary>Settings → Experimental and its own switch are both on.</summary>
    public bool Enabled => config.Experimental && config.ErrandsForYou;

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
    public HomeKind KindOf(HomeRef at) => config.ErrandsHome is >= 0 and <= 2 ? (HomeKind)config.ErrandsHome : at.Kind;

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
            Reachable = v => Reachable(v, at, kind),
            Offers = shop.Vendors.Offers,
            PreferDistrict = at.District,
            WorldAllowed = config.ErrandsOtherWorlds ? w => w == world || Travel.CanVisit(w) != false : w => w == world,
        };
        return ErrandPlanner.Build(shopping, options);
    }

    /// <summary>
    /// A vendor the errands can get to: in your home's own district (its teleport lands you in your
    /// ward; not from an apartment, whose building stands apart), or in a zone with an aetheryte
    /// you're attuned to. Another district's housing vendors aren't (getting to another ward isn't done).
    /// </summary>
    private static bool Reachable(VendorOffer v, HomeRef at, HomeKind kind)
    {
        if (!v.Located) return false;
        if (v.District != null) return v.District == at.District && kind != HomeKind.Apartment;
        var aetheryte = ErrandGame.AetheryteOf(v.TerritoryId);
        return aetheryte != 0 && ErrandGame.Attuned(aetheryte);
    }

    /// <summary>Why the errands can't start now with this plan, or null.</summary>
    public string? CanStart(ErrandPlan plan, HousingContext ctx)
    {
        if (!Enabled) return "Switch it on in Settings → Experimental.";
        if (Active) return "Already on errands.";
        if (runner.Active || OtherRunActive?.Invoke() == true) return "Wait until the run is done.";
        if (ErrandGame.Blocked(false) is { } blocked) return blocked;
        if (ctx.Busy) return "Wait until you're free (not in combat, a cutscene or between areas).";
        var mine = ErrandGame.HomeHere(ctx);
        if (mine == null || session.Plan == null) return "Go inside your home: what's missing is worked out there.";
        if (!ctx.HasPermission) return "You can only place furniture in a home you may decorate.";
        if (!mine.Owned && config.ErrandsHome < 0) return "The game doesn't say this home is yours: choose which home it is below.";
        var world = ErrandGame.CurrentWorld();
        if (world.Length == 0 || world != ErrandGame.HomeWorld()) return "Go back to your home world first.";
        if (!Nav.Installed) return "vnavmesh isn't installed (or is turned off).";
        if (!Travel.Installed) return "Lifestream isn't installed (or is turned off).";
        // What's fetched is placed by the usual run: it has to be able to start here.
        if (runner.CanStart(false) is { } placing) return placing;
        if (plan.IsEmpty) return "Nothing the errands can get.";
        var gil = ErrandGame.Gil();
        if (gil < plan.TotalGil) return $"You have {gil:N0} gil; the errands need about {plan.TotalGil:N0}.";
        if (plan.Needs(StopKind.Market) || (plan.Needs(StopKind.Retainer) && !BellNearby()))
        {
            var hub = ErrandGame.AetheryteOf(ErrandGame.HubTerritory);
            if (hub == 0 || !ErrandGame.Attuned(hub)) return "The market board and the summoning bells are reached through Limsa Lominsa's aetheryte, which you haven't attuned to.";
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

    /// <summary>Starts the errands on a plan you've seen, having agreed to its gil.</summary>
    public void Start(ErrandPlan plan, HousingContext ctx)
    {
        if (CanStart(plan, ctx) is { } why)
        {
            StopReason = why;
            log.Warn($"Errands didn't start: {why}");
            return;
        }
        home = ErrandGame.HomeHere(ctx)!;
        homeKind = KindOf(home);
        homeWorld = ErrandGame.HomeWorld();
        houseBell = BellNearby();
        Plan = plan;
        agreedGil = plan.TotalGil;
        ceiling = Math.Max(0, config.ErrandsMarketCeiling);
        tripsAtStart = plan.Trips.Count;
        cycles = 0;
        Trip = 0;
        VendorSpent = MarketSpent = 0;
        got.Clear();
        travelling = false;
        LetsPlace = false;
        walkTarget = null;
        lastTerritory = ErrandGame.Territory;
        StopReason = null;
        ResultWaiting = false;
        StartedAt = DateTime.Now;
        queue.ActionGap = TimeSpan.FromMilliseconds(Math.Max(300, Pacing.Profile(config.Pace).ClickGapMs));
        queue.Begin(Array.Empty<Step>());
        if (plan.Needs(StopKind.Retainer))
            log.Info(shop.AutoRetainer.KeepOut() ? "AutoRetainer is asked to keep out of the way while the errands use a bell." : "AutoRetainer didn't answer (not installed?); carrying on.");
        log.Info($"Errands started: {plan.Count} things in {Words(plan.Trips.Count, "trip")}, about {plan.TotalGil:N0} gil agreed "
                 + $"({plan.VendorGil:N0} at vendors, {plan.MarketGil:N0} on the market board, under a ceiling of {ceiling:N0}). Home: {homeKind}, "
                 + $"{(houseBell ? "a summoning bell in it" : "no summoning bell in it")}.{(config.ErrandsTrustGuesses ? string.Empty : " Clicks not yet seen working wait for Continue.")}");
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
        // A walk the pause stopped carries on.
        if (walkTarget is { } to) Nav.MoveTo(to);
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
        if (travelling || Travel.Busy) Travel.Abort();
        travelling = false;
        CloseWindows();
        shop.AutoRetainer.LetBe();
        State = RunState.Stopped;
        StopReason = reason;
        StatusText = reason;
        ResultWaiting = true;
        log.Warn($"Errands stopped: {reason} ({Summary()})");
        if (config.ChatMessages) Services.Chat.PrintError($"Errands stopped: {reason}", "Housing Studio");
    }

    private void Finish(string why)
    {
        State = RunState.Finished;
        LetsPlace = false;
        Nav.Stop();
        walkTarget = null;
        shop.AutoRetainer.LetBe();
        StopReason = null;
        StatusText = why;
        ResultWaiting = true;
        log.Done($"Errands finished: {why} ({Summary()})");
        if (config.ChatMessages) Services.Chat.Print($"Errands finished: {why}", "Housing Studio");
    }

    private string Summary() =>
        $"{GotCount} got, {Spent:N0} gil spent ({VendorSpent:N0} at vendors, {MarketSpent:N0} on the market board), {Words(Trip, "trip")}";

    private static string Words(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    /// <summary>Closes every window the errands open, as their own close buttons do.</summary>
    private static void CloseWindows()
    {
        foreach (var name in new[] { UiTable.ContextMenu, ErrandTable.Shop, ErrandTable.ItemSearchResult, ErrandTable.ItemSearch, ErrandTable.SelectIconString })
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
        // Switched off in Settings while it goes: it stops.
        if (!Enabled)
        {
            Stop("Switched off in Settings → Experimental.");
            return;
        }
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
            Finish(Plan.Left.Count == 0 ? "everything's got and placed." : $"all the errands can get is got; {Plan.Left.Sum(l => l.Count)} left for you (see the list).");
            return;
        }

        // Clicks not yet seen working wait for Continue (step by step) until you trust them.
        queue.StepByStep = config.StepByStep || (!config.ErrandsTrustGuesses && queue.Current is { IsAction: true, Key: Guess });
        switch (queue.State)
        {
            case QueueState.Running:
                queue.Tick();
                StatusText = queue.State == QueueState.AwaitingContinue ? $"Next: {queue.Current?.Label}" : StepLabel ?? StatusText;
                if (queue.State == QueueState.Finished) NextSegment();
                else if (queue.State is QueueState.Stopped or QueueState.Skipped) Stop(queue.Message ?? "A step didn't work.");
                return;
            case QueueState.AwaitingContinue:
                return;
            default:
                NextSegment();
                return;
        }
    }

    private void BeginTrip(ErrandTrip trip)
    {
        Trip++;
        cycles++;
        gotThisTrip = 0;
        segments.Clear();
        segment = -1;
        log.Info($"Trip {Trip}: {trip.Count} things ({trip.Slots} bag slots), about {trip.Gil:N0} gil, "
                 + string.Join(", ", trip.Stops.Select(s => s.Kind switch
                 {
                     StopKind.Retainer => $"{s.Retainer?.Name}",
                     StopKind.Vendor => s.Vendor?.Where() ?? "a vendor",
                     _ => $"the market board on {s.World}",
                 })) + ".");
        foreach (var stop in trip.Stops)
        {
            switch (stop.Kind)
            {
                case StopKind.Retainer:
                    AddRetainer(stop);
                    break;
                case StopKind.Vendor:
                    AddVendor(stop);
                    break;
                default:
                    AddMarket(stop);
                    break;
            }
        }
        Add("Going home", GoHomeSteps, NoCleanup);
        Add("Going in", EnterSteps, NoCleanup);
        Add("Placing what's in your bags", PlaceSteps, NoCleanup);
        Add("Seeing what's still missing", ReplanSteps, NoCleanup);
        NextSegment();
    }

    private void Add(string title, Func<List<Step>> steps, Func<List<Step>> cleanup) => segments.Add((title, steps, cleanup));

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
        var (title, steps, cleanup) = segments[segment];
        log.Info($"{title}.");
        StatusText = title;
        queue.Begin(steps(), cleanup());
    }

    // ---------------------------------------------------------------------------------------------
    // Getting about: teleports, worlds, walking.

    private List<Step> TeleportSteps(uint territory, string where)
    {
        var aetheryte = ErrandGame.AetheryteOf(territory);
        return new List<Step>
        {
            Step.Act($"Teleport to {where} (Lifestream, aetheryte {aetheryte})", () =>
            {
                if (ErrandGame.Territory == territory) return StepOutcome.Done;
                if (aetheryte == 0) return StepOutcome.Halt($"No aetheryte for {where} is known.");
                travelling = true;
                return Travel.Teleport(aetheryte) ? StepOutcome.Done : StepOutcome.Halt($"Lifestream wouldn't teleport to {where} (is it installed and up to date?).");
            }),
            new Step
            {
                Label = $"Arrive in {where}",
                Run = () =>
                {
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

    private List<Step> WorldSteps(string world) => new()
    {
        Step.Act($"Go to {world} (Lifestream)", () =>
        {
            if (ErrandGame.CurrentWorld() == world) return StepOutcome.Done;
            travelling = true;
            return Travel.ChangeWorld(world) ? StepOutcome.Done : StepOutcome.Halt($"Lifestream wouldn't go to {world}.");
        }),
        new Step
        {
            Label = $"Arrive on {world}",
            Run = () =>
            {
                if (ErrandGame.CurrentWorld() != world || !ErrandGame.Settled || Travel.Busy) return StepOutcome.Waiting;
                travelling = false;
                lastTerritory = ErrandGame.Territory;
                return StepOutcome.Done;
            },
            Timeout = WorldWait,
            TimeoutMessage = $"Didn't arrive on {world} within {WorldWait.TotalMinutes:0} minutes.",
        },
    };

    /// <summary>Walks (vnavmesh) to within <paramref name="within"/> yalms of a spot, found when the walk starts.</summary>
    private List<Step> WalkSteps(string what, Func<Vector3?> where, float within = Reach)
    {
        var started = DateTime.MinValue;
        var tries = 0;
        Vector3? to = null;
        return new List<Step>
        {
            WaitFor("vnavmesh has this zone's map", () => Nav.Ready, TimeSpan.FromSeconds(90),
                "vnavmesh's map of this zone wasn't ready in 90 s (is vnavmesh installed and on?)."),
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
    private static Step You(string what, Func<bool> done, TimeSpan? timeout = null) => new()
    {
        Label = $"You: {what}",
        Run = () => done() ? StepOutcome.Done : StepOutcome.Waiting,
        Timeout = timeout ?? YouWait,
        TimeoutMessage = $"Waited {(timeout ?? YouWait).TotalMinutes:0} minutes for you to {what[..1].ToLowerInvariant()}{what[1..]}.",
    };

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
        Add("Going to a summoning bell", BellSteps, WindowCleanup);
        Add($"Opening {r.Name}'s bags", () => OpenRetainerSteps(r), WindowCleanup);
        foreach (var item in stop.Items) Add($"Taking {Thing(item)} from {r.Name}", () => WithdrawSteps(r, item), WindowCleanup);
        Add($"Saying goodbye to {r.Name}", () => CloseRetainerSteps(r), WindowCleanup);
    }

    private List<Step> BellSteps()
    {
        var steps = new List<Step>();
        var near = ErrandGame.Me is { } me && ErrandGame.Nearest(Bells(), me, houseBell ? 60 : 120, UsableKinds) != null;
        if (!near) steps.AddRange(TeleportSteps(ErrandGame.HubTerritory, "Limsa Lominsa Lower Decks"));
        steps.AddRange(WalkSteps("a summoning bell", () => ErrandGame.Me is { } p ? ErrandGame.Nearest(Bells(), p, 150, UsableKinds)?.Position : null));
        return steps;
    }

    private List<Step> OpenRetainerSteps(RetainerStock r)
    {
        bool MenuOpen() => ErrandGame.MenuEntries()?.Addon == ErrandTable.SelectString;
        return new List<Step>
        {
            Use("the summoning bell", () => ErrandGame.Me is { } me ? ErrandGame.Nearest(Bells(), me, Reach + 2, UsableKinds) : null),
            Step.Wait("The list of retainers opens", () => GameUi.IsOpen(ErrandTable.RetainerList), WindowWait),
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
            Step.Wait($"{r.Name} comes", MenuOpen, TimeSpan.FromSeconds(8), StepStatus.Done),
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
                    if (room < ErrandPlanner.Stacks(slot.Quantity, stack)) return StepOutcome.Halt($"Your bags are full ({room} free slots; the stack of {slot.Quantity} needs more).");
                    before = ErrandGame.BagCount(item.ItemId);
                    return ErrandGame.OpenRetainerItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
                },
            },
            Step.Wait("The item's menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), TimeSpan.FromSeconds(3)),
            new Step
            {
                Label = $"Choose \"{ErrandTable.RetrieveWords[^1]}\" (the whole stack)",
                IsAction = true,
                Key = Guess,
                Run = () =>
                {
                    var entries = GameUi.MenuEntries();
                    if (entries == null) return StepOutcome.Halt("The item's menu closed.");
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
                Timeout = TimeSpan.FromSeconds(6),
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
        Add($"Going to {v.Where()}", () => VendorTravelSteps(v), WindowCleanup);
        Add($"Opening {v.NpcName}'s shop", () => OpenShopSteps(v), WindowCleanup);
        foreach (var item in stop.Items) Add($"Buying {Thing(item)} from {v.NpcName}", () => BuySteps(v, item), WindowCleanup);
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
        }, WindowCleanup);
    }

    private List<Step> VendorTravelSteps(VendorOffer v)
    {
        var steps = new List<Step>();
        if (v.District != null)
        {
            // Your own district's: your home's teleport lands you in your ward, where they stand.
            if (!ErrandGame.InHomeWard(home!)) steps.AddRange(GoHomeSteps(leave: true));
        }
        else
        {
            steps.AddRange(TeleportSteps(v.TerritoryId, v.PlaceName.Length > 0 ? v.PlaceName : "the vendor's zone"));
        }
        var at = v.Position!.Value;
        steps.AddRange(WalkSteps(v.NpcName, () => ErrandGame.Npc(v.NpcId, 60)?.Position ?? at));
        return steps;
    }

    private List<Step> OpenShopSteps(VendorOffer v)
    {
        var menus = 0;
        Step Opens() => new()
        {
            Label = $"{v.NpcName}'s shop opens",
            Run = () =>
            {
                if (GameUi.IsOpen(ErrandTable.Shop)) return StepOutcome.Done;
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
        return new List<Step>
        {
            Use(v.NpcName, () => ErrandGame.Npc(v.NpcId, Reach + 3)),
            Opens(),
        };
    }

    private List<Step> BuySteps(VendorOffer v, ErrandItem item)
    {
        var bought = 0;
        var before = 0;
        long gilBefore = 0;
        var n = 0;
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
                    var cost = (long)v.Price * n;
                    var gil = ErrandGame.Gil();
                    if (gil < cost) return StepOutcome.Halt($"Not enough gil: {cost:N0} for {n} {Name(item.ItemId)}, and you have {gil:N0}.");
                    if (Spent + cost > agreedGil) return StepOutcome.Halt($"That would spend more than the {agreedGil:N0} gil you agreed to.");
                    var room = ErrandGame.FreeSlots();
                    if (room < ErrandPlanner.Stacks(n, stack)) return StepOutcome.Halt($"Your bags are full ({room} free slots).");
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
                            GameUi.Close(UiTable.YesNo);
                            return StepOutcome.Halt($"The shop asked \"{question}\", which isn't {n} × {Name(item.ItemId)} for {(long)v.Price * n:N0} gil: nothing was bought.");
                        }
                        queue.InsertNext(new[] { Yes(question), Bought() });
                        return StepOutcome.Done;
                    }
                    return StepOutcome.Waiting;
                }
                var spent = Math.Max(0, gilBefore - ErrandGame.Gil());
                VendorSpent += spent;
                Count(item.ItemId, now - before);
                bought += now - before;
                if (spent > (long)v.Price * n) return StepOutcome.Halt($"{Name(item.ItemId)} cost {spent:N0} gil, more than the {(long)v.Price * n:N0} planned.");
                if (bought < item.Count) queue.InsertNext(Once());
                return StepOutcome.Done;
            },
            Timeout = TimeSpan.FromSeconds(6),
            TimeoutMessage = $"{Name(item.ItemId)} didn't come to your bags.",
        };
        return Once();
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
        Add($"Going to a market board on {world}", () => BoardSteps(world), WindowCleanup);
        foreach (var item in stop.Items) Add($"Buying {Thing(item)} on the market board", () => ListingSteps(item), WindowCleanup);
        Add("Closing the market board", () => new List<Step>
        {
            Step.Act("Close the board", () =>
            {
                GameUi.Close(ErrandTable.ItemSearchResult);
                GameUi.Close(ErrandTable.ItemSearch);
                return StepOutcome.Done;
            }),
        }, WindowCleanup);
    }

    private List<Step> BoardSteps(string world)
    {
        var steps = new List<Step>();
        if (ErrandGame.CurrentWorld() != world)
        {
            // A world visit leaves from a city's aetheryte: Limsa's first, then again on the world.
            steps.AddRange(TeleportSteps(ErrandGame.HubTerritory, "Limsa Lominsa Lower Decks"));
            steps.AddRange(WorldSteps(world));
        }
        steps.AddRange(TeleportSteps(ErrandGame.HubTerritory, "Limsa Lominsa Lower Decks"));
        steps.AddRange(WalkSteps("a market board", () => ErrandGame.Me is { } p ? ErrandGame.Nearest(Boards(), p, 150, UsableKinds)?.Position : null));
        steps.Add(Use("the market board", () => ErrandGame.Me is { } me ? ErrandGame.Nearest(Boards(), me, Reach + 2, UsableKinds) : null));
        steps.Add(Step.Wait("The board's search opens", () => GameUi.IsOpen(ErrandTable.ItemSearch) || GameUi.IsOpen(ErrandTable.ItemSearchResult), WindowWait));
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
        List<Step> Once() => new()
        {
            new Step
            {
                Label = $"Buy a listing of {name} ({ErrandTable.ListingBuy.Describe()})",
                IsAction = true,
                Run = () =>
                {
                    if (bought >= wanted) return Finished();
                    if (ErrandGame.Listings(item.ItemId) is not { } listings) return StepOutcome.Halt($"{name}'s listings closed.");
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
                        if (ErrandGame.FreeSlots() < ErrandPlanner.Stacks(l.Quantity, ErrandGame.StackSize(item.ItemId)))
                            return StepOutcome.Halt($"Your bags are full ({ErrandGame.FreeSlots()} free slots).");
                        cost = MarketRule.Cost(l.UnitPrice, l.Quantity, l.TotalTax);
                        unit = l.UnitPrice;
                        quantity = l.Quantity;
                        tried.Add(l.ListingId);
                        before = ErrandGame.BagCount(item.ItemId);
                        gilBefore = gil;
                        log.Info($"  Buying {l.Quantity} × {name} at {l.UnitPrice:N0} gil each ({cost:N0} with tax), row {l.Row}.");
                        return GameUi.Fire(ErrandTable.ListingBuy, UiCallValues.Of(("index", l.Row))) ? StepOutcome.Done : StepOutcome.Halt($"{name}'s listings closed.");
                    }
                    log.Warn($"  No listing of {name} to buy here ({firstWhy ?? "none listed"}): {wanted - bought} left for now.");
                    return Finished();
                },
            },
            WaitFor("The board asks to confirm", () => GameUi.YesNoText() != null, TimeSpan.FromSeconds(5), "The board didn't ask to confirm the purchase."),
            new Step
            {
                Label = $"Answer yes (buy {name})",
                IsAction = true,
                Run = () =>
                {
                    // Only the listing chosen: the question must name its price (another row clicked, or
                    // another question, is said no to and stops the errands).
                    var question = GameUi.YesNoText();
                    if (!MarketRule.QuestionMatches(question, unit, quantity, cost))
                    {
                        GameUi.Close(UiTable.YesNo);
                        return StepOutcome.Halt($"The board asked \"{question}\", which isn't the {cost:N0} gil listing chosen: nothing was bought.");
                    }
                    return GameUi.Fire(UiTable.YesNoYes) ? StepOutcome.Done : StepOutcome.Halt("The yes/no closed.");
                },
            },
            new Step
            {
                Label = $"{name} comes to your bags",
                Run = () =>
                {
                    var now = ErrandGame.BagCount(item.ItemId);
                    if (now <= before) return StepOutcome.Waiting;
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
                Timeout = TimeSpan.FromSeconds(10),
                TimeoutMessage = $"{name} didn't come to your bags (sold to someone else first?).",
            },
        };
        var steps = SearchSteps(item.ItemId, name);
        steps.AddRange(Once());
        return steps;
    }

    /// <summary>
    /// Opens an item's listings: the board searched for its name (as typing it does), its row found
    /// in the results, and opened; what opens is checked to be that item's before anything is bought.
    /// Whatever doesn't work is left to you (search and open it), as before.
    /// </summary>
    private List<Step> SearchSteps(uint itemId, string name)
    {
        var index = -1;
        DateTime? searchedAt = null;
        bool Opened() => GameUi.IsOpen(ErrandTable.ItemSearchResult) && ErrandGame.Listings(itemId) != null;
        Step YouOpen(string what) => You($"{what} {name} and open its listings", Opened);
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
                    if (DateTime.UtcNow - searchedAt.Value < TimeSpan.FromSeconds(3)) return StepOutcome.Waiting;
                    log.Info($"  {name} wasn't found among the search's results{(seen.Count == 0 ? string.Empty : $" ({string.Join(" | ", seen)})")}: you open it.");
                    queue.InsertNext(new[] { YouOpen("In the board's results, find") });
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Open {name}'s listings ({ErrandTable.SearchResultPick.Describe()})",
                IsAction = true,
                Run = () =>
                {
                    if (Opened() || index < 0) return StepOutcome.Done;
                    if (!GameUi.Fire(ErrandTable.SearchResultPick, UiCallValues.Of(("index", index))))
                        queue.InsertNext(new[] { YouOpen("Search the board for") });
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"{name}'s listings come",
                Run = () =>
                {
                    if (Opened()) return StepOutcome.Done;
                    if (GameUi.IsOpen(ErrandTable.ItemSearchResult) && ErrandGame.ListingsItem() is var other && other != 0 && other != itemId)
                    {
                        // Another item's listings: nothing of theirs is bought; back to the search, and you.
                        log.Info($"  Result {index + 1} opened another item's listings ({Name(other)}): you open {name}'s.");
                        GameUi.Close(ErrandTable.ItemSearchResult);
                        queue.InsertNext(new[] { YouOpen("In the board's results, find") });
                        return StepOutcome.Done;
                    }
                    return StepOutcome.Waiting;
                },
                Timeout = TimeSpan.FromSeconds(15),
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

    private List<Step> EnterSteps()
    {
        if (Inside()) return ReadAgainSteps();
        var steps = new List<Step>();
        Vector3? door = null;
        if (homeKind != HomeKind.Apartment && ErrandGame.Me is { } me)
            door = ErrandGame.Nearest(ErrandTable.EntranceWords, me, 30, UsableKinds)?.Position;
        if (door == null)
        {
            if (homeKind != HomeKind.Apartment) log.Warn($"  Couldn't find your door. Near you: {ErrandGame.NamesNear(30)}.");
            steps.Add(Arriving(You("Go inside your home", Inside)));
        }
        else
        {
            steps.AddRange(WalkSteps("your door", () => door));
            steps.Add(new Step
            {
                Label = "Use your door",
                IsAction = true,
                Run = () =>
                {
                    if (ErrandGame.Me is not { } p || ErrandGame.Nearest(ErrandTable.EntranceWords, p, Reach + 2, UsableKinds) is not { } o)
                        return StepOutcome.Halt($"Couldn't find your door within reach. Near you: {ErrandGame.NamesNear(15)}.");
                    travelling = true;
                    return ErrandGame.Interact(o) ? StepOutcome.Done : StepOutcome.Halt("Couldn't use your door.");
                },
            });
            steps.Add(Step.Wait("The game asks to go in", () => GameUi.YesNoText() != null || Inside(), TimeSpan.FromSeconds(5), StepStatus.Done));
            steps.Add(new Step
            {
                Label = "Answer yes (go in)",
                IsAction = true,
                Run = () =>
                {
                    if (Inside() || GameUi.YesNoText() is not { } question) return StepOutcome.Done;
                    // Only the door's own question ("Enter the estate hall?").
                    if (!ErrandTable.EnterWords.Any(w => question.TrimStart().StartsWith(w, StringComparison.OrdinalIgnoreCase)))
                        return StepOutcome.Halt($"The game asked \"{question}\", not to go in: left for you.");
                    return GameUi.Fire(UiTable.YesNoYes) ? StepOutcome.Done : StepOutcome.Halt("The yes/no closed.");
                },
            });
            steps.Add(Arriving(WaitFor("Inside your home", Inside, TimeSpan.FromSeconds(30), "Didn't get inside your home.")));
        }
        steps.AddRange(ReadAgainSteps());
        return steps;
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
                return session.ReadAt > readAfter && session.Plan != null && session.Context.HouseId == home!.HouseId ? StepOutcome.Done : StepOutcome.Waiting;
            },
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutMessage = "Your home couldn't be read again (open Housing → Indoor Furnishings once, and start again).",
        },
    };

    private List<Step> PlaceSteps() => new()
    {
        Step.Act("Start placing (Housing Studio's own run)", () =>
        {
            if (session.Plan == null) return StepOutcome.Halt("Your home isn't matched against the design.");
            var entries = session.Plan.Entries.Where(e => config.FloorShown(e.Target.Floor)).ToList();
            LetsPlace = true;
            runner.Start(false, entries);
            if (runner.Active) return StepOutcome.Done;
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
                if (gotThisTrip == 0) return StepOutcome.Halt("The last trip got nothing, so something's wrong: the log says what each step saw.");
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
        shop.AutoRetainer.LetBe();
    }
}
