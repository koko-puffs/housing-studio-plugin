using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

public enum RunState
{
    Idle,
    Running,
    Paused,
    Finished,
    Stopped,
}

/// <summary>
/// "Start placing": one run that, piece by piece at the pace set in Settings,
/// <list type="number">
/// <item>moves pieces already placed to their spot (the editor's own pick-up and set-down, Rotate mode);</item>
/// <item>places pieces from the storeroom (finds their row in the furnishing list's Storeroom tab,
/// chooses Place in its menu) and from your bags (the item's menu, Place), aims the piece the editor
/// is then holding at its spot and sets it down, and makes sure it sits exactly there;</item>
/// <item>dyes every piece whose dye differs from the design (Furnishing Color mode and the game's dye
/// window), with dyes from your bags.</item>
/// </list>
/// It switches the editor's modes and the list's tab itself, with the clicks in <see cref="UiTable"/>.
/// Each piece is a list of small steps (<see cref="StepQueue"/>): every click is a window callback,
/// every wait has a timeout, and when a piece goes wrong whatever it opened is closed. Every step is
/// written to the activity log. Every frame it checks you're still in the same home with permission,
/// the editor open (bar while it switches modes), and not busy.
/// </summary>
public sealed unsafe class PlacementRunner : IDisposable
{
    private const float LandedDistance = 0.05f;
    private const float LandedYaw = 0.03f;
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ClickWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LandWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan YouWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ByHandWait = TimeSpan.FromMinutes(5);

    /// <summary>While switching modes the editor closes for a moment (back to the Housing menu): that isn't "the editor closed".</summary>
    private static readonly TimeSpan ModeSwitchGrace = TimeSpan.FromSeconds(8);

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly HousingEditor editor;
    private readonly HousingActions actions;
    private readonly FurnitureReader furniture;
    private readonly PlaceAnywhere anywhere;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly Random random = new();
    private readonly StepQueue queue = new(() => DateTime.UtcNow);

    private readonly List<RunJob> jobs = new();
    private List<PlanEntry> entries = new();
    private int index = -1;
    private DateTime nextJobAt;
    private bool dyesPlanned;
    private int failuresInARow;
    private HashSet<int> knownKeys = new();
    private DateTime editorMayCloseUntil;

    /// <summary>The furnishing list's storeroom rows, learnt as they're picked (kept for the whole run).</summary>
    private readonly StoreroomRows rows = new();
    private bool rowsReady;

    private PlaceKind startKind;
    private uint startTerritory;
    private ulong startHouse;
    private Side side;

    public PlacementRunner(Configuration config, LayoutSession session, HousingEditor editor, HousingActions actions, FurnitureReader furniture, PlaceAnywhere anywhere, GameData data, ActivityLog log)
    {
        this.config = config;
        this.session = session;
        this.editor = editor;
        this.actions = actions;
        this.furniture = furniture;
        this.anywhere = anywhere;
        this.data = data;
        this.log = log;
        queue.Trace += (step, what) =>
        {
            if (what == "start") log.Info($"  {(step.IsAction ? "→" : "…")} {step.Label}");
            else if (what != "done") log.Warn($"  ✕ {what}");
        };
    }

    public RunState State { get; private set; } = RunState.Idle;
    public bool DryRun { get; private set; }
    public string? StatusText { get; private set; }
    public string? StopReason { get; private set; }
    public IReadOnlyList<RunJob> Jobs => jobs;
    public bool Active => State is RunState.Running or RunState.Paused;
    public RunJob? Current => Active && index >= 0 && index < jobs.Count ? jobs[index] : null;

    /// <summary>The step being done or waited for.</summary>
    public string? StepLabel => Active && queue.State is QueueState.Running or QueueState.AwaitingContinue ? queue.Current?.Label : null;

    /// <summary>Where the step is in its piece's steps (1-based) and how many there are.</summary>
    public (int Index, int Count) StepProgress => (Math.Min(queue.Index + 1, queue.Count), queue.Count);

    /// <summary>Step-by-step mode is holding before this action.</summary>
    public bool AwaitingContinue => Active && queue.State == QueueState.AwaitingContinue;

    /// <summary>The run is waiting for you (to put a piece down, or press something it can't).</summary>
    public bool WaitingForYou => StepLabel is { } l && l.StartsWith("You:", StringComparison.Ordinal);

    public int Count(RunPhase phase, JobOutcome? outcome = null) =>
        jobs.Count(j => j.Phase == phase && (outcome == null || j.Outcome == outcome));

    public int Done => jobs.Count(j => j.Outcome == JobOutcome.Done);
    public int Failed => jobs.Count(j => j.Outcome == JobOutcome.Failed);
    public int Skipped => jobs.Count(j => j.Outcome == JobOutcome.Skipped);
    public int Total => jobs.Count;

    /// <summary>The dyes have been planned (the dye phase's pieces are in <see cref="Jobs"/>).</summary>
    public bool DyesPlanned => dyesPlanned;

    /// <summary>Why the run can't start right now, or null.</summary>
    public string? CanStart(bool dryRun)
    {
        if (Active) return "Already running.";
        if (session.PlacingBlocked(out var why)) return why;
        var ctx = session.Context;
        if (ctx.Busy) return "Wait until you're free (not in combat, a cutscene or between areas).";
        if (dryRun) return null;
        if (!editor.Ready) return editor.Error;
        if (!ctx.EditorOpen && !GameUi.IsOpen(UiTable.HousingMenu)) return ctx.Kind == PlaceKind.Interior
            ? "Open Housing → Indoor Furnishings (or just the Housing menu) first."
            : "Open Housing → Outdoor Furnishings on your plot first.";
        return null;
    }

