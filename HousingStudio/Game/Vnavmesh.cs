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
/// <item><c>vnavmesh.Path.IsRunning</c> → bool: walking a path.</item>
/// <item><c>vnavmesh.Path.Stop</c>: stops walking.</item>
/// </list>
/// These are the names vnavmesh's IPCProvider has had since 2024, used by many plugins; not seen
/// working with this plugin yet.
/// </remarks>
public sealed class Vnavmesh
{
    public const string InternalName = "vnavmesh";

    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> pathfindInProgress;
    private readonly ICallGateSubscriber<bool> isRunning;
    private readonly ICallGateSubscriber<object> stop;
    private readonly HashSet<string> failed = new();

    public Vnavmesh()
    {
        var pi = Services.PluginInterface;
        isReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        isRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        stop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    /// <summary>Installed and loaded (its IPC may still not answer).</summary>
    public bool Installed => Plugins.Loaded(InternalName);

    /// <summary>The mesh for this zone is built.</summary>
    public bool Ready => Call("Nav.IsReady", () => isReady.InvokeFunc(), false);

    /// <summary>Starts walking to a spot. False when it couldn't start.</summary>
    public bool MoveTo(Vector3 to) => Call("SimpleMove.PathfindAndMoveTo", () => pathfindAndMoveTo.InvokeFunc(to, false), false);

    /// <summary>Finding a path or walking one.</summary>
    public bool Busy => Call("SimpleMove.PathfindInProgress", () => pathfindInProgress.InvokeFunc(), false) || Call("Path.IsRunning", () => isRunning.InvokeFunc(), false);

    /// <summary>Stops walking (safe to call whenever: nothing happens when it isn't).</summary>
    public void Stop() => Call<object?>("Path.Stop", () =>
    {
        stop.InvokeAction();
        return null;
    }, null);

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
