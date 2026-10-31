using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>Check for changes: what changed since the snapshot kept after the last run in this home (spots in the ward for a garden).</summary>
public sealed record DriftCheck(string Home, Side Side, DateTime SnapshotAt, DriftReport Report, DateTime CheckedAt);

/// <summary>
/// Looks after a home between runs, on the framework thread, once a frame:
/// <list type="bullet">
/// <item>after every run (not a preview), a snapshot of what's placed, per home
/// (<c>snapshots/&lt;home&gt;.build</c>), for Check for changes; the old one is dropped when a run
/// starts, so one that ended away from home (a zone change) leaves none rather than a stale one;</item>
/// <item>the run's progress in <c>resume.json</c> as it goes (the design, the home, its pieces, how
/// far), kept when the game closes or disconnects under it, dropped when it finishes or you stop it;</item>
/// <item>"Put them back where they were": the snapshot loaded as the design, and a run started on it
/// once it's matched (pieces added since are left where they are);</item>
/// <item>how many pieces the home has placed, for Start, only while Start asks.</item>
/// </list>
/// Files are written beside their place and moved over it, so a write cut short leaves the old one.
/// </summary>
public sealed class Care
{
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CountEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PutBackWait = TimeSpan.FromSeconds(10);

    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;
    private readonly ActivityLog log;

    public Care(Configuration config, LayoutSession session, PlacementRunner runner, GameData data, ActivityLog log)
    {
        this.config = config;
        this.session = session;
        this.runner = runner;
        this.data = data;
        this.log = log;
        Progress = ReadProgress();
    }

    private static string Folder => Services.PluginInterface.ConfigDirectory.FullName;

    public static string SnapshotFolder => Path.Combine(Folder, "snapshots");

    private static string ResumePath => Path.Combine(Folder, "resume.json");

    /// <summary>The home you're in (<see cref="Homes.Key"/>), or null; as of the last frame.</summary>
    public string? CurrentHome { get; private set; }

    /// <summary>A run cut off before it finished (read at start, kept as runs go), or null.</summary>
    public RunProgress? Progress { get; private set; }

    /// <summary>The last Check for changes, or null.</summary>
    public DriftCheck? LastCheck { get; private set; }

    /// <summary>Pieces placed where you are and on which side, while Start asks (<see cref="WantCount"/>).</summary>
    public (string Home, Side Side, int Placed)? Count { get; private set; }

    // ---------------------------------------------------------------------------------------------
    // Every frame.

    private bool watching;
    private string? runHome;
    private Side runSide;
    private string? runDesign;
    private string runTitle = string.Empty;
    private List<(int Key, uint ItemId)> runPieces = new();
    private int runBase;
    private (int Done, int Total, RunPhase Phase) written;
    private DateTime writtenAt;
    private (RunProgress Progress, DateTime Until)? carryFrom;
    private string? notice;
    private DateTime wantCountUntil;
    private DateTime countAt;

    private DateTime failedAt;

