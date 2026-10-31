using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// Settings → Getting → Way to your door: you walk from where the home teleport lands you to your front
/// door and go in, and the way is recorded (a spot every <see cref="DoorWays.Spacing"/> yalms). Going in
/// (the zone changes) saves it; so does Stop at the door. The errands follow it in
/// <see cref="EnterSteps"/>: vnavmesh's map of the ward has no houses in it.
/// </summary>
public sealed partial class ErrandRunner
{
    private readonly List<Vector3> waySpots = new();
    private uint wayTerritory;
    private Vector3? wayDoor;

    /// <summary>Recording a way to a door now.</summary>
    public bool RecordingWay { get; private set; }

    /// <summary>What the last recording came to (shown under the row), or null.</summary>
    public string? WayNote { get; private set; }

    /// <summary>Starts recording; null when it started, else why not.</summary>
    public string? RecordWay()
    {
        if (Active) return "Not while the errands run.";
        if (ErrandGame.Me is not { } me || !ErrandGame.Settled) return "Not now (between zones?).";
        if (here.Kind == PlaceKind.Interior) return "Go outside first: stand where the home teleport lands you (outside your home).";
        waySpots.Clear();
        waySpots.Add(me);
        wayTerritory = ErrandGame.Territory;
        wayDoor = null;
        RecordingWay = true;
        WayNote = "Recording: walk to your front door and go in.";
        log.Info("Recording the way to your door: walk to it and go in (going in saves it).");
        return null;
    }

    /// <summary>Stops recording: saved when you're at a door, else let go.</summary>
    public void StopWay()
    {
        if (!RecordingWay) return;
        if (ErrandGame.Me is { } me) DoorWays.Add(waySpots, me);
        FinishWay(atDoor: DoorNear(waySpots.LastOrDefault()) is not null);
    }

    /// <summary>Forgets the way to the door you're near (or, with none near, every way in this ward's zone).</summary>
    public int ForgetWays()
    {
        var t = ErrandGame.Territory;
        var door = ErrandGame.Me is { } me ? ErrandGame.Nearest(ErrandTable.EntranceWords, me, 40, UsableKinds)?.Position : null;
        var n = door is { } d
            ? config.ErrandsDoorWays.RemoveAll(w => w.Territory == t && w.End is { } e && Vector2.Distance(new(e.X, e.Z), new(d.X, d.Z)) <= DoorWays.EndsAtDoor)
            : config.ErrandsDoorWays.RemoveAll(w => w.Territory == t);
        if (n > 0) config.Save();
        WayNote = n > 0 ? $"Forgot {(n == 1 ? "the way" : $"{n} ways")}." : "No way recorded here.";
        return n;
    }

    /// <summary>A way's recorded for the door you're near (outside), or for any door in this zone (inside: none, as it's unknown).</summary>
    public bool HasWayHere(out int count)
    {
        var t = ErrandGame.Territory;
        count = config.ErrandsDoorWays.Count(w => w.Territory == t);
        return count > 0;
    }

    private static Vector3? DoorNear(Vector3 at) =>
        ErrandGame.Nearest(ErrandTable.EntranceWords, at, DoorWays.EndsAtDoor, UsableKinds)?.Position;

    /// <summary>Every frame (errands or not): the spot you're at, kept when you've moved; going in saves.</summary>
    private void TickWay()
    {
        if (!RecordingWay) return;
        if (ErrandGame.Territory != wayTerritory || !Services.ClientState.IsLoggedIn)
        {
            // Through the door (or teleported away): saved if the way ended at a door.
            FinishWay(atDoor: wayDoor is { } d && Vector2.Distance(new(d.X, d.Z), new(waySpots[^1].X, waySpots[^1].Z)) <= DoorWays.EndsAtDoor);
            return;
        }
        if (ErrandGame.Me is not { } me || ErrandGame.Moving) return;
        if (!DoorWays.Add(waySpots, me))
        {
            RecordingWay = false;
            WayNote = "Let go: that was longer than a way to a door (about 300 yalms).";
            log.Warn($"  {WayNote}");
            return;
        }
        // The door near you as you go (it's gone once you're through it).
        if (DoorNear(me) is { } door) wayDoor = door;
    }

    private void FinishWay(bool atDoor)
    {
        RecordingWay = false;
        var length = DoorWays.Length(waySpots);
        if (!atDoor || waySpots.Count < 2 || length < DoorWays.Shortest)
        {
            WayNote = !atDoor ? "Let go: it didn't end at a front door (go in through it, or press Stop beside it)." : "Let go: too short to be worth keeping.";
            log.Warn($"  Way to your door: {WayNote}");
            return;
        }
        DoorWays.Keep(config.ErrandsDoorWays, DoorWay.From(wayTerritory, waySpots, DateTime.UtcNow));
        config.Save();
        WayNote = $"Saved: {length:0} yalms, {waySpots.Count} spots. The errands follow it to this door.";
        log.Done($"Way to your door saved ({length:0} yalms, {waySpots.Count} spots, zone {wayTerritory}).");
    }
}
