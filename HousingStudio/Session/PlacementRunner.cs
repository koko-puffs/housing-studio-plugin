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
/// <item>places pieces from the storeroom (picks them in the furnishing list, aims the preview at the
/// spot, chooses Place) and from your bags (opens the item's menu, chooses Place, aims the held piece,
/// sets it down), then makes sure each sits exactly at its spot;</item>
/// <item>dyes every piece whose dye differs from the design (the editor's dye mode and the game's dye
/// window), with dyes from your bags.</item>
/// </list>
/// Each piece is a list of small steps (<see cref="StepQueue"/>): every click is a window callback from
/// <see cref="UiTable"/>, every wait has a timeout, and when a piece goes wrong whatever it opened is
/// closed. Every step is written to the activity log. Every frame it checks you're still in the same
/// home with permission, the editor open, and not busy; it stops on leaving, a zone change, combat, a
/// window that doesn't appear, the furniture list changing under it, or a few failures in a row. A dry
/// run walks the pieces without touching anything; step-by-step mode holds before every click.
/// </summary>
public sealed unsafe class PlacementRunner : IDisposable
{
    private const float LandedDistance = 0.05f;
    private const float LandedYaw = 0.03f;
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LandWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan YouWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ByHandWait = TimeSpan.FromMinutes(5);

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
    private bool storeroomTabAsked;

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

