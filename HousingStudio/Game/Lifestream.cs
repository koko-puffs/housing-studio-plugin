using System;
using System.Collections.Generic;
using Dalamud.Plugin.Ipc;

namespace HousingStudio.Game;

/// <summary>Which home "go home" means: Lifestream has one call for each.</summary>
public enum HomeKind
{
    /// <summary>Your own house (Lifestream's "home").</summary>
    House,

    /// <summary>Your free company's house ("fc").</summary>
    CompanyHouse,

    /// <summary>Your apartment ("apartment").</summary>
    Apartment,
}

/// <summary>
/// Lifestream (NightmareXIV/Lifestream), through its IPC: teleporting, going home, changing worlds on
/// your data centre. Every call is wrapped: not installed, another version's signatures or a throw mean
/// "can't", and the first failure of each call is logged. Framework thread.
/// </summary>
/// <remarks>
/// Lifestream's IPC is its <c>IPCProvider</c> class, each method published as <c>Lifestream.Name</c>:
/// <list type="bullet">
/// <item><c>Lifestream.IsBusy</c> → bool: travelling (its task list isn't empty). [sure: used by other plugins]</item>
/// <item><c>Lifestream.Abort</c>: stops whatever it's doing. [sure]</item>
/// <item><c>Lifestream.Teleport</c> (uint aetheryte, byte subIndex) → bool. [fairly sure]</item>
/// <item><c>Lifestream.TeleportToHome</c>, <c>TeleportToFC</c>, <c>TeleportToApartment</c> → bool: to the
/// estate's own teleport (outside its door). [fairly sure]</item>
/// <item><c>Lifestream.ChangeWorld</c> (string world) → bool: a world visit on your data centre. [fairly sure]</item>
/// <item><c>Lifestream.CanVisitSameDC</c> (string world) → bool. [fairly sure]</item>
/// <item><c>Lifestream.ExecuteCommand</c> (string): as if "/li" and the text were typed. [sure]</item>
/// <item><c>Lifestream.AethernetTeleport</c> (string destination) → bool: a city's aethernet from its
/// aetheryte ("Sapphire Avenue Exchange"). [fairly sure; else "/li" and the name]</item>
/// </list>
/// When a typed call fails, the matching "/li" text is tried through ExecuteCommand ("home", "fc",
/// "apartment", a world's name).
/// </remarks>
public sealed class Lifestream
{
    public const string InternalName = "Lifestream";

    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<object> abort;
    private readonly ICallGateSubscriber<uint, byte, bool> teleport;
    private readonly ICallGateSubscriber<bool> toHome;
    private readonly ICallGateSubscriber<bool> toFc;
    private readonly ICallGateSubscriber<bool> toApartment;
    private readonly ICallGateSubscriber<string, bool> changeWorld;
    private readonly ICallGateSubscriber<string, bool> canVisitSameDc;
    private readonly ICallGateSubscriber<string, object> executeCommand;
    private readonly ICallGateSubscriber<string, bool> aethernet;
    private readonly HashSet<string> failed = new();

    public Lifestream()
    {
        var pi = Services.PluginInterface;
        isBusy = pi.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        abort = pi.GetIpcSubscriber<object>("Lifestream.Abort");
        teleport = pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        toHome = pi.GetIpcSubscriber<bool>("Lifestream.TeleportToHome");
        toFc = pi.GetIpcSubscriber<bool>("Lifestream.TeleportToFC");
        toApartment = pi.GetIpcSubscriber<bool>("Lifestream.TeleportToApartment");
        changeWorld = pi.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld");
        canVisitSameDc = pi.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC");
        executeCommand = pi.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        aethernet = pi.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
    }

    public bool Installed => Plugins.Loaded(InternalName);

    /// <summary>Travelling. (When it doesn't answer: not busy, so the runner's own timeouts decide.)</summary>
    public bool Busy => Call("IsBusy", () => isBusy.InvokeFunc(), false);

    /// <summary>Stops whatever it's doing (safe to call whenever).</summary>
    public void Abort() => Call<object?>("Abort", () =>
    {
        abort.InvokeAction();
        return null;
    }, null);

    /// <summary>Teleports to an aetheryte. False when Lifestream wouldn't.</summary>
    public bool Teleport(uint aetheryte) => Call("Teleport", () => teleport.InvokeFunc(aetheryte, 0), false);

    /// <summary>Teleports to the home's own teleport (outside its door, or the apartment building's).</summary>
    public bool GoHome(HomeKind kind)
    {
        var (sub, name, words) = kind switch
        {
            HomeKind.CompanyHouse => (toFc, "TeleportToFC", "fc"),
            HomeKind.Apartment => (toApartment, "TeleportToApartment", "apartment"),
            _ => (toHome, "TeleportToHome", "home"),
        };
        return Call(name, () => sub.InvokeFunc(), false) || Command(words);
    }

    /// <summary>The city's aethernet to a shard, by its name (standing at the aetheryte). False when Lifestream wouldn't.</summary>
    public bool Aethernet(string destination) => Call("AethernetTeleport", () => aethernet.InvokeFunc(destination), false) || Command(destination);

    /// <summary>A world visit on your data centre (or back to your home world).</summary>
    public bool ChangeWorld(string world) => Call("ChangeWorld", () => changeWorld.InvokeFunc(world), false) || Command(world);

    /// <summary>Whether a world can be visited from here (null when Lifestream doesn't say).</summary>
    public bool? CanVisit(string world)
    {
        try
        {
            return canVisitSameDc.InvokeFunc(world);
        }
        catch (Exception ex)
        {
            if (failed.Add("CanVisitSameDC")) Services.Log.Debug(ex, "Lifestream didn't answer CanVisitSameDC");
            return null;
        }
    }

    /// <summary>"/li" and the text, as typed. False when it couldn't be sent.</summary>
    public bool Command(string text) => Call("ExecuteCommand", () =>
    {
        executeCommand.InvokeAction(text);
        return true;
    }, false);

    private T Call<T>(string what, Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            if (failed.Add(what)) Services.Log.Debug(ex, $"Lifestream didn't answer {what}");
            return fallback;
        }
    }
}
