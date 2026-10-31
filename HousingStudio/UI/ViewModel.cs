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
public sealed record RecentRow(string Path, string Name, string Note, string Kind, bool Exists, bool Backup, DateTime At);

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
    private List<(byte Stain, string Name, int Count)> missingDyes = new();

    /// <summary>The dyes this design needs beyond what's in your bags (<see cref="LayoutSession.MissingDyes"/>), for the plan as it stands.</summary>
    public IReadOnlyList<(byte Stain, string Name, int Count)> MissingDyes
    {
        get
        {
            if (!ReferenceEquals(dyesFor, session.Plan))
            {
                dyesFor = session.Plan;
                missingDyes = session.MissingDyes();
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
    /// The Recent list, read from the disk at most every two seconds (or when the list of files
    /// changes): the window draws every frame, and listing a folder each time would be wasted work on
    /// the game's thread.
    /// </summary>
    public List<RecentRow> Recent(int max = 6)
    {
        var key = string.Join("|", config.RecentFiles);
        bool pending;
        lock (parsing) pending = parsing.Count > 0;
        // While files are being read in the background, the rows are made again a few times a second (to
        // show them as they come), not every frame.
        if (recentRows != null && DateTime.UtcNow < recentUntil && key == recentKey) return recentRows;
        recentRows = ReadRecent(max);
        recentKey = key;
        recentUntil = DateTime.UtcNow + TimeSpan.FromSeconds(pending ? 0.25 : 2);
        return recentRows;
    }

    private List<RecentRow> ReadRecent(int max)
    {
        var rows = new List<(string Path, bool Backup, DateTime At)>();
        foreach (var path in config.RecentFiles)
        {
            // A hand-edited config can hold an empty entry: File.GetLastWriteTime throws on one, every frame.
            if (string.IsNullOrWhiteSpace(path)) continue;
            var exists = File.Exists(path);
            var at = config.RecentOpened.TryGetValue(path, out var t) ? t : exists ? File.GetLastWriteTime(path) : DateTime.MinValue;
            rows.Add((path, IsBackup(path), at));
        }
        try
        {
            if (Directory.Exists(Backups.Folder))
                foreach (var f in new DirectoryInfo(Backups.Folder).GetFiles("*.build").OrderByDescending(f => f.LastWriteTime).Take(3))
                    if (!rows.Any(r => string.Equals(r.Path, f.FullName, StringComparison.OrdinalIgnoreCase)))
                        rows.Add((f.FullName, true, f.LastWriteTime));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        var now = DateTime.Now;
        return rows.OrderByDescending(r => r.At).Take(max).Select(r =>
        {
            var exists = File.Exists(r.Path);
            var layout = exists ? Parsed(r.Path) : null;
            var name = layout?.BuildName ?? Path.GetFileNameWithoutExtension(r.Path);
            var when = r.At == DateTime.MinValue ? null : When.Relative(r.At, now, t => t.ToString("HH:mm"));
            string note;
            if (!exists) note = "Not found";
            else if (layout == null) note = when ?? string.Empty;
            else
            {
                var count = Words.Count(layout.Interior.Count + layout.Garden.Count, "piece");
                var what = r.Backup ? "what was here" : layout.Interior.Count == 0 && layout.Garden.Count > 0 ? "Garden" : Words.Home(layout.HouseSize);
                note = string.Join(" · ", new[] { count, what, when }.Where(s => !string.IsNullOrEmpty(s)));
            }
            var kind = r.Backup ? "BACKUP" : r.Path.EndsWith(".build", StringComparison.OrdinalIgnoreCase) ? "BUILD" : "MAKEPLACE";
            return new RecentRow(r.Path, name, note, kind, exists, r.Backup, r.At);
        }).ToList();
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
