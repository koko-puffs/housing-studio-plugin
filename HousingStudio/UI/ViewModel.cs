using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI.Model;

namespace HousingStudio.UI;

/// <summary>Which of the four main views the session's state calls for.</summary>
public enum MainView
{
    Start,
    Ready,
    Running,
    Done,
}

/// <summary>A row of the Recent list.</summary>
/// <param name="Kept">What a swap kept of a home ("Medium house in Mist before Winter Lodge"): KEPT, "Kept from a swap".</param>
public sealed record RecentRow(string Path, string Name, string Note, string Kind, bool Exists, bool Backup, DateTime At, bool Kept = false);

/// <summary>
/// What the views read, computed from the session, the runner and the plan. Most of it is the
/// runner's own state (phases, time left, problems, the backup); this adds what the window keeps
/// for itself: the run's piece count and figure, whether its result has been seen, the dyes
/// missing (cached per plan, since it reads your bags), the Recent list, and the step in words.
/// </summary>
public sealed class ViewModel
{
    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;

    public ViewModel(Configuration config, LayoutSession session, PlacementRunner runner, GameData data)
    {
        this.config = config;
        this.session = session;
        this.runner = runner;
        this.data = data;
    }

    // ---------------------------------------------------------------------------------------------
    // The run, as the window saw it start.

    private bool wasActive;
    private List<PlanEntry>? starting;

    /// <summary>A run (or preview) has ended and its Done view hasn't been closed yet.</summary>
    public bool ResultWaiting { get; private set; }

    /// <summary>The pieces the run was started on (the design's pieces shown, for this side).</summary>
    public int RunPieces { get; private set; }

    /// <summary>Of those, already where the design has them when it started.</summary>
    public int RunInPlaceAtStart { get; private set; }

    /// <summary>The pieces of the run you don't have (not in your storeroom or bags), so it never worked on them.</summary>
    public IReadOnlyList<PlanEntry> RunMissing { get; private set; } = Array.Empty<PlanEntry>();

    /// <summary>The design's name when the run started (a restore loads the backup as the design).</summary>
    public string RunTitle { get; private set; } = string.Empty;

    /// <summary>Pieces in the house before the run, as its backup saved them.</summary>
    public int LiveAtStart { get; private set; }

    public DateTime RunStartedAt { get; private set; }

    /// <summary>Call right before <see cref="PlacementRunner.Start"/> with what it's given, so the figure counts those pieces.</summary>
    public void Starting(IEnumerable<PlanEntry> entries) => starting = entries.ToList();

    /// <summary>The runner didn't start (nothing to do, or you got busy): forget what it was given, or the next run that starts itself (a restore) would count those.</summary>
    public void NotStarted()
    {
        if (!runner.Active) starting = null;
    }

    /// <summary>Every frame: notices a run starting and ending.</summary>
    public void Observe()
    {
        var active = runner.Active;
        if (active && !wasActive)
        {
            // TODO(runner): a `Pieces` (entries the run was given) would save keeping them here; a
            // restore and a retry start themselves, so for those the plan as it stands is used.
            var entries = starting ?? session.Plan?.Entries.ToList() ?? new List<PlanEntry>();
            RunPieces = entries.Count;
            RunInPlaceAtStart = entries.Count(e => e.State == PieceState.InPlace);
            RunMissing = entries.Where(e => e.State == PieceState.Missing).ToList();
            RunTitle = session.Title;
            LiveAtStart = session.Live.Count;
            RunStartedAt = DateTime.Now;
            ResultWaiting = false;
        }
        if (!active && wasActive && runner.State is RunState.Finished or RunState.Stopped) ResultWaiting = true;
        if (active) starting = null;
        wasActive = active;
    }

    /// <summary>Done: back to Ready.</summary>
    public void Dismiss() => ResultWaiting = false;

    public MainView View =>
        runner.Active ? MainView.Running
        : ResultWaiting && runner.State is RunState.Finished or RunState.Stopped ? MainView.Done
        : session.Layout == null ? MainView.Start
        : MainView.Ready;

