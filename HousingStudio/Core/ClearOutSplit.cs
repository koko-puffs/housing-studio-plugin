using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>
/// Clear out with a storeroom that can't take every piece: the ones it has room for go first, the
/// rest once the design is placed (each piece placed from the storeroom makes room for one).
/// </summary>
public static class ClearOutSplit
{
    /// <summary>
    /// The pieces put away first and those that wait, by the storeroom's <paramref name="room"/>
    /// (null when it isn't known: all go first, and a piece it refuses puts the rest off then).
    /// </summary>
    public static (IReadOnlyList<T> First, IReadOnlyList<T> Later) Split<T>(IReadOnlyList<T> pieces, int? room)
    {
        if (room is not { } r || r >= pieces.Count) return (pieces, Array.Empty<T>());
        var n = Math.Max(0, r);
        return (pieces.Take(n).ToList(), pieces.Skip(n).ToList());
    }
}
