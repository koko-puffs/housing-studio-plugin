using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Switches a restore couldn't put back (tried again by the next Disable).</summary>
    private readonly List<(string Name, nint Address, byte Original)> stuck = new();
    private List<(string Name, nint Address)>? found;
    private string? notFound;

    public bool Active => applied.Count > 0;
    public string? Error { get; private set; }

    private bool Find()
    {
        if (found != null) return true;
        // A search that failed fails again until the game changes: not searched for every piece.
        if (notFound != null)
        {
            Error = notFound;
            return false;
        }
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
                Error = notFound = $"Couldn't find the game's \"{name}\" switch (the game may have been patched), so placing anywhere isn't available.";
                return false;
            }
            // Two patterns landing on one byte would read it back as already on, and never put it back.
            if (list.Exists(f => f.Item2 == at))
            {
                Error = notFound = $"The game's \"{name}\" switch is where another one is (the game may have been patched), so placing anywhere isn't available.";
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
        // The switches as found, read before any is changed (one still stuck from a restore that failed keeps
        // the value it had before this plugin changed it).
        var originals = new List<byte>(found!.Count);
        foreach (var (name, address) in found!)
        {
            var was = stuck.FindIndex(x => x.Address == address);
            if (was >= 0)
            {
                originals.Add(stuck[was].Original);
                continue;
            }
            var current = Marshal.ReadByte(address);
            if (current is not (0 or 1))
            {
                Error = $"The game's \"{name}\" switch held an unexpected value ({current}), so nothing was changed.";
                return false;
            }
            originals.Add(current);
        }
        for (var i = 0; i < found.Count; i++)
        {
            var (name, address) = found[i];
            var original = originals[i];
            if (!Write(address, 1))
            {
                Disable();
                Error = stuck.Count == 0 ? $"Couldn't switch on \"{name}\"; nothing was left changed." : $"Couldn't switch on \"{name}\", nor put every switch back (restarting the game clears them).";
                return false;
            }
            applied.Add((name, address, original));
            stuck.RemoveAll(x => x.Address == address);
        }
        Services.Log.Information("Place anywhere on.");
        return true;
    }

    public void Disable()
    {
        if (applied.Count == 0 && stuck.Count == 0) return;
        // 1.0.2: one that couldn't be put back is kept aside, so the next Disable (the run's end, the plugin's)
        // tries again; Active is off, so the next Enable switches them all on again.
        var still = new List<(string Name, nint Address, byte Original)>();
        foreach (var (name, address, original) in applied.Concat(stuck))
            if (!Write(address, original))
            {
                Services.Log.Error($"Couldn't put \"{name}\" back. Restarting the game clears it.");
                still.Add((name, address, original));
            }
        applied.Clear();
        stuck.Clear();
        stuck.AddRange(still);
        Services.Log.Information(still.Count == 0 ? "Place anywhere put back." : $"Place anywhere put back, but {still.Count} switch(es) not yet.");
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
