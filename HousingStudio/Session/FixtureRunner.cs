using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.UI.Model;

namespace HousingStudio.Session;

/// <summary>
/// "Set them for me": the design's fixtures for the side you're on (each
/// floor's walls, flooring and light inside; the outside's parts on your plot), set through the game's
/// own fixtures window, with the same step, wait and check pattern as placing:
/// <list type="number">
/// <item>the furnishing editor is closed (its own close), the Housing menu opened, and its Interior
/// (or Exterior) Fixtures entry chosen; if the plugin can't, it asks you;</item>
/// <item>the window's slots are read and checked against what's set, and each fixture is put in its
/// slot from your bags as dragging it there would (the game's item move); if it doesn't go, you're
/// asked to put it there;</item>
/// <item>you press the window's apply (its click isn't known yet), and each fixture is checked in the
/// game's own tables (<see cref="FixtureReader"/>) and ticked in Ready.</item>
/// </list>
/// Most of it is a guess (<see cref="FixtureTable"/>), so while it is the run goes step by step: it
/// holds before every action and shows it until you press Continue. Every step is logged; it stops
/// on leaving the home, a zone change, combat, a logout or losing permission, and never presses
/// anything to clean up (the fixtures window is left for you to apply or close).
/// </summary>
public sealed unsafe partial class FixtureRunner
{
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan YouWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ApplyWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(450);

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly StepQueue queue = new(() => DateTime.UtcNow);

    private Side side;
    private PlaceKind startKind;
    private uint startTerritory;
    private ulong startHouse;
    private int? startPlot;
    private string designKey = string.Empty;

    /// <summary>The design loaded when it started (a new one is loaded as a new layout).</summary>
    private object? startLayout;
    private List<FixtureTask> toSet = new();

    /// <summary>The window's slots were found to hold what's set, in the order guessed: fixtures are put in them by the plugin.</summary>
    private bool slotsTrusted;

    /// <summary>The fixtures window is up, and closing it now (before applying) would lose what's in it: the run stops if it closes.</summary>
    private bool watchWindow;

    /// <summary>Fixtures that couldn't be put in (no longer in your bags): not waited for at the apply.</summary>
    private readonly HashSet<FixtureTask> dropped = new();

    private DateTime nextSetCheck;

    /// <summary>The window's container by slot as last seen (when it opened, then after each piece you put in).</summary>
    private Dictionary<int, uint> slotsAtOpen = new();

    /// <summary>The parts' slots known this run: seen holding what's set, kept from earlier runs, learnt from you.</summary>
    private List<FixtureJobs.KnownSlot> known = new();

    /// <summary>Each fixture's slot, as worked out from what's known (null: not known).</summary>
    private readonly Dictionary<FixtureTask, int?> slotOf = new();

    private int? SlotOf(FixtureTask task) => slotOf.TryGetValue(task, out var s) ? s : task.Slot;

    /// <summary>
    /// Works out every fixture's slot from what's known, and whether the plugin may put them in: each
    /// one to set has a slot, and no two the same.
    /// </summary>
    private bool WorkOutSlots()
    {
        slotOf.Clear();
        foreach (var t in toSet) slotOf[t] = FixtureJobs.SlotFor(side, t.Fixture.Level, t.Fixture.Type, known);
        var slots = toSet.Select(t => slotOf[t]).ToList();
        return slots.All(x => x != null) && slots.Distinct().Count() == slots.Count;
    }

    /// <summary>A piece you put in by hand: the one slot it went to is its part's, kept for next time.</summary>
    private void LearnFromYou(FixtureTask task)
    {
        var held = ContainerItems();
        if (held == null) return;
        var item = task.Fixture.ItemId;
        var went = held.Where(kv => kv.Value == item && (!slotsAtOpen.TryGetValue(kv.Key, out var was) || was != item)).Select(kv => kv.Key).ToList();
        slotsAtOpen = held;
        if (went.Count != 1) return;
        var slot = went[0];
        known.RemoveAll(k => FixtureJobs.Norm(k.Level) == FixtureJobs.Norm(task.Fixture.Level) && FixtureJobs.Norm(k.Type) == FixtureJobs.Norm(task.Fixture.Type));
        known.Add(new FixtureJobs.KnownSlot(task.Fixture.Level, task.Fixture.Type, slot));
        config.FixtureSlots[FixtureJobs.SlotKey(side, task.Fixture.Level, task.Fixture.Type)] = slot;
        config.Save();
        log.Info($"  Learnt: the {Describe(task)} goes in slot {slot} of the window (kept for next time).");
        if (!slotsTrusted && WorkOutSlots())
        {
            slotsTrusted = true;
            log.Info("  The rest's slots follow from what's known now: the plugin puts them in.");
        }
    }

