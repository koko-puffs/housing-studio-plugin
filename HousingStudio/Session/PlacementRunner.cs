using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
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
/// tab (found by its name, or from the list's sort order; pieces whose names the list shows go first),
/// the game shows a preview of it, and Place puts it there; and from your bags: stored first (the
/// item's menu, Store), then placed from the storeroom, or if the game won't store it, the item's
/// menu, Place, and the editor's set-down with place anywhere on for it. Each new piece is then moved
/// onto its exact spot;</item>
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
public sealed unsafe partial class PlacementRunner : IDisposable
{
    private const float LandedDistance = 0.05f;
    private const float LandedYaw = 0.03f;
    private const int MaxRowPicksPerPiece = 600;

    /// <summary>
    /// Rows tried one after another for a piece once its name and the list's order can't place it (0.3.3
    /// tried up to 600: two minutes and more for a piece the list didn't show at all).
    /// </summary>
    private const int MaxRowsInTurn = 60;
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
    private bool listExhausted;
    private bool namesStale;

    /// <summary>The furnishing list was seen settled (names filled in, counts steady) since it last changed.</summary>
    private bool listSettled;
    private readonly StepTimings timings = new();
    private uint listNode;
    private string? lastToast;

    /// <summary>Scrolling the list showed nothing new once this run (0.3.1: every time), so it isn't tried again.</summary>
    private bool scrollUseless;

    /// <summary>Watches the list settle every frame between steps, so the wait overlaps the move of the piece just placed.</summary>
    private Settle listWatch = new(5, TimeSpan.FromMilliseconds(250));

    /// <summary>How many pieces the storeroom holds once the last placement has gone through (the list isn't settled before).</summary>
    private int? listExpect;

    /// <summary>The prediction of the list's order was checked against the rows known (logged once a run).</summary>
    private bool orderChecked;

    /// <summary>Picks the sort-order search has made for each piece.</summary>
    private readonly Dictionary<RunJob, int> searchPicks = new();

    /// <summary>Pieces the list has been made again for (its Storeroom tab shown afresh), once each.</summary>
    private readonly HashSet<RunJob> listRemadeFor = new();

    /// <summary>
    /// A piece came into the storeroom (a bag piece stored to be placed): the list doesn't show it until
    /// it's made again (0.3.3 searched 170 rows for a stored Waterfall Partition the list didn't have).
    /// </summary>
    private bool listMissesStored;

    /// <summary>A restore's pieces to put away (placed since its backup), planned once the rest is done.</summary>
    private List<PlacedPiece> toPutAway = new();

    private bool putAwayPlanned = true;

    /// <summary>The run put the editor in Store mode: it's put back in Rotate when the run ends, so a click doesn't put a piece away.</summary>
    private bool storeModeUsed;

    /// <summary>
    /// Rows whose pieces were just placed while the storeroom still showed them (its count lags the new
    /// piece by up to 1.5 s): each taken out of the rows once its slot is empty (0.3.2 kept them, and
    /// every row after one was then off by one).
    /// </summary>
    private readonly List<(int Row, StockSlot Slot)> rowsLeaving = new();

    /// <summary>Place anywhere was switched on for one set-down only (a bag piece the game refused); it's switched off after.</summary>
    private bool anywhereForOne;

    private string? restorePath;

    /// <summary>The pace chosen in Settings (read as it goes: a change takes effect from the next step).</summary>
    private PaceProfile Speed => Pacing.Profile(config.Pace);

    private PlaceKind startKind;
    private uint startTerritory;
    private ulong startHouse;
    private int? startPlot;
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
        Services.Toasts.ErrorToast += OnErrorToast;
        Rehearsals.Register(rehearsing);
        queue.Trace += (step, what) =>
        {
            // Steps that wait for you are said as they start; the rest when they end, with how long they took.
            if (what == "start" && step.Label.StartsWith("You:", StringComparison.Ordinal)) log.Info($"  … {step.Label}");
            else if (what != "start" && what != "done") log.Warn($"  ✕ {what}");
        };
        queue.Timed += (step, took) =>
        {
            timings.AddStep(step.Key ?? KeyOf(step.Label), took);
            if (step.Label.StartsWith("You:", StringComparison.Ordinal)) log.Info($"  … done, after {took.TotalSeconds:0.#} s.");
            else log.Info($"  {(step.IsAction ? "→" : "…")} {step.Label} ({took.TotalMilliseconds:0} ms)");
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
    public TimeSpan TimeLeft
    {
        get
        {
            if (!Active) return TimeSpan.Zero;
            // Worked out a few times a second: the window asks every frame.
            if (DateTime.UtcNow >= timeLeftUntil)
            {
                timeLeft = RunEstimate.TimeLeft(jobs, Speed, !dyesPlanned, dyesExpected);
                timeLeftUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            }
            return timeLeft;
        }
    }

    private TimeSpan timeLeft;
    private DateTime timeLeftUntil;

    /// <summary>Every piece that didn't work this run, with why (made again only when that changes).</summary>
    public IReadOnlyList<RunProblem> Problems
    {
        get
        {
            var count = 0;
            foreach (var j in jobs)
                if (j.Outcome is JobOutcome.Failed or JobOutcome.Skipped) count++;
            var key = (jobsVersion, jobs.Count, count);
            if (problems == null || key != problemsKey)
            {
                problems = jobs.Where(j => j.Outcome is JobOutcome.Failed or JobOutcome.Skipped)
                    .Select(j => new RunProblem(j, Name(j), j.Problem == ProblemKind.None ? ProblemKind.Other : j.Problem, j.Note ?? "Didn't work."))
                    .ToList();
                problemsKey = key;
            }
            return problems;
        }
    }

    private List<RunProblem>? problems;
    private (int, int, int) problemsKey;

    /// <summary>Counts each new run's list of pieces (a retry can have as many as the run before).</summary>
    private int jobsVersion;

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
        if (OtherRunActive?.Invoke() == true) return "Wait until the other run is done (fixtures or errands).";
        if (session.PlacingBlocked(out var why)) return why;
        var ctx = session.Context;
        if (ctx.Busy) return "Wait until you're free (not in combat, a cutscene or between areas).";
        if (dryRun) return null;
        if (!editor.Ready) return editor.Error;
        return null;
    }

    public void Start(bool dryRun, IEnumerable<PlanEntry> shown) => Start(dryRun, shown, forceDyes: false);

    private void Start(bool dryRun, IEnumerable<PlanEntry> shown, bool forceDyes, IReadOnlyList<PlacedPiece>? putAway = null)
    {
        // A rehearsal (Rehearse) is a real run's steps that commit nothing; any other start is a real run or a preview.
        var rehearse = rehearseNext && !dryRun;
        rehearseNext = false;
        var problem = CanStart(dryRun);
        if (problem != null)
        {
            StopReason = problem;
            log.Warn(problem);
            return;
        }

        entries = shown.ToList();
        jobs.Clear();
        jobsVersion++;
        timeLeftUntil = DateTime.MinValue;
        jobs.AddRange(RunSequence.Build(entries));
        // Clear out (chosen in Ready): the pieces not in the design go first, and the rest is planned once they're gone.
        // A rehearsal plans its clear out as a preview does (nothing is put away, so nothing is matched again).
        PlanClearOut(dryRun || rehearse);
        var dyes = config.DyeAfterPlacing || forceDyes;
        // The same calculation as the dye phase's (PlanDyes), predicting what placing will leave.
        dyesExpected = dyes ? DyeCounts.ToApply(entries) : 0;
        var dyedInDesign = DyeCounts.Dyed(entries);
        toPutAway = putAway?.ToList() ?? new List<PlacedPiece>();
        storeModeUsed = false;
        putAwayPlanned = toPutAway.Count == 0;
        if (jobs.Count == 0 && dyesExpected == 0 && toPutAway.Count == 0)
        {
            StopReason = "Nothing to do: every piece shown is in place, in its dye (or missing).";
            log.Info(StopReason);
            return;
        }

        RunNumber++;
        var ctx = session.Context;
        startKind = ctx.Kind;
        startTerritory = ctx.TerritoryId;
        startHouse = ctx.HouseId;
        startPlot = ctx.Plot;
        side = ctx.Side!.Value;
        DryRun = dryRun;
        Rehearsal = rehearse;
        rehearsalHeld = null;
        index = -1;
        dyesPlanned = !dyes;
        failuresInARow = 0;
        rowsReady = false;
        listExhausted = false;
        StopReason = null;
        DyeShortfall = Array.Empty<(byte, string, int, int)>();
        knownKeys = session.Live.Keys.ToHashSet();
        queue.StepByStep = config.StepByStep && !dryRun;
        queue.ActionGap = TimeSpan.FromMilliseconds(dryRun ? 0 : Speed.ClickGapMs);
        queue.StepsPerTick = 8;
        timings.Clear();
        namesStale = false;
        listSettled = false;
        scrollUseless = false;
        listWatch = new Settle(5, Speed.Wait(250));
        listExpect = null;
        orderChecked = false;
        anywhereForOne = false;
        rowsLeaving.Clear();
        searchPicks.Clear();
        listRemadeFor.Clear();
        inTurnPicks.Clear();
        orderLogged.Clear();
        listMissesStored = false;
        queue.Begin(Array.Empty<Step>());
        nextJobAt = DateTime.UtcNow;
        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;

        if (rehearse)
        {
            // Nothing a rehearsal does needs undoing: no backup, no swap kept, place anywhere left off.
            if (config.BackupBeforePlacing) log.Info("Would save a backup of what's placed first: not in a rehearsal (nothing changes).");
            if (config.PlaceAnywhere) log.Info("Would switch place anywhere on: not in a rehearsal (nothing is set down).");
        }
        else if (!dryRun && KeptAs != null)
        {
            // Carrying on a swap (PlanClearOut kept it): its backup stays the one to swap back to.
            log.Info($"Carrying on the swap: what was here before it is still kept as “{KeptAs}”.");
        }
        else if (!dryRun && (config.BackupBeforePlacing || keepAs != null))
        {
            if (SaveBackup() is { } saved)
            {
                LastBackupPath = saved;
                var c = session.Context;
                backupOf = (c.Kind, c.TerritoryId, c.HouseId, c.Plot);
            }
            else if (keepAs != null)
            {
                // A swap keeps what's here to swap back to: without it, it doesn't start.
                StopReason = "Couldn't keep what's here (see the log), so the swap didn't start.";
                log.Warn(StopReason);
                keepAs = null;
                return;
            }
            else log.Warn("No backup was saved for this run (see why above): Put them back won't be offered for it.");
        }
        if (!dryRun && !rehearse && config.PlaceAnywhere && !anywhere.Enable()) log.Warn(anywhere.Error ?? "Couldn't switch on place anywhere; placing with the game's usual checks.");

        log.Info($"{(dryRun ? "Dry run" : rehearse ? "Rehearsal (nothing is committed)" : "Placing")} started: {Count(RunPhase.Move)} to move, {Count(RunPhase.Place)} to place "
                 + $"({jobs.Count(j => j.Kind == JobKind.PlaceFromStoreroom)} from the storeroom, {jobs.Count(j => j.Kind == JobKind.PlaceFromBags)} from your bags)"
                 + $"{(dyes ? $", then {dyesExpected} to dye ({dyedInDesign} of the pieces are dyed in the design; the others already have their dye, or will once placed from the storeroom)" : string.Empty)}.{(queue.StepByStep ? " Step by step: press Continue before each click." : string.Empty)}");
        State = RunState.Running;
        StatusText = "Starting…";
    }

