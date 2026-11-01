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

    /// <summary>What the last recording came to (shown under the row a while), or null.</summary>
    public string? WayNote
    {
        get => RecordingWay || DateTime.UtcNow - WayNoteAt < TimeSpan.FromMinutes(2) ? wayNote : null;
        private set
        {
            wayNote = value;
            WayNoteAt = DateTime.UtcNow;
        }
    }

    private string? wayNote;
    private DateTime WayNoteAt { get; set; }

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
        wayInsideBy = null;
        RecordingWay = true;
        WayNote = "Recording: walk to your front door and go in.";
        log.Info("Recording the way to your door: walk to it and go in (going in saves it).");
        return null;
    }

    /// <summary>Stops recording: saved when you're at a door, else let go.</summary>
    public void StopWay()
    {
        if (!RecordingWay) return;
        if (wayInsideBy != null)
        {
            FinishWay(wayDoor != null, ErrandGame.HomeHere(here)?.Plot ?? 0);
            return;
        }
        if (ErrandGame.Me is { } me) DoorWays.Add(waySpots, me);
        FinishWay(atDoor: DoorNear(waySpots.LastOrDefault()) is not null, plot: 0);
    }

    /// <summary>
    /// Whether the errands have a way to walk to <paramref name="home"/>'s door: recorded for its plot,
    /// or it has no front door to walk to (an apartment). The errands' screen asks for one when not.
    /// </summary>
    public bool HasWayFor(HomeRef? home) =>
        home == null || home.Kind == HomeKind.Apartment || DoorWays.HasFor(config.ErrandsDoorWays, home.WardTerritory, home.Plot);

    /// <summary>The way recorded for this home's plot, for Settings' words (null when none).</summary>
    public DoorWay? WayFor(HomeRef? home) => home is { Plot: { } p } && home.Kind != HomeKind.Apartment
        ? config.ErrandsDoorWays.FirstOrDefault(w => w.Territory == home.WardTerritory && w.Plot == p)
          ?? config.ErrandsDoorWays.FirstOrDefault(w => w.Territory == home.WardTerritory && w.Plot == 0)
        : null;

    /// <summary>A way followed in to this home's plot that didn't know its plot (0.7.12's) learns it.</summary>
    private void LearnWayPlot(DoorWay way, HomeRef? home)
    {
        if (way.Plot != 0 || home?.Plot is not { } p) return;
        way.Plot = p;
        config.Save();
        log.Info($"  The way you recorded goes to plot {p}: kept as that plot's.");
    }

    private static Vector3? DoorNear(Vector3 at) =>
        ErrandGame.Nearest(ErrandTable.EntranceWords, at, DoorWays.EndsAtDoor, UsableKinds)?.Position;

    /// <summary>Through the door: when the zone changed, waiting to be inside to read which plot it is.</summary>
    private DateTime? wayInsideBy;

    /// <summary>Every frame (errands or not): the spot you're at, kept when you've moved; going in saves.</summary>
    private void TickWay()
    {
        if (!RecordingWay) return;
        if (wayInsideBy is { } by)
        {
            // In (and settled): the plot you went into, then saved. Not read in 15 s: saved without it.
            var inside = ErrandGame.Settled && here.Kind == PlaceKind.Interior ? ErrandGame.HomeHere(here) : null;
            if (inside == null && DateTime.UtcNow < by) return;
            wayInsideBy = null;
            var atDoor = wayDoor is { } d && Vector2.Distance(new(d.X, d.Z), new(waySpots[^1].X, waySpots[^1].Z)) <= DoorWays.EndsAtDoor;
            FinishWay(atDoor && (inside == null || inside.WardTerritory == wayTerritory || inside.WardTerritory == 0), inside?.Plot ?? 0);
            return;
        }
        if (ErrandGame.Territory != wayTerritory || !Services.ClientState.IsLoggedIn)
        {
            // Through the door (or teleported away): saved once inside, if the way ended at a door.
            wayInsideBy = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            WayNote = "Going in: saving the way once you're inside…";
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

    private void FinishWay(bool atDoor, int plot)
    {
        RecordingWay = false;
        wayInsideBy = null;
        var length = DoorWays.Length(waySpots);
        if (!atDoor || waySpots.Count < 2 || length < DoorWays.Shortest)
        {
            WayNote = !atDoor ? "Let go: it didn't end at a front door (go in through it, or press Stop beside it)." : "Let go: too short to be worth keeping.";
            log.Warn($"  Way to your door: {WayNote}");
            return;
        }
        // A new way to the same door or into the same plot replaces the old.
        if (plot != 0) config.ErrandsDoorWays.RemoveAll(w => w.Territory == wayTerritory && w.Plot == plot);
        DoorWays.Keep(config.ErrandsDoorWays, DoorWay.From(wayTerritory, waySpots, DateTime.UtcNow, plot));
        config.Save();
        WayNote = $"Saved{(plot != 0 ? $" for plot {plot}" : string.Empty)}: {length:0} yalms. The errands follow it to this door.";
        log.Done($"Way to your door saved ({length:0} yalms, {waySpots.Count} spots, zone {wayTerritory}{(plot != 0 ? $", plot {plot}" : string.Empty)}).");
    }

    /// <summary>A shop asked another price than the game's data for one: kept for every estimate after, and said.</summary>
    private void LearnPrice(uint itemId, uint unitPrice, uint planned)
    {
        if (shop.LearnPrice(itemId, unitPrice))
            log.Info($"  Learnt: the shop asks {unitPrice:N0} gil for one {Name(itemId)}, not the {planned:N0} the game's data says. Estimates use {unitPrice:N0} from now on.");
    }
}