    /// <summary>How many of each item the window's container held when it opened (to see a piece you put in, when the slots aren't trusted).</summary>
    private Dictionary<uint, int> containerBefore = new();

    public FixtureRunner(Configuration config, LayoutSession session, GameData data, ActivityLog log)
    {
        this.config = config;
        this.session = session;
        this.data = data;
        this.log = log;
        Rehearsals.Register(rehearsing);
        queue.ActionGap = ClickGap;
        queue.Trace += (step, what) =>
        {
            if (what == "start" && step.Label.StartsWith("You:", StringComparison.Ordinal)) log.Info($"  … {step.Label}");
            else if (what != "start" && what != "done") log.Warn($"  ✕ {what}");
        };
        queue.Timed += (step, took) => log.Info($"  {(step.IsAction ? "→" : "…")} {step.Label} ({took.TotalMilliseconds:0} ms)");
    }

    public RunState State { get; private set; } = RunState.Idle;
    public bool Active => State == RunState.Running;
    public string? StopReason { get; private set; }

    /// <summary>What the last run set, and what it couldn't (with why), for the window.</summary>
    public string? Result { get; private set; }

    /// <summary>The step being done or waited for, in words.</summary>
    public string? StepLabel => Active ? queue.Current?.Label : null;

    /// <summary>Holding before an action until Continue (always, while the fixtures' clicks are guesses).</summary>
    public bool AwaitingContinue => Active && queue.State == QueueState.AwaitingContinue;

    /// <summary>The run is waiting for you to do something by hand.</summary>
    public bool WaitingForYou => StepLabel is { } l && l.StartsWith("You:", StringComparison.Ordinal);

    public void Continue() => queue.Continue();

    /// <summary>Always (0.7.13: no longer a Beta feature to switch on).</summary>
    public bool Enabled => true;

    /// <summary>The design's fixtures for the side you're on, against what's set and what's in your bags. Framework thread.</summary>
    public List<FixtureTask> Plan()
    {
        var layout = session.Layout;
        var ctx = session.Context;
        // The fixture tables are read in the background when the plugin starts: not on the game's thread.
        if (layout == null || ctx.Side is not { } here || !data.FixturesReady) return new List<FixtureTask>();
        var current = Current(here, ctx);
        if (current == null) return new List<FixtureTask>();
        var design = here == Side.Interior ? layout.InteriorFixtures : layout.ExteriorFixtures;
        return FixtureJobs.Plan(design, current, here, Owned, data.IsUnited);
    }

    /// <summary>A placing run (or a restore about to start): fixtures aren't set while it goes.</summary>
    public Func<bool>? OtherRunActive { get; set; }

    /// <summary>Why "Set them for me" can't start now, or null.</summary>
    public string? CanStart()
    {
        if (Active) return "Already setting them.";
        if (OtherRunActive?.Invoke() == true) return "Wait until the run is done.";
        var ctx = session.Context;
        if (session.Layout == null) return "Load a design first.";
        if (!data.FixturesReady) return "Still reading the game's fixture tables: try again in a moment.";
        if (ctx.Side == null) return "Go to your home (inside for its walls, floors and lights; your plot for the outside).";
        if (!ctx.HasPermission) return "You can only set fixtures in a home you may decorate.";
        if (ctx.Busy) return "Wait until you're free (not in combat, a cutscene or between areas).";
        if (ctx.Side == Side.Garden && ctx.Plot == null) return "Stand on your plot.";
        if (ctx.Side == Side.Interior && session.Layout.HouseSize != null && ctx.HouseSize != null && !Districts.SameSize(session.Layout.HouseSize, ctx.HouseSize))
            return "This design is for another size of home.";
        return null;
    }

