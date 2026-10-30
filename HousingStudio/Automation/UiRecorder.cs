using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HousingStudio.Game;

namespace HousingStudio.Automation;

/// <summary>
/// Watch-only diagnostics: while on, it writes down every game window that opens or closes, every
/// callback a window fires (its values; these are what the automation sends, so a recording of you
/// doing something by hand shows exactly what to send), and the housing state as it changes (editor
/// mode, the piece held, the preview, the storeroom slot picked, counts of pieces). It never sends
/// anything. The same callback hook Refurnish's recorder and ECommons' debug logging use.
/// </summary>
public sealed unsafe class UiRecorder : IDisposable
{
    private const int MaxLines = 1500;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(10);

    /// <summary>True while the plugin itself is firing a callback, so the log can tell its clicks from yours.</summary>
    internal static bool OurClick { get; set; }

    private readonly Hook<AtkUnitBase.Delegates.FireCallback>? hook;
    private readonly List<string> lines = new();
    private DateTime started;
    private string lastState = string.Empty;

    public bool Recording { get; private set; }
    public string? SetupError { get; }
    public int LineCount => lines.Count;

    public UiRecorder()
    {
        try
        {
            hook = Services.GameInterop.HookFromAddress<AtkUnitBase.Delegates.FireCallback>((nint)AtkUnitBase.MemberFunctionPointers.FireCallback, Detour);
        }
        catch (Exception ex)
        {
            SetupError = "Couldn't attach to the game's window callbacks, so callbacks aren't recorded (windows and housing state still are).";
            Services.Log.Error(ex, "UiRecorder hook failed");
        }
    }

    public void Start()
    {
        if (Recording) return;
        lines.Clear();
        lastState = string.Empty;
        started = DateTime.UtcNow;
        Recording = true;
        Add($"Recording started. Housing Studio {typeof(UiRecorder).Assembly.GetName().Version}, client language {Services.ClientState.ClientLanguage}.");
        if (SetupError != null) Add(SetupError);
        Add("Do one thing by hand (place one piece from your bags, one from the storeroom, dye one piece), then stop and copy this.");
        hook?.Enable();
        Services.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnOpen);
        Services.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, OnClose);
        Services.Toasts.ErrorToast += OnErrorToast;
    }

    /// <summary>Writes down every value the windows the automation uses hold right now.</summary>
    public void DumpWindows()
    {
        foreach (var name in new[] { UiTable.FurnishingList, UiTable.DyeList, UiTable.DyeWindow, UiTable.ContextMenu, UiTable.LayoutBar, UiTable.HousingMenu })
            Add("VALUES " + GameUi.DumpValues(name));
        Add("STATE " + DescribeState());
    }

    public void Stop()
    {
        if (!Recording) return;
        Recording = false;
        hook?.Disable();
        Services.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, OnOpen);
        Services.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, OnClose);
        Services.Toasts.ErrorToast -= OnErrorToast;
        Add("Recording stopped.");
    }

    public string AsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Housing Studio diagnostics recording");
        foreach (var l in lines) sb.AppendLine(l);
        return sb.ToString();
    }

    /// <summary>Once a frame while recording: notes changes in the housing state.</summary>
    public void Tick()
    {
        if (!Recording) return;
        if (DateTime.UtcNow - started > MaxDuration)
        {
            Add("Stopped after 10 minutes.");
            Stop();
            return;
        }
        var state = DescribeState();
        if (state != lastState)
        {
            lastState = state;
            Add("STATE " + state);
        }
    }

    public static string DescribeState()
    {
        var hm = HousingManager.Instance();
        var mode = HousingEditor.Mode;
        var active = HousingEditor.ActiveItem;
        var preview = FurnitureReader.Preview();
        var selection = InventoryReader.StoreroomSelection();
        var tab = hm != null && hm->IndoorTerritory != null && hm->IndoorTerritory->ShowStoreroomTab;
        var placed = CountPlaced();
        var previewAt = preview != null ? $"({preview->Position.X:0.00}, {preview->Position.Y:0.00}, {preview->Position.Z:0.00}) r{preview->Rotation:0.00}" : "none";
        return $"mode={mode} active={(active != 0 ? $"0x{active:X}" : "none")} preview={previewAt} storeroomTab={tab} storeroomPick={(selection is { } s ? $"{s.Container}/{s.Slot}" : "none")} placed={placed}";
    }

    private static int CountPlaced()
    {
        var hm = HousingManager.Instance();
        if (hm == null) return 0;
        var fm = hm->IndoorTerritory != null ? &hm->IndoorTerritory->FurnitureManager : hm->OutdoorTerritory != null ? &hm->OutdoorTerritory->FurnitureManager : null;
        return fm == null ? 0 : (int)fm->FurnitureVector.LongCount;
    }

    private bool Detour(AtkUnitBase* self, uint count, AtkValue* values, bool close)
    {
        try
        {
            if (Recording && self != null)
            {
                var who = OurClick ? "plugin" : "you";
                var name = self->NameString;
                Add($"CALLBACK ({who}) {name} close={close} [{GameUi.Describe(values, count)}]");
                if (name == UiTable.ContextMenu)
                {
                    var entries = GameUi.MenuEntries();
                    if (entries != null) Add($"  menu entries: {string.Join(" | ", entries.Select((e, i) => $"{i}:{e}"))}");
                }
                Add("  " + DescribeState());
            }
        }
        catch
        {
            // Never let watching break the game's own call.
        }
        return hook!.Original(self, count, values, close);
    }

    private void OnOpen(AddonEvent type, AddonArgs args)
    {
        if (!Recording) return;
        var a = (AtkUnitBase*)args.Addon.Address;
        // The windows the automation reads get all their words written down (the dye window's list,
        // the furnishing list's rows); the rest, the first few.
        var full = args.AddonName is UiTable.DyeWindow or UiTable.FurnishingList or UiTable.DyeList or UiTable.ContextMenu;
        var strings = a != null ? GameUi.Strings(a, full ? 600 : 12) : new List<string>();
        Add($"OPEN {args.AddonName}{(strings.Count > 0 ? " " + string.Join(" ", strings) : string.Empty)}");
    }

    private void OnClose(AddonEvent type, AddonArgs args)
    {
        if (Recording) Add($"CLOSE {args.AddonName}");
    }

    private void OnErrorToast(ref Dalamud.Game.Text.SeStringHandling.SeString message, ref bool isHandled) => Add($"ERROR TOAST {message.TextValue}");

    private void Add(string line)
    {
        if (started == default) started = DateTime.UtcNow;
        lines.Add($"{(DateTime.UtcNow - started).TotalSeconds,7:0.000}  {line}");
        if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
    }

    public void Dispose()
    {
        Stop();
        hook?.Dispose();
    }
}
