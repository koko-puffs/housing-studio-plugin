using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HousingStudio.Game;

/// <summary>
/// The MakePlace plugin's "place anywhere": three one-byte switches in the game's code that, set to
/// 1, turn off the check that refuses floating, sunk or overlapping furniture ("Unable to place
/// item."). Only switched on while placing with the option ticked, and always put back as found
/// (so if another plugin had it on, it stays on). The byte patterns are the ones the maintained
/// MakePlace forks use for API 15; if any can't be found, or holds anything but 0 or 1, nothing is
/// changed and the option says why.
/// </summary>
public sealed class PlaceAnywhere : IDisposable
{
    private static readonly (string Name, (string Pattern, int Offset)[] Candidates)[] Switches =
    {
        ("place anywhere", new[] { ("C6 83 ?? ?? ?? ?? ?? 0F 29 44 24", 6), ("C6 ?? ?? ?? 00 00 00 8B FE 48 89", 6) }),
        ("wall anywhere", new[] { ("48 85 C0 74 ?? C6 87 ?? ?? 00 00 00", 11) }),
        ("wall-mount anywhere", new[] { ("C6 87 83 01 00 00 00 48 83 C4 ??", 6) }),
    };

    private readonly List<(string Name, nint Address, byte Original)> applied = new();
    private List<(string Name, nint Address)>? found;

    public bool Active => applied.Count > 0;
    public string? Error { get; private set; }

    private bool Find()
    {
        if (found != null) return true;
        var list = new List<(string, nint)>();
        foreach (var (name, candidates) in Switches)
        {
            nint at = 0;
            foreach (var (pattern, offset) in candidates)
                if (Services.SigScanner.TryScanText(pattern, out var addr) && addr != 0)
                {
                    at = addr + offset;
                    break;
                }
            if (at == 0)
            {
                Error = $"Couldn't find the game's \"{name}\" switch (the game may have been patched), so placing anywhere isn't available.";
                return false;
            }
            list.Add((name, at));
        }
        found = list;
        return true;
    }

    public bool Enable()
    {
        if (Active) return true;
        Error = null;
        if (!Find()) return false;
        foreach (var (name, address) in found!)
        {
            var current = Marshal.ReadByte(address);
            if (current is not (0 or 1))
            {
                Error = $"The game's \"{name}\" switch held an unexpected value ({current}), so nothing was changed.";
                return false;
            }
        }
        foreach (var (name, address) in found!)
        {
            var original = Marshal.ReadByte(address);
            if (!Write(address, 1))
            {
                Disable();
                Error = $"Couldn't switch on \"{name}\"; nothing was left changed.";
                return false;
            }
            applied.Add((name, address, original));
        }
        Services.Log.Information("Place anywhere on.");
        return true;
    }

    public void Disable()
    {
        if (applied.Count == 0) return;
        foreach (var (name, address, original) in applied)
            if (!Write(address, original))
                Services.Log.Error($"Couldn't put \"{name}\" back. Restarting the game clears it.");
        applied.Clear();
        Services.Log.Information("Place anywhere put back.");
    }

    private static bool Write(nint address, byte value)
    {
        if (!VirtualProtect(address, 1, PageExecuteReadWrite, out var old)) return false;
        Marshal.WriteByte(address, value);
        VirtualProtect(address, 1, old, out _);
        return true;
    }

    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(nint lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

    public void Dispose() => Disable();
}
