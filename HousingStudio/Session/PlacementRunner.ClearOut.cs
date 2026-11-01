using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// Clearing out and swapping. Clear out (chosen in Ready for a run): the pieces placed here that
/// the design doesn't use are put away in the storeroom first, with the editor's Store mode as Put
/// them back does (<see cref="StoreSteps"/>), and then the design is matched again against what's
/// placed and placed. A swap is a run with clear out that keeps the backup it saves under a name
/// you'll know ("Medium house in Mist before Winter Lodge"); Swap back loads that backup as the
/// design and runs it with clear out.
/// </summary>
/// <summary>Where a run puts pieces away (0.9.1's Clear out this house asks; other runs use the storeroom, then your bags).</summary>
public enum PutAwayTo
{
    /// <summary>The storeroom, and your bags once it's full.</summary>
    StoreroomThenBags,

    /// <summary>The storeroom only: what it has no room for is left placed.</summary>
    Storeroom,

    /// <summary>Your bags (the editor's "to your bags").</summary>
    Bags,
}

public sealed unsafe partial class PlacementRunner
{
    /// <summary>Where the next run puts pieces away; taken by its start (and back to the storeroom, then bags, after).</summary>
    public PutAwayTo NextPutAwayTo { get; set; } = PutAwayTo.StoreroomThenBags;

    /// <summary>Where this run puts pieces away.</summary>
    private PutAwayTo putAwayTo = PutAwayTo.StoreroomThenBags;

    /// <summary>May this run put pieces in your bags (and do they have room)?</summary>
    private bool BagsTakeIt => putAwayTo != PutAwayTo.Storeroom && ErrandGame.FreeSlots() > 0;

    /// <summary>Another run that acts in the home (setting fixtures): a placing run doesn't start while it goes.</summary>
    public Func<bool>? OtherRunActive { get; set; }

    /// <summary>The pieces the next <see cref="Start(bool, IEnumerable{PlanEntry}, bool, IReadOnlyList{PlacedPiece})"/> clears out first, taken by it.</summary>
    private IReadOnlyList<PlacedPiece>? pendingClearOut;

    /// <summary>The name the next run keeps its backup under (a swap), or null.</summary>
    private string? keepAs;

    /// <summary>The design is still to be matched again and planned, once the clear out is done.</summary>
    private bool clearOutReplan;

    /// <summary>The design's pieces the run was given (by the design's key and item), planned again after the clear out.</summary>
    private HashSet<(int Key, uint ItemId)> clearOutWanted = new();

    /// <summary>When the run first found the clear out done (a moment is given for the game's lists to catch up).</summary>
    private DateTime? clearOutDoneAt;

    /// <summary>The pieces this run clears out (the first jobs), for the window.</summary>
    private int clearOutCount;

    /// <summary>
    /// Clear out's pieces the storeroom has no room for at the start: put away once the design is
    /// placed (placing it from the storeroom makes room), found again then by item and spot.
    /// </summary>
    private List<PlacedPiece> clearOutLater = new();

    /// <summary>The storeroom's room when it last refused a piece (it's tried again once that changes).</summary>
    private int? refusedAtRoom;

    /// <summary>A restore (Swap back) puts away what isn't in the backup first, rather than last.</summary>
    private bool restoreClearsFirst;

    /// <summary>The name the last run kept what was here under (a swap), or null.</summary>
    public string? KeptAs { get; private set; }

    /// <summary>The design a swap placed (its file): a later run of it (Try again, Carry on) is still that swap.</summary>
    private string? swapDesign;

    /// <summary>
    /// This run carries on the last swap: the same design, in the same home, and not a swap of its own.
    /// It keeps the swap's backup (what was here before it) for Swap back, rather than saving the
    /// half-swapped home over it.
    /// </summary>
    private bool CarriesOnSwap(bool dryRun)
    {
        if (dryRun || keepAs != null || KeptAs == null || swapDesign == null || backupOf == null) return false;
        if (!string.Equals(session.FilePath, swapDesign, StringComparison.OrdinalIgnoreCase)) return false;
        var c = session.Context;
        return (c.Kind, c.TerritoryId, c.HouseId, c.Plot) == backupOf.Value;
    }

    /// <summary>How many pieces this run put away in the storeroom (clearing out, or putting things back).</summary>
    public int PutAwayDone
    {
        get
        {
            // Asked every frame by the window: a plain count, no allocation.
            var n = 0;
            foreach (var j in jobs)
                if (j.Kind == JobKind.Store && j.Outcome == JobOutcome.Done) n++;
            return n;
        }
    }