    public void Start()
    {
        // A rehearsal (Rehearse) is the same steps, closing the window instead of applying; any other start is real.
        Rehearsal = rehearseNext;
        rehearseNext = false;
        StopReason = null;
        Result = null;
        if (CanStart() is { } why)
        {
            StopReason = why;
            return;
        }
        var ctx = session.Context;
        side = ctx.Side!.Value;
        var tasks = Plan();
        toSet = tasks.Where(t => t.State == FixtureState.ToSet && t.Slot != null).ToList();
        designKey = FixtureTicks.DesignKey(session.FilePath, session.RawTitle);
        startLayout = session.Layout;
        // What's set already is ticked (the ticks are kept per design); a rehearsal changes no ticks.
        if (!Rehearsal) TickSet(tasks.Where(t => t.State == FixtureState.Set).Select(t => t.Fixture));
        if (toSet.Count == 0)
        {
            StopReason = tasks.Any(t => t.State == FixtureState.NotOwned) ? "Nothing to set from your bags: the fixtures left aren't in them." : "Nothing to set here: what's left is yours to set.";
            log.Info($"Fixtures: {StopReason}");
            return;
        }
        startKind = ctx.Kind;
        startTerritory = ctx.TerritoryId;
        startHouse = ctx.HouseId;
        startPlot = ctx.Plot;
        slotsTrusted = false;
        watchWindow = false;
        dropped.Clear();
        containerBefore = new Dictionary<uint, int>();
        slotsAtOpen = new Dictionary<int, uint>();
        known = new List<FixtureJobs.KnownSlot>();
        slotOf.Clear();
        // While any of it is a guess, every action waits for Continue.
        queue.StepByStep = !FixtureTable.Verified || config.StepByStep;
        queue.StepsPerTick = 4;
        if (Rehearsal) RehearsalStarted(tasks);
        queue.Begin(Steps(), new[] { Rehearsal ? CloseWindowUnapplied() : LeaveWindow() });
        State = RunState.Running;
        log.Info($"{(Rehearsal ? "Rehearsing setting" : "Setting")} fixtures ({(side == Side.Interior ? "inside" : "outside")}): {string.Join(", ", toSet.Select(Describe))}. "
                 + $"{(queue.StepByStep ? "Step by step: press Continue before each action. " : string.Empty)}"
                 + $"{Count(tasks, FixtureState.NotOwned)} not in your bags, {Count(tasks, FixtureState.ByHand)} yours to set, {Count(tasks, FixtureState.Set)} set already.");

        static int Count(List<FixtureTask> t, FixtureState s) => t.Count(x => x.State == s);
    }

    public void Stop(string reason)
    {
        if (!Active) return;
        queue.Abort(reason);
        State = RunState.Stopped;
        StopReason = reason;
        Result = Summary();
        if (Rehearsal && !reason.StartsWith("Stopped by you", StringComparison.Ordinal)) Rehearsals.Report.Issue("Setting the fixtures", $"Stopped: {reason}");
        log.Warn($"Fixtures {(Rehearsal ? "rehearsal " : string.Empty)}stopped: {reason}");
        if (config.ChatMessages) Services.Chat.PrintError($"Stopped setting fixtures: {reason}", "Housing Studio");
    }