    public void Start(bool dryRun, IEnumerable<PlanEntry> shown)
    {
        var problem = CanStart(dryRun);
        if (problem != null)
        {
            StopReason = problem;
            log.Warn(problem);
            return;
        }

        entries = shown.ToList();
        jobs.Clear();
        jobs.AddRange(RunSequence.Build(entries));
        var needDyes = config.DyeAfterPlacing && entries.Any(e => e.State != PieceState.Missing && e.Target.Stain != e.CurrentStain);
        if (jobs.Count == 0 && !needDyes)
        {
            StopReason = "Nothing to do: every piece shown is in place, in its dye (or missing).";
            log.Info(StopReason);
            return;
        }

        var ctx = session.Context;
        startKind = ctx.Kind;
        startTerritory = ctx.TerritoryId;
        startHouse = ctx.HouseId;
        side = ctx.Side!.Value;
        DryRun = dryRun;
        index = -1;
        dyesPlanned = !config.DyeAfterPlacing;
        failuresInARow = 0;
        rowsReady = false;
        StopReason = null;
        knownKeys = session.Live.Keys.ToHashSet();
        queue.StepByStep = config.StepByStep && !dryRun;
        queue.ActionGap = TimeSpan.FromMilliseconds(dryRun ? 0 : 300);
        queue.Begin(Array.Empty<Step>());
        nextJobAt = DateTime.UtcNow;
        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;

        if (!dryRun && config.BackupBeforePlacing) Backups.Save(session, data, log, "before placing");
        if (!dryRun && config.PlaceAnywhere && !anywhere.Enable()) log.Warn(anywhere.Error ?? "Couldn't switch on place anywhere; placing with the game's usual checks.");

        log.Info($"{(dryRun ? "Dry run" : "Placing")} started: {Count(RunPhase.Move)} to move, {Count(RunPhase.Place)} to place "
                 + $"({jobs.Count(j => j.Kind == JobKind.PlaceFromStoreroom)} from the storeroom, {jobs.Count(j => j.Kind == JobKind.PlaceFromBags)} from your bags)"
                 + $"{(config.DyeAfterPlacing ? ", then dyes" : string.Empty)}.{(queue.StepByStep ? " Step by step: press Continue before each click." : string.Empty)}");
        State = RunState.Running;
        StatusText = "Starting…";
    }

    public void Pause(string why)
    {
        if (State != RunState.Running) return;
        State = RunState.Paused;
        StatusText = why;
        log.Warn($"Paused: {why} (a step that's waiting carries on waiting when you resume)");
        if (config.ChatMessages) Services.Chat.Print($"Paused: {why}", "Housing Studio");
    }

    public void Resume()
    {
        if (State != RunState.Paused) return;
        State = RunState.Running;
        failuresInARow = 0;
        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
        log.Info("Resumed.");
    }

    /// <summary>Step-by-step mode: do the click it's holding before.</summary>
    public void Continue() => queue.Continue();

    public void Stop(string reason)
    {
        if (!Active) return;
        if (queue.State is QueueState.Running or QueueState.AwaitingContinue) queue.Abort(reason);
        if (Current is { Outcome: JobOutcome.Working } job)
        {
            job.Outcome = JobOutcome.Skipped;
            job.Note = "Stopped.";
        }
        State = RunState.Stopped;
        StopReason = reason;
        StatusText = reason;
        anywhere.Disable();
        LogRows();
        log.Warn($"Stopped: {reason}");
        if (config.ChatMessages) Services.Chat.PrintError($"Stopped: {reason}", "Housing Studio");
    }

    /// <summary>Skips the piece being worked on (closing whatever it opened).</summary>
    public void SkipCurrent()
    {
        if (Current is not { } job) return;
        queue.Abort("Skipped by you.");
        job.Outcome = JobOutcome.Skipped;
        job.Note = "Skipped by you.";
        log.Info($"Skipped {Name(job)}.");
        NextAfter(quick: true);
    }

    /// <summary>Every frame, on the framework thread.</summary>
    public void Tick(HousingContext ctx)
    {
        if (!Active) return;

        if (ctx.Kind != startKind || ctx.TerritoryId != startTerritory || ctx.HouseId != startHouse)
        {
            Stop("You left the home this was started in.");
            return;
        }
        if (ctx.Busy)
        {
            Stop("The game got busy (combat, a cutscene, or a zone change).");
            return;
        }
        if (!DryRun && !ctx.HasPermission)
        {
            Stop("You're no longer allowed to decorate here.");
            return;
        }
        if (State == RunState.Paused) return;
        if (!DryRun && !ctx.EditorOpen && DateTime.UtcNow > editorMayCloseUntil && !GameUi.IsOpen(UiTable.HousingMenu))
        {
            Stop("The furnishing editor closed.");
            return;
        }

        switch (queue.State)
        {
            case QueueState.Running:
                queue.Tick();
                StatusText = queue.State == QueueState.AwaitingContinue ? $"Next: {queue.Current?.Label}" : StepLabel ?? StatusText;
                AfterQueue();
                return;
            case QueueState.AwaitingContinue:
                return;
            default:
                if (DateTime.UtcNow >= nextJobAt) StartNextJob();
                return;
        }
    }