    /// <summary>How many pieces this run clears out first (0 when it doesn't).</summary>
    public int ClearOutCount => clearOutCount;

    /// <summary>
    /// Starts a run that first puts away <paramref name="clearOut"/> (pieces placed here and not in
    /// the design), then places the design; with <paramref name="keepAsName"/>, a swap: its backup is
    /// kept under that name (and saved even with the backup switched off).
    /// </summary>
    public void Start(bool dryRun, IEnumerable<PlanEntry> shown, IReadOnlyList<PlacedPiece>? clearOut, string? keepAsName = null)
    {
        pendingClearOut = clearOut;
        keepAs = dryRun ? null : keepAsName;
        try
        {
            Start(dryRun, shown, forceDyes: false);
        }
        finally
        {
            pendingClearOut = null;
            keepAs = null;
        }
    }

    /// <summary>
    /// Swap back: loads the backup the last run (a swap) kept as the design, and once it's matched,
    /// runs it with the pieces placed since put away first. As Put them back, only in the home the
    /// backup is of.
    /// </summary>
    public bool SwapBack(out string why)
    {
        if (!RestoreBackup(out why)) return false;
        restoreClearsFirst = true;
        log.Info("Swapping back: what isn't in the kept home is put away first.");
        return true;
    }

    /// <summary>A restore's run, once its backup is matched (<see cref="Update"/>): put away last, or first for Swap back.</summary>
    private void StartRestore(IReadOnlyList<PlacedPiece> putAway)
    {
        var clearFirst = restoreClearsFirst;
        restoreClearsFirst = false;
        if (!clearFirst)
        {
            Start(false, session.Plan!.Entries, forceDyes: true, putAway: putAway);
            return;
        }
        pendingClearOut = putAway;
        try
        {
            Start(false, session.Plan!.Entries, forceDyes: true);
        }
        finally
        {
            pendingClearOut = null;
        }
    }

    /// <summary>
    /// At the start of a run: the clear out's pieces become its first jobs (the editor's Store mode,
    /// one by one). For a real run the rest waits: whether storing a piece renumbers the furniture
    /// slots the moves use isn't known (as for Put them back), so the design is matched again once
    /// they're gone (<see cref="AfterClearOut"/>). A preview lists them first and goes on.
    /// </summary>
    private void PlanClearOut(bool dryRun)
    {
        var pieces = pendingClearOut;
        pendingClearOut = null;
        clearOutReplan = false;
        clearOutDoneAt = null;
        clearOutWanted = new HashSet<(int, uint)>();
        clearOutCount = 0;
        clearOutLater = new List<PlacedPiece>();
        refusedAtRoom = null;
        // A preview leaves the swap as it was; a run that isn't the swap carried on forgets it.
        if (!dryRun && !CarriesOnSwap(dryRun))
        {
            KeptAs = null;
            swapDesign = null;
        }
        if (pieces is not { Count: > 0 }) return;

        // (The run's side is set after this: it's where you are now.)
        var garden = session.Context.Side == Side.Garden;
        // A storeroom without room for them all: what fits goes first, the rest once the design is
        // placed (each piece placed from the storeroom makes room for one).
        int? room = putAwayTo switch
        {
            PutAwayTo.Bags => ErrandGame.FreeSlots(),
            // (Your bags take what the storeroom can't.)
            PutAwayTo.StoreroomThenBags => InventoryReader.StoreroomRoom(garden ? Side.Garden : Side.Interior) is { } r ? r + ErrandGame.FreeSlots() : (int?)null,
            _ => InventoryReader.StoreroomRoom(garden ? Side.Garden : Side.Interior),
        };
        // What sits on other pieces or hangs on walls goes first, the pieces under them after: the game
        // won't put away a piece with something on it ("Another furnishing has been placed on that item";
        // a clear out to an empty backup put a round table away before what stood on it). OrderBy keeps
        // the order otherwise.
        pieces = pieces.OrderByDescending(p => data.GoesOnSomethingElse(p.ItemId)).ToList();
        var (first, later) = ClearOutSplit.Split(pieces, room);
        if (later.Count > 0)
            log.Warn($"{(putAwayTo == PutAwayTo.Bags ? "Your bags have" : putAwayTo == PutAwayTo.StoreroomThenBags ? "The storeroom and your bags have" : "The storeroom has")} room for {Words(first.Count)} of the {Words(pieces.Count)} to clear out: {(first.Count > 0 ? $"those go first, and " : string.Empty)}"
                     + $"the other {later.Count} once the design is placed (placing from the storeroom makes room). Any it still has no room for are left for you.");
        var stores = first.Select(p => StoreJob(p, garden)).ToList();
        clearOutCount = pieces.Count;
        if (dryRun)
        {
            // A preview or rehearsal: the rest are listed last, where a run would put them away.
            jobs.InsertRange(0, stores);
            jobs.AddRange(later.Select(p => StoreJob(p, garden)));
            return;
        }
        clearOutLater = later.ToList();
        clearOutWanted = entries.Select(e => (e.Target.Key, e.Target.ItemId)).ToHashSet();
        jobs.Clear();
        jobs.AddRange(stores);
        clearOutReplan = true;
        log.Info($"Clearing out first: {Words(stores.Count)} placed here and not in the design go to {PutAwayWords} (the editor's {(putAwayTo == PutAwayTo.Bags ? "to your bags" : "Store")} mode); "
                 + "then the design is matched again against what's placed, and placed.");
    }