    /// <summary>Every frame, on the framework thread.</summary>
    public void Tick(HousingContext ctx)
    {
        if (!Active) return;
        if (!Enabled)
        {
            Stop("Set the fixtures for me was switched off.");
            return;
        }
        if (ctx.Kind != startKind || ctx.TerritoryId != startTerritory || ctx.HouseId != startHouse || (side == Side.Garden && ctx.Plot != startPlot))
        {
            Stop("You left the home this was started in.");
            return;
        }
        if (ctx.Busy)
        {
            Stop("The game got busy (combat, a cutscene, or a zone change).");
            return;
        }
        // Its Continue and Stop are in the design's Ready: closing the design or opening another stops it.
        if (!ReferenceEquals(session.Layout, startLayout))
        {
            Stop("The design was closed or another was opened.");
            return;
        }
        if (!ctx.HasPermission)
        {
            Stop("You're no longer allowed to decorate here.");
            return;
        }
        if (watchWindow && !GameUi.IsOpen(Window))
        {
            Stop("The fixtures window closed before the fixtures were applied.");
            return;
        }
        queue.Tick();
        switch (queue.State)
        {
            case QueueState.Finished:
                State = RunState.Finished;
                Result = Summary();
                log.Done($"Fixtures: {Result}");
                if (config.ChatMessages) Services.Chat.Print($"Fixtures: {Result}", "Housing Studio");
                break;
            case QueueState.Skipped:
            case QueueState.Stopped:
                Stop(queue.Message ?? "A step didn't work.");
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The steps.

    private string Window => side == Side.Interior ? FixtureTable.InteriorWindow : FixtureTable.ExteriorWindow;

    private int EditContainer => side == Side.Interior ? FixtureTable.InteriorEdit : FixtureTable.ExteriorEdit;

    private string MenuName => side == Side.Interior ? "Remodel Interior" : "Remodel Exterior";

    private List<Step> Steps()
    {
        var steps = new List<Step>
        {
            new Step
            {
                Label = $"Get to the Housing menu (or the {MenuName} window)",
                Run = () =>
                {
                    if (GameUi.IsOpen(Window)) return StepOutcome.Done;
                    queue.InsertNext(ToHousingMenu());
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Choose {MenuName} in the Housing menu",
                Run = () =>
                {
                    if (GameUi.IsOpen(Window)) return StepOutcome.Done;
                    queue.InsertNext(ChooseMenuEntry());
                    return StepOutcome.Done;
                },
            },
            ReadSlots(),
        };
        foreach (var task in toSet) steps.AddRange(PutInSlot(task));
        // A rehearsal closes the window instead of applying: nothing is set.
        steps.AddRange(Rehearsal ? RehearseApply() : Apply());
        return steps;
    }

    /// <summary>The furnishing editor closed (its own close, as recorded), or the Housing menu opened (the housing agent).</summary>
    private List<Step> ToHousingMenu()
    {
        var mode = HousingEditor.Mode;
        if (mode == EditorMode.Place) return new List<Step> { Step.Wait("The editor has finished placing a piece", () => HousingEditor.Mode != EditorMode.Place, TimeSpan.FromSeconds(30)), new Step { Label = "Again: to the Housing menu", Run = () => { queue.InsertNext(ToHousingMenu()); return StepOutcome.Done; } } };
        if (mode != EditorMode.None)
        {
            var close = mode == EditorMode.Dye ? UiTable.DyeListClose : UiTable.LayoutClose;
            return new List<Step>
            {
                Step.Act($"Close the furnishing editor: {close.Describe()}", () => GameUi.Fire(close) ? StepOutcome.Done : StepOutcome.Halt($"{close.Addon} isn't open, so the editor couldn't be closed.")),
                Step.Wait("The Housing menu comes back", () => GameUi.IsOpen(UiTable.HousingMenu), WindowWait),
            };
        }
        if (GameUi.IsOpen(UiTable.HousingMenu)) return new List<Step>();
        return new List<Step>
        {
            Step.Act("Open the Housing menu (the housing agent)", () => HousingActions.OpenHousingMenu() ? StepOutcome.Done : StepOutcome.Halt("Couldn't reach the housing agent to open the Housing menu.")),
            Step.Wait("The Housing menu opens", () => GameUi.IsOpen(UiTable.HousingMenu), WindowWait, StepStatus.Done),
            new Step
            {
                Label = "Did the Housing menu open?",
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.HousingMenu) || GameUi.IsOpen(Window)) return StepOutcome.Done;
                    queue.InsertNext(new[] { YouWaitFor("You: open the Housing menu (Main menu → Social → Housing); it didn't open by itself", () => GameUi.IsOpen(UiTable.HousingMenu) || GameUi.IsOpen(Window), "The Housing menu didn't open.") });
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>
    /// The fixtures entry, by its words, at its place in the menu (only when the menu's first two
    /// entries read as the two recorded); else you choose it. Then the window, or a safe stop.
    /// </summary>
    private List<Step> ChooseMenuEntry()
    {
        var steps = new List<Step>();
        var entries = MenuTexts();
        var words = side == Side.Interior ? FixtureTable.InteriorMenuWords(Services.ClientState.ClientLanguage) : FixtureTable.ExteriorMenuWords(Services.ClientState.ClientLanguage);
        var entry = FixtureJobs.MenuEntry(entries, words);
        log.Info($"  The Housing menu's words: {(entries.Count == 0 ? "none read" : string.Join(" | ", entries.Select((e, i) => $"{i}:{e}")))}.");
        var offset = FixtureJobs.MenuOffset(entries);
        if (entry >= offset && offset >= 0)
        {
            // Its click is its place among the entries (the menu's title, read first, isn't one).
            var blanks = UiCallValues.Of(("entry", entry - offset));
            steps.Add(Step.Act($"Choose \"{entries[entry]}\": {FixtureTable.MenuEntry.Describe(blanks)} (a guess: its place in the menu)", () =>
                GameUi.Fire(FixtureTable.MenuEntry, blanks) ? StepOutcome.Done : StepOutcome.Halt("The Housing menu closed.")));
            steps.Add(Step.Wait($"The {MenuName} window ({Window}) opens", () => GameUi.IsOpen(Window), WindowWait, StepStatus.Done));
        }
        else
        {
            log.Info($"  {(entry < 0 ? $"No entry reads like \"{words[0]}\"" : "The menu's entries aren't read in their places")}: you're asked to choose it.");
        }
        steps.Add(new Step
        {
            Label = $"Is the {MenuName} window open?",
            Run = () =>
            {
                if (GameUi.IsOpen(Window)) return StepOutcome.Done;
                queue.InsertNext(new[] { YouWaitFor($"You: choose {MenuName} in the Housing menu", () => GameUi.IsOpen(Window), $"The {MenuName} window ({Window}) didn't open, so nothing was set.") });
                return StepOutcome.Done;
            },
        });
        return steps;
    }

    /// <summary>The Housing menu's text values, in order (its entries, if it holds them so).</summary>
    private static List<string> MenuTexts() =>
        GameUi.TextValues(UiTable.HousingMenu).OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(t => t.Length > 0).ToList();

    /// <summary>
    /// Once the window is up: its container's slots, logged, and checked against what's set (the
    /// order guessed in <see cref="FixtureJobs"/>). If they agree, fixtures go in their slots from
    /// here; if not, you put each one in (and the container is watched for it).
    /// </summary>
    private Step ReadSlots() => new()
    {
        Label = $"Read the {MenuName} window's slots (container {EditContainer})",
        Timeout = WindowWait,
        TimeoutMessage = $"The {MenuName} window's slots didn't load (container {EditContainer}), so nothing was set.",
        Run = () =>
        {
            var held = ContainerItems();
            if (held == null) return StepOutcome.Waiting;
            watchWindow = true;
            containerBefore = held.Values.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
            slotsAtOpen = held;
            log.Info($"  Its slots: {(held.Count == 0 ? "all empty" : string.Join(", ", held.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {data.AnyItemName(kv.Value)}")))}.");
            var current = Current(side, session.Context) ?? new List<CurrentFixture>();
            // 0.9.1: the slots seen holding what's set, and those kept from earlier runs (what's seen now wins).
            var seen = FixtureJobs.SlotsSeen(held, current);
            known = seen.Concat(FixtureJobs.KeptSlots(config.FixtureSlots, side)
                .Where(k => !seen.Any(x => FixtureJobs.Norm(x.Level) == FixtureJobs.Norm(k.Level) && FixtureJobs.Norm(x.Type) == FixtureJobs.Norm(k.Type)))
                // 0.9.9: a kept slot that's now seen holding another part is out of date.
                .Where(k => !seen.Any(x => x.Slot == k.Slot))).ToList();
            log.Info($"  Slots known: {(known.Count == 0 ? "none" : string.Join(", ", known.Select(k => $"{k.Level.ToLowerInvariant()} {k.Type.ToLowerInvariant()} → {k.Slot}")))}.");
            slotsTrusted = WorkOutSlots();
            log.Info(slotsTrusted
                ? $"  Each fixture's slot follows from those: {string.Join(", ", toSet.Select(t => $"{Describe(t)} → {SlotOf(t)}"))}. The plugin puts each one in."
                : $"  Not every fixture's slot follows from what's known ({(known.Count == 0 ? "nothing set here is in the window to go by" : "the slots seen don't fit one order")}): "
                  + "you put in the ones it can't tell, and the slot each goes to is learnt for next time.");
            return StepOutcome.Done;
        },
    };

