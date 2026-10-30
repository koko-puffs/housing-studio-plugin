using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>
/// "The same for a few frames": a state counts as settled once it has read the same over
/// <see cref="Frames"/> frames in a row and for at least <see cref="Minimum"/>. 0.2.4 acted on state
/// read in the same frame as the click that changed it (a piece "picked up" that the game then
/// dropped; a list "loaded" before its names were filled in). Pure, so it's tested.
/// </summary>
public sealed class Settle
{
    private string? last;
    private int seen;
    private DateTime since;

    public Settle(int frames, TimeSpan minimum)
    {
        Frames = Math.Max(1, frames);
        Minimum = minimum;
    }

    public int Frames { get; }
    public TimeSpan Minimum { get; }

    /// <summary>
    /// Feeds this frame's state (null: not ready at all). True once the same, ready, state has been
    /// seen <see cref="Frames"/> frames running and for at least <see cref="Minimum"/>.
    /// </summary>
    public bool Update(string? state, DateTime now)
    {
        if (state == null || state != last)
        {
            last = state;
            seen = state == null ? 0 : 1;
            since = now;
            return false;
        }
        seen++;
        return seen >= Frames && now - since >= Minimum;
    }

    public void Reset()
    {
        last = null;
        seen = 0;
    }
}

/// <summary>When the furnishing list has finished filling in (pure).</summary>
public static class ListReady
{
    /// <summary>
    /// The list's state for <see cref="Settle"/>: null while it isn't ready (no rows, or rows whose names
    /// are still blank, as 0.2.4 read "32 rows:  |  |  |"), else a signature that changes when it does.
    /// <paramref name="storeroomCount"/> must be the storeroom's count, settled too: the list holds every
    /// piece, up to what its component holds at once (200 in 0.2.3).
    /// </summary>
    public static string? State(IReadOnlyList<string>? rows, int storeroomCount, int componentHolds = 200)
    {
        if (rows == null || rows.Count == 0 || storeroomCount <= 0) return null;
        if (rows.Any(r => Menus.Clean(r).Length == 0)) return null;
        if (rows.Count < Math.Min(storeroomCount, componentHolds)) return null;
        return $"{storeroomCount}|{rows.Count}|{Menus.Clean(rows[0])}|{Menus.Clean(rows[^1])}";
    }
}
