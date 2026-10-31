using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using HousingStudio.Core;

namespace HousingStudio.Game;

/// <summary>
/// What your retainers hold, as this plugin saw it: whenever a retainer's inventory window is open, its
/// bags are counted (the retainer's name, when, and each item's count) and kept, per character, in
/// retainers.json in the plugin's folder. Framework thread only (the file is written in the background).
/// </summary>
public sealed unsafe class RetainerRecord
{
    /// <summary>A record is saved again at most this often when only its time changed.</summary>
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

    private static readonly string[] InventoryWindows = { "InventoryRetainer", "InventoryRetainerLarge" };

    private static readonly InventoryType[] Pages =
    {
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
        InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7,
    };

    public sealed class Saved
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime SeenAt { get; set; }
        public Dictionary<uint, int> Items { get; set; } = new();
    }

    private readonly string path;
    private readonly object writing = new();

    /// <summary>By character (content id, as text: JSON keys), each retainer seen.</summary>
    private Dictionary<string, List<Saved>> record = new();

    private DateTime nextLook;
    private DateTime savedAt;
    private int version;

    public RetainerRecord()
    {
        path = Path.Combine(Services.PluginInterface.GetPluginConfigDirectory(), "retainers.json");
        try
        {
            if (File.Exists(path)) record = JsonSerializer.Deserialize<Dictionary<string, List<Saved>>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't read retainers.json: retainers are counted again as you open them");
            record = new();
        }
    }

    /// <summary>Goes up whenever a retainer's count changes.</summary>
    public int Version => version;

    /// <summary>The retainers of the character you're playing, as last seen.</summary>
    public List<RetainerStock> Stocks()
    {
        var me = Services.PlayerState.IsLoaded ? Services.PlayerState.ContentId : 0;
        if (me == 0 || !record.TryGetValue(me.ToString(), out var list)) return new List<RetainerStock>();
        return list.Select(s => new RetainerStock(s.Id, s.Name, s.SeenAt, s.Items, HoldingOrigin.Seen)).ToList();
    }

    /// <summary>Looks (once a second) for an open retainer's inventory and counts it.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now < nextLook) return;
        nextLook = now + TimeSpan.FromSeconds(1);
        if (!Services.PlayerState.IsLoaded || Services.PlayerState.ContentId == 0) return;
        if (!InventoryWindows.Any(Automation.GameUi.IsOpen)) return;
        var rm = RetainerManager.Instance();
        if (rm == null || !rm->IsReady) return;
        var active = rm->GetActiveRetainer();
        if (active == null || active->RetainerId == 0) return;
        var mgr = InventoryManager.Instance();
        if (mgr == null) return;

        var slots = new List<(uint, long)>();
        var loaded = false;
        foreach (var t in Pages)
        {
            var c = mgr->GetInventoryContainer(t);
            if (c == null || !c->IsLoaded) continue;
            loaded = true;
            for (var i = 0; i < c->Size; i++)
            {
                var item = c->GetInventorySlot(i);
                if (item == null || item->IsEmpty()) continue;
                slots.Add((item->GetBaseItemId(), item->GetQuantity()));
            }
        }
        if (!loaded) return;
        Remember(active->RetainerId, active->NameString, now, RetainerStocks.Count(slots));
    }

    private void Remember(ulong id, string name, DateTime at, Dictionary<uint, int> items)
    {
        var me = Services.PlayerState.ContentId.ToString();
        if (!record.TryGetValue(me, out var list)) record[me] = list = new List<Saved>();
        var saved = list.FirstOrDefault(s => s.Id == id);
        var changed = saved == null || saved.Name != name || saved.Items.Count != items.Count
                      || saved.Items.Any(kv => !items.TryGetValue(kv.Key, out var n) || n != kv.Value);
        if (saved == null) list.Add(saved = new Saved { Id = id });
        saved.Name = name;
        saved.SeenAt = at;
        saved.Items = items;
        if (changed) Interlocked.Increment(ref version);
        if (changed || at - savedAt > SaveEvery) Save(at);
    }

    private void Save(DateTime at)
    {
        savedAt = at;
        string json;
        try
        {
            json = JsonSerializer.Serialize(record);
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't save what your retainers hold");
            return;
        }
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                lock (writing)
                {
                    var temp = path + ".tmp";
                    File.WriteAllText(temp, json);
                    File.Move(temp, path, true);
                }
            }
            catch (Exception ex)
            {
                Services.Log.Warning(ex, "Couldn't save what your retainers hold");
            }
        });
    }

    /// <summary>Your retainers' names by id, from the game's retainer list (empty until it's been sent this session).</summary>
    public static Dictionary<ulong, string> Names()
    {
        var names = new Dictionary<ulong, string>();
        var rm = RetainerManager.Instance();
        if (rm == null || !rm->IsReady) return names;
        foreach (ref var r in rm->Retainers)
            if (r.RetainerId != 0 && r.NameString.Length > 0) names[r.RetainerId] = r.NameString;
        return names;
    }
}