    private List<Step> PutInSlot(FixtureTask task)
    {
        var name = data.AnyItemName(task.Fixture.ItemId);
        var where = Describe(task);
        return new List<Step>
        {
            new Step
            {
                Label = $"Put {name} in the {where} slot",
                Run = () =>
                {
                    if (InSlot(task)) return StepOutcome.Done;
                    if (!slotsTrusted || SlotOf(task) is not { } slot)
                    {
                        queue.InsertNext(new[] { YouPut(task) });
                        return StepOutcome.Done;
                    }
                    // 1.0.0: a slot from an earlier run (kept for another home) past this window's end, or
                    // holding another part now, isn't moved to: you put this one in, and its slot is learnt.
                    var size = ContainerSize();
                    if (size is not { } n || slot < 0 || slot >= n
                        || (ContainerItems() is { } held && held.TryGetValue(slot, out var there) && there != task.Fixture.ItemId))
                    {
                        log.Info($"  The {where} slot ({slot}) isn't free in this window ({(size is { } m ? $"{m} slots" : "not read")}): you put {name} in.");
                        queue.InsertNext(new[] { YouPut(task) });
                        return StepOutcome.Done;
                    }
                    var from = FindInBags(task.Fixture.ItemId);
                    if (from == null)
                    {
                        log.Warn($"  {name} isn't in your bags any more: it's left out.");
                        dropped.Add(task);
                        return StepOutcome.Done;
                    }
                    var (bag, bagSlot) = from.Value;
                    queue.InsertNext(new[]
                    {
                        Step.Act($"Move {name} from {bag} slot {bagSlot} to the window's slot {slot} (container {EditContainer}), as dragging it there does", () =>
                        {
                            var mgr = InventoryManager.Instance();
                            if (mgr == null) return StepOutcome.Halt("Couldn't reach your bags.");
                            var result = mgr->MoveItemSlot(bag, (ushort)bagSlot, (InventoryType)EditContainer, (ushort)slot);
                            log.Info($"  The game's item move returned {result}.");
                            return StepOutcome.Done;
                        }),
                        Step.Wait($"{name} is in its slot", () => InSlot(task), TimeSpan.FromSeconds(3), StepStatus.Done),
                        new Step
                        {
                            Label = $"Is {name} in its slot?",
                            Run = () =>
                            {
                                if (InSlot(task)) return StepOutcome.Done;
                                log.Warn($"  {name} didn't go in its slot from the plugin.");
                                queue.InsertNext(new[] { YouPut(task) });
                                return StepOutcome.Done;
                            },
                        },
                    });
                    return StepOutcome.Done;
                },
            },
        };
    }