    private static RunJob StoreJob(PlacedPiece p, bool garden) => new()
    {
        Kind = JobKind.Store,
        Target = new Target(-1 - p.Key, p.ItemId, p.Position, p.Yaw, p.Stain, garden ? Floor.Garden : Floor.Ground, false),
        Key = p.Key,
    };

    /// <summary>
    /// Before one of clear out's first pieces: if the storeroom has no room now (or refused a piece at
    /// this room), it and the rest of them wait for the end (<see cref="PlanClearOutLater"/>), rather
    /// than each being refused. True when they were put off.
    /// </summary>
    private bool PutOffClearOut()
    {
        if (!clearOutReplan || index >= jobs.Count || jobs[index].Kind != JobKind.Store) return false;
        // (Your bags take them, while they have room and may.)
        if (putAwayTo == PutAwayTo.Bags ? ErrandGame.FreeSlots() > 0 : !StoreroomRefuses() || BagsTakeIt) return false;
        var n = 0;
        while (index + n < jobs.Count && jobs[index + n].Kind == JobKind.Store && jobs[index + n].Outcome == JobOutcome.Pending) n++;
        if (n == 0) return false;
        foreach (var j in jobs.GetRange(index, n))
            clearOutLater.Add(new PlacedPiece(j.Key!.Value, j.Target.ItemId, j.Target.Position, j.Target.Yaw, j.Target.Stain));
        jobs.RemoveRange(index, n);
        jobsVersion++;
        log.Warn($"The storeroom is full: the other {Words(n)} to clear out wait until the design is placed (placing from the storeroom makes room).");
        return true;
    }

    /// <summary>The storeroom has no room (or refused a piece, and its room hasn't changed since).</summary>
    private bool StoreroomRefuses()
    {
        var room = InventoryReader.StoreroomRoom(side);
        return room is <= 0 || (refusedAtRoom != null && room == refusedAtRoom);
    }

    /// <summary>
    /// At the end of a run: clear out's pieces that waited for room become its last jobs. Each is found
    /// again by its item and spot (storing and placing may have given it another furniture slot); one
    /// that's gone or moved is left be.
    /// </summary>
    private void PlanClearOutLater()
    {
        var live = ReadLive();
        if (live == null || clearOutLater.Count == 0) return; // kept for when it can be read
        var waiting = clearOutLater;
        clearOutLater = new List<PlacedPiece>();
        var used = new HashSet<int>();
        var garden = side == Side.Garden;
        var added = 0;
        foreach (var p in waiting.OrderByDescending(p => data.GoesOnSomethingElse(p.ItemId)))
        {
            var key = live.TryGetValue(p.Key, out var same) && same.ItemId == p.ItemId && Vector3.Distance(same.Position, p.Position) <= 0.25f && !used.Contains(p.Key)
                ? p.Key
                : live.Values.Where(l => l.ItemId == p.ItemId && !used.Contains(l.Key) && Vector3.Distance(l.Position, p.Position) <= 0.25f)
                    .OrderBy(l => Vector3.Distance(l.Position, p.Position)).Select(l => (int?)l.Key).FirstOrDefault();
            if (key == null)
            {
                log.Info($"  {data.ItemName(p.ItemId)}: no longer where it was; not put away.");
                continue;
            }
            used.Add(key.Value);
            jobs.Add(StoreJob(p with { Key = key.Value }, garden));
            added++;
        }
        jobsVersion++;
        log.Info($"Clearing out the rest: {Words(added)} the storeroom had no room for at the start{(InventoryReader.StoreroomRoom(side) is { } r ? $" (room for {r} now)" : string.Empty)}.");
    }