/// <summary>
/// Allagan Tools' record of your retainers, through its IPC when it's installed. Every call is wrapped: a
/// missing, not-yet-ready or failing Allagan Tools just means nothing is known from it. Framework thread.
/// </summary>
/// <remarks>
/// Uses <c>AllaganTools.IsInitialized</c> (→ bool), <c>AllaganTools.GetCharactersOwnedByActive</c>
/// (bool includeOwner → HashSet&lt;ulong&gt;: the retainers' ids) and <c>AllaganTools.GetCharacterItems</c>
/// (ulong characterId → HashSet&lt;ulong[]&gt;, each array item id then quantity first).
/// </remarks>
public sealed class AllaganTools
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(60);

    private readonly ICallGateSubscriber<bool> isInitialized;
    private readonly ICallGateSubscriber<bool, HashSet<ulong>> ownedByActive;
    private readonly ICallGateSubscriber<ulong, HashSet<ulong[]>> characterItems;
    private DateTime nextRefresh;
    private bool loggedFailure;

    public AllaganTools()
    {
        isInitialized = Services.PluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        ownedByActive = Services.PluginInterface.GetIpcSubscriber<bool, HashSet<ulong>>("AllaganTools.GetCharactersOwnedByActive");
        characterItems = Services.PluginInterface.GetIpcSubscriber<ulong, HashSet<ulong[]>>("AllaganTools.GetCharacterItems");
    }

    /// <summary>What Allagan Tools last said (empty when it isn't there).</summary>
    public IReadOnlyList<RetainerStock> Stocks { get; private set; } = Array.Empty<RetainerStock>();

    /// <summary>Whether Allagan Tools answered last time.</summary>
    public bool Available { get; private set; }

    public int Version { get; private set; }

    /// <summary>Asks Allagan Tools again (once a minute, or now when <paramref name="soon"/>).</summary>
    public void Refresh(bool soon = false)
    {
        var now = DateTime.UtcNow;
        if (!soon && now < nextRefresh) return;
        nextRefresh = now + RefreshEvery;
        List<RetainerStock> stocks;
        try
        {
            if (!isInitialized.InvokeFunc())
            {
                Set(Array.Empty<RetainerStock>(), false);
                return;
            }
            stocks = new List<RetainerStock>();
            foreach (var id in ownedByActive.InvokeFunc(false) ?? new HashSet<ulong>())
            {
                var items = characterItems.InvokeFunc(id);
                if (items == null) continue;
                var counts = RetainerStocks.Count(items.Where(a => a is { Length: >= 2 }).Select(a => ((uint)Math.Min(a[0], uint.MaxValue), (long)Math.Min(a[1], long.MaxValue))));
                if (counts.Count > 0) stocks.Add(new RetainerStock(id, "a retainer", null, counts, HoldingOrigin.AllaganTools));
            }
        }
        catch (Exception ex)
        {
            // Not installed (IpcNotReadyError), another version's signatures, or it threw: nothing from it.
            if (!loggedFailure) Services.Log.Debug(ex, "Allagan Tools didn't answer; retainers come from this plugin's own record");
            loggedFailure = true;
            Set(Array.Empty<RetainerStock>(), false);
            return;
        }
        Set(stocks, true);
    }

    private void Set(IReadOnlyList<RetainerStock> stocks, bool available)
    {
        var same = Available == available && Stocks.Count == stocks.Count
                   && Stocks.Zip(stocks).All(p => p.First.RetainerId == p.Second.RetainerId && p.First.Items.Count == p.Second.Items.Count
                                                  && p.First.Items.All(kv => p.Second.Count(kv.Key) == kv.Value));
        Available = available;
        if (same) return;
        Stocks = stocks;
        Version++;
    }
}