    private Step YouPut(FixtureTask task) => YouWaitFor(
        $"You: put {data.AnyItemName(task.Fixture.ItemId)} in the {Describe(task)} slot of the {MenuName} window",
        () =>
        {
            // (Counted, as you may put it anywhere the game takes it; the slot it went to is learnt.)
            if (!InSlotByCount(task)) return false;
            LearnFromYou(task);
            return true;
        },
        $"{data.AnyItemName(task.Fixture.ItemId)} wasn't put in its slot.");

    /// <summary>
    /// Apply: its click isn't known, so you press it; the run waits until the game's own tables show
    /// the fixtures (the window may close then), and ticks each one set.
    /// </summary>
    private List<Step> Apply()
    {
        var apply = side == Side.Interior ? FixtureTable.InteriorApply : FixtureTable.ExteriorApply;
        var steps = new List<Step>();
        // When the run last said yes: the wait below doesn't say it again within a second (0.9.9).
        var saidYesAt = DateTime.MinValue;
        if (apply != null)
        {
            steps.Add(Step.Act($"Apply: {apply.Describe()}", () =>
            {
                watchWindow = false;
                return GameUi.Fire(apply) ? StepOutcome.Done : StepOutcome.Halt($"The {MenuName} window closed.");
            }));
            // "Proceed with fixture changes?" [Recording 2]: yes, only to that question.
            // Only the question ends this wait (0.8.0 also ended it when the window closed, which it can
            // a frame before the question shows: then nobody said yes). The question coming later is
            // answered while the run waits for the fixtures, below.
            steps.Add(Step.Wait("The game asks to proceed", () => GameUi.YesNoText() != null, WindowWait, StepStatus.Done));
            steps.Add(new Step
            {
                Label = "Answer yes to \"Proceed with fixture changes?\"",
                IsAction = true,
                Run = () =>
                {
                    if (GameUi.YesNoText() is not { } question) return StepOutcome.Done;
                    if (!FixtureTable.ApplyWords.Any(w => question.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        return StepOutcome.Halt($"The game asked \"{question}\", not to proceed with the fixtures: left for you.");
                    if (!GameUi.Fire(UiTable.YesNoYes)) return StepOutcome.Halt("The yes/no closed.");
                    saidYesAt = DateTime.UtcNow;
                    return StepOutcome.Done;
                },
            });
        }
        steps.Add(new Step
        {
            Label = apply != null ? "The fixtures are set" : $"You: apply the fixtures in the {MenuName} window (and say yes if it asks)",
            Timeout = ApplyWait,
            OnTimeout = StepStatus.Done,
            Run = () =>
            {
                // Applying may close the window: from here, that's expected.
                watchWindow = false;
                // The proceed question, if it came late: yes (the apply was the run's own click), once only
                // (1.0.0: again every second while it lingered); a question still up after that is yours.
                if (apply != null && saidYesAt == DateTime.MinValue && GameUi.YesNoText() is { } late
                    && FixtureTable.ApplyWords.Any(w => late.Contains(w, StringComparison.OrdinalIgnoreCase)))
                {
                    saidYesAt = DateTime.UtcNow;
                    GameUi.Fire(UiTable.YesNoYes);
                    return StepOutcome.Waiting;
                }
                // The game's tables, read a few times a second (not every frame).
                if (DateTime.UtcNow < nextSetCheck) return StepOutcome.Waiting;
                nextSetCheck = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                var now = Current(side, session.Context);
                return toSet.All(t => dropped.Contains(t) || IsSet(t, now)) ? StepOutcome.Done : StepOutcome.Waiting;
            },
        });
        steps.Add(new Step
        {
            Label = "Check each fixture in the game's tables",
            Run = () =>
            {
                var now = Current(side, session.Context);
                TickSet(toSet.Where(t => IsSet(t, now)).Select(t => t.Fixture));
                foreach (var t in toSet) log.Info($"  {Describe(t)}: {data.AnyItemName(t.Fixture.ItemId)} {(IsSet(t, now) ? "is set" : "isn't set")}.");
                return StepOutcome.Done;
            },
        });
        return steps;
    }

    /// <summary>On a stop: nothing is pressed; the window is left for you (apply what's in it, or close it to keep things as they were).</summary>
    private Step LeaveWindow() => new()
    {
        Label = "Leave the fixtures window to you",
        Run = () =>
        {
            if (GameUi.IsOpen(Window)) log.Info($"  The {MenuName} window is left open: apply what's in it, or close it to keep the fixtures as they were.");
            return StepOutcome.Done;
        },
    };

    private Step YouWaitFor(string label, Func<bool> until, string otherwise)
    {
        var asked = false;
        return new Step
        {
            Label = label,
            Timeout = YouWait,
            TimeoutMessage = otherwise,
            Run = () =>
            {
                if (until()) return StepOutcome.Done;
                if (!asked)
                {
                    asked = true;
                    if (config.ChatMessages) Services.Chat.Print(label[(label.IndexOf(':') + 1)..].Trim(), "Housing Studio");
                }
                return StepOutcome.Waiting;
            },
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Reading the game.

    /// <summary>What's set on a side now, or null when it can't be read (not in the home, or no plot).</summary>
    private List<CurrentFixture>? Current(Side here, HousingContext ctx)
    {
        try
        {
            if (here == Side.Interior)
            {
                if (ctx.Kind != PlaceKind.Interior) return null;
                return session.Fixtures.ReadInterior(ctx.HouseSize).Select(f => new CurrentFixture(f.Level, f.Type, f.ItemId, f.DyeHex)).ToList();
            }
            if (ctx.Kind != PlaceKind.Ward || ctx.Plot is not { } plot) return null;
            return (session.Fixtures.ReadExterior(plot) ?? new List<SavedFixture>()).Select(f => new CurrentFixture(f.Level, f.Type, f.ItemId, f.DyeHex)).ToList();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't read the fixtures");
            return null;
        }
    }

    /// <summary>Is the fixture set, in what's set now (read once for several).</summary>
    private static bool IsSet(FixtureTask task, List<CurrentFixture>? now)
    {
        return now != null && now.Any(c => FixtureJobs.Norm(c.Level) == FixtureJobs.Norm(task.Fixture.Level)
                                           && FixtureJobs.Norm(c.Type) == FixtureJobs.Norm(task.Fixture.Type) && c.ItemId == task.Fixture.ItemId);
    }

    /// <summary>How many slots the window's container has, or null while it isn't loaded.</summary>
    private int? ContainerSize()
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return null;
        var c = mgr->GetInventoryContainer((InventoryType)EditContainer);
        return c == null || !c->IsLoaded ? null : (int)c->Size;
    }

    /// <summary>The window's container by slot (empty slots left out), or null while it isn't loaded.</summary>
    private Dictionary<int, uint>? ContainerItems()
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return null;
        var c = mgr->GetInventoryContainer((InventoryType)EditContainer);
        if (c == null || !c->IsLoaded) return null;
        var items = new Dictionary<int, uint>();
        for (var i = 0; i < c->Size; i++)
        {
            var item = c->GetInventorySlot(i);
            if (item != null && !item->IsEmpty()) items[i] = item->GetBaseItemId();
        }
        return items;
    }

    /// <summary>Is the fixture in the window: in its slot (slots trusted), or one more of it than when the window opened.</summary>
    private bool InSlot(FixtureTask task)
    {
        var held = ContainerItems();
        if (held == null) return false;
        if (slotsTrusted && SlotOf(task) is { } slot) return held.TryGetValue(slot, out var item) && item == task.Fixture.ItemId;
        return InSlotByCount(task, held);
    }

    /// <summary>One more of the fixture in the window than when it opened (and than the ones before it of the same item).</summary>
    private bool InSlotByCount(FixtureTask task, Dictionary<int, uint>? held = null)
    {
        held ??= ContainerItems();
        if (held == null) return false;
        var wanted = containerBefore.GetValueOrDefault(task.Fixture.ItemId) + toSet.TakeWhile(t => t != task).Count(t => !dropped.Contains(t) && t.Fixture.ItemId == task.Fixture.ItemId) + 1;
        return held.Values.Count(i => i == task.Fixture.ItemId) >= wanted;
    }

    /// <summary>How many of an item are in your bags.</summary>
    private static int Owned(uint itemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;
        var n = 0;
        foreach (var t in InventoryReader.Bags)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item != null && !item->IsEmpty() && item->GetBaseItemId() == itemId) n += (int)Math.Max(1, item->GetQuantity());
            }
        }
        return n;
    }