    /// <summary>Every frame, after the runner's. Never throws into the frame (that would stop a run): what fails is logged.</summary>
    public void Tick(HousingContext ctx)
    {
        try
        {
            TickNow(ctx);
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - failedAt > TimeSpan.FromSeconds(30)) Services.Log.Error(ex, "Housing Studio couldn't look after the home this frame");
            failedAt = DateTime.UtcNow;
        }
    }

    private void TickNow(HousingContext ctx)
    {
        var world = Services.PlayerState.IsLoaded ? Services.PlayerState.CurrentWorld.RowId : 0;
        CurrentHome = Homes.Key(ctx.Side, world, ctx.TerritoryId, ctx.HouseId, ctx.Plot);

        if (!watching && runner.Active && !runner.DryRun) RunStarted(ctx);
        else if (watching && runner.Active) KeepProgress(force: false);
        else if (watching) RunEnded(ctx);

        if (putBack != null) StartPutBack();

        if (DateTime.UtcNow < wantCountUntil && DateTime.UtcNow >= countAt && !runner.Active)
        {
            countAt = DateTime.UtcNow + CountEvery;
            Count = CurrentHome != null && ctx.HasPermission && !ctx.Busy && session.ReadPlacedNow(out var pieces, out _, out _)
                ? (CurrentHome, ctx.Side!.Value, pieces.Count)
                : null;
        }
    }

    /// <summary>Start shows how full the home is: counted every two seconds while it asks (every frame it draws).</summary>
    public void WantCount() => wantCountUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1);

    /// <summary>A message for you (a put-back that couldn't start), taken once.</summary>
    public string? TakeNotice()
    {
        var n = notice;
        notice = null;
        return n;
    }

    // ---------------------------------------------------------------------------------------------
    // The run's progress, and the snapshot after it.

    private void RunStarted(HousingContext ctx)
    {
        watching = true;
        runHome = CurrentHome;
        runSide = ctx.Side ?? Side.Interior;
        runDesign = session.FilePath;
        runTitle = session.Title;
        runPieces = runner.Entries.Select(e => (e.Target.Key, e.Target.ItemId)).Distinct().ToList();
        runBase = 0;
        // Carrying on a run cut off: its numbers go on from where it was ("piece 38 of 120"), and its pieces stay its own.
        if (carryFrom is { } carry && DateTime.UtcNow < carry.Until && carry.Progress is var from && from.Fits(runDesign, runHome, runSide))
        {
            runBase = Math.Max(0, from.Total - (runner.Total + runner.DyesStillExpected));
            runPieces = from.Pieces.ToList();
        }
        carryFrom = null;
        written = (-1, -1, RunPhase.Move);
        writtenAt = DateTime.MinValue;

        // The last snapshot here no longer says what this run left: it's made again when the run ends.
        if (runHome != null) Delete(SnapshotPath(runHome), "the old snapshot");
        if (LastCheck?.Home == runHome) LastCheck = null;
        KeepProgress(force: true);
    }

    private void KeepProgress(bool force)
    {
        if (runHome == null || runDesign == null || runPieces.Count == 0) return;
        var ended = runner.Done + runner.Failed + runner.Skipped;
        var total = runBase + runner.Total + runner.DyesStillExpected;
        var now = (runBase + ended, Math.Max(total, runBase + ended), runner.Phase ?? RunPhase.Move);
        if (now == written) return;
        if (!force && DateTime.UtcNow - writtenAt < ProgressEvery) return;
        var p = new RunProgress(runDesign, runTitle, runHome, runSide, runPieces, now.Item1, Math.Max(1, now.Item2), now.Item3, DateTime.UtcNow);
        if (Write(ResumePath, p.ToJson(), "the run's progress")) written = now;
        writtenAt = DateTime.UtcNow;
        Progress = p;
    }

    private void RunEnded(HousingContext ctx)
    {
        watching = false;
        var finished = runner.State == RunState.Finished;
        var byYou = runner.StopReason?.StartsWith("Stopped by you", StringComparison.Ordinal) == true;
        if (finished || byYou) Forget();
        else
        {
            KeepProgress(force: true);
            log.Info($"The run's progress is kept: load “{runTitle}” here again to carry on.");
        }

        // A snapshot of what the run left, while you're still in its home.
        if (runHome == null || CurrentHome != runHome || ctx.Busy || !ctx.HasPermission) return;
        var path = SnapshotPath(runHome);
        try
        {
            Directory.CreateDirectory(SnapshotFolder);
            if (Backups.SaveTo(path, session, data, $"As it was after the run, {DateTime.Now:d MMM HH:mm}", out var why))
            {
                lock (snapshots) snapshots.Remove(runHome);
            }
            else log.Warn($"Couldn't keep a snapshot of what's placed (for Check for changes): {why}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Couldn't keep a snapshot of what's placed: {ex.Message}");
        }
    }

    /// <summary>Forgets the run cut off (its file too).</summary>
    public void Forget()
    {
        Progress = null;
        carryFrom = null;
        Delete(ResumePath, "the run's progress");
    }

    /// <summary>The run about to start carries on this one (its numbers and its pieces go on).</summary>
    public void CarryingOn(RunProgress p) => carryFrom = (p, DateTime.UtcNow + TimeSpan.FromSeconds(5));

    /// <summary>The run cut off in this design and this home, to offer to carry on; null when there's none (or a run goes).</summary>
    public RunProgress? Offer(string? designPath, Side? side) =>
        !runner.Active && !watching && Progress is { } p && p.Fits(designPath, CurrentHome, side) ? p : null;

    private RunProgress? ReadProgress()
    {
        try
        {
            if (!File.Exists(ResumePath)) return null;
            var info = new FileInfo(ResumePath);
            var p = info.Length <= 2_000_000 ? RunProgress.Parse(File.ReadAllText(ResumePath)) : null;
            if (p == null)
            {
                Services.Log.Warning("The run's progress file couldn't be read; it's dropped.");
                Delete(ResumePath, "the run's progress");
            }
            return p;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Log.Warning(ex, "Couldn't read the run's progress");
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Snapshots and Check for changes.

    /// <summary>Where this home's snapshot is kept.</summary>
    public static string SnapshotPath(string home) => Path.Combine(SnapshotFolder, home + ".build");

    private readonly Dictionary<string, (DateTime? At, DateTime Until)> snapshots = new();

    /// <summary>When this home's snapshot was kept, or null when there's none (looked at on the disk every few seconds).</summary>
    public DateTime? SnapshotAt(string? home)
    {
        if (home == null) return null;
        lock (snapshots)
        {
            if (snapshots.TryGetValue(home, out var s) && DateTime.UtcNow < s.Until) return s.At;
            DateTime? at = null;
            try
            {
                var path = SnapshotPath(home);
                if (File.Exists(path)) at = File.GetLastWriteTime(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
            snapshots[home] = (at, DateTime.UtcNow + TimeSpan.FromSeconds(5));
            return at;
        }
    }

    /// <summary>Compares this home's snapshot with what's placed now (<see cref="LastCheck"/>). Framework thread; null, or why it couldn't.</summary>
    public string? CheckForChanges()
    {
        var ctx = session.Context;
        var home = CurrentHome;
        if (home == null || ctx.Side == null) return "Go to your home first.";
        if (!ctx.HasPermission) return "You can check a home you're allowed to decorate.";
        var path = SnapshotPath(home);
        Layout layout;
        try
        {
            if (!File.Exists(path)) return "There's no snapshot of this home yet: one is kept after each run.";
            if (new FileInfo(path).Length > LayoutReader.MaxBytes) return "The snapshot is too big to read.";
            layout = LayoutReader.Parse(File.ReadAllText(path));
        }
        catch (LayoutFormatException ex)
        {
            return $"The snapshot couldn't be read: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The snapshot couldn't be read: {ex.Message}";
        }
        if (!session.ReadPlacedNow(out var pieces, out var plot, out var why)) return why;
        var side = ctx.Side.Value;
        var was = new List<Target>();
        if (side == Side.Interior)
            foreach (var p in layout.Interior)
                was.Add(new Target(p.Index, p.ItemId, p.Position, p.Yaw, data.StainOf(p.DyeHex), Floors.Of(p.Position.Y, ctx.HouseSize), false));
        else
        {
            if ((plot ?? session.Plot) is not { } spot) return "Couldn't tell which plot you're on.";
            foreach (var p in layout.Garden)
            {
                var (at, yaw) = Transforms.PlotToWard(p.Position, p.Yaw, spot);
                was.Add(new Target(p.Index, p.ItemId, at, yaw, data.StainOf(p.DyeHex), Floor.Garden, false));
            }
        }
        var now = pieces.Select(p => new PlacedPiece(p.Key, p.ItemId, p.Position, p.Yaw, p.Stain)).ToList();
        var report = Drift.Compare(was, now);
        LastCheck = new DriftCheck(home, side, SnapshotAt(home) ?? DateTime.Now, report, DateTime.Now);
        log.Info($"Checked for changes since the run: {UI.Model.CareWords.Drift(report)} ({report.Same} as they were).");
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Put them back where they were.

    private (string Path, string Home, Side Side, DateTime Until)? putBack;

    /// <summary>
    /// Loads this home's snapshot (a copy of it, as the run makes the snapshot again) as the design;
    /// the run starts once it's matched. Framework thread; null, or why it can't.
    /// </summary>
    public string? PutBack()
    {
        if (runner.Active) return "Already running.";
        var home = CurrentHome;
        var side = session.Context.Side;
        if (home == null || side == null) return "Go to your home first.";
        var path = SnapshotPath(home);
        var copy = Path.Combine(SnapshotFolder, home + ".putback.build");
        try
        {
            if (!File.Exists(path)) return "There's no snapshot of this home.";
            File.Copy(path, copy, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't open the snapshot: {ex.Message}";
        }
        if (!session.Load(copy)) return session.LoadError ?? "Couldn't open the snapshot.";
        putBack = (copy, home, side.Value, DateTime.UtcNow + PutBackWait);
        session.Refresh(HousingContext.Read(Plugin.Plots));
        log.Info("Putting back what changed since the run: it starts once the snapshot is matched against the home.");
        return null;
    }

    private void StartPutBack()
    {
        if (putBack is not { } p) return;
        if (session.FilePath != p.Path)
        {
            putBack = null;
            return;
        }
        if (CurrentHome != p.Home)
        {
            putBack = null;
            notice = "You left the home: nothing was put back.";
            return;
        }
        if (runner.Active) return;
        if (session.Plan == null || session.PlanSide != p.Side)
        {
            if (DateTime.UtcNow < p.Until) return;
            putBack = null;
            notice = session.Warnings.FirstOrDefault() ?? "The snapshot couldn't be matched against the home.";
            return;
        }
        putBack = null;
        // Every piece of the snapshot: what's where it was makes no step, the moved are moved back, the
        // ones taken away placed again. Pieces placed since aren't in it, and stay where they are.
        runner.Start(false, session.Plan.Entries);
        if (!runner.Active) notice = runner.StopReason ?? "Couldn't start putting them back.";
    }

    // ---------------------------------------------------------------------------------------------

    private bool Write(string path, string text, string what)
    {
        var temp = path + ".saving";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Couldn't keep {what}: {ex.Message}");
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
            return false;
        }
    }

    private void Delete(string path, string what)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Services.Log.Warning(ex, $"Couldn't remove {what}");
        }
        if (path.EndsWith(".build", StringComparison.Ordinal))
            lock (snapshots) snapshots.Clear();
    }
}
