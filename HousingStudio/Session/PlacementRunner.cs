using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Component.GUI;
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

public enum JobKind
{
    /// <summary>Already placed: picked up and set down at its spot by the plugin.</summary>
    Move,

    /// <summary>In your bags or the storeroom: you put it down (anywhere), the plugin then sets it at its spot.</summary>
    Place,
}

public enum JobStatus
{
    Pending,
    Working,
    Done,
    Skipped,
    Failed,
}

public sealed class Job
{
    public required PlanEntry Entry { get; init; }
    public required JobKind Kind { get; init; }
    public JobStatus Status { get; set; }
    public string? Note { get; set; }

    /// <summary>The furniture slot of the piece being set: the placed one, or the new one once you've put it down.</summary>
    public int? Key { get; set; }

    public Target Target => Entry.Target;
}

/// <summary>
/// Works through a plan one piece at a time, at the pace set in Settings:
/// <list type="bullet">
/// <item>pieces already placed are picked up and set down at their spot with the game's own editor
/// functions (Rotate mode), and checked to have landed there;</item>
/// <item>pieces from your bags or the storeroom wait for you to put them down through the game's
/// own menus (anywhere will do, and in any order); the moment one appears it's set at its spot
/// the same way. While a storeroom piece's menu is open, the game's preview of it is moved to its
/// spot first, so "Place" puts it right there.</item>
/// </list>
/// Before every step it checks you're still in the same home, with permission, in the editor's
/// Rotate mode, and not busy; it stops when you leave, when anything doesn't match what it expects,
/// or after a few failures in a row. A dry run walks the same steps without touching anything.
/// </summary>
public sealed unsafe class PlacementRunner : IDisposable
{
    private enum Phase
    {
        Next,
        AfterSelect,
        Verify,
        WaitForPiece,
    }

    private const float LandedDistance = 0.05f;
    private const float LandedYaw = 0.03f;
    private static readonly TimeSpan SelectWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan NewPieceScan = TimeSpan.FromMilliseconds(400);

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly HousingEditor editor;
    private readonly FurnitureReader furniture;
    private readonly PlaceAnywhere anywhere;
    private readonly GameData data;
    private readonly ActivityLog log;
    private readonly Random random = new();

    private readonly List<Job> jobs = new();
    private int index;
    private Phase phase;
    private DateTime at;
    private DateTime phaseStarted;
    private int failuresInARow;
    private bool retried;
    private nint instance;
    private HashSet<int> knownKeys = new();
    private (int Container, int Slot)? aimedAt;
    private bool askedThisJob;
    private bool rotateHintShown;
    private string? pendingPause;

    // What the run started in: leaving it stops the run.
    private PlaceKind startKind;
    private uint startTerritory;
    private ulong startHouse;
    private Side side;

    public PlacementRunner(Configuration config, LayoutSession session, HousingEditor editor, FurnitureReader furniture, PlaceAnywhere anywhere, GameData data, ActivityLog log)
    {
        this.config = config;
        this.session = session;
        this.editor = editor;
        this.furniture = furniture;
        this.anywhere = anywhere;
        this.data = data;
        this.log = log;
    }

    public RunState State { get; private set; } = RunState.Idle;
    public bool DryRun { get; private set; }
    public string? StatusText { get; private set; }
    public string? StopReason { get; private set; }
    public IReadOnlyList<Job> Jobs => jobs;
    public Job? Current => index >= 0 && index < jobs.Count && State is RunState.Running or RunState.Paused ? jobs[index] : null;
    public bool Active => State is RunState.Running or RunState.Paused;