    private void AfterQueue()
    {
        if (Current is not { } job) return;
        switch (queue.State)
        {
            case QueueState.Finished:
                job.Outcome = JobOutcome.Done;
                job.Note ??= DryRun ? "Dry run: nothing touched." : "Done.";
                if (!DryRun) log.Done($"{Name(job)}: {job.Note}");
                failuresInARow = 0;
                NextAfter(quick: false);
                break;
            case QueueState.Skipped:
                job.Outcome = JobOutcome.Failed;
                job.Note = queue.Message;
                log.Warn($"{Name(job)}: {queue.Message}");
                failuresInARow++;
                if (failuresInARow >= Math.Max(1, config.MaxFailuresInARow))
                {
                    Stop($"{failuresInARow} pieces in a row didn't work, so something's wrong. The activity log says what each step saw.");
                    return;
                }
                NextAfter(quick: false);
                break;
            case QueueState.Stopped:
                job.Outcome = JobOutcome.Failed;
                job.Note = queue.Message;
                Stop(queue.Message ?? "A step didn't work.");
                break;
        }
    }

    private void NextAfter(bool quick)
    {
        var delay = quick ? 150 : Pacing.NextDelayMs(config.IntervalMs, config.Jitter, random.NextDouble());
        if (DryRun) delay /= 2;
        nextJobAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(delay);
        queue.Begin(Array.Empty<Step>());
    }

    private void StartNextJob()
    {
        index++;
        if (index >= jobs.Count)
        {
            if (!dyesPlanned)
            {
                dyesPlanned = true;
                PlanDyes();
                if (index < jobs.Count)
                {
                    index--;
                    nextJobAt = DateTime.UtcNow;
                    return;
                }
            }
            Finish();
            return;
        }

        var job = jobs[index];
        job.Outcome = JobOutcome.Working;
        log.Info($"{PhaseWord(job.Phase)}: {Name(job)}{(job.Target.Stain != 0 ? $" ({data.DyeName(job.Target.Stain)})" : string.Empty)}");
        if (DryRun)
        {
            queue.Begin(new[] { Step.Act($"Would {DryWords(job)}", () => StepOutcome.Done) });
            return;
        }
        var (steps, cleanup) = Script(job);
        queue.Begin(steps, cleanup);
    }

    // ---------------------------------------------------------------------------------------------
    // Scripts: the steps for each kind of piece.

    private (List<Step> Steps, List<Step> Cleanup) Script(RunJob job) => job.Kind switch
    {
        JobKind.Move => (MoveSteps(job, job.Key!.Value), new List<Step>()),
        JobKind.PlaceFromStoreroom when side == Side.Interior && config.StoreroomAutomatic => (StoreroomSteps(job), MenuCleanup()),
        JobKind.PlaceFromBags when config.BagsAutomatic => (BagSteps(job), MenuCleanup()),
        JobKind.Dye => (DyeSteps(job), DyeCleanup()),
        _ => (ByHandSteps(job), new List<Step>()),
    };

    private List<Step> MoveSteps(RunJob job, int key)
    {
        var name = Name(job);
        nint instance = 0;
        return new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Act($"Pick up {name} (the editor's pick-up function)", () =>
            {
                var live = ReadLive();
                if (live == null || !live.TryGetValue(key, out var piece)) return StepOutcome.Skip("It's no longer where the plan saw it (moved, stored, or the list changed).");
                if (piece.ItemId != job.Target.ItemId)
                    return StepOutcome.Halt($"The furniture list changed under the plan (slot {key} is now {data.ItemName(piece.ItemId)}). Load the plan again and start over.");
                if (Landed(key, job.Target))
                {
                    job.Note ??= "Already in place.";
                    queue.FinishAfterThis();
                    return StepOutcome.Done;
                }
                if (piece.Instance == 0) return StepOutcome.Skip("The game hasn't loaded it (walk closer to it).");
                instance = piece.Instance;
                return editor.Select(instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't pick the piece up (the editor closed?).");
            }),
            Step.Wait("The game picks it up", () => HousingEditor.ActiveItem == instance, TimeSpan.FromSeconds(1.5), StepStatus.SkipJob),
            Step.Act($"Set {name} at its spot (the editor's set-down function)", () =>
            {
                if (HousingEditor.ActiveItem != instance) return StepOutcome.Skip("The game dropped the piece before it could be set.");
                var sg = (SharedGroupLayoutInstance*)instance;
                sg->Transform.Translation = job.Target.Position;
                sg->Transform.Rotation = Transforms.YawQuaternion(job.Target.Yaw);
                return editor.Place(instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't set the piece down (the editor closed?).");
            }, TimeSpan.FromMilliseconds(150)),
            new Step
            {
                Label = "It lands at its spot",
                Run = () => Landed(key, job.Target) ? StepOutcome.Done : StepOutcome.Waiting,
                Timeout = TimeSpan.FromSeconds(3),
                OnTimeout = StepStatus.SkipJob,
                Delay = TimeSpan.FromMilliseconds(200),
                TimeoutMessage = "It didn't end up at its spot. The game refuses floating or overlapping pieces; \"Place anywhere\" in Settings allows them.",
            },
        };
    }

    private List<Step> StoreroomSteps(RunJob job)
    {
        var name = Name(job);
        var chosen = new int[] { -1 };
        var steps = new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Wait($"The furnishing list ({UiTable.FurnishingList}) is open", () => GameUi.IsOpen(UiTable.FurnishingList), WindowWait),
            Step.Act($"Open the Storeroom tab: {UiTable.ListStoreroomTab.Describe()}", () =>
            {
                if (InventoryReader.StoreroomTabShown()) return StepOutcome.Done;
                return Fire(UiTable.ListStoreroomTab);
            }),
            Step.Wait("The Storeroom tab shows", InventoryReader.StoreroomTabShown, WindowWait),
            Step.Wait("The storeroom is loaded", () => InventoryReader.StoreroomLoaded(side), WindowWait),
            Step.Act($"Find {name}'s row in the list", () =>
            {
                var occupied = InventoryReader.StoreroomOccupied(side);
                if (!occupied.Any(o => o.ItemId == job.Target.ItemId)) return StepOutcome.Skip("It's not in the storeroom any more.");
                if (!rowsReady || rows.Count != occupied.Count)
                {
                    if (rowsReady) log.Info($"  The storeroom now has {occupied.Count} pieces (the list had {rows.Count} rows): learning the rows again.");
                    rows.Reset(occupied.Count);
                    rowsReady = true;
                }
                var row = rows.Find(job.Target.ItemId, job.Target.Stain);
                if (row != null) log.Info($"  Row {row} held {name} when it was picked earlier.");
                else log.Info($"  {name}'s row isn't known yet: picking rows from {rows.NextUnknown()} down, remembering each ({rows.Known.Count} of {rows.Count} known).");
                var next = row ?? rows.NextUnknown();
                if (next == null) return StepOutcome.Skip("Every row of the list was picked and none held it.");
                queue.InsertNext(TryRow(job, next.Value, row != null, chosen));
                return StepOutcome.Done;
            }),
        };
        steps.Add(ChoosePlace("the storeroom row's menu"));
        steps.Add(AnswerYesNo());
        steps.AddRange(HeldSteps(job, StepStatus.Stop, () =>
        {
            // The row is gone from the list once its piece is placed (unless it was a stack of several).
            var r = chosen[0];
            if (r >= 0 && rows.Known.TryGetValue(r, out var what) && !InventoryReader.StillHolds(new StockSlot(StockSource.Storeroom, what.Container, what.Slot, what.ItemId, 0, 1)))
                rows.Removed(r);
        }));
        return steps;
    }

