using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>
/// The words for looking after a home: how full it is, what changed since a run, what the game
/// would refuse, and carrying on a run that was cut off. Pure, so it's tested.
/// </summary>
public static class CareWords
{
    private static string Where(Side side) => side == Side.Garden ? "in the garden" : "inside";

    /// <summary>"312 of 450 pieces placed inside", "20 of 60 pieces placed in the garden"; just the count when the limit isn't known.</summary>
    public static string HowFull(int placed, int? limit, Side side) =>
        limit is { } l ? $"{placed} of {l} pieces placed {Where(side)}" : $"{Words.Count(placed, "piece")} placed {Where(side)}";

    /// <summary>
    /// When the design won't fit: "This design has 480 pieces inside; a house (medium) holds 450." or
    /// "With the 60 pieces here that aren't in it, that makes 480; a house (medium) holds 450 inside.",
    /// then what Start does about it. Null when it fits.
    /// </summary>
    public static string? Limit(LimitCheck c, Side side, string? houseSize)
    {
        if (c.Over == 0 && !c.DesignOver) return null;
        var home = side == Side.Garden ? "your garden" : $"a {Words.HomeInSentence(houseSize)}";
        string first;
        if (c.DesignOver)
            first = $"This design has {c.Design} pieces {Where(side)}; {home} holds {c.Limit}.";
        else
            first = $"With the {Words.Count(c.Others, "piece")} here that {(c.Others == 1 ? "isn't" : "aren't")} in it, that makes {c.After}; {home} holds {c.Limit} {Where(side)}.";
        if (c.Over == 0)
            return first + " Some of it is missing or left out, so what Start places still fits.";
        return first + $" Start places pieces until it's full, then the game refuses the last {Words.Count(c.Over, "piece")} and the run stops after a few of them. "
               + (c.Others > 0 ? "Put some pieces away first, or leave some out (Near me, floors)." : "Leave some out (Near me, floors) to choose which.");
    }

    /// <summary>"2 moved, 1 added, 3 taken away and 1 in another dye"; "Nothing has changed" when nothing has.</summary>
    public static string Drift(DriftReport r)
    {
        var parts = new List<string>();
        if (r.Moved.Count > 0) parts.Add($"{r.Moved.Count} moved");
        if (r.Added.Count > 0) parts.Add($"{r.Added.Count} added");
        if (r.Gone.Count > 0) parts.Add($"{r.Gone.Count} taken away");
        if (r.Redyed.Count > 0) parts.Add($"{r.Redyed.Count} in another dye");
        return parts.Count == 0 ? "Nothing has changed" : Words.Capital(Words.List(parts));
    }

    /// <summary>Names with counts, the most first: "Oak Table ×2, Bed and 3 more".</summary>
    public static string Names(IEnumerable<uint> items, Func<uint, string> name, int show = 3) =>
        Words.Some(items.GroupBy(i => i).OrderByDescending(g => g.Count()).ThenBy(g => name(g.Key), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"{name(g.Key)} ×{g.Count()}" : name(g.Key)).ToList(), show);

    /// <summary>What a clash is, after the piece's name: "is stacked on another".</summary>
    public static string Reason(ClashKind kind, int count) => (kind, count == 1) switch
    {
        (ClashKind.Stacked, true) => "sits on another of the same at the same spot",
        (ClashKind.Stacked, false) => "sit on others of the same at the same spot",
        (ClashKind.UnderFloor, true) => "is below the lowest floor",
        (ClashKind.UnderFloor, false) => "are below the lowest floor",
        (ClashKind.AboveCeiling, true) => "is above the top floor's ceiling",
        (ClashKind.AboveCeiling, false) => "are above the top floor's ceiling",
        (ClashKind.OutsideWalls, true) => "is outside the walls",
        (ClashKind.OutsideWalls, false) => "are outside the walls",
        (ClashKind.WrongSide, true) => "can't go on this side (indoor and outdoor furnishings stay on their own)",
        _ => "can't go on this side (indoor and outdoor furnishings stay on their own)",
    };

    /// <summary>The lead line for the clashes: what the game will do with them, Place anywhere on or off.</summary>
    public static string ClashLead(int pieces, bool placeAnywhere, bool wrongSideOnly)
    {
        var n = Words.Count(pieces, "piece");
        if (wrongSideOnly) return $"The game won't place {(pieces == 1 ? "1 piece" : n)} here, whatever the settings.";
        return placeAnywhere
            ? $"{Words.Capital(n)} {(pieces == 1 ? "sits" : "sit")} where the game's own check would refuse {(pieces == 1 ? "it" : "them")}. Place anywhere is on, so {(pieces == 1 ? "it's" : "they're")} placed anyway."
            : $"Place anywhere is off, so the game will likely refuse {n}:";
    }

    /// <summary>"Carry on from piece 38 of 120".</summary>
    public static string CarryOn(RunProgress p)
    {
        var (next, of) = p.NextPiece;
        return $"Carry on from piece {next} of {of}";
    }

    /// <summary>"A run of this design was cut off at piece 38 of 120 (today 21.14)."</summary>
    public static string CutOff(RunProgress p, string? when)
    {
        var (next, of) = p.NextPiece;
        return $"A run of this design was cut off at piece {next} of {of}{(when != null ? $" ({when})" : string.Empty)}, while {Phase(p.Phase)}. "
               + "Carrying on matches it against what's placed now, so nothing is done twice.";
    }

    private static string Phase(RunPhase p) => p switch
    {
        RunPhase.Move => "moving pieces",
        RunPhase.Place => "placing pieces",
        _ => "dyeing",
    };
}