    private static (InventoryType Bag, int Slot)? FindInBags(uint itemId)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return null;
        foreach (var t in InventoryReader.Bags)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item != null && !item->IsEmpty() && item->GetBaseItemId() == itemId) return (t, i);
            }
        }
        return null;
    }

    private void TickSet(IEnumerable<LayoutFixture> fixtures)
    {
        var changed = false;
        foreach (var f in fixtures)
        {
            var key = FixtureTicks.KeyOf(f.Level, f.Type, f.ItemId);
            if (FixtureTicks.IsDone(config.FixturesDone, designKey, key)) continue;
            FixtureTicks.Set(config.FixturesDone, designKey, key, true);
            changed = true;
        }
        if (changed) config.Save();
    }

    private string Summary()
    {
        if (Rehearsal) return RehearsalSummary();
        var now = Current(side, session.Context);
        var set = toSet.Count(t => IsSet(t, now));
        return set == toSet.Count ? $"{set} of {toSet.Count} set." : $"{set} of {toSet.Count} set; the rest are still to set (the log says what each step saw).";
    }

    private static string Describe(FixtureTask t) =>
        t.Fixture.Level == "Exterior" ? t.Fixture.Type.ToLowerInvariant() : $"{t.Fixture.Level.ToLowerInvariant()}'s {t.Fixture.Type.ToLowerInvariant()}";
}
