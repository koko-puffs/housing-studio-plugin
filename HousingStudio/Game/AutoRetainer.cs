using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Dalamud.Plugin.Ipc;

namespace HousingStudio.Game;

/// <summary>
/// AutoRetainer's record of your retainers, through its IPC when it's installed: their names and ids, even
/// before the game has sent its retainer list this session (so Allagan Tools' counts get names). AutoRetainer
/// doesn't keep what retainers hold. Every call is wrapped: missing or failing just means nothing from it.
/// Framework thread.
/// </summary>
/// <remarks>
/// Uses <c>AutoRetainer.GetOfflineCharacterData</c> (ulong contentId → its OfflineCharacterData). That's
/// AutoRetainer's own class, so it's taken as an object and read by name: its <c>RetainerData</c> list,
/// each with <c>Name</c> and <c>RetainerID</c> (fields or properties).
/// </remarks>
public sealed class AutoRetainer
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(60);

    private readonly ICallGateSubscriber<ulong, object> characterData;
    private DateTime nextRefresh;
    private bool loggedFailure;

    private readonly ICallGateSubscriber<bool> getSuppressed;
    private readonly ICallGateSubscriber<bool, object> setSuppressed;
    private readonly ICallGateSubscriber<bool> isBusy;
    private bool suppressedByUs;

    public AutoRetainer()
    {
        characterData = Services.PluginInterface.GetIpcSubscriber<ulong, object>("AutoRetainer.GetOfflineCharacterData");
        getSuppressed = Services.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.GetSuppressed");
        setSuppressed = Services.PluginInterface.GetIpcSubscriber<bool, object>("AutoRetainer.SetSuppressed");
        isBusy = Services.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.IsBusy");
    }

    /// <summary>
    /// Errands (experimental): asks AutoRetainer to keep out of the way while they use a summoning bell
    /// (<c>AutoRetainer.SetSuppressed(true)</c>, its own "suppressed" state, which stops it opening
    /// retainers by itself), unless it already is. False when it didn't answer (not installed, or
    /// another version). <see cref="LetBe"/> puts it back.
    /// </summary>
    public bool KeepOut()
    {
        try
        {
            if (getSuppressed.InvokeFunc()) return true;
            setSuppressed.InvokeAction(true);
            suppressedByUs = true;
            return true;
        }
        catch (Exception ex)
        {
            if (!loggedSuppress) Services.Log.Debug(ex, "AutoRetainer didn't answer GetSuppressed/SetSuppressed");
            loggedSuppress = true;
            return false;
        }
    }

    /// <summary>Lets AutoRetainer carry on, if <see cref="KeepOut"/> held it.</summary>
    public void LetBe()
    {
        if (!suppressedByUs) return;
        suppressedByUs = false;
        try
        {
            setSuppressed.InvokeAction(false);
        }
        catch (Exception ex)
        {
            Services.Log.Debug(ex, "AutoRetainer didn't answer SetSuppressed");
        }
    }

    /// <summary>AutoRetainer is doing something (its <c>IsBusy</c>); false when it doesn't answer.</summary>
    public bool Busy
    {
        get
        {
            try
            {
                return isBusy.InvokeFunc();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private bool loggedSuppress;

    /// <summary>The retainers' names by id, as AutoRetainer last said (empty when it isn't there).</summary>
    public IReadOnlyDictionary<ulong, string> Names { get; private set; } = new Dictionary<ulong, string>();

    public bool Available { get; private set; }

    /// <summary>Asks AutoRetainer again (once a minute).</summary>
    public void Refresh()
    {
        var now = DateTime.UtcNow;
        if (now < nextRefresh) return;
        nextRefresh = now + RefreshEvery;
        var me = Services.PlayerState.IsLoaded ? Services.PlayerState.ContentId : 0;
        if (me == 0) return;
        try
        {
            var names = new Dictionary<ulong, string>();
            if (Read(characterData.InvokeFunc(me), "RetainerData") is IEnumerable retainers)
                foreach (var r in retainers)
                {
                    if (r == null) continue;
                    var id = Read(r, "RetainerID") is ulong u ? u : 0;
                    var name = Read(r, "Name") as string;
                    if (id != 0 && !string.IsNullOrEmpty(name)) names[id] = name;
                }
            Names = names;
            Available = true;
        }
        catch (Exception ex)
        {
            // Not installed (IpcNotReadyError), another version's shape, or it threw: nothing from it.
            if (!loggedFailure) Services.Log.Debug(ex, "AutoRetainer didn't answer; retainers are named from the game and this plugin's record");
            loggedFailure = true;
            Names = new Dictionary<ulong, string>();
            Available = false;
        }
    }

    /// <summary>A public field's or property's value by name, or null.</summary>
    private static object? Read(object? from, string name)
    {
        if (from == null) return null;
        var t = from.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        if (t.GetField(name, flags) is { } f) return f.GetValue(from);
        if (t.GetProperty(name, flags) is { } p && p.GetIndexParameters().Length == 0) return p.GetValue(from);
        return null;
    }
}
