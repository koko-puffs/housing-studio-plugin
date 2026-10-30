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

/// <summary>A piece that didn't work, for the problems list.</summary>
public sealed record RunProblem(RunJob Job, string Name, ProblemKind Kind, string Reason);

/// <summary>
/// "Start placing": one run that, piece by piece at the pace set in Settings,
/// <list type="number">
/// <item>moves pieces already placed to their spot (the editor's own pick-up and set-down, Rotate mode);</item>
/// <item>places pieces from the storeroom: picks the piece's row in the furnishing list's Storeroom
/// tab (the game then shows a preview of it in the room), moves the preview to the piece's spot, and
/// chooses Place, as a recording showed; and from your bags: the item's menu, Place, then the editor
/// holds it, the plugin aims and sets it down. Each new piece is then nudged onto its exact spot;</item>
/// <item>dyes every piece whose dye differs from the design (Furnishing Color mode and the game's dye
/// window).</item>
/// </list>
/// It opens the Housing menu, switches the editor's modes and the list's tab itself, with the clicks in
/// <see cref="UiTable"/>. Each piece is a list of small steps (<see cref="StepQueue"/>): every click is
/// a window callback, every wait has a timeout, and when a piece goes wrong whatever it opened is
/// closed. Every step is written to the activity log.
///
/// For the window: <see cref="Phase"/>, <see cref="Current"/>, <see cref="StepLabel"/>,
/// <see cref="TimeLeft"/>, <see cref="Problems"/>, <see cref="DyeShortfall"/>, <see cref="RetryProblems"/>,
/// <see cref="LastBackupPath"/> and <see cref="RestoreBackup"/>.
/// </summary>
public sealed unsafe class PlacementRunner : IDisposable
{
    private const float LandedDistance = 0.05f;
    private const float LandedYaw = 0.03f;
    private const int MaxRowPicksPerPiece = 600;
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ClickWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LandWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan YouWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ByHandWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RowsSaveEvery = TimeSpan.FromSeconds(10);

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
    private int dyesExpected;
    private int failuresInARow;
    private HashSet<int> knownKeys = new();
    private DateTime editorMayCloseUntil;

    /// <summary>The furnishing list's storeroom rows (kept for the run, and saved per house between runs).</summary>
    private readonly StoreroomRows rows = new();
    private bool rowsReady;
    private bool rowsDirty;
    private DateTime rowsSavedAt;
    private List<string>? listNames;

    private string? restorePath;

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

    // ---------------------------------------------------------------------------------------------
    // What the window shows.

    public RunState State { get; private set; } = RunState.Idle;
    public bool DryRun { get; private set; }
    public string? StatusText { get; private set; }
    public string? StopReason { get; private set; }
    public IReadOnlyList<RunJob> Jobs => jobs;
    public bool Active => State is RunState.Running or RunState.Paused;

    /// <summary>The piece being worked on, or null.</summary>
    public RunJob? Current => Active && index >= 0 && index < jobs.Count ? jobs[index] : null;

    /// <summary>The phase being worked on (or the last one), or null before a run.</summary>
    public RunPhase? Phase => Current?.Phase ?? (index > 0 && index - 1 < jobs.Count ? jobs[index - 1].Phase : null);

    /// <summary>The step being done or waited for, in words ("Pick row 12: HousingGoods ← [i:1, u:12]").</summary>
    public string? StepLabel => Active && queue.State is QueueState.Running or QueueState.AwaitingContinue ? queue.Current?.Label : null;

    /// <summary>Where the step is in its piece's steps (1-based) and how many there are.</summary>
    public (int Index, int Count) StepProgress => (Math.Min(queue.Index + 1, queue.Count), queue.Count);

    /// <summary>Step-by-step mode is holding before this action.</summary>
    public bool AwaitingContinue => Active && queue.State == QueueState.AwaitingContinue;

    /// <summary>The run is waiting for you (to put a piece down, or open something it can't).</summary>
    public bool WaitingForYou => StepLabel is { } l && l.StartsWith("You:", StringComparison.Ordinal);

    /// <summary>Pieces in a phase, optionally only those with an outcome.</summary>
    public int Count(RunPhase phase, JobOutcome? outcome = null) =>
        jobs.Count(j => j.Phase == phase && (outcome == null || j.Outcome == outcome));

