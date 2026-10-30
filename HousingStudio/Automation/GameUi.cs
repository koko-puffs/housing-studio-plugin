using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HousingStudio.Core;

namespace HousingStudio.Automation;

/// <summary>Reading and clicking the game's windows (framework thread only).</summary>
public static unsafe class GameUi
{
    /// <summary>A window that's open, shown and finished loading, or null.</summary>
    public static AtkUnitBase* Ready(string name)
    {
        var a = Services.GameGui.GetAddonByName<AtkUnitBase>(name);
        return a != null && a->IsVisible && a->IsReady && a->IsFullyLoaded() ? a : null;
    }

    public static bool IsOpen(string name) => Ready(name) != null;

    /// <summary>
    /// Fires a window's callback, which is what clicking in it does (ECommons' <c>Callback.Fire</c>
    /// does the same). Returns false (and does nothing) when the window isn't open.
    /// </summary>
    public static bool Fire(UiCall call, IReadOnlyDictionary<string, long>? blanks = null)
    {
        var addon = Ready(call.Addon);
        if (addon == null) return false;
        var values = call.Resolve(blanks);
        var size = sizeof(AtkValue) * Math.Max(1, values.Count);
        var ptr = (AtkValue*)Marshal.AllocHGlobal(size);
        try
        {
            new Span<byte>(ptr, size).Clear();
            for (var i = 0; i < values.Count; i++)
            {
                var (kind, value) = values[i];
                switch (kind)
                {
                    case ArgKind.UInt:
                        ptr[i].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.UInt;
                        ptr[i].UInt = (uint)value;
                        break;
                    case ArgKind.Bool:
                        ptr[i].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Bool;
                        ptr[i].Byte = (byte)(value != 0 ? 1 : 0);
                        break;
                    default:
                        ptr[i].Type = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int;
                        ptr[i].Int = (int)value;
                        break;
                }
            }
            UiRecorder.OurClick = true;
            try
            {
                addon->FireCallback((uint)values.Count, ptr, call.Close);
            }
            finally
            {
                UiRecorder.OurClick = false;
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal((nint)ptr);
        }
    }

    /// <summary>The entries of the open context menu, as words (count in AtkValues[0], texts from AtkValues[8]) [ECommons].</summary>
    public static List<string>? MenuEntries()
    {
        var a = Ready(UiTable.ContextMenu);
        if (a == null || a->AtkValues == null || a->AtkValuesCount == 0) return null;
        var count = (int)a->AtkValues[0].UInt;
        var list = new List<string>();
        for (var i = 0; i < count && 8 + i < a->AtkValuesCount; i++)
        {
            var v = a->AtkValues[8 + i];
            list.Add(IsText(v.Type) && v.String.Value != null ? Text(v.String.Value) : string.Empty);
        }
        return list;
    }

    /// <summary>The yes/no box's question, or null when there isn't one.</summary>
    public static string? YesNoText()
    {
        var a = (AddonSelectYesno*)Ready(UiTable.YesNo);
        if (a == null) return null;
        if (a->AtkUnitBase.AtkValues != null && a->AtkUnitBase.AtkValuesCount > 0 && IsText(a->AtkUnitBase.AtkValues[0].Type) && a->AtkUnitBase.AtkValues[0].String.Value != null)
            return Text(a->AtkUnitBase.AtkValues[0].String.Value);
        return string.Empty;
    }

    /// <summary>Closes a window the way its close button does, if it's open.</summary>
    public static void Close(string name)
    {
        var a = Ready(name);
        if (a != null) a->Close(true);
    }

    /// <summary>The id of the open bag window (the item menu opens "from" it), or 0.</summary>
    public static uint BagWindowId()
    {
        foreach (var name in UiTable.BagWindows)
        {
            var a = Ready(name);
            if (a != null) return a->Id;
        }
        return 0;
    }

    /// <summary>Every text value a window holds, in order (for the recorder).</summary>
    public static List<string> Strings(AtkUnitBase* a, int max = 40)
    {
        var list = new List<string>();
        if (a == null || a->AtkValues == null) return list;
        for (var i = 0; i < a->AtkValuesCount && list.Count < max; i++)
        {
            var v = a->AtkValues[i];
            if (IsText(v.Type) && v.String.Value != null) list.Add($"[{i}] {Text(v.String.Value)}");
        }
        return list;
    }

    public static bool IsText(FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType type) =>
        ((int)type & 0xF) is 8 or 0xA;

    public static string Text(byte* ptr)
    {
        try
        {
            return SeString.Parse(ptr).TextValue.Trim();
        }
        catch
        {
            return Marshal.PtrToStringUTF8((nint)ptr) ?? string.Empty;
        }
    }

    /// <summary>A callback's values as text ("i:1, u:7, s:\"Place\""), for logs.</summary>
    public static string Describe(AtkValue* values, uint count)
    {
        var parts = new List<string>();
        for (var i = 0; i < count && i < 32; i++)
        {
            var v = values[i];
            var t = (int)v.Type & 0xF;
            parts.Add(t switch
            {
                0 => "empty",
                2 => $"b:{v.Byte != 0}",
                3 => $"i:{v.Int}",
                4 => $"i64:{v.Int64}",
                5 => $"u:{v.UInt}",
                7 => $"f:{v.Float}",
                _ when IsText(v.Type) && v.String.Value != null => $"s:\"{Text(v.String.Value)}\"",
                _ => $"type{(int)v.Type}",
            });
        }
        return string.Join(", ", parts);
    }
}