    /// <summary>
    /// When a run's jobs are done: if a clear out was first, the design is matched again against what's
    /// placed now (the session read afresh; its pieces' slots and the storeroom have changed) and its
    /// pieces become the run's next jobs. True while it waits for that, or right after adding them
    /// (the run then carries on with them).
    /// </summary>
    private bool AfterClearOut()
    {
        if (!clearOutReplan) return false;
        var now = DateTime.UtcNow;
        if (clearOutDoneAt == null && storeroomFirst)
        {
            clearOutDoneAt = now;
            log.Info("The storeroom is in: matching the design against your storeroom, bags and what's placed.");
        }
        if (clearOutDoneAt == null)
        {
            clearOutDoneAt = now;
            log.Info($"Cleared out: {PutAwayDone} of {Words(clearOutCount)} put away{(clearOutLater.Count > 0 ? $" ({clearOutLater.Count} wait for room in the storeroom, until the design is placed)" : string.Empty)}. Matching the design again against what's placed now.");
        }
        // A moment for the furniture list and the storeroom to catch up with the last piece put away.
        if (now - clearOutDoneAt.Value < Speed.Wait(500))
        {
            WaitForPlan(now, 100);
            return true;
        }
        session.Refresh(HousingContext.Read(Plugin.Plots));
        liveThisFrame = null;
        if (session.Plan == null || session.PlanSide != side)
        {
            if (now - clearOutDoneAt.Value > TimeSpan.FromSeconds(10))
            {
                Stop($"After clearing out, what's placed couldn't be matched to the design ({session.Warnings.FirstOrDefault() ?? "no plan"}). Start it again.");
                return true;
            }
            WaitForPlan(now, 250);
            return true;
        }

        clearOutReplan = false;
        entries = session.Plan.Entries.Where(e => clearOutWanted.Contains((e.Target.Key, e.Target.ItemId))).ToList();
        var rest = RunSequence.Build(entries).ToList();
        jobs.AddRange(rest);
        jobsVersion++;
        knownKeys = session.Live.Keys.ToHashSet();
        if (!dyesPlanned) dyesExpected = DyeCounts.ToApply(entries);
        timeLeftUntil = DateTime.MinValue;
        if (PutAwayDone > 0)
        {
            // The storeroom has more pieces now: its rows are learnt again, and the list is made again
            // before it's read (it doesn't show pieces that came in while it was open).
            rowsReady = false;
            namesStale = true;
            listSettled = false;
            listWatch.Reset();
            listExpect = null;
            listMissesStored = true;
        }
        log.Info($"Now placing the design: {rest.Count(j => j.Phase == RunPhase.Move)} to move, {rest.Count(j => j.Phase == RunPhase.Place)} to place"
                 + $"{(dyesPlanned ? string.Empty : $", then {dyesExpected} to dye")}.");
        index--;
        nextJobAt = now;
        return true;
    }

    /// <summary>Comes back to <see cref="AfterClearOut"/> in a moment (the same index: nothing new to do yet).</summary>
    private void WaitForPlan(DateTime now, int ms)
    {
        index--;
        nextJobAt = now + TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>The backup before a run: kept under the swap's name, or one of the folder's usual (pruned) backups.</summary>
    private string? SaveBackup()
    {
        if (keepAs == null) return Backups.Save(session, data, log, "before placing");
        var path = Backups.SaveKept(session, data, log, keepAs);
        if (path == null) return null;
        KeptAs = keepAs;
        swapDesign = session.FilePath;
        // On the Recent list, so it's found again to swap back to another day; second, as the first
        // is the design that reopens when the game starts.
        config.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        config.RecentFiles.Insert(Math.Min(1, config.RecentFiles.Count), path);
        if (config.RecentFiles.Count > 8) config.RecentFiles = config.RecentFiles.Take(8).ToList();
        config.RecentOpened[path] = DateTime.Now;
        config.Save();
        return path;
    }

    private string PutAwayWords => putAwayTo switch
    {
        PutAwayTo.Bags => "your bags",
        PutAwayTo.Storeroom => "the storeroom",
        _ => "the storeroom (your bags once it's full)",
    };

    private static string Words(int n) => n == 1 ? "1 piece" : $"{n} pieces";
}