    /// <summary>A phase's progress: done, didn't work (failed or skipped), and how many in all.</summary>
    public (int Done, int Problems, int Total) PhaseProgress(RunPhase phase) =>
        (Count(phase, JobOutcome.Done), jobs.Count(j => j.Phase == phase && j.Outcome is JobOutcome.Failed or JobOutcome.Skipped), Count(phase));

    public int Done => jobs.Count(j => j.Outcome == JobOutcome.Done);
    public int Failed => jobs.Count(j => j.Outcome == JobOutcome.Failed);
    public int Skipped => jobs.Count(j => j.Outcome == JobOutcome.Skipped);
    public int Total => jobs.Count;

    /// <summary>The dyes have been planned (the dye phase's pieces are in <see cref="Jobs"/>).</summary>
    public bool DyesPlanned => dyesPlanned;

    /// <summary>About how long the run has left, from the pace and how long pieces have taken so far.</summary>
    public TimeSpan TimeLeft => Active ? RunEstimate.TimeLeft(jobs, config.IntervalMs, !dyesPlanned, dyesExpected) : TimeSpan.Zero;

    /// <summary>Every piece that didn't work this run, with why.</summary>
    public IReadOnlyList<RunProblem> Problems =>
        jobs.Where(j => j.Outcome is JobOutcome.Failed or JobOutcome.Skipped)
            .Select(j => new RunProblem(j, Name(j), j.Problem == ProblemKind.None ? ProblemKind.Other : j.Problem, j.Note ?? "Didn't work."))
            .ToList();

    /// <summary>Dyes the dye phase needed more of than your bags hold (its own dye items), when planned.</summary>
    public IReadOnlyList<(byte Stain, string Name, int Needed, int Owned)> DyeShortfall { get; private set; } = Array.Empty<(byte, string, int, int)>();

    /// <summary>The backup saved before the last run started, or null.</summary>
    public string? LastBackupPath { get; private set; }

    /// <summary>How many storeroom list rows are known (picked) and how many the list has.</summary>
    public (int Known, int Rows) StoreroomRowsKnown => (rows.Known.Count, rows.Count);

    /// <summary>Why the run can't start right now, or null.</summary>
    public string? CanStart(bool dryRun)
    {
        if (Active) return "Already running.";
        if (session.PlacingBlocked(out var why)) return why;
        var ctx = session.Context;
        if (ctx.Busy) return "Wait until you're free (not in combat, a cutscene or between areas).";
        if (dryRun) return null;
        if (!editor.Ready) return editor.Error;
        return null;
    }

    public void Start(bool dryRun, IEnumerable<PlanEntry> shown) => Start(dryRun, shown, forceDyes: false);

    private void Start(bool dryRun, IEnumerable<PlanEntry> shown, bool forceDyes)
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
        var dyes = config.DyeAfterPlacing || forceDyes;
        dyesExpected = dyes ? entries.Count(e => e.State != PieceState.Missing && e.Target.Stain != 0 && e.Target.Stain != e.CurrentStain) : 0;
        if (jobs.Count == 0 && dyesExpected == 0)
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
        dyesPlanned = !dyes;
        failuresInARow = 0;
        rowsReady = false;
        listNames = null;
        StopReason = null;
        DyeShortfall = Array.Empty<(byte, string, int, int)>();
        knownKeys = session.Live.Keys.ToHashSet();
        queue.StepByStep = config.StepByStep && !dryRun;
        queue.ActionGap = TimeSpan.FromMilliseconds(dryRun ? 0 : 300);
        queue.Begin(Array.Empty<Step>());
        nextJobAt = DateTime.UtcNow;
        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;

        if (!dryRun && config.BackupBeforePlacing) LastBackupPath = Backups.Save(session, data, log, "before placing") ?? LastBackupPath;
        if (!dryRun && config.PlaceAnywhere && !anywhere.Enable()) log.Warn(anywhere.Error ?? "Couldn't switch on place anywhere; placing with the game's usual checks.");