    /// <summary>Starts a run on the pieces that didn't work last time (as they stand now: moved or placed ones then only need their dye).</summary>
    public void RetryProblems()
    {
        if (Active || session.Plan == null) return;
        // Its pieces are the last run's home's (its put-away pieces by their slots there; a
        // garden's plot too, as the ward's other plots share its territory).
        var here = session.Context;
        if (here.Kind != startKind || here.TerritoryId != startTerritory || here.HouseId != startHouse || here.Plot != startPlot)
        {
            StopReason = "Go back to the home the run was in to try those again.";
            return;
        }
        var problems = Problems;
        var wanted = problems.Select(p => (p.Job.Target.Key, p.Job.Target.ItemId)).ToHashSet();
        if (wanted.Count == 0) return;
        var again = session.Plan.Entries.Where(e => wanted.Contains((e.Target.Key, e.Target.ItemId))).ToList();
        // Pieces a restore couldn't put away aren't the design's: they go to be put away again.
        var putAgain = problems.Where(p => p.Job.Kind == JobKind.Store && p.Job.Key is not null)
            .Select(p => new PlacedPiece(p.Job.Key!.Value, p.Job.Target.ItemId, p.Job.Target.Position, p.Job.Target.Yaw, p.Job.Target.Stain))
            .ToList();
        log.Info($"Trying again: {again.Count + putAgain.Count} piece{(again.Count + putAgain.Count == 1 ? "" : "s")}.");
        Start(false, again, forceDyes: true, putAway: putAgain);
    }

    /// <summary>
    /// Puts everything back as the backup made before the last run had it: loads that backup as the
    /// design and starts a run on it once it's been matched (next refresh). False when there's no backup.
    /// </summary>
    public bool RestoreBackup() => RestoreBackup(out _);

    public bool RestoreBackup(out string why)
    {
        if (CanRestore() is { } problem)
        {
            why = problem;
            return false;
        }
        if (!session.Load(LastBackupPath!))
        {
            why = session.LoadError ?? "Couldn't open the backup.";
            return false;
        }
        why = string.Empty;
        restorePath = LastBackupPath;
        restoreClearsFirst = false;
        log.Info($"Restoring the backup {LastBackupPath}: it starts once it's matched against the house.");
        return true;
    }

    /// <summary>Where the last backup was taken: a restore only runs there (another home's, or the garden's inside, would store everything).</summary>
    private (PlaceKind Kind, uint Territory, ulong House, int? Plot)? backupOf;

    /// <summary>Why the last backup can't be put back here and now, or null.</summary>
    public string? CanRestore()
    {
        if (Active) return "Already running.";
        if (LastBackupPath == null || backupOf == null) return "There's no backup from this session.";
        if (!System.IO.File.Exists(LastBackupPath)) return "The backup isn't there any more.";
        var c = session.Context;
        // (A garden's plot too: the ward's other plots have the same territory and may share its id.)
        if ((c.Kind, c.TerritoryId, c.HouseId, c.Plot) != backupOf.Value)
            return backupOf.Value.Kind == PlaceKind.Ward ? "The backup is of your garden: go back to it to put things back." : "The backup is of a home's inside: go back in to put things back.";
        return CanStart(false);
    }

    /// <summary>A message for you from the runner (a restore that couldn't start), taken once.</summary>
    public string? TakeNotice()
    {
        var n = notice;
        notice = null;
        return n;
    }

    private string? notice;

    /// <summary>Every frame, before <see cref="Tick"/>: starts a restore once its backup has been matched.</summary>
    public void Update()
    {
        liveThisFrame = null;
        if (restorePath == null || Active) return;
        if (session.FilePath != restorePath) { restorePath = null; return; }
        if (session.Plan == null || session.PlanSide == null) return;
        restorePath = null;
        if (CanRestore() is { } problem)
        {
            log.Warn($"Not putting things back: {problem}");
            notice = problem;
            return;
        }
        // The backup is what was there: what's placed now and isn't in it (placed since) goes back to
        // the storeroom; but a backup with nothing for here would put everything away, so it doesn't.
        var backupHas = session.PlanSide == Side.Interior ? session.Layout?.Interior.Count : session.Layout?.Garden.Count;
        IReadOnlyList<PlacedPiece> putAway = backupHas > 0 ? session.Plan.Extras : Array.Empty<PlacedPiece>();
        if (backupHas is not > 0) log.Warn("The backup has no pieces here, so nothing is put away.");
        StartRestore(putAway);
        if (!Active) notice = StopReason ?? "Couldn't start putting them back.";
    }

    private DateTime? pausedAt;

    public void Pause(string why)
    {
        if (State != RunState.Running) return;
        State = RunState.Paused;
        pausedAt = DateTime.UtcNow;
        StatusText = why;
        log.Warn($"Paused: {why} (a step that's waiting carries on waiting when you resume)");
        if (config.ChatMessages) Services.Chat.Print($"Paused: {why}", "Housing Studio");
    }

    public void Resume()
    {
        if (State != RunState.Paused) return;
        State = RunState.Running;
        // The step waiting carries on its wait: the pause doesn't count against its timeout.
        if (pausedAt is { } at) queue.Hold(DateTime.UtcNow - at);
        pausedAt = null;
        failuresInARow = 0;
        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
        log.Info("Resumed.");
    }

    /// <summary>Step-by-step mode: do the click it's holding before.</summary>
    public void Continue() => queue.Continue();