    public int Done => jobs.Count(j => j.Status == JobStatus.Done);
    public int Failed => jobs.Count(j => j.Status == JobStatus.Failed);
    public int Skipped => jobs.Count(j => j.Status == JobStatus.Skipped);
    public int Total => jobs.Count;

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
            ? "Open Housing → Indoor Furnishings, then switch to Rotate (the circular arrows)."
            : "Open Housing → Outdoor Furnishings on your plot, then switch to Rotate (the circular arrows).";
        if (ctx.Mode != EditorMode.Rotate) return "Switch the furnishing editor to Rotate (the circular arrows).";
        return null;
    }

    /// <summary>Starts working through the pieces shown by the filters.</summary>
    public void Start(bool dryRun, IEnumerable<PlanEntry> entries)
    {
        var problem = CanStart(dryRun);
        if (problem != null)
        {
            StopReason = problem;
            log.Warn(problem);
            return;
        }

        jobs.Clear();
        foreach (var e in Matcher.WorkOrder(entries))
        {
            var kind = e.State switch
            {
                PieceState.Move => JobKind.Move,
                PieceState.FromInventory or PieceState.FromStoreroom => JobKind.Place,
                _ => (JobKind?)null,
            };
            if (kind == null) continue;
            jobs.Add(new Job { Entry = e, Kind = kind.Value, Key = e.Placed?.Key });
        }
        if (jobs.Count == 0)
        {
            StopReason = "Nothing to do: every piece shown is already in place (or missing).";
            log.Info(StopReason);
            return;
        }

        var ctx = session.Context;
        startKind = ctx.Kind;
        startTerritory = ctx.TerritoryId;
        startHouse = ctx.HouseId;
        side = ctx.Side!.Value;
        DryRun = dryRun;
        pendingPause = null;
        index = 0;
        phase = Phase.Next;
        at = DateTime.UtcNow;
        failuresInARow = 0;
        StopReason = null;
        knownKeys = session.Live.Keys.ToHashSet();
        aimedAt = null;
        askedThisJob = false;
        rotateHintShown = false;

        if (!dryRun && config.BackupBeforePlacing) Backups.Save(session, data, log, "before placing");
        if (!dryRun && config.PlaceAnywhere && !anywhere.Enable()) log.Warn(anywhere.Error ?? "Couldn't switch on place anywhere; placing with the game's usual checks.");

        var moves = jobs.Count(j => j.Kind == JobKind.Move);
        var places = jobs.Count - moves;
        log.Info($"{(dryRun ? "Dry run" : "Placing")} started: {moves} to move, {places} to put down from your bags or storeroom.");
        State = RunState.Running;
        StatusText = "Starting…";
    }

    public void Pause(string why)
    {
        if (State != RunState.Running) return;
        // Never in the middle of a piece: it's picked up, so finish setting it down first.
        if (phase is Phase.AfterSelect or Phase.Verify)
        {
            pendingPause = why;
            StatusText = "Pausing after this piece…";
            return;
        }
        pendingPause = null;
        State = RunState.Paused;
        StatusText = why;
        log.Warn($"Paused: {why}");
        if (config.ChatMessages) Services.Chat.Print($"Paused: {why}", "Housing Studio");
    }

    public void Resume()
    {
        if (State != RunState.Paused) return;
        var ctx = session.Context;
        if (!DryRun && ctx.Mode != EditorMode.Rotate && Current?.Kind == JobKind.Move)
        {
            StatusText = "Switch to Rotate first.";
            return;
        }
        State = RunState.Running;
        phase = Phase.Next;
        at = DateTime.UtcNow;
        failuresInARow = 0;
        log.Info("Resumed.");
    }

    public void Stop(string reason)
    {
        if (!Active) return;
        State = RunState.Stopped;
        StopReason = reason;
        StatusText = reason;
        anywhere.Disable();
        log.Warn($"Stopped: {reason}");
        if (config.ChatMessages) Services.Chat.PrintError($"Stopped: {reason}", "Housing Studio");
    }

    /// <summary>Skips the piece being worked on (for one you'd rather place yourself).</summary>
    public void SkipCurrent()
    {
        if (Current is not { } job) return;
        if (phase is Phase.AfterSelect or Phase.Verify)
        {
            StatusText = "This piece is being set down; skip the next one instead.";
            return;
        }
        job.Status = JobStatus.Skipped;
        job.Note = "Skipped.";
        log.Info($"Skipped {Name(job)}.");
        Advance(quick: true);
    }

    /// <summary>Called every frame on the framework thread.</summary>
    public void Tick(HousingContext ctx)
    {
        if (State != RunState.Running && State != RunState.Paused) return;

        // Checks that stop everything, at every step.
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
        if (pendingPause != null && phase is Phase.Next or Phase.WaitForPiece)
        {
            Pause(pendingPause);
            return;
        }

        // Checks that pause (nothing is half-done when they fail between steps).
        if (!DryRun && phase is Phase.Next or Phase.WaitForPiece)
        {
            if (!ctx.EditorOpen)
            {
                Pause("The furnishing editor closed. Open it again and switch to Rotate, then press Resume.");
                return;
            }
        }

        if (DateTime.UtcNow < at) return;
        if (index >= jobs.Count)
        {
            Finish();
            return;
        }

        var job = jobs[index];
        try
        {
            switch (phase)
            {
                case Phase.Next:
                    StepNext(ctx, job);
                    break;
                case Phase.AfterSelect:
                    StepAfterSelect(job);
                    break;
                case Phase.Verify:
                    StepVerify(job);
                    break;
                case Phase.WaitForPiece:
                    StepWaitForPiece(ctx, job);
                    break;
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Placement step failed");
            Stop($"Something went wrong ({ex.Message}).");
        }
    }

    private void StepNext(HousingContext ctx, Job job)
    {
        job.Status = JobStatus.Working;
        if (DryRun)
        {
            var what = job.Kind == JobKind.Move
                ? $"move {Name(job)} to {Where(job)}"
                : $"ask you to put down {Name(job)} from {InventoryReader.Describe(job.Entry.Stock!)}, then set it at {Where(job)}";
            log.Info($"Dry run: would {what}.");
            StatusText = $"Dry run: {Name(job)}";
            job.Status = JobStatus.Done;
            job.Note = "Dry run: nothing touched.";
            Advance(quick: false, factor: 0.5);
            return;
        }

        if (job.Kind == JobKind.Place && job.Key == null)
        {
            phase = Phase.WaitForPiece;
            phaseStarted = DateTime.UtcNow;
            askedThisJob = false;
            aimedAt = null;
            StepWaitForPiece(ctx, job);
            return;
        }

        if (ctx.Mode != EditorMode.Rotate)
        {
            if (job.Kind == JobKind.Place)
            {
                // You may still be setting a piece down; wait for Rotate rather than pausing.
                if (!rotateHintShown)
                {
                    StatusText = $"Switch to Rotate so {Name(job)} can be set at its spot.";
                    rotateHintShown = true;
                }
                at = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
                return;
            }
            Pause("Switch the furnishing editor to Rotate (the circular arrows), then press Resume.");
            return;
        }
        rotateHintShown = false;

        // Find the piece again, now, and make sure it's still what it should be.
        var live = ReadLive();
        if (live == null || job.Key is not { } key || !live.TryGetValue(key, out var piece))
        {
            Fail(job, "it's no longer where the plan saw it (moved, stored, or the list changed).");
            return;
        }
        if (piece.ItemId != job.Target.ItemId)
        {
            Stop($"The game's furniture list changed under the plan (slot {key} is now {data.ItemName(piece.ItemId)}). Reload the plan and start again.");
            return;
        }
        if (piece.Instance == 0)
        {
            Fail(job, "the game hasn't loaded it (walk closer to it).");
            return;
        }
        if (Vector3.Distance(piece.Position, job.Target.Position) <= LandedDistance && Transforms.AngleDiff(piece.Yaw, job.Target.Yaw) <= LandedYaw)
        {
            job.Status = JobStatus.Done;
            job.Note = "Already in place.";
            Advance(quick: true);
            return;
        }

        instance = piece.Instance;
        StatusText = $"Setting {Name(job)} at its spot…";
        if (!editor.Select(instance))
        {
            Stop("Couldn't pick the piece up (the editor closed?).");
            return;
        }
        phase = Phase.AfterSelect;
        at = DateTime.UtcNow + SelectWait;
    }

    private void StepAfterSelect(Job job)
    {
        // Only change anything when the game really picked this exact piece.
        if (HousingEditor.ActiveItem != instance)
        {
            if (!retried)
            {
                retried = true;
                phase = Phase.Next;
                at = DateTime.UtcNow + SelectWait;
                return;
            }
            Fail(job, "the game didn't pick it up, so it wasn't moved.");
            return;
        }
        var sg = (SharedGroupLayoutInstance*)instance;
        var q = Transforms.YawQuaternion(job.Target.Yaw);
        sg->Transform.Translation = new Vector3(job.Target.Position.X, job.Target.Position.Y, job.Target.Position.Z);
        sg->Transform.Rotation = new Quaternion(q.X, q.Y, q.Z, q.W);
        if (!editor.Place(instance))
        {
            Stop("Couldn't set the piece down (the editor closed?).");
            return;
        }
        phase = Phase.Verify;
        phaseStarted = DateTime.UtcNow;
        at = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
    }

    private void StepVerify(Job job)
    {
        var stored = FurnitureReader.Stored(side == Side.Garden, job.Key!.Value);
        if (stored is { } s && Vector3.Distance(s.Position, job.Target.Position) <= LandedDistance && Transforms.AngleDiff(s.Yaw, job.Target.Yaw) <= LandedYaw)
        {
            job.Status = JobStatus.Done;
            job.Note = job.Kind == JobKind.Move ? "Moved to its spot." : "Put down and set at its spot.";
            log.Done($"{Name(job)}: {job.Note}");
            failuresInARow = 0;
            Advance(quick: false);
            return;
        }
        if (DateTime.UtcNow - phaseStarted < VerifyTimeout)
        {
            at = DateTime.UtcNow + TimeSpan.FromMilliseconds(150);
            return;
        }
        var landed = stored is { } l ? $" (it's at {l.Position.X:0.00}, {l.Position.Y:0.00}, {l.Position.Z:0.00})" : string.Empty;
        Fail(job, $"it didn't end up at its spot{landed}. The game may refuse floating or overlapping pieces; \"Place anywhere\" in Settings allows them.");
    }

    private void StepWaitForPiece(HousingContext ctx, Job job)
    {
        var stock = job.Entry.Stock!;
        if (!askedThisJob)
        {
            askedThisJob = true;
            var how = stock.Source == StockSource.Storeroom
                ? "pick it in Furnishings → Storeroom and choose Place"
                : $"take it from {InventoryReader.Describe(stock)} and put it down anywhere";
            StatusText = $"Your turn: {Name(job)} — {how}. It'll be set at its spot.";
            log.Info($"Waiting for you to put down {Name(job)} ({how}).");
            if (config.ChatMessages) Services.Chat.Print($"Next: {Name(job)} — {how}. Housing Studio sets it at its spot.", "Housing Studio");
        }

        if (config.AimStoreroomPreview && side == Side.Interior) AimStoreroomPreview(job);

        // A piece you've just put down: give it to the job it belongs to.
        if (DateTime.UtcNow - phaseStarted < NewPieceScan) return;
        phaseStarted = DateTime.UtcNow;
        var live = ReadLive();
        if (live == null) return;
        foreach (var (key, piece) in live)
        {
            if (knownKeys.Contains(key)) continue;
            knownKeys.Add(key);
            var owner = PickJobFor(piece);
            if (owner == null)
            {
                log.Info($"You put down {data.ItemName(piece.ItemId)}; this design doesn't need another one here, so it's left as it is.");
                continue;
            }
            owner.Key = key;
            if (owner != job)
            {
                // You put down a different piece first: do that one now, this one stays next.
                jobs.Remove(owner);
                jobs.Insert(index, owner);
            }
            log.Info($"You put down {Name(owner)}; setting it at its spot.");
            phase = Phase.Next;
            retried = false;
            at = DateTime.UtcNow + TimeSpan.FromMilliseconds(400);
            return;
        }
    }

    /// <summary>The waiting piece of the same item nearest to where you put this one down.</summary>
    private Job? PickJobFor(LivePiece piece)
    {
        Job? best = null;
        var bestDist = float.MaxValue;
        for (var i = index; i < jobs.Count; i++)
        {
            var j = jobs[i];
            if (j.Kind != JobKind.Place || j.Key != null || j.Status is JobStatus.Done or JobStatus.Skipped or JobStatus.Failed) continue;
            if (j.Target.ItemId != piece.ItemId) continue;
            var d = Vector3.Distance(j.Target.Position, piece.Position) - (i == index ? 0.5f : 0f);
            if (d < bestDist)
            {
                bestDist = d;
                best = j;
            }
        }
        return best;
    }

    /// <summary>
    /// While the storeroom's menu is open for a piece this design needs, move the game's preview of
    /// it to that piece's spot, so choosing "Place" puts it there (Refurnish places from the
    /// storeroom the same way). Done once per menu; nothing is sent to the game until you choose.
    /// </summary>
    private void AimStoreroomPreview(Job job)
    {
        var menu = Services.GameGui.GetAddonByName<AtkUnitBase>("ContextMenu");
        if (menu == null || !menu->IsVisible)
        {
            aimedAt = null;
            return;
        }
        var selection = InventoryReader.StoreroomSelection();
        if (selection == null || selection == aimedAt) return;
        var (container, slot) = selection.Value;
        if (!InventoryReader.InteriorStoreroom.Contains((FFXIVClientStructs.FFXIV.Client.Game.InventoryType)container)) return;
        var held = InventoryReader.StillHolds(new StockSlot(StockSource.Storeroom, container, slot, job.Target.ItemId, 0, 1))
            ? job
            : jobs.Skip(index).FirstOrDefault(j => j.Kind == JobKind.Place && j.Key == null && j.Status == JobStatus.Pending
                                                    && InventoryReader.StillHolds(new StockSlot(StockSource.Storeroom, container, slot, j.Target.ItemId, 0, 1)));
        if (held == null) return;
        var preview = FurnitureReader.Preview();
        if (preview == null) return;
        var p = held.Target.Position;
        preview->SetPosition(p.X, p.Y, p.Z);
        preview->SetRotation(held.Target.Yaw);
        aimedAt = selection;
        log.Info($"Aimed the storeroom preview of {Name(held)} at its spot; choose Place to put it there.");
    }

    private Dictionary<int, LivePiece>? ReadLive()
    {
        var list = side == Side.Interior ? furniture.ReadInterior() : furniture.ReadGarden(out _);
        return list?.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First());
    }

    private void Fail(Job job, string why)
    {
        job.Status = JobStatus.Failed;
        job.Note = why;
        log.Warn($"{Name(job)}: {why}");
        failuresInARow++;
        if (failuresInARow >= Math.Max(1, config.MaxFailuresInARow))
        {
            Stop($"{failuresInARow} pieces in a row couldn't be set, so something's wrong. Check the log, then start again.");
            return;
        }
        Advance(quick: false);
    }

    private void Advance(bool quick, double factor = 1)
    {
        index++;
        phase = Phase.Next;
        retried = false;
        var delay = quick ? 150 : (int)(Pacing.NextDelayMs(config.IntervalMs, config.Jitter, random.NextDouble()) * factor);
        at = DateTime.UtcNow + TimeSpan.FromMilliseconds(delay);
        if (index >= jobs.Count) at = DateTime.UtcNow;
    }

    private void Finish()
    {
        State = RunState.Finished;
        anywhere.Disable();
        var summary = $"{(DryRun ? "Dry run done" : "Done")}: {Done} of {Total} set{(Failed > 0 ? $", {Failed} couldn't be" : "")}{(Skipped > 0 ? $", {Skipped} skipped" : "")}.";
        StatusText = summary;
        log.Done(summary);
        if (config.ChatMessages) Services.Chat.Print(summary, "Housing Studio");
    }

    private string Name(Job job) => data.ItemName(job.Target.ItemId);

    private string Where(Job job) => $"({job.Target.Position.X:0.00}, {job.Target.Position.Y:0.00}, {job.Target.Position.Z:0.00}), facing {Transforms.TidyYaw(job.Target.Yaw) * 180 / MathF.PI:0}°";

    public void Dispose() => anywhere.Disable();
}