    /// <summary>The run is waiting for you (to press a mode button, click a tab, put a piece down).</summary>
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
        if (!ctx.EditorOpen) return ctx.Kind == PlaceKind.Interior
            ? "Open Housing → Indoor Furnishings first."
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
        storeroomTabAsked = false;
        StopReason = null;
        knownKeys = session.Live.Keys.ToHashSet();
        queue.StepByStep = config.StepByStep && !dryRun;
        queue.ActionGap = TimeSpan.FromMilliseconds(dryRun ? 0 : 300);
        queue.Begin(Array.Empty<Step>());
        nextJobAt = DateTime.UtcNow;

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
        if (!DryRun && !ctx.EditorOpen)
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
            EnsureMode(EditorMode.Rotate, "Rotate (the circular arrows)"),
            Step.Act($"Pick up {name} (the editor's pick-up function)", () =>
            {
                var live = ReadLive();
                if (live == null || !live.TryGetValue(key, out var piece)) return StepOutcome.Skip("It's no longer where the plan saw it (moved, stored, or the list changed).");
                if (piece.ItemId != job.Target.ItemId)
                    return StepOutcome.Halt($"The furniture list changed under the plan (slot {key} is now {data.ItemName(piece.ItemId)}). Load the plan again and start over.");
                if (Landed(key, job.Target))
                {
                    job.Note = "Already in place.";
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
        var labels = PlaceLabels();
        return new List<Step>
        {
            Step.Wait($"The furnishing list ({UiTable.FurnishingList}) is open", () => GameUi.IsOpen(UiTable.FurnishingList), WindowWait),
            StoreroomTab(),
            Step.Wait("The storeroom is loaded", () => InventoryReader.StoreroomLoaded(side), WindowWait),
            Step.Act($"Find {name} in the storeroom list", () =>
            {
                var occupied = InventoryReader.StoreroomOccupied(side);
                var wanted = occupied.Where(o => o.ItemId == job.Target.ItemId).OrderByDescending(o => o.Stain == job.Target.Stain).ToList();
                if (wanted.Count == 0) return StepOutcome.Skip("It's not in the storeroom any more.");
                var guess = Menus.GuessStoreroomIndex(occupied.Select(o => (o.Container, o.Slot)), (wanted[0].Container, wanted[0].Slot));
                var order = Menus.SearchOrder(guess, occupied.Count, 12).ToList();
                log.Info($"  {name} is in storeroom {wanted[0].Container}/{wanted[0].Slot}; trying list position {guess} first.");
                queue.InsertNext(TryListPosition(job, order, 0));
                return StepOutcome.Done;
            }),
            Step.Wait("The game shows a preview of it", () => FurnitureReader.Preview() != null, WindowWait),
            Step.Act($"Aim the preview of {name} at its spot", () =>
            {
                HousingActions.AimHeld(job.Target.Position, job.Target.Yaw);
                return StepOutcome.Done;
            }, TimeSpan.FromMilliseconds(250)),
            ChoosePlace(labels, "the storeroom piece's menu", TimeSpan.FromMilliseconds(300)),
            AnswerYesNo(),
            NewPiece(job, LandWait, StepStatus.SkipJob, "The game didn't place it (no new piece appeared). The spot may be refused: \"Place anywhere\" in Settings allows floating or overlapping pieces."),
        };
    }

    /// <summary>Picks list position <paramref name="order"/>[i]; if the game picked another piece, closes its menu and tries the next.</summary>
    private List<Step> TryListPosition(RunJob job, List<int> order, int i)
    {
        if (i >= order.Count)
            return new List<Step> { Step.Act("Give up looking", () => StepOutcome.Skip("Couldn't find it in the storeroom list near where it should be.")) };
        var position = order[i];
        var blanks = UiCallValues.Of(("index", position));
        return new List<Step>
        {
            Step.Act($"Pick list position {position}: {UiTable.ListSelect.Describe(blanks)}", () =>
                GameUi.Fire(UiTable.ListSelect, blanks) ? StepOutcome.Done : StepOutcome.Halt("The furnishing list closed.")),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu) && InventoryReader.StoreroomSelection() != null, WindowWait),
            Step.Act("Check it's the right piece", () =>
            {
                var sel = InventoryReader.StoreroomSelection()!.Value;
                var holds = InventoryReader.StillHolds(new StockSlot(StockSource.Storeroom, sel.Container, sel.Slot, job.Target.ItemId, 0, 1));
                log.Info($"  list position {position} picked storeroom {sel.Container}/{sel.Slot}: {(holds ? "the right piece" : "another piece")}.");
                if (holds) return StepOutcome.Done;
                GameUi.Fire(UiTable.MenuCancel);
                var next = new List<Step> { Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait) };
                next.AddRange(TryListPosition(job, order, i + 1));
                queue.InsertNext(next);
                return StepOutcome.Done;
            }),
        };
    }

    private List<Step> BagSteps(RunJob job)
    {
        var name = Name(job);
        var stock = job.Entry?.Stock;
        var labels = PlaceLabels();
        return new List<Step>
        {
            Step.Act($"Open {name}'s menu ({(stock != null ? InventoryReader.Describe(stock) : "bags")})", () =>
            {
                var slot = FindInBags(job.Target.ItemId, stock);
                if (slot == null) return StepOutcome.Skip("It's not in your bags any more.");
                log.Info($"  {name}: container {slot.Container}, slot {slot.Slot}; menu opened from window id {GameUi.BagWindowId()}.");
                return HousingActions.OpenBagItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
            }),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
            ChoosePlace(labels, "the item's menu", TimeSpan.FromMilliseconds(250)),
            AnswerYesNo(),
            Step.Wait("The game hands you the piece to place",
                () => HousingEditor.Mode == EditorMode.Place || FurnitureReader.Preview() != null, WindowWait),
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
                    return StepOutcome.Halt($"The editor isn't holding a piece to set down (mode {HousingEditor.Mode}, preview {(FurnitureReader.Preview() != null ? "shown" : "none")}).");
                return editor.Place(held) ? StepOutcome.Done : StepOutcome.Halt("Couldn't set the held piece down.");
            }, TimeSpan.FromMilliseconds(150)),
            NewPiece(job, LandWait, StepStatus.Stop,
                "The held piece wasn't set down automatically. Click to put it down (or press Escape), then set \"Pieces from your bags\" to \"I put them down\" in Settings, and send a recording (Settings → Diagnostics)."),
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
        var shade = slot is { } sl ? UiTable.ShadeIndex(sl.Shade) : null;
        var pick = UiCallValues.Of(("order", (slot?.SubOrder ?? 1) - 1), ("stain", job.Target.Stain));
        var shadeValues = UiCallValues.Of(("shade", shade ?? 0));
        return new List<Step>
        {
            EnsureMode(EditorMode.Dye, "Dye (the paintbrush)"),
            Step.Act($"Check you have {dye}", () =>
            {
                if (!actions.CanDye) return StepOutcome.Halt(actions.DyeError!);
                if (shade == null) return StepOutcome.Skip($"{dye} isn't in the dye window's shades.");
                if (StainOf(key) == job.Target.Stain)
                {
                    job.Note = "Already in its dye.";
                    queue.FinishAfterThis();
                    return StepOutcome.Done;
                }
                return HousingActions.DyesOwned(data, job.Target.Stain) > 0 ? StepOutcome.Done : StepOutcome.Skip($"You're out of {dye}.");
            }),
            Step.Act("Close the dye window if it's open", () =>
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
            Step.Act("Open the dye window on it", () => actions.OpenDyeWindow() ? StepOutcome.Done : StepOutcome.Skip("The game didn't say which piece is picked."), TimeSpan.FromMilliseconds(250)),
            Step.Wait($"The dye window ({UiTable.DyeWindow}) opens", () => GameUi.IsOpen(UiTable.DyeWindow), WindowWait),
            Step.Act($"Choose the shade: {UiTable.DyeShade.Describe(shadeValues)}", () => Fire(UiTable.DyeShade, shadeValues), TimeSpan.FromMilliseconds(150)),
            Step.Act($"Choose {dye}: {UiTable.DyePick.Describe(pick)}", () => Fire(UiTable.DyePick, pick), TimeSpan.FromMilliseconds(200)),
            Step.Act($"Press Dye: {UiTable.DyeApply.Describe()}", () => Fire(UiTable.DyeApply), TimeSpan.FromMilliseconds(200)),
            new Step
            {
                Label = $"Confirm ({UiTable.DyeConfirm}: {UiTable.DyeConfirmYes.Describe()}) if the game asks",
                IsAction = true,
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.DyeConfirm))
                    {
                        GameUi.Fire(UiTable.DyeConfirmYes);
                        return StepOutcome.Done;
                    }
                    return StainOf(key) == job.Target.Stain ? StepOutcome.Done : StepOutcome.Waiting;
                },
                Timeout = WindowWait,
                OnTimeout = StepStatus.SkipJob,
                TimeoutMessage = "Neither the dye confirmation appeared nor the dye changed. The Dye button may be greyed out (not enough dye, or this piece can't take it).",
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
    }

    // ---------------------------------------------------------------------------------------------
    // Shared steps.

    /// <summary>Waits for the editor to be in a mode: switches it if the click is known, else asks you to.</summary>
    private Step EnsureMode(EditorMode mode, string button)
    {
        var fired = false;
        var asked = false;
        return new Step
        {
            Label = UiTable.EditorSetMode == null ? $"You: switch the furnishing editor to {button}" : $"Switch the editor to {button}",
            Run = () =>
            {
                if (HousingEditor.Mode == mode) return StepOutcome.Done;
                if (UiTable.EditorSetMode != null && !fired)
                {
                    fired = true;
                    var blanks = UiCallValues.Of(("mode", (long)mode));
                    log.Info($"  → {UiTable.EditorSetMode.Describe(blanks)}");
                    GameUi.Fire(UiTable.EditorSetMode, blanks);
                }
                else if (UiTable.EditorSetMode == null && !asked)
                {
                    asked = true;
                    if (config.ChatMessages) Services.Chat.Print($"Switch the furnishing editor to {button} to carry on.", "Housing Studio");
                }
                return StepOutcome.Waiting;
            },
            Timeout = UiTable.EditorSetMode == null ? YouWait : WindowWait,
            TimeoutMessage = $"The editor wasn't switched to {button}.",
        };
    }

    private Step StoreroomTab() => new()
    {
        Label = UiTable.ListStoreroomTab == null ? "You: click the furnishing list's Storeroom tab" : "Switch the furnishing list to its Storeroom tab",
        Run = () =>
        {
            if (InventoryReader.StoreroomTabShown()) return StepOutcome.Done;
            if (!storeroomTabAsked)
            {
                storeroomTabAsked = true;
                if (UiTable.ListStoreroomTab != null)
                {
                    log.Info($"  → {UiTable.ListStoreroomTab.Describe()}");
                    GameUi.Fire(UiTable.ListStoreroomTab);
                }
                else if (config.ChatMessages)
                {
                    Services.Chat.Print("Click the Storeroom tab in the furnishing list to carry on.", "Housing Studio");
                }
            }
            return StepOutcome.Waiting;
        },
        Timeout = UiTable.ListStoreroomTab == null ? YouWait : WindowWait,
        TimeoutMessage = "The furnishing list didn't switch to its Storeroom tab.",
    };

    private Step ChoosePlace(IReadOnlyList<string> labels, string where, TimeSpan delay) => Step.Act($"Choose \"{labels.FirstOrDefault()}\" in {where}", () =>
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
    }, delay);

    /// <summary>Presses Yes if the game asks a yes/no question within a moment (logging the question); goes on if it doesn't.</summary>
    private Step AnswerYesNo()
    {
        DateTime? since = null;
        return new Step
        {
            Label = $"Answer Yes if the game asks ({UiTable.YesNo}: {UiTable.YesNoYes.Describe()})",
            IsAction = true,
            Run = () =>
            {
                since ??= DateTime.UtcNow;
                var question = GameUi.YesNoText();
                if (question != null)
                {
                    log.Info($"  The game asks: \"{question}\"");
                    GameUi.Fire(UiTable.YesNoYes);
                    return StepOutcome.Done;
                }
                return DateTime.UtcNow - since > TimeSpan.FromSeconds(1.2) ? StepOutcome.Done : StepOutcome.Waiting;
            },
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

    /// <summary>Waits for the new piece to appear, then (if it isn't exactly at its spot) moves it there.</summary>
    private Step NewPiece(RunJob job, TimeSpan timeout, StepStatus onTimeout, string why) => new()
    {
        Label = $"{Name(job)} appears in the room",
        Run = () =>
        {
            if (!FindNewPiece(job)) return StepOutcome.Waiting;
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
        var owned = needs.Select(n => n.Want).Where(s => s != 0).Distinct().ToDictionary(s => s, s => HousingActions.DyesOwned(data, s));
        var plan = DyePlanner.Plan(needs, owned);
        jobs.AddRange(plan.Jobs);
        log.Info($"Dyes: {plan.Jobs.Count} piece{(plan.Jobs.Count == 1 ? "" : "s")} to dye.");
        foreach (var (stain, needed, have) in plan.Short)
            log.Warn($"Short of {data.DyeName(stain)}: {needed} needed, {have} in your bags. Those pieces are skipped (the shopping list has the dyes).");
        foreach (var n in plan.Undye)
            log.Info($"{data.ItemName(n.Target.ItemId)} is dyed {data.DyeName(n.Has)} but the design has it undyed; removing a dye is left to you.");
        if (plan.Jobs.Count > 0 && !actions.CanDye) log.Warn(actions.DyeError!);
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