    /// <summary>A restore (Put them back, Swap back) still waiting to be matched doesn't start.</summary>
    public void CancelPending() => restorePath = null;

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
        if (Rehearsal && !reason.StartsWith("Stopped by you", StringComparison.Ordinal))
            Rehearsals.Report.Issue("The rehearsal", $"Stopped: {reason}", reason.StartsWith("Timed out", StringComparison.Ordinal) ? RehearsalTrouble.TimedOut : RehearsalTrouble.Failed);
        LeaveStoreMode();
        anywhereForOne = false;
        anywhere.Disable();
        SaveRows(force: true);
        LogRows();
        if (!DryRun) foreach (var line in timings.Summary()) log.Info(line);
        log.Warn($"{(Rehearsal ? "Rehearsal stopped" : "Stopped")}: {reason}");
        if (clearOutLater.Count > 0)
            log.Warn($"{clearOutLater.Count} piece{(clearOutLater.Count == 1 ? " was" : "s were")} to be cleared out once the design was placed (the storeroom had no room at the start): still placed. Clear out again from Ready to put them away.");
        if (config.ChatMessages) Services.Chat.PrintError($"{(Rehearsal ? "Rehearsal stopped" : "Stopped")}: {reason}", "Housing Studio");
    }

    /// <summary>Skips the piece being worked on (closing whatever it opened).</summary>
    public void SkipCurrent()
    {
        // Only a piece still being worked on: one that just finished (the click came a frame late) stays as it ended.
        if (Current is not { Outcome: JobOutcome.Working } job) return;
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
        liveThisFrame = null;
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
        if (!DryRun) queue.ActionGap = TimeSpan.FromMilliseconds(Speed.ClickGapMs);
        // A rehearsal's let-go (unconfirmed) waits for Continue until you trust it, as the errands' guesses do.
        if (Rehearsal) queue.StepByStep = config.StepByStep || (!UiTable.LetGo.Verified && !config.RehearsalTrustLetGo && queue.Current is { IsAction: true, Key: LetGoKey });
        SaveRows(force: false);
        if (!DryRun) WatchList();
        if (anywhereForOne && queue.State != QueueState.Running && queue.State != QueueState.AwaitingContinue) EndAnywhereForOne();

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
                if (job.StartedAt is { } started) timings.AddPiece(job.Phase, job.EndedAt.Value - started);
                job.Note ??= DryRun ? "Dry run: nothing touched." : "Done.";
                if (!DryRun) log.Done($"{Name(job)}: {job.Note}");
                failuresInARow = 0;
                NextAfter(quick: false);
                break;
            case QueueState.Skipped:
                job.Outcome = JobOutcome.Failed;
                job.EndedAt = DateTime.UtcNow;
                if (job.StartedAt is { } startedS) timings.AddPiece(job.Phase, job.EndedAt.Value - startedS);
                job.Note = queue.Message;
                job.Problem = queue.Problem;
                log.Warn($"{Name(job)}: {queue.Message}");
                if (Rehearsal) Rehearsals.Report.Issue(Name(job), queue.Message ?? "Didn't work.", TroubleOf(queue.Message));
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
                if (job.StartedAt is { } startedT) timings.AddPiece(job.Phase, job.EndedAt.Value - startedT);
                job.Note = queue.Message;
                job.Problem = queue.Problem;
                Stop(queue.Message ?? "A step didn't work.");
                break;
        }
    }

    private void NextAfter(bool quick)
    {
        var delay = quick ? 150 : Pacing.NextDelayMs(Speed.PieceGapMs, config.Jitter, random.NextDouble());
        if (DryRun) delay /= 2;
        nextJobAt = DateTime.UtcNow + TimeSpan.FromMilliseconds(delay);
        queue.Begin(Array.Empty<Step>());
    }

    private void StartNextJob()
    {
        index++;
        if (index >= jobs.Count)
        {
            if (AfterClearOut()) return;
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
            if (!putAwayPlanned)
            {
                putAwayPlanned = true;
                PlanPutAway();
                if (index < jobs.Count)
                {
                    index--;
                    nextJobAt = DateTime.UtcNow;
                    return;
                }
            }
            if (clearOutLater.Count > 0)
            {
                PlanClearOutLater();
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
        // Clear out's first pieces, with a storeroom that's full: they wait for the end.
        if (PutOffClearOut())
        {
            index--;
            nextJobAt = DateTime.UtcNow;
            return;
        }

        if (!DryRun) ChooseReadable();
        var job = jobs[index];
        job.Outcome = JobOutcome.Working;
        job.StartedAt = DateTime.UtcNow;
        log.Info($"{(job.Kind == JobKind.Store ? "Putting away" : PhaseWord(job.Phase))}: {Name(job)}{(job.Target.Stain != 0 ? $" ({data.DyeName(job.Target.Stain)})" : string.Empty)}");
        if (DryRun)
        {
            queue.Begin(new[] { Step.Act($"Would {DryWords(job)}", () => StepOutcome.Done) });
            return;
        }
        var (steps, cleanup) = Rehearsal ? RehearsalScript(job) : Script(job);
        queue.Begin(steps, cleanup);
    }

    /// <summary>
    /// When the next piece comes from the storeroom: of the storeroom pieces still to place on the same
    /// floor and in the same pass (floor pieces before tabletop and wall-mounted ones, so those still
    /// come after what they sit on), the one whose name is read in the list nearest its top goes next.
    /// Each placement takes a row out of the list, so the rows past the 200 it lets be read move up as
    /// the run goes, and pieces are found by name instead of searched for (0.3.1 scanned rows one by one).
    /// </summary>
    private void ChooseReadable()
    {
        var job = jobs[index];
        if (job.Kind != JobKind.PlaceFromStoreroom || side != Side.Interior || !config.StoreroomAutomatic || !rowsReady) return;
        if (namesStale && listSettled && GameUi.IsOpen(UiTable.FurnishingList)) RefreshListNames();
        if (rows.Names.Count == 0) return;
        var candidates = new List<int>();
        for (var i = index; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j.Outcome == JobOutcome.Pending && j.Kind == JobKind.PlaceFromStoreroom && j.Target.Floor == job.Target.Floor && j.Target.Late == job.Target.Late)
                candidates.Add(i);
        }
        var pick = StoreroomOrder.FirstReadable(candidates.Select(i => Name(jobs[i])).ToList(), rows.Names);
        if (pick == null || candidates[pick.Value] == index) return;
        var at = candidates[pick.Value];
        var chosen = jobs[at];
        jobs.RemoveAt(at);
        jobs.Insert(index, chosen);
        var row = rows.ByName(Name(chosen), chosen.Target.ItemId).FirstOrDefault();
        log.Info($"  Next from the storeroom: {Name(chosen)}, whose row ({row}) can be read in the list; {Name(job)} comes once its row can be read, or nothing else's can.");
    }

    // ---------------------------------------------------------------------------------------------
    // Scripts: the steps for each kind of piece.

    private (List<Step> Steps, List<Step> Cleanup) Script(RunJob job) => job.Kind switch
    {
        JobKind.Move => (MoveSteps(job, job.Key!.Value), HeldCleanup()),
        JobKind.PlaceFromStoreroom when side == Side.Interior && config.StoreroomAutomatic => (StoreroomSteps(job), MenuCleanup().Concat(HeldCleanup()).ToList()),
        JobKind.PlaceFromBags when config.BagsAutomatic => (BagSteps(job), MenuCleanup().Concat(HeldCleanup()).ToList()),
        JobKind.Dye => (DyeSteps(job), DyeCleanup()),
        JobKind.Store => (StoreSteps(job), new List<Step>()),
        _ => (ByHandSteps(job), HeldCleanup()),
    };

    /// <summary>
    /// Still in the home the run started in (its territory, with a housing territory loaded), and
    /// not on the way out of it (a zone change or logout shows as these first, while the old
    /// territory is still loaded).
    /// </summary>
    private unsafe bool StillInStartHome()
    {
        if (Services.Condition[ConditionFlag.BetweenAreas] || Services.Condition[ConditionFlag.BetweenAreas51]
            || Services.Condition[ConditionFlag.LoggingOut])
            return false;
        var hm = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
        if (hm == null || (hm->IndoorTerritory == null && hm->OutdoorTerritory == null)) return false;
        return Services.ClientState.TerritoryType == startTerritory;
    }

    /// <summary>
    /// When a piece ends badly (skipped, stopped) with the editor still holding something: a picked-up
    /// piece is set down where it is now (in Rotate, the game's own set-down); a piece being placed
    /// can't be let go of from here, so you're told (Esc puts it back), else the next piece would
    /// wait 30 s for the editor and stop.
    /// </summary>
    private List<Step> HeldCleanup() => new()
    {
        new Step
        {
            Label = "Let go of what the editor holds",
            Run = () =>
            {
                // Gone from the home (a zone change, a logout, kicked): the editor's state may be
                // half torn down, so nothing is read or called.
                if (!StillInStartHome()) return StepOutcome.Done;
                var held = HousingEditor.ActiveItem;
                if (held == 0) return StepOutcome.Done;
                var mode = HousingEditor.Mode;
                if (mode == EditorMode.Rotate || mode == EditorMode.Move)
                {
                    if (editor.Place(held)) log.Info("  Set down the piece the editor was holding, where it is now.");
                }
                else if (mode == EditorMode.Place)
                {
                    log.Warn("  The editor is still holding a piece to place: press Esc to put it back.");
                    if (config.ChatMessages) Services.Chat.Print("The editor is still holding a piece: press Esc to put it back.", "Housing Studio");
                }
                return StepOutcome.Done;
            },
        },
    };

    private List<Step> MoveSteps(RunJob job, int key, int attempt = 0, bool justPlaced = false)
    {
        var name = Name(job);
        nint instance = 0;
        var steps = new List<Step> { EnsureMode(EditorMode.Rotate) };
        if (justPlaced)
        {
            // The game's own handling of the placement holds the new piece for a moment: never take that for the pick-up.
            steps.Add(new Step
            {
                Label = "Nothing is held",
                Key = "nothing held",
                Timeout = TimeSpan.FromSeconds(2),
                OnTimeout = StepStatus.Done,
                Run = () => HousingEditor.ActiveItem == 0 ? StepOutcome.Done : StepOutcome.Waiting,
            });
        }
        steps.AddRange(new List<Step>
        {
            new Step
            {
                Label = $"Pick up {name} (the editor's pick-up function){(attempt > 0 ? $", try {attempt + 1}" : string.Empty)}",
                Key = "pick up",
                IsAction = true,
                Run = () =>
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
                    // The piece's object as the furniture list has it now (not one remembered from before: the
                    // game may make a new one once a placement goes through).
                    instance = piece.Instance;
                    if (justPlaced || attempt > 0)
                        log.Info($"  Picking up 0x{instance:X} (slot {key}); held before: {(HousingEditor.ActiveItem == 0 ? "nothing" : $"0x{HousingEditor.ActiveItem:X}")}.");
                    return editor.Select(instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't pick the piece up (the editor closed?).");
                },
            },
            HeldSteady(() => instance, justPlaced),
            new Step
            {
                Label = "Picked up?",
                Key = "picked up?",
                Run = () =>
                {
                    var now = InstanceOf(key);
                    if (HousingEditor.ActiveItem == instance && now == instance)
                    {
                        // A rehearsal lets go of it where it is (never shifted) instead of setting it down at its spot.
                        queue.InsertNext(Rehearsal ? RehearseHold(job, key, instance) : SetDownSteps(job, key, instance, attempt, justPlaced));
                        return StepOutcome.Done;
                    }
                    // A piece just placed isn't always ready to be picked at once (0.2.3: Simple Hingan Pillar).
                    if (attempt < 2)
                    {
                        log.Info($"  The game didn't keep {name} picked up (held {(HousingEditor.ActiveItem == 0 ? "nothing" : $"0x{HousingEditor.ActiveItem:X}")}, wanted 0x{instance:X}"
                                 + $"{(now != instance ? $"; the piece's object is now 0x{now:X}" : string.Empty)}); trying again.");
                        queue.InsertNext(new[] { Step.Pause("Wait a moment", Speed.Wait(300)) }.Concat(MoveSteps(job, key, attempt + 1, justPlaced)));
                        return StepOutcome.Done;
                    }
                    return StepOutcome.Skip("The game wouldn't pick it up (three tries). It's placed; \"Try again\" moves it later.", ProblemKind.DidNotLand);
                },
            },
        });
        return steps;
    }

    /// <summary>The object of the piece in this furniture slot, as the furniture list has it now (0 if none).</summary>
    private nint InstanceOf(int key)
    {
        var live = ReadLive();
        return live != null && live.TryGetValue(key, out var p) ? p.Instance : 0;
    }

    private List<Step> SetDownSteps(RunJob job, int key, nint instance, int attempt, bool justPlaced)
    {
        var name = Name(job);
        return new List<Step>
        {
            new Step
            {
                Label = $"Set {name} at its spot (the editor's set-down function)",
                Key = "set down",
                IsAction = true,
                Run = () =>
                {
                    if (HousingEditor.ActiveItem != instance)
                    {
                        // Dropped between the pick-up and now: pick it up again (0.3.0 gave up here).
                        if (attempt < 2)
                        {
                            log.Info($"  The game let go of {name} before it could be set (held {(HousingEditor.ActiveItem == 0 ? "nothing" : $"0x{HousingEditor.ActiveItem:X}")}); picking it up again.");
                            queue.InsertNext(new[] { Step.Pause("Wait a moment", Speed.Wait(400)) }.Concat(MoveSteps(job, key, attempt + 1, justPlaced)));
                            return StepOutcome.Done;
                        }
                        return StepOutcome.Skip("The game dropped the piece before it could be set (three tries).", ProblemKind.DidNotLand);
                    }
                    var sg = (SharedGroupLayoutInstance*)instance;
                    sg->Transform.Translation = job.Target.Position;
                    sg->Transform.Rotation = Transforms.YawQuaternion(job.Target.Yaw);
                    return editor.Place(instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't set the piece down (the editor closed?).");
                },
            },
            new Step
            {
                Label = "It lands at its spot",
                Key = "lands",
                Run = () => Landed(key, job.Target) ? StepOutcome.Done : StepOutcome.Waiting,
                Timeout = TimeSpan.FromSeconds(3),
                OnTimeout = StepStatus.SkipJob,
                TimeoutProblem = ProblemKind.DidNotLand,
                TimeoutMessage = "It didn't end up at its spot. The game refuses floating or overlapping pieces; \"Place anywhere\" in Settings allows them.",
            },
        };
    }

    /// <summary>
    /// From the storeroom, using only what's been seen to work: pick the piece's row (the game shows a
    /// preview of it, as in Recording 2), choose Place without touching the preview (Recording 2 placed
    /// the piece where the game's preview was), find the new piece in the furniture list, then move it
    /// to its exact spot with the move that works in game. 0.2.2 moved the preview first, and the game
    /// never placed the piece; that's now an option (<see cref="Configuration.MovePreviewBeforePlace"/>).
    /// </summary>
    private List<Step> StoreroomSteps(RunJob job)
    {
        var steps = new List<Step>
        {
            new Step
            {
                // One check (one frame) when everything is as the last piece left it; the details only when not.
                Label = "Ready for the storeroom",
                Key = "ready",
                Run = () =>
                {
                    if (listMissesStored && GameUi.IsOpen(UiTable.FurnishingList) && InventoryReader.StoreroomTabShown())
                    {
                        listMissesStored = false;
                        listRemadeFor.Add(job);
                        queue.InsertNext(RemakeList("a piece was just stored, and the list shows it only once it's made again"));
                        return StepOutcome.Done;
                    }
                    if (HousingEditor.Mode == EditorMode.Rotate && GameUi.IsOpen(UiTable.FurnishingList)
                        && InventoryReader.StoreroomTabShown() && InventoryReader.StoreroomLoaded(side) && listSettled) return StepOutcome.Done;
                    queue.InsertNext(new[]
                    {
                        EnsureMode(EditorMode.Rotate),
                        Step.Wait($"The furnishing list ({UiTable.FurnishingList}) is open", () => GameUi.IsOpen(UiTable.FurnishingList), WindowWait),
                        new Step
                        {
                            Label = "The Storeroom tab is showing",
                            Run = () =>
                            {
                                if (InventoryReader.StoreroomTabShown()) return StepOutcome.Done;
                                queue.InsertNext(new[]
                                {
                                    Step.Act($"Open the Storeroom tab: {UiTable.ListStoreroomTab.Describe()}", () => Fire(UiTable.ListStoreroomTab)),
                                    Step.Wait("The Storeroom tab shows", InventoryReader.StoreroomTabShown, WindowWait),
                                });
                                return StepOutcome.Done;
                            },
                        },
                        Step.Wait("The storeroom is loaded", () => InventoryReader.StoreroomLoaded(side), WindowWait),
                        ListSettles(),
                    });
                    return StepOutcome.Done;
                },
            },
        };
        steps.AddRange(StoreroomAttempt(job, 0));
        return steps;
    }

    /// <summary>One go at placing a storeroom piece; a second go follows if the first didn't place it.</summary>
    private List<Step> StoreroomAttempt(RunJob job, int attempt)
    {
        var name = Name(job);
        // The row picked, and the storeroom container and slot it showed (its row can move by the time it's placed).
        var chosen = new[] { -1, -1, -1 };
        var tried = new HashSet<int>();
        Vector3? previewAt = null;
        var placedBefore = -1;
        var storedBefore = -1;
        nint previewItem = 0;
        var placed = false;
        var previewSettle = new Settle(3, Speed.Wait(60));
        var steps = new List<Step>
        {
            FindRowStep(job, tried, chosen),
            new Step
            {
                // The preview and the editor's active item, both, for a few frames: 0.3.0 chose Place for
                // Doman Dogwood 10 ms after the preview showed and 27 ms before the active item was set.
                Label = "The game shows a preview of it in the room, as the piece being placed",
                Key = "preview",
                Timeout = WindowWait,
                OnTimeout = StepStatus.SkipJob,
                TimeoutProblem = ProblemKind.DidNotLand,
                TimeoutMessage = "The game didn't show the piece's preview (or didn't make it the piece being placed).",
                Run = () =>
                {
                    var active = HousingEditor.ActiveItem;
                    var state = FurnitureReader.Preview() != null && active != 0 ? $"0x{active:X}" : null;
                    if (!previewSettle.Update(state, DateTime.UtcNow)) return StepOutcome.Waiting;
                    previewItem = active;
                    return StepOutcome.Done;
                },
            },
        };
        if (config.MovePreviewBeforePlace)
        {
            steps.Add(Step.Act($"Move the preview of {name} to its spot (option)", () =>
            {
                HousingActions.AimPreview(job.Target.Position, job.Target.Yaw);
                return StepOutcome.Done;
            }, Speed.Wait(150)));
        }
        steps.Add(new Step
        {
            Label = "Note where the game's preview is",
            Key = "note preview",
            Run = () =>
            {
                previewAt = HousingActions.PreviewAt()?.Position;
                // What's placed right now, so a piece put down since the run's (a second old) list
                // isn't taken for the one this places.
                if (ReadLive() is { } liveNow) knownKeys.UnionWith(liveNow.Keys);
                placedBefore = FurnitureReader.PlacedCount(side == Side.Garden);
                storedBefore = InventoryReader.StoreroomOccupied(side).Count;
                lastToast = null;
                if (previewAt is { } p) log.Info($"  The preview is at ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}), held as 0x{previewItem:X}; {placedBefore} pieces placed, {storedBefore} in the storeroom.");
                return StepOutcome.Done;
            },
        });
        if (Rehearsal)
        {
            // Up to the piece shown at its spot; then the menu closed instead of Place.
            steps.AddRange(RehearsePlace(job, chosen, () => placedBefore));
            return steps;
        }
        steps.Add(ChoosePlace("the storeroom row's menu"));
        steps.Add(new Step
        {
            // Watches for the new piece straight away (no fixed wait); answers a yes/no if one comes up.
            Label = $"A new {name} is in the furniture list",
            Key = "placed",
            Timeout = LandWait,
            OnTimeout = StepStatus.Done, // handled below: a second try, then skip with the reason
            Run = () =>
            {
                AnswerYes();
                var key = IdentifyNew(job, previewAt);
                if (key != null)
                {
                    var count = FurnitureReader.PlacedCount(side == Side.Garden);
                    log.Info($"  Placed: furniture slot {key} ({placedBefore} → {count} pieces placed).");
                    job.Key = key;
                    // The picked piece's row now: found by its storeroom slot, as rows above it may have
                    // come out (and the rest moved down one) since it was picked.
                    var r = chosen[0];
                    if (chosen[1] >= 0)
                    {
                        var (c, sl, picked) = (chosen[1], chosen[2], chosen[0]);
                        r = rows.Known.Where(kv => kv.Value.Container == c && kv.Value.Slot == sl)
                            .OrderBy(kv => Math.Abs(kv.Key - picked)).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
                    }
                    if (r >= 0 && rows.Known.TryGetValue(r, out var what))
                    {
                        rowsLeaving.Add((r, new StockSlot(StockSource.Storeroom, what.Container, what.Slot, what.ItemId, 0, 1)));
                        RowLeft();
                    }
                    // The list shifts (and re-sorts) once a piece leaves it: read its names again before the next piece.
                    // The list is watched settling while the piece is moved (WatchList), once the storeroom has one fewer.
                    namesStale = true;
                    listSettled = false;
                    listWatch.Reset();
                    listExpect = storedBefore - 1;
                    if (!Landed(key.Value, job.Target))
                        queue.InsertNext(new[] { SettlesAfterPlace(key.Value, storedBefore, previewItem) }.Concat(MoveSteps(job, key.Value, justPlaced: true)));
                    else job.Note = "Placed at its spot.";
                    placed = true;
                    return StepOutcome.Done;
                }
                return StepOutcome.Waiting;
            },
        });
        steps.Add(new Step
        {
            Label = $"Check {name} was placed",
            Run = () =>
        {
            if (placed) return StepOutcome.Done;
            var toast = lastToast != null ? $" The game said: \"{lastToast}\"." : string.Empty;
            var count = FurnitureReader.PlacedCount(side == Side.Garden);
            if (attempt == 0)
            {
                log.Warn($"  The game didn't place it ({placedBefore} → {count} pieces placed).{toast} Trying once more.");
                GameUi.Close(UiTable.ContextMenu);
                queue.InsertNext(StoreroomAttempt(job, 1));
                return StepOutcome.Done;
            }
            return StepOutcome.Skip($"The game didn't place it, twice ({placedBefore} → {count} pieces placed).{toast} "
                                    + "Where you stand matters: the game puts a storeroom piece in front of you, so stand somewhere open.", ProblemKind.DidNotLand);
        },
        });
        return steps;
    }

    /// <summary>
    /// Waits until the furnishing list has filled in and stopped changing: its rows have names (0.2.4 read
    /// 32 blank rows the moment the tab opened) and the storeroom's count and the list's rows read the
    /// same for a few frames (0.2.4 read the count while it was still loading). Goes on after 4 s anyway.
    /// </summary>
    private Step ListSettles()
    {
        var settle = new Settle(5, Speed.Wait(250));
        return new Step
        {
            Label = "The furnishing list has filled in",
            Key = "list settles",
            Timeout = TimeSpan.FromSeconds(10),
            Run = () =>
            {
                var count = InventoryReader.StoreroomOccupied(side).Count;
                var list = ListReader.Read(UiTable.FurnishingList).OrderByDescending(l => l.Rows.Count).FirstOrDefault();
                // How many storeroom pages have loaded goes into the state too: the count climbs as they do.
                var state = listExpect is { } expect && count > expect ? null : ListReady.State(list?.Rows, count);
                if (state != null) state += $"|{InventoryReader.StoreroomPagesLoaded(side)}";
                if (settle.Update(state, DateTime.UtcNow))
                {
                    listSettled = true;
                    listExpect = null;
                    return StepOutcome.Done;
                }
                if (queue.Elapsed > TimeSpan.FromSeconds(4))
                {
                    // Go on with what's there (the names are checked by picking anyway).
                    log.Info($"  The list didn't settle in 4 s ({count} pieces in the storeroom, {list?.Rows.Count ?? 0} rows read, {list?.Rows.Count(r => r.Length > 0) ?? 0} with names); going on.");
                    listSettled = true;
                    listExpect = null;
                    return StepOutcome.Done;
                }
                return StepOutcome.Waiting;
            },
        };
    }

    /// <summary>
    /// After Place: waits until the placement has gone through before the new piece is picked up: the
    /// storeroom has one piece fewer (the server's answer), nothing is held, the editor is back in
    /// Rotate, and the new piece has its object in the furniture list, all steady for a few frames.
    /// 0.3.0 picked the piece up 50 ms after it appeared and the game let go of it 19 ms later; the
    /// object it picked was the preview's (0x97A011BD60 both times): the game keeps the preview's
    /// object for the new piece, so a changed object here is logged. If the storeroom's count hasn't
    /// moved after 1.5 s, it goes on without it.
    /// </summary>
    private Step SettlesAfterPlace(int key, int storedBefore, nint previewItem)
    {
        var settle = new Settle(4, Speed.Wait(120));
        return new Step
        {
            Label = "The game has finished placing it",
            Key = "settle after place",
            Timeout = TimeSpan.FromSeconds(3),
            OnTimeout = StepStatus.Done,
            Run = () =>
            {
                var stored = InventoryReader.StoreroomOccupied(side).Count;
                var instance = InstanceOf(key);
                var through = stored < storedBefore || queue.Elapsed > TimeSpan.FromSeconds(1.5);
                var state = through && instance != 0 && HousingEditor.ActiveItem == 0 && HousingEditor.Mode == EditorMode.Rotate
                    ? $"{FurnitureReader.PlacedCount(side == Side.Garden)}|{stored}|{instance:X}"
                    : null;
                if (!settle.Update(state, DateTime.UtcNow)) return StepOutcome.Waiting;
                log.Info($"  Placed and settled: the storeroom has {stored} (was {storedBefore}); the new piece is 0x{instance:X}"
                         + $"{(instance == previewItem ? " (the preview's object, kept by the game)" : previewItem != 0 ? $" (the preview was 0x{previewItem:X})" : string.Empty)}.");
                return StepOutcome.Done;
            },
        };
    }

    /// <summary>
    /// Between steps, every frame: once the last placement has gone through (the storeroom has one
    /// fewer), watches the furnishing list settle and reads its names again, so it's ready by the time
    /// the next piece needs it (0.3.1 waited 265 ms for it before every piece). Cheap: no clicks.
    /// </summary>
    private void WatchList()
    {
        RowLeft();
        if (listSettled || !rowsReady || listExpect == null) return;
        if (!GameUi.IsOpen(UiTable.FurnishingList) || !InventoryReader.StoreroomTabShown()) return;
        var count = InventoryReader.StoreroomOccupied(side).Count;
        if (count > listExpect.Value)
        {
            listWatch.Reset();
            return;
        }
        var list = ListReader.Read(UiTable.FurnishingList).OrderByDescending(l => l.Rows.Count).FirstOrDefault();
        var state = ListReady.State(list?.Rows, count);
        if (state != null) state += $"|{InventoryReader.StoreroomPagesLoaded(side)}";
        if (!listWatch.Update(state, DateTime.UtcNow)) return;
        listSettled = true;
        listExpect = null;
        if (namesStale) RefreshListNames();
    }

    /// <summary>
    /// The editor holds this piece, and has for a few frames running (not just the frame of the click):
    /// three frames and 50 ms; a piece just placed, four frames and 120 ms (0.3.0: let go after 19 ms).
    /// </summary>
    private Step HeldSteady(Func<nint> instance, bool justPlaced = false)
    {
        var settle = justPlaced ? new Settle(4, Speed.Wait(120)) : new Settle(3, Speed.Wait(50));
        return new Step
        {
            Label = "The game picks it up",
            Key = "picked up",
            Timeout = TimeSpan.FromSeconds(1.5),
            OnTimeout = StepStatus.Done, // decided next: try again, or give up
            Run = () =>
            {
                var target = instance();
                var held = target != 0 && HousingEditor.ActiveItem == target ? $"0x{target:X}" : null;
                return settle.Update(held, DateTime.UtcNow) ? StepOutcome.Done : StepOutcome.Waiting;
            },
        };
    }

    private Step FindRowStep(RunJob job, HashSet<int> tried, int[] chosen)
    {
        var name = Name(job);
        return new Step
        {
            Label = $"Find {name}'s row in the list",
            Key = "find row",
            Run = () =>
            {
                if (!InventoryReader.StoreroomOccupied(side).Any(o => o.ItemId == job.Target.ItemId))
                    return StepOutcome.Skip("It's not in the storeroom any more.", ProblemKind.NotFound);
                if (!listSettled)
                {
                    // The list changes after a placement (and fills in after the tab opens): wait for it to settle, then look.
                    queue.InsertNext(new[] { ListSettles(), FindRowStep(job, tried, chosen) });
                    return StepOutcome.Done;
                }
                PrepareRows();
                if (namesStale) RefreshListNames();
                // Not among the names read yet, and the list has rows past them: read further first.
                if (rows.Find(job.Target.ItemId, job.Target.Stain) == null && rows.ByName(name, job.Target.ItemId).Count == 0 && !listExhausted && !scrollUseless && NamedUpTo() < rows.Count)
                {
                    queue.InsertNext(new[] { ReadMoreRows(), FindRowStep(job, tried, chosen) });
                    return StepOutcome.Done;
                }
                queue.InsertNext(NextPick(job, tried, chosen));
                return StepOutcome.Done;
            },
        };
    }

    /// <summary>
    /// The steps for the next row to try: that row's, or when neither the piece's name nor the list's
    /// order says where it is, the list made again once (it may not show the piece: one just stored, or
    /// a list the game didn't refresh) and the search started over; else giving up.
    /// </summary>
    private List<Step> NextPick(RunJob job, HashSet<int> tried, int[] chosen)
    {
        var next = NextRow(job, tried, out var inTurn);
        if (inTurn && next != null && inTurnPicks.GetValueOrDefault(job) == 1 && listRemadeFor.Contains(job))
            log.Info($"  Trying rows in turn for {Name(job)} (at most {MaxRowsInTurn}).");
        if (inTurn && listRemadeFor.Add(job))
        {
            log.Info($"  Neither its name nor the list's order puts {Name(job)} in a row: the list is made again before looking further.");
            tried.Clear();
            searchPicks.Remove(job);
            inTurnPicks.Remove(job);
            orderLogged.Remove(job);
            var steps = RemakeList("the piece wasn't where the list should have it");
            steps.Add(FindRowStep(job, tried, chosen));
            return steps;
        }
        if (next != null) return TryRow(job, next.Value, tried, chosen);
        var name = Name(job);
        return new List<Step>
        {
            Step.Act("Give up looking", () => StepOutcome.Skip(
                $"The furnishing list didn't show {name} (looked in {tried.Count} rows, and made the list again). Place it from the list by hand, or \"Try again\" later.",
                ProblemKind.NotFound)),
        };
    }

    /// <summary>
    /// Shows the list's Placed tab and then its Storeroom tab again (one button, [i:11], switches between
    /// them, as you did in 0.3.3's recording), so the game makes the list afresh; then waits for it to fill in.
    /// </summary>
    private List<Step> RemakeList(string why) => new()
    {
        Step.Act($"Make the list again ({why}): show the Placed tab: {UiTable.ListStoreroomTab.Describe()}", () => Fire(UiTable.ListStoreroomTab)),
        Step.Wait("The Placed tab shows", () => !InventoryReader.StoreroomTabShown(), WindowWait, StepStatus.Done),
        Step.Act($"Back to the Storeroom tab: {UiTable.ListStoreroomTab.Describe()}", () =>
        {
            if (InventoryReader.StoreroomTabShown()) return StepOutcome.Done;
            return Fire(UiTable.ListStoreroomTab);
        }),
        Step.Wait("The Storeroom tab shows", InventoryReader.StoreroomTabShown, WindowWait),
        Step.Wait("The storeroom is loaded", () => InventoryReader.StoreroomLoaded(side), WindowWait),
        Step.Act("Read the list afresh", () =>
        {
            namesStale = true;
            listSettled = false;
            listWatch.Reset();
            listExpect = null;
            return StepOutcome.Done;
        }),
        ListSettles(),
    };

    /// <summary>Reads the list's names again (cheap: no clicks) after it changed, and forgets picked rows that no longer match.</summary>
    private void RefreshListNames()
    {
        namesStale = false;
        var occupied = Occupied();
        var read = ReadNames(occupied);
        if (read == null) return;
        var forgotten = rows.RefreshNames(0, read.Value.Names, data.ItemName);
        listExhausted = false;
        log.Info($"  Read the list's names again ({read.Value.Names.Count} of {rows.Count} rows, {read.Value.Source}){(forgotten > 0 ? $"; {forgotten} remembered rows had moved and were forgotten" : string.Empty)}.");
    }

    /// <summary>
    /// The list's row names: every row's, from the window's own values, when they hold exactly one
    /// name per storeroom piece; else those the list component holds (200 of 445 in 0.2.3).
    /// </summary>
    private (List<string> Names, string Source)? ReadNames(List<StoreroomRow> occupied)
    {
        var storeroomNames = occupied.Select(o => Menus.Clean(data.ItemName(o.ItemId))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fromValues = ListReader.NamesInValues(UiTable.FurnishingList, storeroomNames);
        if (fromValues.Count == occupied.Count) return (fromValues, "the window's values: every row");
        var lists = ListReader.Read(UiTable.FurnishingList);
        var best = lists.Where(l => l.Rows.Any(r => r.Length > 0)).OrderByDescending(l => l.Rows.Count).FirstOrDefault();
        if (best == null) return null;
        listNode = best.NodeId;
        return (best.Rows, $"{best.Source}, node {best.NodeId}");
    }

    private int NamedUpTo() => rows.Names.Count == 0 ? 0 : rows.Names.Keys.Max() + 1;

    /// <summary>
    /// Gets the storeroom list's rows ready, once per run: those saved for this house if the storeroom
    /// is as it was, and the names the list holds (read without clicking).
    /// </summary>
    private void PrepareRows()
    {
        RowLeft();
        var occupied = Occupied();
        if (rowsReady && rows.Count == occupied.Count) return;
        var fp = StoreroomRows.Fingerprint(occupied);
        // Rows still to come out are counted in the rows being replaced.
        rowsLeaving.Clear();
        if (config.StoreroomRows.TryGetValue(HouseKey, out var saved) && rows.Import(saved, fp))
            log.Info($"  Using {rows.Known.Count} storeroom list rows learnt in an earlier run (the storeroom hasn't changed since).");
        else
        {
            if (rowsReady) log.Info($"  The storeroom now has {occupied.Count} pieces (the list had {rows.Count} rows): learning the rows again.");
            rows.Reset(occupied.Count);
        }
        rowsReady = true;
        listExhausted = false;
        namesStale = false;
        log.Info($"  The furnishing list's own rows: {ListReader.Describe(ListReader.Read(UiTable.FurnishingList))}");
        var read = ReadNames(occupied);
        if (read == null)
        {
            listExhausted = true;
            log.Info("  Couldn't read the rows' names from the list: rows are found by picking them (once each; groups of the same piece are skipped).");
            return;
        }
        rows.RefreshNames(0, read.Value.Names, data.ItemName);
        log.Info($"  Read the names of rows 0–{read.Value.Names.Count - 1} of {rows.Count} ({read.Value.Source}); rows are found by name and checked by picking.");
    }

    /// <summary>Scrolls the list past the rows whose names are known and reads again, lining the new names up by their overlap.</summary>
    private Step ReadMoreRows() => new()
    {
        Label = "Read more of the list's rows",
        Run = () =>
        {
            var from = NamedUpTo();
            if (!ListReader.ScrollTo(UiTable.FurnishingList, listNode, Math.Min(rows.Count - 1, from + 10)))
            {
                listExhausted = true;
                scrollUseless = true;
                log.Info("  Couldn't scroll the list to read further rows: the rest are found by picking.");
                return StepOutcome.Done;
            }
            queue.InsertNext(new[]
            {
                new Step
                {
                    Label = "Read the rows the list holds now",
                    Delay = Speed.Wait(250),
                    Run = () =>
                    {
                        var list = ListReader.Read(UiTable.FurnishingList).FirstOrDefault(l => l.NodeId == listNode);
                        var offset = list == null ? null : ListWindows.Align(rows.Names, list.Rows);
                        var before = NamedUpTo();
                        if (list != null && offset != null && offset.Value + list.Rows.Count > before)
                        {
                            rows.LearnNames(offset.Value, list.Rows);
                            log.Info($"  After scrolling, the list holds rows {offset}–{offset + list.Rows.Count - 1}: names now known up to row {NamedUpTo() - 1} of {rows.Count}.");
                        }
                        else
                        {
                            listExhausted = true;
                            scrollUseless = true;
                            log.Info($"  Scrolling didn't show further rows ({(list == null ? "list gone" : offset == null ? "couldn't line the rows up" : "same rows")}): rows past those read are found from the list's sort order (not scrolled again this run).");
                        }
                        // Back to the top: the names are read again from row 0 after each placement (0.3.2 left the
                        // list scrolled, and the rows read then were taken for rows 0 onwards).
                        ListReader.ScrollTo(UiTable.FurnishingList, listNode, 0);
                        return StepOutcome.Done;
                    },
                },
            });
            return StepOutcome.Done;
        },
    };

    /// <summary>
    /// The next row to try for a piece: a known row, a row whose name is the piece's, else the row the
    /// list's sort order puts it in (<see cref="StoreroomOrder"/>: predicted from the storeroom's
    /// contents, bounded by every row whose item is known, halving what's left with each miss), and
    /// only if that order turns out not to hold, the first row that could be it.
    /// </summary>
    private int? NextRow(RunJob job, HashSet<int> tried, out bool inTurn)
    {
        inTurn = false;
        if (tried.Count >= MaxRowPicksPerPiece) return null;
        var item = job.Target.ItemId;
        var name = Menus.Clean(Name(job));
        var dye = DyeWanted(job);
        // A row picked before and found to hold this piece in another dye isn't it, while the storeroom has
        // one in the dye wanted (0.3.3 picked the same six black partitions again for every undyed one).
        var dyeMatters = Occupied().Any(o => o.ItemId == item && o.Stain == dye);
        bool OtherDye(int r) => dyeMatters && rows.Known.TryGetValue(r, out var k) && k.ItemId == item && k.Stain != dye;
        bool NameFits(int r) => !rows.Names.TryGetValue(r, out var n) || string.Equals(n, name, StringComparison.OrdinalIgnoreCase);
        var known = rows.Find(item, dye);
        if (known is { } kr && !tried.Contains(kr) && NameFits(kr) && !OtherDye(kr)) return kr;
        foreach (var r in rows.ByName(Name(job), item))
            if (!tried.Contains(r) && !OtherDye(r)) return r;
        // Rows known to hold it in another dye are passed over by the order search too (0.3.4 went back to them).
        var skip = new HashSet<int>(tried);
        foreach (var (r, _) in rows.Known) if (OtherDye(r)) skip.Add(r);
        var searched = searchPicks.GetValueOrDefault(job);
        if (searched < SearchPicks)
        {
            if (SearchByOrder(job, skip, searched) is { } probe)
            {
                searchPicks[job] = searched + 1;
                return probe;
            }
            // The rows known don't fit the order around it: no more order picks for it (0.3.3 asked again every pick).
            searchPicks[job] = SearchPicks + 1;
        }
        else if (searched == SearchPicks && !inTurnPicks.ContainsKey(job))
            log.Info($"  {SearchPicks} picks didn't find {Name(job)} by the list's order.");
        // Rows in turn, a limited number: never a row read to hold another piece; rows only guessed to be
        // another piece's are tried before giving up.
        inTurn = true;
        var scanned = inTurnPicks.GetValueOrDefault(job);
        if (scanned >= MaxRowsInTurn) return null;
        var next = rows.NextCandidate(Name(job), item, skip);
        if (next != null) inTurnPicks[job] = scanned + 1;
        return next;
    }

    /// <summary>
    /// A piece still to place from the storeroom, later in the run, of the same item and in the same pass
    /// (floor, and floor pieces before what sits on them) as <paramref name="job"/>, wanting a copy in
    /// <paramref name="stain"/>; or null.
    /// </summary>
    private RunJob? TakerOf(RunJob job, byte stain)
    {
        for (var i = index + 1; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j.Outcome == JobOutcome.Pending && j.Kind == JobKind.PlaceFromStoreroom && j.Target.ItemId == job.Target.ItemId
                && j.Target.Floor == job.Target.Floor && j.Target.Late == job.Target.Late && DyeWanted(j) == stain)
                return j;
        }
        return null;
    }

    /// <summary>The dye of the copy the plan chose (the design's, or one it will dye), the dye a storeroom row is checked for.</summary>
    private static byte DyeWanted(RunJob job) => job.Entry?.Stock?.Stain ?? job.Target.Stain;

    /// <summary>Rows tried in turn for each piece (see <see cref="MaxRowsInTurn"/>).</summary>
    private readonly Dictionary<RunJob, int> inTurnPicks = new();

    /// <summary>Pieces whose rows were found not to fit the list's order (said once each).</summary>
    private readonly HashSet<RunJob> orderLogged = new();

    /// <summary>Picks the sort-order search may make for one piece before rows are tried in turn (under 20 find any of 445 rows).</summary>
    private const int SearchPicks = 20;

    /// <summary>The row the list's sort order says to pick next for a piece, or null when the known rows contradict the order.</summary>
    private int? SearchByOrder(RunJob job, HashSet<int> tried, int picks)
    {
        var occupied = Occupied();
        var items = occupied.Select(o => o.ItemId).ToList();
        var seen = SeenRows(occupied);
        if (!orderChecked)
        {
            orderChecked = true;
            var (right, knownRows) = StoreroomOrder.Check(StoreroomOrder.Predict(items, data.SortKey), seen);
            log.Info($"  The list's sort order (UI category, then the item's sort number, then item id) predicts {right} of the {knownRows} rows whose item is known.");
        }
        var predicted = StoreroomOrder.FirstRow(items, data.SortKey, job.Target.ItemId);
        var probe = StoreroomOrder.Next(job.Target.ItemId, data.SortKey, rows.Count, seen, tried, predicted, picks);
        if (probe is not { } p)
        {
            if (orderLogged.Add(job)) log.Info($"  The rows known don't fit the list's sort order around {Name(job)} (predicted row {predicted}).");
            return null;
        }
        log.Info(p.Predicted
            ? $"  {Name(job)} should start at row {p.Row}, by the list's sort order (it can be in rows {p.Low}–{p.High})."
            : $"  {Name(job)} is in rows {p.Low}–{p.High} by the rows known (predicted {predicted}); trying row {p.Row}.");
        return p.Row;
    }

    /// <summary>What's known of each row's item: picked, inferred from a group, or read by name (when the name is one item's only).</summary>
    private Dictionary<int, uint> SeenRows(List<StoreroomRow> occupied)
    {
        var byName = occupied.Select(o => o.ItemId).Distinct()
            .GroupBy(i => Menus.Clean(data.ItemName(i)), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<int, uint>();
        foreach (var (row, n) in rows.Names)
            if (byName.TryGetValue(n, out var item)) seen[row] = item;
        foreach (var (row, item) in rows.Inferred) seen[row] = item;
        foreach (var (row, what) in rows.Known) seen[row] = what.ItemId;
        return seen;
    }

    /// <summary>The new piece of this item that's appeared since the run started (nearest the preview), or null.</summary>
    private int? IdentifyNew(RunJob job, Vector3? previewAt)
    {
        var live = ReadLive();
        if (live == null) return null;
        var key = NewPieces.Identify(knownKeys, live.Values.Select(p => new SeenPiece(p.Key, p.ItemId, p.Position)), job.Target.ItemId, previewAt);
        if (key != null) knownKeys.Add(key.Value);
        return key;
    }

    /// <summary>Picks a row; learns what it holds; if it isn't the piece, closes its menu and tries the next.</summary>
    private List<Step> TryRow(RunJob job, int row, HashSet<int> tried, int[] chosen)
    {
        var name = Name(job);
        var blanks = UiCallValues.Of(("index", row));
        var expected = rows.Names.TryGetValue(row, out var n) && string.Equals(n, Menus.Clean(name), StringComparison.OrdinalIgnoreCase)
                       || rows.Known.TryGetValue(row, out var k) && k.ItemId == job.Target.ItemId;
        tried.Add(row);
        return new List<Step>
        {
            new Step
            {
                Label = $"Pick row {row}: {UiTable.ListSelect.Describe(blanks)}",
                Key = "pick row",
                IsAction = true,
                Run = () => GameUi.Fire(UiTable.ListSelect, blanks) ? StepOutcome.Done : StepOutcome.Halt("The furnishing list closed."),
            },
            new Step
            {
                Label = "Its menu opens",
                Key = "menu opens",
                Timeout = WindowWait,
                Run = () => GameUi.IsOpen(UiTable.ContextMenu) && InventoryReader.StoreroomSelection() != null ? StepOutcome.Done : StepOutcome.Waiting,
            },
            new Step
            {
                Label = "See which piece the game picked",
                Key = "check row",
                Run = () =>
                {
                    var (container, slot) = InventoryReader.StoreroomSelection()!.Value;
                    var occupied = Occupied();
                    var held = occupied.FirstOrDefault(o => o.Container == container && o.Slot == slot);
                    var seen = new StoreroomRow(container, slot, held.ItemId, held.Stain);
                    if (held.ItemId != 0) rows.Learn(row, seen, occupied.Count(o => o.ItemId == held.ItemId));
                    rowsDirty = true;
                    // The piece in the dye the plan chose it in (the design's, or one to dye) while the storeroom
                    // has one: 0.3.2 took any of the item, so an undyed table placed for a black one used up a
                    // dye and the black one was left over.
                    var sameItem = held.ItemId == job.Target.ItemId;
                    var dye = DyeWanted(job);
                    var right = sameItem && (held.Stain == dye || !occupied.Any(o => o.ItemId == held.ItemId && o.Stain == dye));
                    log.Info($"  row {row} → storeroom {container}/{slot}: {(held.ItemId == 0 ? "nothing?" : data.ItemName(held.ItemId))}"
                             + $"{(right ? " (this one)" : sameItem ? $" in {DyeWord(held.Stain)}; the storeroom has one in {DyeWord(dye)}" : string.Empty)}");
                    if (right)
                    {
                        chosen[0] = row;
                        chosen[1] = container;
                        chosen[2] = slot;
                        return StepOutcome.Done;
                    }
                    // Another of this item still to place wants this very dye: it's placed for that one now (no pick
                    // wasted), and this one's comes later (0.3.4 passed over dozens of copies in a mixed-dye group).
                    if (sameItem && TakerOf(job, held.Stain) is { } other)
                    {
                        log.Info($"  That's the copy another {name} needs ({DyeWord(held.Stain)}): it's placed for that one now; this one ({DyeWord(dye)}) comes later.");
                        job.SwapTargets(other);
                        chosen[0] = row;
                        chosen[1] = container;
                        chosen[2] = slot;
                        return StepOutcome.Done;
                    }
                    // Not it: close its menu now, in this step (only the pick is a click with the gap before it;
                    // 0.3.1 spent 0.6 s a row with a second, paced click to close it).
                    if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                    var after = new List<Step>
                    {
                        Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
                    };
                    if (expected && !sameItem)
                    {
                        // The row was meant to hold it (by its name, or remembered): the list has changed. Read it again, not scan.
                        log.Info($"  Row {row} was expected to hold {name}; the list has changed, so its names are read again.");
                        namesStale = true;
                        after.Add(FindRowStep(job, tried, chosen));
                    }
                    else after.AddRange(NextPick(job, tried, chosen));
                    queue.InsertNext(after);
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>
    /// From your bags. Placing straight from a bag item's menu didn't work (0.3.0: the editor held the
    /// piece, the set-down got "Unable to place item." and nothing was placed), while placing from the
    /// storeroom does, so a bag piece is first stored: its menu's "Store" (0:Place | 1:Store | …), then,
    /// once the storeroom has it and the bag doesn't, the storeroom's steps place it. If the game won't
    /// store it (the storeroom is full, or it says no), the piece is placed from the bag as before,
    /// with "place anywhere" on for that one set-down; if that fails too, you put it down.
    /// </summary>
    private List<Step> BagSteps(RunJob job)
    {
        if (BagRoute.First(side == Side.Interior) == BagWay.Held) return HeldAttempt(job);
        var name = Name(job);
        var stock = job.Entry?.Stock;
        var storedBefore = -1;
        var inBagsBefore = -1;
        var settle = new Settle(3, Speed.Wait(100));
        var storeWords = UiTable.StoreLabels(Services.ClientState.ClientLanguage);
        var noStore = false;
        return new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Act($"Open {name}'s menu ({(stock != null ? InventoryReader.Describe(stock) : "bags")})", () =>
            {
                var slot = FindInBags(job.Target.ItemId, stock);
                if (slot == null) return StepOutcome.Skip("It's not in your bags any more.", ProblemKind.NotFound);
                storedBefore = StoredCount(job.Target.ItemId);
                inBagsBefore = BagCount(job.Target.ItemId);
                lastToast = null;
                log.Info($"  {name}: container {slot.Container}, slot {slot.Slot}; menu opened from window id {GameUi.BagWindowId()}. {inBagsBefore} in your bags, {storedBefore} in the storeroom.");
                return HousingActions.OpenBagItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
            }),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
            new Step
            {
                Label = $"Choose \"{storeWords.FirstOrDefault()}\" in the item's menu (to send it to the storeroom)",
                IsAction = true,
                Run = () =>
                {
                    var menu = GameUi.MenuEntries();
                    if (menu == null) return StepOutcome.Halt("The item's menu closed before \"Store\" could be chosen.");
                    log.Info($"  menu entries: {string.Join(" | ", menu.Select((e, i) => $"{i}:{e}"))}");
                    var entry = Menus.FindEntry(menu, storeWords);
                    if (entry < 0)
                    {
                        // Not a reason to stop the run (another client language): the bag's own way, below.
                        noStore = true;
                        log.Warn($"  The menu has no entry like \"{string.Join("\" / \"", storeWords)}\".");
                        GameUi.Fire(UiTable.MenuCancel);
                        return StepOutcome.Done;
                    }
                    var blanks = UiCallValues.Of(("entry", entry));
                    log.Info($"  → {UiTable.MenuChoose.Describe(blanks)}");
                    return GameUi.Fire(UiTable.MenuChoose, blanks) ? StepOutcome.Done : StepOutcome.Halt("The item's menu closed.");
                },
            },
            new Step
            {
                Label = $"{name} goes into the storeroom",
                Key = "stored",
                // Decided in the step (stored → the storeroom's steps; refused or no change in 3.5 s → the
                // bag's own way); the queue's timeout is only a backstop, and then the piece is skipped.
                Timeout = TimeSpan.FromSeconds(8),
                OnTimeout = StepStatus.SkipJob,
                Run = () =>
                {
                    if (noStore)
                    {
                        queue.InsertNext(new[] { Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait, StepStatus.Done) }.Concat(HeldAttempt(job)));
                        return StepOutcome.Done;
                    }
                    var timedOut = queue.Elapsed > TimeSpan.FromSeconds(3.5);
                    // A yes/no is answered once a click gap (not every frame), and not past the wait.
                    if (!timedOut && AnswerYes()) return StepOutcome.Waiting;
                    var stored = StoredCount(job.Target.ItemId);
                    var inBags = BagCount(job.Target.ItemId);
                    var refused = lastToast != null;
                    // A store that happened wins, even if it settles after the 3.5 s.
                    var isStored = BagRoute.Stored(storedBefore, stored, inBagsBefore, inBags);
                    if (isStored || (!refused && !timedOut))
                    {
                        var state = isStored ? $"{stored}|{inBags}" : null;
                        if (!settle.Update(state, DateTime.UtcNow)) return StepOutcome.Waiting;
                        log.Info($"  Stored: {stored} in the storeroom (was {storedBefore}), {inBags} in your bags (was {inBagsBefore}). Placing it from the storeroom.");
                        // Its row goes where the list's order puts it, the rows after it moving down (rather than every row being learnt again).
                        var occupiedNow = Occupied();
                        if (rowsReady && rows.Count == occupiedNow.Count - 1)
                        {
                            var at = StoreroomOrder.FirstRow(occupiedNow.Select(o => o.ItemId).Where(i => i != job.Target.ItemId), data.SortKey, job.Target.ItemId);
                            rows.Inserted(at);
                            rowsDirty = true;
                            log.Info($"  Its row should be {at}, by the list's order: the rows after it move down one.");
                        }
                        // The list has one more row now: read again before looking for it.
                        namesStale = true;
                        listSettled = false;
                        listWatch.Reset();
                        listExpect = null;
                        listMissesStored = true;
                        queue.InsertNext(StoreroomSteps(job));
                        return StepOutcome.Done;
                    }
                    log.Warn($"  It wasn't stored ({(refused ? $"the game said \"{lastToast}\"" : "nothing changed in 3.5 s")}; {stored} in the storeroom, {inBags} in your bags): placing it from the bag instead.");
                    if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                    queue.InsertNext(new[] { Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait, StepStatus.Done) }.Concat(HeldAttempt(job)));
                    return StepOutcome.Done;
                },
            },
        };
    }

    private DateTime lastYes;

    /// <summary>
    /// When the game asks yes/no, answers Yes, once a click gap (a dialog can take frames to close;
    /// answering every frame could also answer the one after it). True while one is open.
    /// </summary>
    private bool AnswerYes()
    {
        if (GameUi.YesNoText() is not { } question) return false;
        if (DateTime.UtcNow - lastYes < TimeSpan.FromMilliseconds(Math.Max(Speed.ClickGapMs, 250))) return true;
        lastYes = DateTime.UtcNow;
        log.Info($"  The game asks: \"{question}\" → {UiTable.YesNoYes.Describe()}");
        GameUi.Fire(UiTable.YesNoYes);
        return true;
    }

    private string DyeWord(byte stain) => stain == 0 ? "no dye" : data.DyeName(stain);

    /// <summary>How many of this item the storeroom holds.</summary>
    private int StoredCount(uint itemId) => InventoryReader.StoreroomOccupied(side).Count(o => o.ItemId == itemId);

    /// <summary>How many of this item your bags hold.</summary>
    private int BagCount(uint itemId) =>
        InventoryReader.Read(data, side, out _).Where(s => s.Source == StockSource.Inventory && s.ItemId == itemId).Sum(s => Math.Max(1, s.Quantity));

    /// <summary>
    /// Placing from the bag item's menu: Place, the editor holds the piece, it's aimed and set down
    /// with "place anywhere" on for this one set-down (0.3.0's try without it was refused). If no new
    /// piece appears, you put it down (it's back in your bags) and the plugin sets it at its spot.
    /// </summary>
    private List<Step> HeldAttempt(RunJob job)
    {
        var name = Name(job);
        var stock = job.Entry?.Stock;
        var steps = new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Act($"Open {name}'s menu ({(stock != null ? InventoryReader.Describe(stock) : "bags")}) to place it from there", () =>
            {
                var slot = FindInBags(job.Target.ItemId, stock);
                if (slot == null) return StepOutcome.Skip("It's not in your bags any more.", ProblemKind.NotFound);
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
    /// Recording 2, where a real click set it down). Aim it, set it down with the editor's own function
    /// (place anywhere on for this set-down), wait for it to appear, and nudge it onto the spot; if it
    /// doesn't appear, you put it down.
    /// </summary>
    private List<Step> HeldSteps(RunJob job)
    {
        var name = Name(job);
        return new List<Step>
        {
            Step.Wait("The editor holds the piece (Place mode)", () => HousingEditor.Mode == EditorMode.Place && HousingEditor.ActiveItem != 0, WindowWait, StepStatus.SkipJob),
            Step.Act($"Aim {name} at its spot", () =>
            {
                HousingActions.AimHeld(job.Target.Position, job.Target.Yaw);
                return StepOutcome.Done;
            }, Speed.Wait(200)),
            Step.Act($"Set {name} down (the editor's set-down on the held piece, place anywhere on)", () =>
            {
                HousingActions.AimHeld(job.Target.Position, job.Target.Yaw);
                // Only a piece the editor is placing right now: never one already in the room.
                var held = HousingEditor.Mode == EditorMode.Place ? HousingEditor.ActiveItem : 0;
                if (held == 0) return StepOutcome.Halt($"The editor isn't holding a piece to set down (mode {HousingEditor.Mode}).");
                if (!anywhere.Active)
                {
                    if (anywhere.Enable())
                    {
                        anywhereForOne = true;
                        log.Info("  Place anywhere is on for this set-down only.");
                    }
                    else log.Info($"  Couldn't switch place anywhere on ({anywhere.Error}); setting it down with the game's usual checks.");
                }
                lastToast = null;
                return editor.Place(held) ? StepOutcome.Done : StepOutcome.Halt("Couldn't set the held piece down.");
            }, Speed.Wait(150)),
            new Step
            {
                Label = $"{name} appears in the room",
                Key = "appears",
                Delay = Speed.Wait(100),
                Timeout = TimeSpan.FromSeconds(4),
                OnTimeout = StepStatus.Done, // decided here
                Run = () =>
                {
                    if (FindNewPiece(job))
                    {
                        EndAnywhereForOne();
                        if (!Landed(job.Key!.Value, job.Target)) queue.InsertNext(MoveSteps(job, job.Key.Value, justPlaced: true));
                        else job.Note = "Placed at its spot.";
                        return StepOutcome.Done;
                    }
                    // Refused (0.3.0: "Unable to place item.", then back to Rotate with nothing placed), or nothing after 3.5 s.
                    var refused = lastToast != null && HousingEditor.Mode != EditorMode.Place;
                    if (!refused && queue.Elapsed < TimeSpan.FromSeconds(3.4)) return StepOutcome.Waiting;
                    EndAnywhereForOne();
                    log.Warn($"  {name} wasn't set down{(lastToast != null ? $" (the game said \"{lastToast}\")" : string.Empty)}: you put it down, and it's then set at its spot.");
                    queue.InsertNext(ByHandSteps(job));
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>Switches place anywhere off again if it was switched on for one set-down.</summary>
    private void EndAnywhereForOne()
    {
        if (!anywhereForOne) return;
        anywhereForOne = false;
        anywhere.Disable();
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
                Label = $"You: put {name} down anywhere (it's in {where}; if the editor still holds it, click to set it down); it'll be set at its spot",
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
                // Just put down: picked up only once the game has let go of it, as for a placed piece.
                queue.InsertNext(MoveSteps(job, job.Key!.Value, justPlaced: true));
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
                Delay = Speed.Wait(200),
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
                        }, Speed.Wait(300)),
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
                if (Rehearsal) queue.InsertNext(RehearseDyeEnd(job, key));
                return Fire(UiTable.DyePick, values);
            }),
            Step.Act($"Press Dye: {UiTable.DyeApply.Describe()}", () => Fire(UiTable.DyeApply)),
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
                    // Only on this piece: the window opens on whatever's picked, and dyeing
                    // another piece would use up the dye on it.
                    HousingEditor.ActiveItem != instance
                        ? StepOutcome.Skip("The piece to dye wasn't the one picked.", ProblemKind.DyeFailed)
                        : actions.OpenDyeWindow() ? StepOutcome.Done : StepOutcome.Skip(actions.DyeError ?? "The game didn't say which piece is picked.", ProblemKind.DyeFailed)),
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
                Delay = Speed.Wait(250),
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
                        OnTimeout = StepStatus.Done, // decided here: you're asked only if it didn't open
                        Run = () =>
                        {
                            if (GameUi.IsOpen(UiTable.HousingMenu) || HousingEditor.Mode != EditorMode.None) return StepOutcome.Done;
                            return StepOutcome.Waiting;
                        },
                    });
                    steps.Add(new Step
                    {
                        Label = "Did the Housing menu open?",
                        Run = () =>
                        {
                            if (GameUi.IsOpen(UiTable.HousingMenu) || HousingEditor.Mode != EditorMode.None) return StepOutcome.Done;
                            queue.InsertNext(new[]
                            {
                                new Step
                                {
                                    Label = "You: open the Housing menu (Main menu → Social → Housing); it didn't open by itself",
                                    Timeout = YouWait,
                                    Run = () =>
                                    {
                                        editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                                        return GameUi.IsOpen(UiTable.HousingMenu) || HousingEditor.Mode != EditorMode.None ? StepOutcome.Done : StepOutcome.Waiting;
                                    },
                                    TimeoutMessage = "The Housing menu didn't open.",
                                },
                            });
                            return StepOutcome.Done;
                        },
                    });
                    continue;
                }
                var call = CallOf(click);
                if (click is ModeClick.Rotate or ModeClick.Move)
                {
                    // The bar's button, only if the editor isn't in its mode already (Indoor Furnishings opens in Rotate or Move).
                    var bar = click;
                    steps.Add(new Step
                    {
                        Label = $"{ClickWord(bar)} if the editor isn't in {ModeWord(ModeRoute.After(bar))}",
                        Run = () =>
                        {
                            if (!ModeRoute.Needed(bar, HousingEditor.Mode)) return StepOutcome.Done;
                            queue.InsertNext(new[]
                            {
                                Step.Act($"{ClickWord(bar)}: {call.Describe()}", () =>
                                {
                                    editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                                    return GameUi.Fire(call) ? StepOutcome.Done : StepOutcome.Halt($"{call.Addon} isn't open, so the editor couldn't be switched to {ModeWord(mode)}.");
                                }),
                                Step.Wait($"The editor is in {ModeWord(ModeRoute.After(bar))}", () =>
                                {
                                    editorMayCloseUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                                    return ModeRoute.Reached(bar, HousingEditor.Mode);
                                }, ClickWait),
                            });
                            return StepOutcome.Done;
                        },
                    });
                    continue;
                }
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
                var waitFor = click == ModeClick.OpenFurnishings ? "The furnishing editor opens (in Rotate or Move)" : $"The editor is in {ModeWord(ModeRoute.After(click))}";
                steps.Add(ModeRoute.LeadsToHousingMenu(click)
                    ? Step.Wait("The Housing menu comes back", () => GameUi.IsOpen(UiTable.HousingMenu), ClickWait)
                    : Step.Wait(waitFor, () =>
                    {
                        editorMayCloseUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                        return ModeRoute.Reached(click, HousingEditor.Mode);
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
            return StepOutcome.Halt($"The menu has no entry like \"{string.Join("\" / \"", labels)}\" (it has: {string.Join(", ", menu)}). Type the right words in Settings → Help → The \"Place\" words.");
        var blanks = UiCallValues.Of(("entry", entry));
        log.Info($"  → {UiTable.MenuChoose.Describe(blanks)}");
        return GameUi.Fire(UiTable.MenuChoose, blanks) ? StepOutcome.Done : StepOutcome.Halt("The menu closed.");
    });

    /// <summary>Presses Yes if the game asks a yes/no question within a moment (logging the question); goes on if it doesn't.</summary>
    private Step AnswerYesNo()
    {
        return new Step
        {
            Label = $"Answer Yes if the game asks ({UiTable.YesNo})",
            Run = () =>
            {
                var question = GameUi.YesNoText();
                if (question != null)
                {
                    lastYes = DateTime.UtcNow;
                    log.Info($"  The game asks: \"{question}\" → {UiTable.YesNoYes.Describe()}");
                    GameUi.Fire(UiTable.YesNoYes);
                    return StepOutcome.Done;
                }
                // Already placing (bags) or placed (storeroom): nothing was asked.
                if (HousingEditor.Mode == EditorMode.Place || queue.Elapsed > TimeSpan.FromSeconds(0.8)) return StepOutcome.Done;
                return StepOutcome.Waiting;
            },
            Timeout = TimeSpan.FromSeconds(3),
        };
    }

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

    /// <summary>Takes placed pieces' rows out of the rows once the storeroom no longer holds them (see <see cref="rowsLeaving"/>).</summary>
    private void RowLeft()
    {
        if (rowsLeaving.Count == 0) return;
        // The lowest last, so taking one out doesn't move the others still to come out.
        foreach (var leaving in rowsLeaving.OrderByDescending(l => l.Row).ToList())
        {
            if (InventoryReader.StillHolds(leaving.Slot)) continue;
            rowsLeaving.Remove(leaving);
            rows.Removed(leaving.Row);
            for (var i = 0; i < rowsLeaving.Count; i++)
                if (rowsLeaving[i].Row > leaving.Row) rowsLeaving[i] = (rowsLeaving[i].Row - 1, rowsLeaving[i].Slot);
            rowsDirty = true;
        }
    }

    /// <summary>Saves the learnt rows for this house (every few seconds while they change, and when the run ends).</summary>
    private void SaveRows(bool force)
    {
        if (!rowsReady || !rowsDirty || DryRun) return;
        if (!force && DateTime.UtcNow - rowsSavedAt < RowsSaveEvery) return;
        var occupied = Occupied();
        // Only rows that line up with the storeroom as it is now (not while a placed piece's row is still to come out).
        if (rows.Count != occupied.Count || rowsLeaving.Count > 0) return;
        config.StoreroomRows[HouseKey] = rows.Export(StoreroomRows.Fingerprint(occupied));
        config.Save();
        rowsDirty = false;
        rowsSavedAt = DateTime.UtcNow;
    }

    private void PlanDyes()
    {
        var live = ReadLive();
        if (live == null) return;
        // Where each piece is now: its job's slot (moved, or placed this run), else the slot it was in place in.
        var keyOf = new Dictionary<(int, uint), int>();
        foreach (var j in jobs.Where(j => j.Key is >= 0 && j.Phase != RunPhase.Dye)) keyOf[(j.Target.Key, j.Target.ItemId)] = j.Key!.Value;
        int? KeyOf(PlanEntry e) => keyOf.TryGetValue((e.Target.Key, e.Target.ItemId), out var k) ? k : e.State is PieceState.InPlace or PieceState.Move ? e.Placed?.Key : null;
        byte? LiveStain(int key) => live.TryGetValue(key, out var piece) ? piece.Stain : null;
        // The same calculation as the run's first line (DyeCounts.ToApply); a dry run places nothing, so
        // it predicts what placing would leave, as that line does.
        var needs = DyeCounts.Needs(entries.Where(e => KeyOf(e) is not { } k || !live.TryGetValue(k, out var p) || p.ItemId == e.Target.ItemId), KeyOf, LiveStain, predict: DryRun || Rehearsal);
        // Count every needed dye as available here: the dye window may use another dye item (it used
        // "Standard Spectrum Dye" in the recordings), so the game decides, and a refused dye is skipped then.
        var owned = needs.Select(n => n.Want).Where(s => s != 0).Distinct().ToDictionary(s => s, s => int.MaxValue);
        var plan = DyePlanner.Plan(needs, owned);
        foreach (var j in plan.Jobs.Where(j => j.Key == DyeCounts.NotPlacedYet)) j.Key = null;
        jobs.AddRange(plan.Jobs);
        log.Info($"Dyes: {plan.Jobs.Count} piece{(plan.Jobs.Count == 1 ? "" : "s")} to dye{(DryRun || Rehearsal ? " (once placed)" : string.Empty)}{(dyesExpected != plan.Jobs.Count ? $"; {dyesExpected} were expected at the start{(DryRun || Rehearsal ? string.Empty : " (pieces that didn't get placed can't be dyed)")}" : string.Empty)}.");
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

    /// <summary>
    /// A restore's last phase: each piece placed now that isn't in the backup (and was placed when the
    /// restore started), to be put away in the storeroom. Last, after the moves, placements and dyes:
    /// whether storing a piece renumbers the others' furniture slots, which those use, isn't known.
    /// </summary>
    private void PlanPutAway()
    {
        var live = ReadLive();
        if (live == null) return;
        foreach (var p in toPutAway)
        {
            // The same piece, still where it was (a slot that came to hold another copy isn't put away).
            if (!live.TryGetValue(p.Key, out var now) || now.ItemId != p.ItemId || Vector3.Distance(now.Position, p.Position) > 0.25f) continue;
            var target = new Target(-1 - p.Key, p.ItemId, p.Position, p.Yaw, p.Stain, Floor.Ground, false);
            jobs.Add(new RunJob { Kind = JobKind.Store, Target = target, Key = p.Key });
        }
        log.Info($"Putting away: {Words(jobs.Count(j => j.Kind == JobKind.Store))} placed since the backup, not in it.");

        static string Words(int n) => n == 1 ? "1 piece" : $"{n} pieces";
    }

    /// <summary>
    /// Puts a placed piece in the storeroom as you would: the editor's Store mode (its Store button), and
    /// the piece picked with the editor's own pick (in Store mode, picking a piece stores it, as 0.3.3's
    /// recording showed); a yes/no is answered. Not yet seen to work from the plugin: if the piece doesn't
    /// go, it's skipped, to be put away by hand.
    /// </summary>
    private List<Step> StoreSteps(RunJob job)
    {
        var name = Name(job);
        var key = job.Key!.Value;
        return new List<Step>
        {
            // From the second piece on, the editor is in Store mode already: no Rotate and back.
            HousingEditor.Mode == EditorMode.Store ? Step.Pause("Already in Store mode", TimeSpan.Zero) : EnsureMode(EditorMode.Rotate),
            new Step
            {
                Label = "The editor is in Store mode",
                Run = () =>
                {
                    if (HousingEditor.Mode == EditorMode.Store) return StepOutcome.Done;
                    queue.InsertNext(new[]
                    {
                        Step.Act($"Press Store: {UiTable.LayoutStore.Describe()}", () =>
                        {
                            editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                            storeModeUsed = true;
                            return Fire(UiTable.LayoutStore);
                        }),
                        Step.Wait("The editor is in Store mode", () => HousingEditor.Mode == EditorMode.Store, ClickWait),
                    });
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Put {name} away (picked in Store mode)",
                Key = "store pick",
                IsAction = true,
                Run = () =>
                {
                    var live = ReadLive();
                    if (live == null || !live.TryGetValue(key, out var piece) || piece.ItemId != job.Target.ItemId)
                        return StepOutcome.Skip("It's no longer where it was (moved or put away already).", ProblemKind.NotFound);
                    if (piece.Instance == 0) return StepOutcome.Skip("The game hasn't loaded it (walk closer to it).", ProblemKind.NotLoaded);
                    if (StoreroomRefuses()) return StepOutcome.Skip(StoreroomFullWords, ProblemKind.Other);
                    lastToast = null;
                    return editor.Select(piece.Instance) ? StepOutcome.Done : StepOutcome.Halt("Couldn't pick the piece (the editor closed?).");
                },
            },
            new Step
            {
                Label = "It goes to the storeroom",
                Key = "stored away",
                Timeout = TimeSpan.FromSeconds(4),
                OnTimeout = StepStatus.SkipJob,
                TimeoutProblem = ProblemKind.Other,
                TimeoutMessage = "It wasn't put away (Store mode didn't take it from the plugin). Put it in the storeroom by hand.",
                Run = () =>
                {
                    if (AnswerYes()) return StepOutcome.Waiting;
                    var live = ReadLive();
                    if (live == null || live.TryGetValue(key, out var piece) && piece.ItemId == job.Target.ItemId)
                    {
                        // Still placed: the game said something (the storeroom is full?), or 3.5 s went by.
                        var said = lastToast != null && queue.Elapsed > TimeSpan.FromSeconds(1);
                        if (!said && queue.Elapsed <= TimeSpan.FromSeconds(3.5)) return StepOutcome.Waiting;
                        var room = InventoryReader.StoreroomRoom(side);
                        log.Warn($"  {name} wasn't put away ({(said ? $"the game said \"{lastToast}\"" : "nothing changed in 3.5 s")}; room for {room?.ToString() ?? "?"} in the storeroom).");
                        if (said || room is <= 0)
                        {
                            refusedAtRoom = room;
                            return StepOutcome.Skip(said
                                ? $"It wasn't put away (the game said \"{lastToast}\"): make room in the storeroom and Try again, or put it in your bags by hand (the editor's inventory button)."
                                : StoreroomFullWords, ProblemKind.Other);
                        }
                        return StepOutcome.Skip("It wasn't put away (Store mode didn't take it from the plugin). Put it in the storeroom by hand.", ProblemKind.Other);
                    }
                    job.Note = "Put away in the storeroom.";
                    return StepOutcome.Done;
                },
            },
        };
    }

    private const string StoreroomFullWords = "The storeroom is full, so it wasn't put away: make room in the storeroom and Try again, or put it in your bags by hand (the editor's inventory button).";

    private void LogRows()
    {
        if (!rowsReady || rows.Known.Count == 0) return;
        log.Info($"Storeroom list rows known ({rows.Known.Count} of {rows.Count}): {string.Join("; ", rows.Describe(data.ItemName))}");
    }

    private bool FindNewPiece(RunJob job)
    {
        var key = IdentifyNew(job, null);
        if (key == null) return false;
        job.Key = key;
        return true;
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

    private Dictionary<int, LivePiece>? liveThisFrame;

    /// <summary>
    /// What's placed, by furniture slot: read once a frame (the game doesn't change it between two
    /// steps of the same frame, and several steps a frame ask). Callers don't change it.
    /// </summary>
    private Dictionary<int, LivePiece>? ReadLive()
    {
        if (liveThisFrame != null) return liveThisFrame;
        var list = side == Side.Interior ? furniture.ReadInterior() : furniture.ReadGarden(out _);
        if (list == null) return null;
        var byKey = new Dictionary<int, LivePiece>(list.Count);
        foreach (var p in list) byKey.TryAdd(p.Key, p);
        return liveThisFrame = byKey;
    }

    /// <summary>Back to Rotate if the run left the editor in Store mode.</summary>
    private void LeaveStoreMode()
    {
        if (!storeModeUsed) return;
        storeModeUsed = false;
        // (Stopped by a zone change or logout: the editor may be half torn down, so it's let be.)
        if (StillInStartHome() && HousingEditor.Mode == EditorMode.Store && GameUi.Fire(UiTable.LayoutRotate)) log.Info("Back to Rotate (out of Store mode).");
    }

    private void Finish()
    {
        State = RunState.Finished;
        LeaveStoreMode();
        anywhere.Disable();
        SaveRows(force: true);
        LogRows();
        if (!DryRun) foreach (var line in timings.Summary()) log.Info(line);
        var parts = new List<string> { $"{Done} of {Total} done" };
        if (Failed > 0) parts.Add($"{Failed} didn't work");
        if (Skipped > 0) parts.Add($"{Skipped} skipped");
        if (Rehearsal) RehearsalEnded();
        var summary = $"{(DryRun ? "Dry run finished" : Rehearsal ? "Rehearsal finished (nothing committed)" : "Finished")}: {string.Join(", ", parts)}.";
        StatusText = summary;
        log.Done(summary);
        if (config.ChatMessages) Services.Chat.Print(summary, "Housing Studio");
    }

    public string Name(RunJob job) => data.ItemName(job.Target.ItemId);

    /// <summary>A step's label without its piece's name and numbers, for grouping in the timing summary.</summary>
    private string KeyOf(string label)
    {
        if (Current is { } job) label = label.Replace(Name(job), "…", StringComparison.Ordinal);
        label = System.Text.RegularExpressions.Regex.Replace(label, @"\d+", "#");
        var colon = label.IndexOf(':');
        return (colon > 0 ? label[..colon] : label).Trim();
    }

    private string DryWords(RunJob job) => job.Kind switch
    {
        JobKind.Store => $"put {Name(job)} away in the storeroom",
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

    private void OnErrorToast(ref Dalamud.Game.Text.SeStringHandling.SeString message, ref bool isHandled)
    {
        if (!Active) return;
        lastToast = message.TextValue;
        log.Warn($"  The game says: \"{lastToast}\"");
    }

    public void Dispose()
    {
        Services.Toasts.ErrorToast -= OnErrorToast;
        Rehearsals.Unregister(rehearsing);
        anywhere.Disable();
        try
        {
            SaveRows(force: true);
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't keep the storeroom rows learnt");
        }
    }
}