        log.Info($"{(dryRun ? "Dry run" : "Placing")} started: {Count(RunPhase.Move)} to move, {Count(RunPhase.Place)} to place "
                 + $"({jobs.Count(j => j.Kind == JobKind.PlaceFromStoreroom)} from the storeroom, {jobs.Count(j => j.Kind == JobKind.PlaceFromBags)} from your bags)"
                 + $"{(dyes ? $", then about {dyesExpected} to dye" : string.Empty)}.{(queue.StepByStep ? " Step by step: press Continue before each click." : string.Empty)}");
        State = RunState.Running;
        StatusText = "Starting…";
    }

    /// <summary>Starts a run on the pieces that didn't work last time (as they stand now: moved or placed ones then only need their dye).</summary>
    public void RetryProblems()
    {
        if (Active || session.Plan == null) return;
        var wanted = Problems.Select(p => (p.Job.Target.Key, p.Job.Target.ItemId)).ToHashSet();
        if (wanted.Count == 0) return;
        var again = session.Plan.Entries.Where(e => wanted.Contains((e.Target.Key, e.Target.ItemId))).ToList();
        log.Info($"Trying again: {again.Count} piece{(again.Count == 1 ? "" : "s")}.");
        Start(false, again, forceDyes: true);
    }

    /// <summary>
    /// Puts everything back as the backup made before the last run had it: loads that backup as the
    /// design and starts a run on it once it's been matched (next refresh). False when there's no backup.
    /// </summary>
    public bool RestoreBackup()
    {
        if (Active || LastBackupPath == null || !System.IO.File.Exists(LastBackupPath)) return false;
        if (!session.Load(LastBackupPath)) return false;
        restorePath = LastBackupPath;
        log.Info($"Restoring the backup {LastBackupPath}: it starts once it's matched against the house.");
        return true;
    }

    /// <summary>Every frame, before <see cref="Tick"/>: starts a restore once its backup has been matched.</summary>
    public void Update()
    {
        if (restorePath == null || Active) return;
        if (session.FilePath != restorePath) { restorePath = null; return; }
        if (session.Plan == null || session.PlanSide == null) return;
        restorePath = null;
        Start(false, session.Plan.Entries, forceDyes: true);
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
            job.Problem = ProblemKind.Skipped;
            job.Note = "Stopped.";
            job.EndedAt = DateTime.UtcNow;
        }
        State = RunState.Stopped;
        StopReason = reason;
        StatusText = reason;
        anywhere.Disable();
        SaveRows(force: true);
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
        job.Problem = ProblemKind.Skipped;
        job.Note = "Skipped by you.";
        job.EndedAt = DateTime.UtcNow;
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
        SaveRows(force: false);

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
                job.EndedAt = DateTime.UtcNow;
                job.Note ??= DryRun ? "Dry run: nothing touched." : "Done.";
                if (!DryRun) log.Done($"{Name(job)}: {job.Note}");
                failuresInARow = 0;
                NextAfter(quick: false);
                break;
            case QueueState.Skipped:
                job.Outcome = JobOutcome.Failed;
                job.EndedAt = DateTime.UtcNow;
                job.Note = queue.Message;
                job.Problem = queue.Problem;
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
                job.EndedAt = DateTime.UtcNow;
                job.Note = queue.Message;
                job.Problem = queue.Problem;
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
        job.StartedAt = DateTime.UtcNow;
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
                if (live == null || !live.TryGetValue(key, out var piece))
                    return StepOutcome.Skip("It's no longer where the plan saw it (moved, stored, or the list changed).", ProblemKind.NotFound);
                if (piece.ItemId != job.Target.ItemId)
                    return StepOutcome.Halt($"The furniture list changed under the plan (slot {key} is now {data.ItemName(piece.ItemId)}). Load the plan again and start over.");
                if (Landed(key, job.Target))
                {
                    job.Note ??= "Already in place.";
                    queue.FinishAfterThis();
                    return StepOutcome.Done;
                }
                if (piece.Instance == 0) return StepOutcome.Skip("The game hasn't loaded it (walk closer to it).", ProblemKind.NotLoaded);
                instance = piece.Instance;
                return editor.Select(instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't pick the piece up (the editor closed?).");
            }),
            Step.Wait("The game picks it up", () => HousingEditor.ActiveItem == instance, TimeSpan.FromSeconds(1.5), StepStatus.SkipJob),
            Step.Act($"Set {name} at its spot (the editor's set-down function)", () =>
            {
                if (HousingEditor.ActiveItem != instance) return StepOutcome.Skip("The game dropped the piece before it could be set.", ProblemKind.DidNotLand);
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
                TimeoutProblem = ProblemKind.DidNotLand,
                TimeoutMessage = "It didn't end up at its spot. The game refuses floating or overlapping pieces; \"Place anywhere\" in Settings allows them.",
            },
        };
    }

    /// <summary>
    /// From the storeroom, as Recording 2 showed it done by hand: pick the row (the game shows a
    /// preview of the piece and keeps the editor in Rotate), choose Place in the row's menu, and the
    /// piece is placed where the preview is. So the preview is moved to the piece's spot first.
    /// </summary>
    private List<Step> StoreroomSteps(RunJob job)
    {
        var name = Name(job);
        var chosen = new[] { -1 };
        var tried = new HashSet<int>();
        return new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Wait($"The furnishing list ({UiTable.FurnishingList}) is open", () => GameUi.IsOpen(UiTable.FurnishingList), WindowWait),
            Step.Act($"Open the Storeroom tab: {UiTable.ListStoreroomTab.Describe()}", () =>
                InventoryReader.StoreroomTabShown() ? StepOutcome.Done : Fire(UiTable.ListStoreroomTab)),
            Step.Wait("The Storeroom tab shows", InventoryReader.StoreroomTabShown, WindowWait),
            Step.Wait("The storeroom is loaded", () => InventoryReader.StoreroomLoaded(side), WindowWait),
            Step.Act($"Find {name}'s row in the list", () =>
            {
                if (!InventoryReader.StoreroomOccupied(side).Any(o => o.ItemId == job.Target.ItemId))
                    return StepOutcome.Skip("It's not in the storeroom any more.", ProblemKind.NotFound);
                PrepareRows();
                var next = NextRow(job, tried);
                if (next == null) return StepOutcome.Skip("Every row of the list was picked and none held it.", ProblemKind.NotFound);
                queue.InsertNext(TryRow(job, next.Value, tried, chosen));
                return StepOutcome.Done;
            }),
            Step.Wait("The game shows a preview of it in the room", () => FurnitureReader.Preview() != null, WindowWait),
            Step.Act($"Move the preview of {name} to its spot", () =>
            {
                HousingActions.AimPreview(job.Target.Position, job.Target.Yaw);
                return StepOutcome.Done;
            }, TimeSpan.FromMilliseconds(150)),
            new Step
            {
                Label = "The preview sits at its spot",
                Delay = TimeSpan.FromMilliseconds(250),
                Timeout = TimeSpan.FromSeconds(1.5),
                Run = () =>
                {
                    if (HousingActions.PreviewAt() is { } at && Vector3.Distance(at.Position, job.Target.Position) <= LandedDistance) return StepOutcome.Done;
                    HousingActions.AimPreview(job.Target.Position, job.Target.Yaw);
                    return StepOutcome.Waiting;
                },
                OnTimeout = StepStatus.Stop,
                TimeoutMessage = "The game kept moving the preview away from the spot, so Place wasn't chosen.",
            },
            ChoosePlace("the storeroom row's menu"),
            AnswerYesNo(),
            NewPiece(job, LandWait, StepStatus.SkipJob,
                "The game didn't place it (no new piece appeared). The spot may be refused: \"Place anywhere\" in Settings allows floating or overlapping pieces.",
                () =>
                {
                    // The row leaves the list once its piece is placed (unless it was one of a stack).
                    var r = chosen[0];
                    if (r >= 0 && rows.Known.TryGetValue(r, out var what)
                        && !InventoryReader.StillHolds(new StockSlot(StockSource.Storeroom, what.Container, what.Slot, what.ItemId, 0, 1)))
                    {
                        rows.Removed(r);
                        rowsDirty = true;
                    }
                }),
        };
    }

    /// <summary>
    /// Gets the storeroom list's rows ready for this run: those saved for this house if the storeroom
    /// is as it was, and the list's own row names if they can be read.
    /// </summary>
    private void PrepareRows()
    {
        var occupied = Occupied();
        if (rowsReady && rows.Count == occupied.Count && listNames != null) return;
        if (!rowsReady || rows.Count != occupied.Count)
        {
            var fp = StoreroomRows.Fingerprint(occupied);
            if (config.StoreroomRows.TryGetValue(HouseKey, out var saved) && rows.Import(saved, fp))
                log.Info($"  Using {rows.Known.Count} storeroom list rows learnt in an earlier run (the storeroom hasn't changed since).");
            else
            {
                if (rowsReady) log.Info($"  The storeroom now has {occupied.Count} pieces (the list had {rows.Count} rows): learning the rows again.");
                rows.Reset(occupied.Count);
            }
            rowsReady = true;
        }
        var lists = ListReader.Read(UiTable.FurnishingList);
        log.Info($"  The furnishing list's own rows: {ListReader.Describe(lists)}");
        var best = ListReader.Best(lists, occupied.Count);
        listNames = best != null && best.Rows.Count == occupied.Count ? best.Rows : new List<string>();
        log.Info(listNames.Count > 0
            ? $"  Reading the storeroom rows' names from the list ({best!.Source}, node {best.NodeId}): rows are found by name."
            : "  Couldn't read the rows' names from the list: rows are found by picking them (once each; groups of the same piece are skipped).");
    }

    /// <summary>The next row to try for a piece: a known row, a row the list's names say, else the first row not seen yet.</summary>
    private int? NextRow(RunJob job, HashSet<int> tried)
    {
        if (tried.Count >= MaxRowPicksPerPiece) return null;
        var known = rows.Find(job.Target.ItemId, job.Target.Stain);
        if (known != null && !tried.Contains(known.Value)) return known;
        if (listNames is { Count: > 0 })
            foreach (var r in rows.ByName(listNames, Name(job), job.Target.ItemId))
                if (!tried.Contains(r) && !(rows.Known.TryGetValue(r, out var k) && k.ItemId != job.Target.ItemId)) return r;
        for (var r = 0; r < rows.Count; r++)
            if (!rows.Known.ContainsKey(r) && !rows.Inferred.ContainsKey(r) && !tried.Contains(r)) return r;
        return null;
    }

    /// <summary>Picks a row; learns what it holds; if it isn't the piece, closes its menu and tries the next.</summary>
    private List<Step> TryRow(RunJob job, int row, HashSet<int> tried, int[] chosen)
    {
        var name = Name(job);
        var blanks = UiCallValues.Of(("index", row));
        tried.Add(row);
        return new List<Step>
        {
            Step.Act($"Pick row {row}: {UiTable.ListSelect.Describe(blanks)}", () =>
                GameUi.Fire(UiTable.ListSelect, blanks) ? StepOutcome.Done : StepOutcome.Halt("The furnishing list closed.")),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu) && InventoryReader.StoreroomSelection() != null, WindowWait),
            Step.Act("See which piece the game picked", () =>
            {
                var (container, slot) = InventoryReader.StoreroomSelection()!.Value;
                var occupied = Occupied();
                var held = occupied.FirstOrDefault(o => o.Container == container && o.Slot == slot);
                var seen = new StoreroomRow(container, slot, held.ItemId, held.Stain);
                if (rows.Known.ContainsKey(row) && !rows.Matches(row, seen))
                {
                    log.Info($"  Row {row} now picks storeroom {container}/{slot}, not what it did: the list changed, learning it again.");
                    rows.Reset(occupied.Count);
                }
                if (held.ItemId != 0) rows.Learn(row, seen, occupied.Count(o => o.ItemId == held.ItemId));
                rowsDirty = true;
                var right = held.ItemId == job.Target.ItemId;
                log.Info($"  row {row} → storeroom {container}/{slot}: {(held.ItemId == 0 ? "nothing?" : data.ItemName(held.ItemId))}{(right ? " (this one)" : string.Empty)}");
                if (right)
                {
                    chosen[0] = row;
                    return StepOutcome.Done;
                }
                GameUi.Fire(UiTable.MenuCancel);
                var after = new List<Step> { Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait) };
                var next = NextRow(job, tried);
                if (next == null)
                    after.Add(Step.Act("Give up looking", () => StepOutcome.Skip($"No row of the list held {name}.", ProblemKind.NotFound)));
                else
                    after.AddRange(TryRow(job, next.Value, tried, chosen));
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
                if (slot == null) return StepOutcome.Skip("It's not in your bags any more.", ProblemKind.NotFound);
                log.Info($"  {name}: container {slot.Container}, slot {slot.Slot}; menu opened from window id {GameUi.BagWindowId()}.");
                return HousingActions.OpenBagItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
            }),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
            ChoosePlace("the item's menu"),
            AnswerYesNo(),
        };
        steps.AddRange(HeldSteps(job));
        return steps;
    }

    /// <summary>
    /// After "Place" on a bag item: the editor holds the piece (mode Place, its active item, no preview;
    /// Recording 2, where a real click set it down). Aim it, set it down with the editor's own function,
    /// wait for it to appear, and nudge it onto the spot.
    /// </summary>
    private List<Step> HeldSteps(RunJob job)
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
                if (held == 0) return StepOutcome.Halt($"The editor isn't holding a piece to set down (mode {HousingEditor.Mode}).");
                return editor.Place(held) ? StepOutcome.Done : StepOutcome.Halt("Couldn't set the held piece down.");
            }, TimeSpan.FromMilliseconds(150)),
            NewPiece(job, LandWait, StepStatus.Stop,
                "The held piece wasn't set down automatically. Click to put it down (or press Escape); then send a recording (Settings → Diagnostics). "
                + "Until it's fixed, switch \"Place pieces from my bags for me\" off in Settings: you put each piece down anywhere, the plugin sets it at its spot.",
                null),
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
                TimeoutProblem = ProblemKind.DidNotLand,
            },
            Step.Act($"Set {name} at its spot", () =>
            {
                queue.InsertNext(MoveSteps(job, job.Key!.Value));
                return StepOutcome.Done;
            }),
        };
    }

    /// <summary>
    /// Dyeing, as Recording 2 showed it: in Furnishing Color, a piece's row in the mode's list is picked
    /// ([0, row]), its menu opened ([1, row]) and "Paint" chosen, which opens the dye window. The plugin
    /// first tries picking the piece itself (the editor's pick-up, which opened the dye window when you
    /// clicked the piece in Recording 1), then the list by name, then the game's "interact" function.
    /// </summary>
    private List<Step> DyeSteps(RunJob job)
    {
        var name = Name(job);
        var key = job.Key!.Value;
        var dye = data.DyeName(job.Target.Stain);
        var slot = data.DyeSlot(job.Target.Stain);
        var row = new[] { -1 };
        nint instance = 0;
        return new List<Step>
        {
            EnsureMode(EditorMode.Dye),
            Step.Act($"Check {name} still needs {dye}", () =>
            {
                if (StainOf(key) == job.Target.Stain)
                {
                    job.Note = "Already in its dye.";
                    queue.FinishAfterThis();
                    return StepOutcome.Done;
                }
                if (HousingActions.DyesOwned(data, job.Target.Stain) <= 0)
                    log.Info($"  None of {dye}'s own dye items in your bags; trying anyway (the window may use another dye, as it used Standard Spectrum Dye in the recordings).");
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
                    return StepOutcome.Skip("It's no longer where the plan saw it.", ProblemKind.NotFound);
                if (piece.Instance == 0) return StepOutcome.Skip("The game hasn't loaded it (walk closer to it).", ProblemKind.NotLoaded);
                instance = piece.Instance;
                return editor.Select(instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't pick the piece.");
            }),
            new Step
            {
                Label = $"The dye window ({UiTable.DyeWindow}) opens",
                Delay = TimeSpan.FromMilliseconds(200),
                Timeout = TimeSpan.FromSeconds(1),
                OnTimeout = StepStatus.Done,
                Run = () => GameUi.IsOpen(UiTable.DyeWindow) ? StepOutcome.Done : StepOutcome.Waiting,
            },
            Step.Act("If it didn't, open it from Furnishing Color's list (\"Paint\")", () =>
            {
                if (GameUi.IsOpen(UiTable.DyeWindow)) return StepOutcome.Done;
                queue.InsertNext(DyeListSteps(job, instance));
                return StepOutcome.Done;
            }),
            Step.Wait($"The dye window ({UiTable.DyeWindow}) is open", () => GameUi.IsOpen(UiTable.DyeWindow), WindowWait),
            Step.Act($"Find {dye} in the dye window's list", () =>
            {
                var r = Menus.DyeRow(GameUi.TextValues(UiTable.DyeWindow), dye, UiTable.DyeListFirstValue);
                if (r == null && slot is { } sl && UiTable.ShadeIndex(sl.Shade) is { } shade)
                {
                    var values = UiCallValues.Of(("shade", shade));
                    log.Info($"  {dye} isn't in the list shown: {UiTable.DyeShade.Describe(values)}");
                    GameUi.Fire(UiTable.DyeShade, values);
                    queue.InsertNext(new[]
                    {
                        Step.Act($"Find {dye} in the shade's list", () =>
                        {
                            row[0] = Menus.DyeRow(GameUi.TextValues(UiTable.DyeWindow), dye, UiTable.DyeListFirstValue) ?? Fallback();
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
                TimeoutProblem = ProblemKind.DyeMissing,
                TimeoutMessage = $"Neither the dye confirmation appeared nor the dye changed. The Dye button may be greyed out (no {dye} to use, or this piece can't take it).",
            },
            new Step
            {
                Label = $"{name} turns {dye}",
                Run = () => StainOf(key) == job.Target.Stain ? StepOutcome.Done : StepOutcome.Waiting,
                Timeout = TimeSpan.FromSeconds(4),
                OnTimeout = StepStatus.SkipJob,
                TimeoutProblem = ProblemKind.DyeFailed,
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

    /// <summary>
    /// Opens the dye window through Furnishing Color's list: rows whose name is the piece's are picked
    /// ([0, row]) until the editor's active item is this piece, then its menu ([1, row]) and "Paint".
    /// Without readable names, the game's "interact with the picked piece" function is used instead.
    /// </summary>
    private List<Step> DyeListSteps(RunJob job, nint instance)
    {
        var name = Name(job);
        var lists = ListReader.Read(UiTable.DyeList);
        log.Info($"  Furnishing Color's own rows: {ListReader.Describe(lists)}");
        var best = ListReader.Best(lists, 0);
        var candidates = best == null ? new List<int>() : best.Rows.Select((n, i) => (n, i)).Where(x => string.Equals(Menus.Clean(x.n), Menus.Clean(name), StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
        if (candidates.Count == 0)
        {
            return new List<Step>
            {
                Step.Act("Open the dye window on the picked piece (the game's \"interact\" function)", () =>
                    actions.OpenDyeWindow() ? StepOutcome.Done : StepOutcome.Skip(actions.DyeError ?? "The game didn't say which piece is picked.", ProblemKind.DyeFailed)),
            };
        }
        return DyeListTry(job, instance, candidates, 0);
    }

    private List<Step> DyeListTry(RunJob job, nint instance, List<int> candidates, int i)
    {
        if (i >= candidates.Count)
            return new List<Step> { Step.Act("Give up on the list", () => StepOutcome.Skip("None of Furnishing Color's rows for it picked this piece.", ProblemKind.NotFound)) };
        var r = candidates[i];
        var values = UiCallValues.Of(("index", r));
        return new List<Step>
        {
            Step.Act($"Pick row {r} in Furnishing Color's list: {UiTable.DyeListSelect.Describe(values)}", () => Fire(UiTable.DyeListSelect, values)),
            new Step
            {
                Label = "Is it this piece?",
                Delay = TimeSpan.FromMilliseconds(250),
                Timeout = TimeSpan.FromSeconds(1.5),
                Run = () =>
                {
                    if (HousingEditor.ActiveItem != instance) return StepOutcome.Waiting;
                    queue.InsertNext(new[]
                    {
                        Step.Act($"Open its menu: {UiTable.DyeListMenu.Describe(values)}", () => Fire(UiTable.DyeListMenu, values)),
                        Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
                        ChooseEntry(UiTable.PaintLabels(Services.ClientState.ClientLanguage), "Furnishing Color's menu"),
                    });
                    return StepOutcome.Done;
                },
                OnTimeout = StepStatus.Done, // not this one: the next step tries the next row
            },
            Step.Act("Next row if that wasn't it", () =>
            {
                if (HousingEditor.ActiveItem != instance && !GameUi.IsOpen(UiTable.DyeWindow) && !GameUi.IsOpen(UiTable.ContextMenu))
                    queue.InsertNext(DyeListTry(job, instance, candidates, i + 1));
                return StepOutcome.Done;
            }),
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Shared steps.

    /// <summary>
    /// Gets the editor into a mode with the clicks the recordings showed (<see cref="ModeRoute"/>): the
    /// Housing menu (opened through the housing agent when nothing is open), the bar's Move/Rotate, or
    /// closing the editor and choosing Indoor Furnishings or Furnishing Color. If there's no known way, asks you.
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
                if (click == ModeClick.OpenHousingMenu)
                {
                    steps.Add(Step.Act("Open the Housing menu (the housing agent)", () =>
                    {
                        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                        return HousingActions.OpenHousingMenu() ? StepOutcome.Done : StepOutcome.Halt("Couldn't reach the housing agent to open the Housing menu.");
                    }));
                    steps.Add(new Step
                    {
                        Label = "The Housing menu opens",
                        Timeout = ClickWait,
                        Run = () => GameUi.IsOpen(UiTable.HousingMenu) ? StepOutcome.Done : StepOutcome.Waiting,
                        OnTimeout = StepStatus.Done, // the next step asks you
                    });
                    steps.Add(new Step
                    {
                        Label = "You: open the Housing menu (Main menu → Social → Housing), if it didn't open",
                        Timeout = YouWait,
                        Run = () =>
                        {
                            editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                            return GameUi.IsOpen(UiTable.HousingMenu) || HousingEditor.Mode != EditorMode.None ? StepOutcome.Done : StepOutcome.Waiting;
                        },
                        TimeoutMessage = "The Housing menu didn't open.",
                    });
                    continue;
                }
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
                steps.Add(ModeRoute.LeadsToHousingMenu(click)
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
        ModeClick.OpenHousingMenu => "Open the Housing menu",
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

    private Step ChoosePlace(string where) => ChooseEntry(PlaceLabels(), where);

    private Step ChooseEntry(IReadOnlyList<string> labels, string where) => Step.Act($"Choose \"{labels.FirstOrDefault()}\" in {where}", () =>
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
                var question = GameUi.YesNoText();
                if (question != null)
                {
                    log.Info($"  The game asks: \"{question}\" → {UiTable.YesNoYes.Describe()}");
                    GameUi.Fire(UiTable.YesNoYes);
                    return StepOutcome.Done;
                }
                // Already placing (bags) or placed (storeroom): nothing was asked.
                if (HousingEditor.Mode == EditorMode.Place || DateTime.UtcNow - since > TimeSpan.FromSeconds(0.8)) return StepOutcome.Done;
                return StepOutcome.Waiting;
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
        TimeoutProblem = ProblemKind.DidNotLand,
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
                if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                return StepOutcome.Done;
            },
        },
    };

    // ---------------------------------------------------------------------------------------------

    private string HouseKey => startHouse.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private List<StoreroomRow> Occupied() =>
        InventoryReader.StoreroomOccupied(side).Select(o => new StoreroomRow(o.Container, o.Slot, o.ItemId, o.Stain)).ToList();

    /// <summary>Saves the learnt rows for this house (every few seconds while they change, and when the run ends).</summary>
    private void SaveRows(bool force)
    {
        if (!rowsReady || !rowsDirty || DryRun) return;
        if (!force && DateTime.UtcNow - rowsSavedAt < RowsSaveEvery) return;
        config.StoreroomRows[HouseKey] = rows.Export(StoreroomRows.Fingerprint(Occupied()));
        config.Save();
        rowsDirty = false;
        rowsSavedAt = DateTime.UtcNow;
    }

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
        // Count every needed dye as available here: the dye window may use another dye item (it used
        // "Standard Spectrum Dye" in the recordings), so the game decides, and a refused dye is skipped then.
        var owned = needs.Select(n => n.Want).Where(s => s != 0).Distinct().ToDictionary(s => s, s => int.MaxValue);
        var plan = DyePlanner.Plan(needs, owned);
        jobs.AddRange(plan.Jobs);
        log.Info($"Dyes: {plan.Jobs.Count} piece{(plan.Jobs.Count == 1 ? "" : "s")} to dye.");
        var shortfall = new List<(byte, string, int, int)>();
        foreach (var stain in plan.Jobs.Select(j => j.Target.Stain).Distinct())
        {
            var need = plan.Jobs.Count(j => j.Target.Stain == stain);
            var have = HousingActions.DyesOwned(data, stain);
            if (have >= need) continue;
            shortfall.Add((stain, data.DyeName(stain), need, have));
            log.Info($"  {data.DyeName(stain)}: {need} to dye, {have} of its own dye items in your bags (the game may use another dye; pieces it refuses are skipped).");
        }
        DyeShortfall = shortfall;
        foreach (var n in plan.Undye)
            log.Info($"{data.ItemName(n.Target.ItemId)} is dyed {data.DyeName(n.Has)} but the design has it undyed; removing a dye is left to you.");
    }

    private void LogRows()
    {
        if (!rowsReady || rows.Known.Count == 0) return;
        log.Info($"Storeroom list rows known ({rows.Known.Count} of {rows.Count}): {string.Join("; ", rows.Describe(data.ItemName))}");
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
        SaveRows(force: true);
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

    public void Dispose()
    {
        SaveRows(force: true);
        anywhere.Disable();
    }
}