    /// <summary>Picks a row; learns what it holds; if it isn't the piece, closes its menu and tries the next.</summary>
    private List<Step> TryRow(RunJob job, int row, bool wasKnown, int[] chosen)
    {
        var name = Name(job);
        var blanks = UiCallValues.Of(("index", row));
        return new List<Step>
        {
            Step.Act($"Pick row {row}: {UiTable.ListSelect.Describe(blanks)}", () =>
                GameUi.Fire(UiTable.ListSelect, blanks) ? StepOutcome.Done : StepOutcome.Halt("The furnishing list closed.")),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu) && InventoryReader.StoreroomSelection() != null, WindowWait),
            Step.Act("See which piece the game picked", () =>
            {
                var (container, slot) = InventoryReader.StoreroomSelection()!.Value;
                var held = InventoryReader.StoreroomOccupied(side).FirstOrDefault(o => o.Container == container && o.Slot == slot);
                var seen = new StoreroomRow(container, slot, held.ItemId, held.Stain);
                if (wasKnown && !rows.Matches(row, seen))
                {
                    log.Info($"  Row {row} now picks storeroom {container}/{slot} ({data.ItemName(held.ItemId)}), not what it did: the list changed, learning it again.");
                    rows.Reset(rows.Count);
                }
                if (held.ItemId != 0) rows.Learn(row, seen);
                var right = held.ItemId == job.Target.ItemId;
                log.Info($"  row {row} → storeroom {container}/{slot}: {(held.ItemId == 0 ? "empty?" : data.ItemName(held.ItemId))}{(right ? " (this one)" : string.Empty)}");
                if (right)
                {
                    chosen[0] = row;
                    return StepOutcome.Done;
                }
                GameUi.Fire(UiTable.MenuCancel);
                var after = new List<Step> { Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait) };
                var next = rows.Find(job.Target.ItemId, job.Target.Stain) ?? rows.NextUnknown();
                if (next == null)
                    after.Add(Step.Act("Give up looking", () => StepOutcome.Skip($"Every row of the list was picked and none held {name}.")));
                else
                    after.AddRange(TryRow(job, next.Value, rows.Known.ContainsKey(next.Value), chosen));
                queue.InsertNext(after);
                return StepOutcome.Done;
            }),
        };
    }

    private List<Step> BagSteps(RunJob job)
    {
        var name = Name(job);
        var stock = job.Entry?.Stock;
        var steps = new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Act($"Open {name}'s menu ({(stock != null ? InventoryReader.Describe(stock) : "bags")})", () =>
            {
                var slot = FindInBags(job.Target.ItemId, stock);
                if (slot == null) return StepOutcome.Skip("It's not in your bags any more.");
                log.Info($"  {name}: container {slot.Container}, slot {slot.Slot}; menu opened from window id {GameUi.BagWindowId()}.");
                return HousingActions.OpenBagItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
            }),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
            ChoosePlace("the item's menu"),
            AnswerYesNo(),
        };
        steps.AddRange(HeldSteps(job, StepStatus.Stop, null));
        return steps;
    }

    /// <summary>
    /// After "Place": the editor holds the piece (mode Place, the piece as its active item, no
    /// preview, as the recording showed). Aim it at its spot, set it down, wait for it to appear,
    /// and nudge it onto the spot if it's off.
    /// </summary>
    private List<Step> HeldSteps(RunJob job, StepStatus onNoLanding, Action? landed)
    {
        var name = Name(job);
        return new List<Step>
        {
            Step.Wait("The editor holds the piece (Place mode)", () => HousingEditor.Mode == EditorMode.Place && HousingEditor.ActiveItem != 0, WindowWait),
            Step.Act($"Aim {name} at its spot", () =>
            {
                HousingActions.AimHeld(job.Target.Position, job.Target.Yaw);
                return StepOutcome.Done;
            }, TimeSpan.FromMilliseconds(200)),
            Step.Act($"Set {name} down (the editor's set-down on the held piece)", () =>
            {
                HousingActions.AimHeld(job.Target.Position, job.Target.Yaw);
                // Only a piece the editor is placing right now: never one already in the room.
                var held = HousingEditor.Mode == EditorMode.Place ? HousingEditor.ActiveItem : 0;
                if (held == 0)
                    return StepOutcome.Halt($"The editor isn't holding a piece to set down (mode {HousingEditor.Mode}).");
                return editor.Place(held) ? StepOutcome.Done : StepOutcome.Halt("Couldn't set the held piece down.");
            }, TimeSpan.FromMilliseconds(150)),
            NewPiece(job, LandWait, onNoLanding,
                "The held piece wasn't set down automatically. Click to put it down (or press Escape); then send a recording (Settings → Diagnostics). "
                + "Until it's fixed, switch \"Place … for me\" off in Settings: you put each piece down anywhere, the plugin sets it at its spot.",
                landed),
        };
    }

    /// <summary>For what isn't automated (or switched to by hand): you put the piece down anywhere, the plugin sets it at its spot.</summary>
    private List<Step> ByHandSteps(RunJob job)
    {
        var name = Name(job);
        var where = job.Entry?.Stock is { } s ? InventoryReader.Describe(s) : "your bags or the storeroom";
        var announced = false;
        return new List<Step>
        {
            new Step
            {
                Label = $"You: put {name} down anywhere (it's in {where}); it'll be set at its spot",
                Run = () =>
                {
                    if (!announced)
                    {
                        announced = true;
                        if (config.ChatMessages) Services.Chat.Print($"Put {name} down anywhere (from {where}); Housing Studio sets it at its spot.", "Housing Studio");
                    }
                    return FindNewPiece(job) ? StepOutcome.Done : StepOutcome.Waiting;
                },
                Timeout = ByHandWait,
            },
            Step.Act($"Set {name} at its spot", () =>
            {
                queue.InsertNext(MoveSteps(job, job.Key!.Value));
                return StepOutcome.Done;
            }),
        };
    }

    private List<Step> DyeSteps(RunJob job)
    {
        var name = Name(job);
        var key = job.Key!.Value;
        var dye = data.DyeName(job.Target.Stain);
        var slot = data.DyeSlot(job.Target.Stain);
        var row = new int[] { -1 };
        return new List<Step>
        {
            EnsureMode(EditorMode.Dye),
            Step.Act($"Check {name} still needs {dye}", () =>
            {
                if (!actions.CanDye) return StepOutcome.Halt(actions.DyeError!);
                if (StainOf(key) == job.Target.Stain)
                {
                    job.Note = "Already in its dye.";
                    queue.FinishAfterThis();
                    return StepOutcome.Done;
                }
                var owned = HousingActions.DyesOwned(data, job.Target.Stain);
                if (owned <= 0) log.Info($"  None of {dye}'s own dye items in your bags; trying anyway (the window may offer another dye, as it offered Standard Spectrum Dye in a recording).");
                return StepOutcome.Done;
            }),
            Step.Act($"Close the dye window if it's open: {UiTable.DyeClose.Describe()}", () =>
            {
                if (GameUi.IsOpen(UiTable.DyeWindow)) GameUi.Fire(UiTable.DyeClose);
                return StepOutcome.Done;
            }),
            Step.Wait("The dye window is closed", () => !GameUi.IsOpen(UiTable.DyeWindow), WindowWait),
            Step.Act($"Pick {name}", () =>
            {
                var live = ReadLive();
                if (live == null || !live.TryGetValue(key, out var piece) || piece.ItemId != job.Target.ItemId)
                    return StepOutcome.Skip("It's no longer where the plan saw it.");
                if (piece.Instance == 0) return StepOutcome.Skip("The game hasn't loaded it (walk closer to it).");
                return editor.Select(piece.Instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't pick the piece.");
            }),
            new Step
            {
                // In the dye mode, picking a piece opens the dye window (the recording); if it doesn't, open it on the picked piece.
                Label = $"The dye window ({UiTable.DyeWindow}) opens",
                IsAction = true,
                Delay = TimeSpan.FromMilliseconds(200),
                Timeout = WindowWait,
                Run = Opener(),
            },
            Step.Act($"Find {dye} in the dye window's list", () =>
            {
                var r = DyeRow(job.Target.Stain);
                if (r == null && slot is { } sl && UiTable.ShadeIndex(sl.Shade) is { } shade)
                {
                    var values = UiCallValues.Of(("shade", shade));
                    log.Info($"  {dye} isn't in the list shown: {UiTable.DyeShade.Describe(values)}");
                    GameUi.Fire(UiTable.DyeShade, values);
                    queue.InsertNext(new[]
                    {
                        Step.Act($"Find {dye} in the shade's list", () =>
                        {
                            row[0] = DyeRow(job.Target.Stain) ?? Fallback();
                            return StepOutcome.Done;
                        }, TimeSpan.FromMilliseconds(300)),
                    });
                    return StepOutcome.Done;
                }
                row[0] = r ?? Fallback();
                return StepOutcome.Done;

                int Fallback()
                {
                    var f = slot?.SubOrder ?? 0;
                    log.Info($"  {dye} not found by name in the window; using its place in the dye sheet ({f}).");
                    return f;
                }
            }),
            Step.Act($"Choose {dye}", () =>
            {
                var values = UiCallValues.Of(("row", row[0]), ("stain", job.Target.Stain));
                log.Info($"  → {UiTable.DyePick.Describe(values)}");
                return Fire(UiTable.DyePick, values);
            }, TimeSpan.FromMilliseconds(200)),
            Step.Act($"Press Dye: {UiTable.DyeApply.Describe()}", () => Fire(UiTable.DyeApply), TimeSpan.FromMilliseconds(200)),
            new Step
            {
                Label = $"Confirm: {UiTable.DyeConfirmYes.Describe()}",
                IsAction = true,
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.DyeConfirm))
                    {
                        log.Info($"  The game asks: {string.Join(" ", GameUi.TextValues(UiTable.DyeConfirm).Values.Where(v => v.Length > 0).Take(4))}");
                        GameUi.Fire(UiTable.DyeConfirmYes);
                        return StepOutcome.Done;
                    }
                    return StainOf(key) == job.Target.Stain ? StepOutcome.Done : StepOutcome.Waiting;
                },
                Timeout = WindowWait,
                OnTimeout = StepStatus.SkipJob,
                TimeoutMessage = $"Neither the dye confirmation appeared nor the dye changed. The Dye button may be greyed out (no {dye}, or this piece can't take it).",
            },
            new Step
            {
                Label = $"{name} turns {dye}",
                Run = () => StainOf(key) == job.Target.Stain ? StepOutcome.Done : StepOutcome.Waiting,
                Timeout = TimeSpan.FromSeconds(4),
                OnTimeout = StepStatus.SkipJob,
                TimeoutMessage = $"Its dye didn't change to {dye}.",
            },
            Step.Act("Close the dye window", () =>
            {
                if (GameUi.IsOpen(UiTable.DyeWindow)) GameUi.Fire(UiTable.DyeClose);
                job.Note ??= $"Dyed {dye}.";
                return StepOutcome.Done;
            }),
        };

        Func<StepOutcome> Opener()
        {
            DateTime? since = null;
            var asked = false;
            return () =>
            {
                since ??= DateTime.UtcNow;
                if (GameUi.IsOpen(UiTable.DyeWindow)) return StepOutcome.Done;
                if (!asked && DateTime.UtcNow - since > TimeSpan.FromSeconds(0.8))
                {
                    asked = true;
                    log.Info("  The dye window didn't open on its own: opening it on the picked piece.");
                    if (!actions.OpenDyeWindow()) return StepOutcome.Skip("The game didn't say which piece is picked.");
                }
                return StepOutcome.Waiting;
            };
        }
    }

    /// <summary>The dye's row in the dye window's list (the list starts with "No Color" at <see cref="UiTable.DyeListFirstValue"/>), or null.</summary>
    private int? DyeRow(byte stain)
    {
        var name = data.DyeName(stain);
        foreach (var (i, text) in GameUi.TextValues(UiTable.DyeWindow).OrderBy(kv => kv.Key))
            if (i >= UiTable.DyeListFirstValue && string.Equals(text, name, StringComparison.OrdinalIgnoreCase))
                return i - UiTable.DyeListFirstValue;
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Shared steps.

    /// <summary>
    /// Gets the editor into a mode with the clicks a recording showed (<see cref="ModeRoute"/>): the
    /// bar's Move/Rotate, or closing the editor and choosing Indoor Furnishings or Furnishing Color in
    /// the Housing menu. If there's no known way from where it is, asks you.
    /// </summary>
    private Step EnsureMode(EditorMode mode) => new()
    {
        Label = $"Check the editor is in {ModeWord(mode)}",
        Timeout = TimeSpan.FromSeconds(30),
        TimeoutMessage = $"The editor couldn't be put in {ModeWord(mode)} (it's still placing a piece?).",
        Run = () =>
        {
            var now = HousingEditor.Mode;
            if (now == mode) return StepOutcome.Done;
            var route = ModeRoute.Plan(now, mode, GameUi.IsOpen(UiTable.HousingMenu));
            if (route == null)
            {
                if (now == EditorMode.Place) return StepOutcome.Waiting; // a piece is being placed; wait for it
                queue.InsertNext(new[] { AskForMode(mode) });
                return StepOutcome.Done;
            }
            log.Info($"  The editor is in {ModeWord(now)}; switching to {ModeWord(mode)}: {string.Join(", then ", route)}.");
            var steps = new List<Step>();
            foreach (var click in route)
            {
                var call = CallOf(click);
                steps.Add(Step.Act($"{ClickWord(click)}: {call.Describe()}", () =>
                {
                    editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                    if (GameUi.Fire(call)) return StepOutcome.Done;
                    if (click == ModeClick.CloseDyeList && GameUi.IsOpen(UiTable.DyeList))
                    {
                        GameUi.Close(UiTable.DyeList);
                        return StepOutcome.Done;
                    }
                    return StepOutcome.Halt($"{call.Addon} isn't open, so the editor couldn't be switched to {ModeWord(mode)}.");
                }));
                var after = ModeRoute.After(click);
                steps.Add(after == EditorMode.None
                    ? Step.Wait("The Housing menu comes back", () => GameUi.IsOpen(UiTable.HousingMenu), ClickWait)
                    : Step.Wait($"The editor is in {ModeWord(after)}", () =>
                    {
                        editorMayCloseUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                        return HousingEditor.Mode == after;
                    }, ClickWait));
            }
            queue.InsertNext(steps);
            return StepOutcome.Done;
        },
    };

    private Step AskForMode(EditorMode mode)
    {
        var asked = false;
        return new Step
        {
            Label = $"You: put the editor in {ModeWord(mode)} (Housing → {(mode == EditorMode.Dye ? "Furnishing Color" : "Indoor Furnishings")})",
            Timeout = YouWait,
            TimeoutMessage = $"The editor wasn't put in {ModeWord(mode)}.",
            Run = () =>
            {
                editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                if (HousingEditor.Mode == mode) return StepOutcome.Done;
                if (!asked)
                {
                    asked = true;
                    if (config.ChatMessages) Services.Chat.Print($"Put the furnishing editor in {ModeWord(mode)} to carry on.", "Housing Studio");
                }
                return StepOutcome.Waiting;
            },
        };
    }

    private static UiCall CallOf(ModeClick click) => click switch
    {
        ModeClick.Rotate => UiTable.LayoutRotate,
        ModeClick.Move => UiTable.LayoutMove,
        ModeClick.CloseEditor => UiTable.LayoutClose,
        ModeClick.CloseDyeList => UiTable.DyeListClose,
        ModeClick.OpenFurnishings => UiTable.MenuFurnishings,
        _ => UiTable.MenuDye,
    };

    private static string ClickWord(ModeClick click) => click switch
    {
        ModeClick.Rotate => "Press Rotate",
        ModeClick.Move => "Press Move",
        ModeClick.CloseEditor => "Close the furnishing editor",
        ModeClick.CloseDyeList => "Close Furnishing Color",
        ModeClick.OpenFurnishings => "Choose Indoor Furnishings",
        _ => "Choose Furnishing Color",
    };

    public static string ModeWord(EditorMode m) => m switch
    {
        EditorMode.Rotate => "Rotate",
        EditorMode.Move => "Move",
        EditorMode.Dye => "Furnishing Color (dye)",
        EditorMode.Place => "Place",
        EditorMode.None => "no mode (closed)",
        _ => m.ToString(),
    };

    private Step ChoosePlace(string where)
    {
        var labels = PlaceLabels();
        return Step.Act($"Choose \"{labels.FirstOrDefault()}\" in {where}", () =>
        {
            var menu = GameUi.MenuEntries();
            if (menu == null) return StepOutcome.Halt($"The menu closed before \"{labels.FirstOrDefault()}\" could be chosen.");
            var entry = Menus.FindEntry(menu, labels);
            log.Info($"  menu entries: {string.Join(" | ", menu.Select((e, i) => $"{i}:{e}"))}");
            if (entry < 0)
                return StepOutcome.Halt($"The menu has no entry like \"{string.Join("\" / \"", labels)}\" (it has: {string.Join(", ", menu)}). Type the right words in Settings → Diagnostics.");
            var blanks = UiCallValues.Of(("entry", entry));
            log.Info($"  → {UiTable.MenuChoose.Describe(blanks)}");
            return GameUi.Fire(UiTable.MenuChoose, blanks) ? StepOutcome.Done : StepOutcome.Halt("The menu closed.");
        }, TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Presses Yes if the game asks a yes/no question within a moment (logging the question); goes on if it doesn't.</summary>
    private Step AnswerYesNo()
    {
        DateTime? since = null;
        return new Step
        {
            Label = $"Answer Yes if the game asks ({UiTable.YesNo})",
            Run = () =>
            {
                since ??= DateTime.UtcNow;
                if (HousingEditor.Mode == EditorMode.Place) return StepOutcome.Done; // already holding it: nothing was asked
                var question = GameUi.YesNoText();
                if (question != null)
                {
                    log.Info($"  The game asks: \"{question}\" → {UiTable.YesNoYes.Describe()}");
                    GameUi.Fire(UiTable.YesNoYes);
                    return StepOutcome.Done;
                }
                return DateTime.UtcNow - since > TimeSpan.FromSeconds(1.2) ? StepOutcome.Done : StepOutcome.Waiting;
            },
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

    /// <summary>Waits for the new piece to appear, then (if it isn't exactly at its spot) moves it there.</summary>
    private Step NewPiece(RunJob job, TimeSpan timeout, StepStatus onTimeout, string why, Action? landed) => new()
    {
        Label = $"{Name(job)} appears in the room",
        Run = () =>
        {
            if (!FindNewPiece(job)) return StepOutcome.Waiting;
            landed?.Invoke();
            if (!Landed(job.Key!.Value, job.Target)) queue.InsertNext(MoveSteps(job, job.Key.Value));
            else job.Note = "Placed at its spot.";
            return StepOutcome.Done;
        },
        Timeout = timeout,
        OnTimeout = onTimeout,
        Delay = TimeSpan.FromMilliseconds(300),
        TimeoutMessage = why,
    };

    private static List<Step> MenuCleanup() => new()
    {
        new Step
        {
            Label = "Close the menu it opened",
            Run = () =>
            {
                if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                return StepOutcome.Done;
            },
        },
    };

    private static List<Step> DyeCleanup() => new()
    {
        new Step
        {
            Label = "Close the dye windows it opened",
            Run = () =>
            {
                if (GameUi.IsOpen(UiTable.DyeConfirm)) GameUi.Fire(UiTable.DyeConfirmNo);
                if (GameUi.IsOpen(UiTable.DyeWindow)) GameUi.Fire(UiTable.DyeClose);
                return StepOutcome.Done;
            },
        },
    };

    // ---------------------------------------------------------------------------------------------

    private void PlanDyes()
    {
        var live = ReadLive();
        if (live == null) return;
        var keyOf = new Dictionary<(int, uint), int>();
        foreach (var j in jobs.Where(j => j.Key != null && j.Outcome == JobOutcome.Done)) keyOf[(j.Target.Key, j.Target.ItemId)] = j.Key!.Value;
        var needs = new List<DyeNeed>();
        foreach (var e in entries)
        {
            if (e.State == PieceState.Missing) continue;
            int? key = keyOf.TryGetValue((e.Target.Key, e.Target.ItemId), out var k) ? k : e.State == PieceState.InPlace ? e.Placed?.Key : null;
            if (key == null || !live.TryGetValue(key.Value, out var piece) || piece.ItemId != e.Target.ItemId) continue;
            if (piece.Stain == e.Target.Stain) continue;
            needs.Add(new DyeNeed(key.Value, e.Target, piece.Stain));
        }
        // Count every needed dye as available here: the dye window may use another dye item (it offered
        // "Standard Spectrum Dye" in a recording), so the game decides, and a refused dye is skipped then.
        var owned = needs.Select(n => n.Want).Where(s => s != 0).Distinct().ToDictionary(s => s, s => int.MaxValue);
        var plan = DyePlanner.Plan(needs, owned);
        jobs.AddRange(plan.Jobs);
        log.Info($"Dyes: {plan.Jobs.Count} piece{(plan.Jobs.Count == 1 ? "" : "s")} to dye.");
        foreach (var stain in plan.Jobs.Select(j => j.Target.Stain).Distinct())
        {
            var need = plan.Jobs.Count(j => j.Target.Stain == stain);
            var have = HousingActions.DyesOwned(data, stain);
            if (have < need) log.Info($"  {data.DyeName(stain)}: {need} to dye, {have} of its own dye items in your bags (the game may offer another dye; pieces it refuses are skipped).");
        }
        foreach (var n in plan.Undye)
            log.Info($"{data.ItemName(n.Target.ItemId)} is dyed {data.DyeName(n.Has)} but the design has it undyed; removing a dye is left to you.");
        if (plan.Jobs.Count > 0 && !actions.CanDye) log.Warn(actions.DyeError!);
    }

    private void LogRows()
    {
        if (!rowsReady || rows.Known.Count == 0) return;
        log.Info($"Storeroom list rows learnt this run ({rows.Known.Count} of {rows.Count}): {string.Join("; ", rows.Describe(data.ItemName))}");
    }

    private bool FindNewPiece(RunJob job)
    {
        var live = ReadLive();
        if (live == null) return false;
        foreach (var (key, piece) in live.OrderBy(kv => kv.Key))
        {
            if (knownKeys.Contains(key)) continue;
            knownKeys.Add(key);
            if (piece.ItemId != job.Target.ItemId)
            {
                log.Info($"A {data.ItemName(piece.ItemId)} appeared; it isn't the piece being placed, so it's left as it is.");
                continue;
            }
            job.Key = key;
            return true;
        }
        return false;
    }

    private StockSlot? FindInBags(uint itemId, StockSlot? hint)
    {
        if (hint != null && hint.Source == StockSource.Inventory && InventoryReader.StillHolds(hint)) return hint;
        return InventoryReader.Read(data, side, out _).FirstOrDefault(s => s.Source == StockSource.Inventory && s.ItemId == itemId);
    }

    private IReadOnlyList<string> PlaceLabels()
    {
        var custom = (config.PlaceMenuWords ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return custom.Length > 0 ? custom : UiTable.PlaceLabels(Services.ClientState.ClientLanguage);
    }

    private static StepOutcome Fire(UiCall call, IReadOnlyDictionary<string, long>? blanks = null) =>
        GameUi.Fire(call, blanks) ? StepOutcome.Done : StepOutcome.Halt($"{call.Addon} isn't open.");

    private bool Landed(int key, Target target)
    {
        var stored = FurnitureReader.Stored(side == Side.Garden, key);
        return stored is { } s && Vector3.Distance(s.Position, target.Position) <= LandedDistance && Transforms.AngleDiff(s.Yaw, target.Yaw) <= LandedYaw;
    }

    private byte StainOf(int key) => FurnitureReader.StoredStain(side == Side.Garden, key) ?? 0;

    private Dictionary<int, LivePiece>? ReadLive()
    {
        var list = side == Side.Interior ? furniture.ReadInterior() : furniture.ReadGarden(out _);
        return list?.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
    }

    private void Finish()
    {
        State = RunState.Finished;
        anywhere.Disable();
        LogRows();
        var parts = new List<string> { $"{Done} of {Total} done" };
        if (Failed > 0) parts.Add($"{Failed} didn't work");
        if (Skipped > 0) parts.Add($"{Skipped} skipped");
        var summary = $"{(DryRun ? "Dry run finished" : "Finished")}: {string.Join(", ", parts)}.";
        StatusText = summary;
        log.Done(summary);
        if (config.ChatMessages) Services.Chat.Print(summary, "Housing Studio");
    }

    public string Name(RunJob job) => data.ItemName(job.Target.ItemId);

    private string DryWords(RunJob job) => job.Kind switch
    {
        JobKind.Move => $"move {Name(job)} to its spot",
        JobKind.PlaceFromStoreroom => $"place {Name(job)} from the storeroom at its spot",
        JobKind.PlaceFromBags => $"place {Name(job)} from {(job.Entry?.Stock is { } s ? InventoryReader.Describe(s) : "your bags")} at its spot",
        _ => $"dye {Name(job)} {data.DyeName(job.Target.Stain)}",
    };

    public static string PhaseWord(RunPhase p) => p switch
    {
        RunPhase.Move => "Moving",
        RunPhase.Place => "Placing",
        _ => "Dyeing",
    };

    public void Dispose() => anywhere.Disable();
}