    /// <summary>
    /// The big figure: pieces where the design has them (those already there, plus each moved or
    /// placed; pieces put away are counted with the moves by the runner, but aren't the design's).
    /// </summary>
    public int Figure => Math.Min(RunPieces, RunInPlaceAtStart + runner.Count(RunPhase.Move, JobOutcome.Done) - runner.PutAwayDone + runner.Count(RunPhase.Place, JobOutcome.Done));

    /// <summary>The step in hand in plain words ("Setting it at its spot").</summary>
    // TODO(runner): a `StepText` in plain words; the labels are written for the log.
    public string? StepText => runner.DryRun && runner.Current != null ? "Marking its spot" : StepWords.Friendly(runner.StepLabel, runner.Current is { } j ? data.ItemName(j.Target.ItemId) : null);

    /// <summary>The run stopped because you said so (not for safety).</summary>
    public bool StoppedByYou => runner.StopReason?.StartsWith("Stopped by you", StringComparison.Ordinal) == true;

    private (string? Path, DateTime? At) backupAt;

    /// <summary>When the last backup was saved (read from the disk once per backup, not every frame).</summary>
    public DateTime? BackupAt
    {
        get
        {
            var p = runner.LastBackupPath;
            if (p != backupAt.Path) backupAt = (p, p != null && File.Exists(p) ? File.GetLastWriteTime(p) : null);
            return backupAt.At;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Dyes missing, read from your bags once per plan.

    private Plan? dyesFor;
    private DateTime dyesAgain;
    private List<(byte Stain, string Name, int Count)> missingDyes = new();

    /// <summary>The dyes this design needs beyond what's in your bags (<see cref="LayoutSession.MissingDyes"/>), for the plan as it stands.</summary>
    public IReadOnlyList<(byte Stain, string Name, int Count)> MissingDyes
    {
        get
        {
            // Again for a new plan, and every 2 s (dyes in your bags don't change the plan: bought by hand,
            // they stayed "to buy", and the errands bought them again). The same list while it says the same.
            var now = DateTime.UtcNow;
            if (!ReferenceEquals(dyesFor, session.Plan) || now >= dyesAgain)
            {
                dyesFor = session.Plan;
                dyesAgain = now + TimeSpan.FromSeconds(2);
                var fresh = session.MissingDyes();
                if (!fresh.SequenceEqual(missingDyes)) missingDyes = fresh;
            }
            return missingDyes;
        }
    }

    public HashSet<byte> MissingStains => MissingDyes.Select(d => d.Stain).ToHashSet();

    // ---------------------------------------------------------------------------------------------
    // Recent: the files you opened, and the backups the runs saved.

    private readonly ConcurrentDictionary<string, (DateTime Stamp, Layout? Layout)> parsed = new();
    private readonly HashSet<string> parsing = new();

    private List<RecentRow>? recentRows;
    private DateTime recentUntil;
    private string recentKey = string.Empty;

    /// <summary>
    /// The Recent list (0.9.0: designs only, pinned ones left out, newest first), read from the disk at
    /// most every two seconds (or when the files or pins change): the window draws every frame, and
    /// listing a folder each time would be wasted work on the game's thread.
    /// </summary>
    public List<RecentRow> Recent(int max = 8)
    {
        Refresh();
        return recentRows!.Where(r => !r.Backup && !IsPinned(r.Path)).Take(max).ToList();
    }

    /// <summary>How many designs Recent has in all (for "Show all N").</summary>
    public int RecentCount
    {
        get
        {
            Refresh();
            return recentRows!.Count(r => !r.Backup && !IsPinned(r.Path));
        }
    }

    /// <summary>The backups the runs saved, and homes kept by a swap, newest first (This house's Backups).</summary>
    public List<RecentRow> BackupRows(int max = 12)
    {
        Refresh();
        return backupRows!.Take(max).ToList();
    }

    /// <summary>The pinned designs, in the order pinned (Home's tiles).</summary>
    public List<RecentRow> Pinned()
    {
        Refresh();
        return pinnedRows!;
    }

    public bool IsPinned(string? path) =>
        path != null && config.PinnedDesigns.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    private List<RecentRow>? backupRows;
    private List<RecentRow>? pinnedRows;

    private void Refresh()
    {
        var key = string.Join("|", config.RecentFiles) + "#" + string.Join("|", config.PinnedDesigns);
        bool pending;
        lock (parsing) pending = parsing.Count > 0;
        // While files are being read in the background, the rows are made again a few times a second (to
        // show them as they come), not every frame.
        if (recentRows != null && DateTime.UtcNow < recentUntil && key == recentKey) return;
        ReadRecent();
        recentKey = key;
        recentUntil = DateTime.UtcNow + TimeSpan.FromSeconds(pending ? 0.25 : 2);
    }

    private void ReadRecent()
    {
        var rows = new List<(string Path, bool Backup, DateTime At)>();
        foreach (var path in config.RecentFiles)
        {
            // A hand-edited config can hold an empty entry: File.GetLastWriteTime throws on one, every frame.
            if (string.IsNullOrWhiteSpace(path)) continue;
            rows.Add((path, IsBackup(path), OpenedAt(path)));
        }
        try
        {
            if (Directory.Exists(Backups.Folder))
                foreach (var f in new DirectoryInfo(Backups.Folder).GetFiles("*.build").OrderByDescending(f => f.LastWriteTime).Take(12))
                    if (!rows.Any(r => string.Equals(r.Path, f.FullName, StringComparison.OrdinalIgnoreCase)))
                        rows.Add((f.FullName, true, f.LastWriteTime));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var now = DateTime.Now;
        var all = rows.OrderByDescending(r => r.At).Select(r => Row(r.Path, r.Backup, r.At, now)).ToList();
        recentRows = all;
        backupRows = all.Where(r => r.Backup).ToList();
        pinnedRows = config.PinnedDesigns.Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Row(p, IsBackup(p), OpenedAt(p), now)).ToList();
    }

    private DateTime OpenedAt(string path)
    {
        if (config.RecentOpened.TryGetValue(path, out var t)) return t;
        try
        {
            return File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return DateTime.MinValue;
        }
    }

    private RecentRow Row(string path, bool backup, DateTime at, DateTime now)
    {
        var exists = File.Exists(path);
        var kept = backup && SwapNames.IsKept(Path.GetFileName(path));
        var layout = exists ? Parsed(path) : null;
        var name = DesignNames.Display(layout?.BuildName ?? Path.GetFileNameWithoutExtension(path));
        var when = at == DateTime.MinValue ? null : When.Relative(at, now, t => t.ToString("HH:mm"));
        string note;
        if (!exists) note = "Not found";
        else if (layout == null) note = kept ? string.Join(" · ", new[] { "Kept from a swap", when }.Where(s => !string.IsNullOrEmpty(s))) : when ?? string.Empty;
        else
        {
            var count = Words.Count(layout.Interior.Count + layout.Garden.Count, "piece");
            // A swap's kept home leads with that ("Kept from a swap · 391 pieces · yesterday"), so it's found to swap back to.
            var parts = kept ? new[] { "Kept from a swap", count, when }
                : new[] { count, backup ? "what was here" : layout.Interior.Count == 0 && layout.Garden.Count > 0 ? "Garden" : Words.Home(layout.HouseSize), when };
            note = string.Join(" · ", parts.Where(s => !string.IsNullOrEmpty(s)));
        }
        var kind = kept ? "KEPT" : backup ? "BACKUP" : path.EndsWith(".build", StringComparison.OrdinalIgnoreCase) ? "BUILD" : "MAKEPLACE";
        return new RecentRow(path, name, note, kind, exists, backup, at, kept);
    }

    public static bool IsBackup(string path)
    {
        try
        {
            var dir = Path.GetFullPath(Backups.Folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), dir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>A file's layout for its row (its name, pieces and size), read once in the background.</summary>
    private Layout? Parsed(string path)
    {
        DateTime stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (parsed.TryGetValue(path, out var p) && p.Stamp == stamp) return p.Layout;
        lock (parsing)
        {
            if (!parsing.Add(path)) return p.Layout;
        }
        Task.Run(() =>
        {
            Layout? layout = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= LayoutReader.MaxBytes) layout = LayoutReader.Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                // Any file that can't be read is a row without its details (never a parse left pending,
                // which would read the disk again every 0.25 s for the rest of the session).
            }
            finally
            {
                parsed[path] = (stamp, layout);
                lock (parsing) parsing.Remove(path);
            }
        });
        return p.Layout;
    }
}
