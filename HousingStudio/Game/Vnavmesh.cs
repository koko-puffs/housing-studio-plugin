using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Ipc;

namespace HousingStudio.Game;

/// <summary>
/// Whether another plugin is installed and loaded, by its internal name, from Dalamud's list. Checked
/// before any of its IPC is called, so a missing plugin is told apart from one that failed. The list is
/// read at most every 2 seconds (the estimate screen asks every frame).
/// </summary>
public static class Plugins
{
    private static readonly Dictionary<string, (DateTime Until, bool Loaded)> Known = new(StringComparer.OrdinalIgnoreCase);

    public static bool Loaded(string internalName)
    {
        var now = DateTime.UtcNow;
        if (Known.TryGetValue(internalName, out var k) && now < k.Until) return k.Loaded;
        bool loaded;
        try
        {
            loaded = Services.PluginInterface.InstalledPlugins.Any(p => p.IsLoaded && string.Equals(p.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            loaded = false;
        }
        Known[internalName] = (now + TimeSpan.FromSeconds(2), loaded);
        return loaded;
    }
}

/// <summary>
/// vnavmesh (awgil/ffxiv_navmesh), through its IPC: walking to a spot in the zone you're in. Every call
/// is wrapped: not installed, not ready, another version's signatures or a throw mean "can't", and the
/// first failure of each call is logged. Framework thread.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>vnavmesh.Nav.IsReady</c> → bool: the zone's mesh is built (it builds after each zone change).</item>
/// <item><c>vnavmesh.SimpleMove.PathfindAndMoveTo</c> (Vector3 destination, bool fly) → bool: finds a path and walks it.</item>
/// <item><c>vnavmesh.SimpleMove.PathfindInProgress</c> → bool: still finding the path.</item>
/// <item><c>vnavmesh.Path.MoveTo</c> (List&lt;Vector3&gt; waypoints, bool fly): walks through the spots given, in order, with no pathfinding (a recorded way to your door).</item>
/// <item><c>vnavmesh.Path.IsRunning</c> → bool: walking a path.</item>
/// <item><c>vnavmesh.Path.Stop</c>: stops walking.</item>
/// <item><c>vnavmesh.Nav.PathfindCancelAll</c>: cancels a path being worked out; it reloads the zone's mesh
/// (<c>navmeshManager.Reload(true)</c>), so it's only called while one is.</item>
/// <item><c>vnavmesh.SimpleMove.PathfindAndMoveCloseTo</c> (Vector3, bool fly, float range) → bool: as
/// PathfindAndMoveTo, the path ending once within range (1.0.4).</item>
/// <item><c>vnavmesh.Query.Mesh.NearestPointReachable</c> (Vector3, float halfExtentXZ, float halfExtentY)
/// → Vector3?: the nearest point of the floor you can reach (1.0.4).</item>
/// </list>
/// Checked against vnavmesh's <c>IPCProvider.cs</c> (awgil/ffxiv_navmesh, master, November 2026).
/// </remarks>
public sealed class Vnavmesh
{
    public const string InternalName = "vnavmesh";

    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> pathfindInProgress;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> follow;
    private readonly ICallGateSubscriber<bool> isRunning;
    private readonly ICallGateSubscriber<object> stop;
    private readonly ICallGateSubscriber<object> cancelPathfinds;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestReachable;
    private readonly HashSet<string> failed = new();

    public Vnavmesh()
    {
        var pi = Services.PluginInterface;
        isReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        follow = pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        isRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        cancelPathfinds = pi.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");
        pathfindAndMoveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        nearestReachable = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
    }

    /// <summary>Installed and loaded (its IPC may still not answer).</summary>
    public bool Installed => Plugins.Loaded(InternalName);

    /// <summary>The mesh for this zone is built.</summary>
    public bool Ready => Call("Nav.IsReady", () => isReady.InvokeFunc(), false);

    /// <summary>Starts walking to a spot. False when it couldn't start.</summary>
    public bool MoveTo(Vector3 to)
    {
        // 1.0.2: a walk still going is stopped first, so two never race.
        if (Walking) Stop();
        return Call("SimpleMove.PathfindAndMoveTo", () => pathfindAndMoveTo.InvokeFunc(to, false), false);
    }

    /// <summary>
    /// Starts walking until within <paramref name="range"/> yalms of a spot (vnavmesh's
    /// <c>SimpleMove.PathfindAndMoveCloseTo</c>, 1.0.4: the path ends as soon as it's that close, so a spot
    /// behind a counter is walked up to from the front). An older vnavmesh without it: walked to the spot.
    /// False when it couldn't start.
    /// </summary>
    public bool MoveCloseTo(Vector3 to, float range)
    {
        if (Walking) Stop();
        if (!failed.Contains("SimpleMove.PathfindAndMoveCloseTo"))
        {
            try
            {
                return pathfindAndMoveCloseTo.InvokeFunc(to, false, range);
            }
            catch (Exception ex)
            {
                failed.Add("SimpleMove.PathfindAndMoveCloseTo");
                Services.Log.Debug(ex, "vnavmesh didn't answer SimpleMove.PathfindAndMoveCloseTo: walking to the spot itself");
            }
        }
        return Call("SimpleMove.PathfindAndMoveTo", () => pathfindAndMoveTo.InvokeFunc(to, false), false);
    }

    /// <summary>
    /// The nearest point of the walkable floor you can reach to a spot (within <paramref name="acrossXZ"/>
    /// yalms across and <paramref name="acrossY"/> up or down; <c>Query.Mesh.NearestPointReachable</c>),
    /// or null: for an NPC behind a counter, the floor in front of it.
    /// </summary>
    public Vector3? NearestReachable(Vector3 to, float acrossXZ, float acrossY) =>
        Call("Query.Mesh.NearestPointReachable", () => nearestReachable.InvokeFunc(to, acrossXZ, acrossY), null);

    /// <summary>Walks through these spots in order, as they are (no pathfinding). False when it couldn't start.</summary>
    public bool Follow(List<Vector3> spots) => spots.Count > 0 && Call("Path.MoveTo", () =>
    {
        follow.InvokeAction(spots, false);
        return true;
    }, false);

    /// <summary>Walking a path found (not still working one out).</summary>
    public bool Walking => Call("Path.IsRunning", () => isRunning.InvokeFunc(), false);

    /// <summary>Working out a path (it starts walking once found).</summary>
    public bool Pathfinding => Call("SimpleMove.PathfindInProgress", () => pathfindInProgress.InvokeFunc(), false);

    /// <summary>Finding a path or walking one.</summary>
    public bool Busy => Pathfinding || Walking;

    /// <summary>Stops walking (safe to call whenever: nothing happens when it isn't).</summary>
    public void Stop()
    {
        // A path still being worked out starts walking once it's found: cancelled too. Only then: vnavmesh's
        // Nav.PathfindCancelAll reloads the zone's whole mesh (its IPCProvider: navmeshManager.Reload), and
        // 1.0.2 and before called it at every stop, so the map was built again after each walk.
        if (Pathfinding)
            Call<object?>("Nav.PathfindCancelAll", () =>
            {
                cancelPathfinds.InvokeAction();
                return null;
            }, null);
        Call<object?>("Path.Stop", () =>
        {
            stop.InvokeAction();
            return null;
        }, null);
    }

    private T Call<T>(string what, Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            if (failed.Add(what)) Services.Log.Debug(ex, $"vnavmesh didn't answer {what}");
            return fallback;
        }
    }
}
