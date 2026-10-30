using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>Where a piece of the design stands in the game, for matching. Positions are in the ward or the house, in yalms.</summary>
/// <param name="Key">The design's own id for it (its index in the layout's list for its side).</param>
/// <param name="Stain">The dye it should have (Stain row), 0 when undyed.</param>
/// <param name="Late">Stands on or hangs from something else (tabletop, wall-mounted): placed after the rest.</param>
public sealed record Target(int Key, uint ItemId, Vector3 Position, float Yaw, byte Stain, Floor Floor, bool Late);

/// <summary>A piece already placed in the house or garden.</summary>
/// <param name="Key">The game's own index for it (its furniture slot), stable while you're in the room.</param>
public sealed record PlacedPiece(int Key, uint ItemId, Vector3 Position, float Yaw, byte Stain);

public enum StockSource
{
    Inventory,
    Storeroom,
}

/// <summary>Furniture waiting in a bag or the house's storeroom.</summary>
public sealed record StockSlot(StockSource Source, int Container, int Slot, uint ItemId, byte Stain, int Quantity);

public enum PieceState
{
    /// <summary>Placed, where it should be and turned the right way.</summary>
    InPlace,

    /// <summary>Placed, but somewhere else: to be moved.</summary>
    Move,

    /// <summary>In the house's storeroom: to be placed.</summary>
    FromStoreroom,

    /// <summary>In your bags: to be placed.</summary>
    FromInventory,

    /// <summary>Nowhere: to buy or craft.</summary>
    Missing,
}

public sealed class PlanEntry
{
    public required Target Target { get; init; }
    public PieceState State { get; set; }
    public PlacedPiece? Placed { get; set; }
    public StockSlot? Stock { get; set; }

    /// <summary>The dye the piece has now (placed or in stock), when that differs from the design's.</summary>
    public bool DyeDiffers => (Placed?.Stain ?? Stock?.Stain ?? Target.Stain) != Target.Stain;

    public byte CurrentStain => Placed?.Stain ?? Stock?.Stain ?? 0;
}

public sealed class Plan
{
    public List<PlanEntry> Entries { get; } = new();

    /// <summary>Placed pieces the design doesn't use (they're left where they are).</summary>
    public List<PlacedPiece> Extras { get; } = new();

    public int Count(PieceState s) => Entries.Count(e => e.State == s);
}

public sealed record MatchOptions(float PositionTolerance = 0.01f, float YawTolerance = 0.01f);

/// <summary>
/// Matches each piece of a design to something you have, in this order:
/// <list type="number">
/// <item>a piece already placed (the same item, the same dye if one is, the nearest first);</item>
/// <item>a piece in the storeroom already in the design's dye;</item>
/// <item>a piece in your bags (furniture in bags is never dyed);</item>
/// <item>any other piece in the storeroom (it'll need dyeing);</item>
/// </list>
/// and what's left is missing. Pure (no game access), so it's tested on its own.
/// </summary>
public static class Matcher
{
    /// <summary>A dye mismatch costs this much, in yalms, when pairing placed pieces: the same dye always wins first.</summary>
    private const float DyePenalty = 10_000f;

    public static Plan Build(IReadOnlyList<Target> targets, IReadOnlyList<PlacedPiece> placed, IReadOnlyList<StockSlot> stock, MatchOptions? options = null)
    {
        options ??= new MatchOptions();
        var plan = new Plan();
        var entries = targets.Select(t => new PlanEntry { Target = t, State = PieceState.Missing }).ToList();

        // 1. Placed pieces, pairing the closest (same dye first) across each item.
        var usedPlaced = new HashSet<int>();
        foreach (var group in entries.GroupBy(e => e.Target.ItemId))
        {
            var candidates = placed.Where(p => p.ItemId == group.Key).ToList();
            if (candidates.Count == 0) continue;
            var pairs = new List<(float Cost, PlanEntry Entry, PlacedPiece Piece)>();
            foreach (var e in group)
                foreach (var p in candidates)
                {
                    var cost = Vector3.Distance(e.Target.Position, p.Position)
                               + Transforms.AngleDiff(e.Target.Yaw, p.Yaw) * 0.01f
                               + (p.Stain == e.Target.Stain ? 0 : DyePenalty);
                    pairs.Add((cost, e, p));
                }
            // Deterministic: by cost, then by the design's order, then the game's.
            foreach (var (_, e, p) in pairs.OrderBy(x => x.Cost).ThenBy(x => x.Entry.Target.Key).ThenBy(x => x.Piece.Key))
            {
                if (e.Placed != null || usedPlaced.Contains(p.Key)) continue;
                e.Placed = p;
                usedPlaced.Add(p.Key);
                var close = Vector3.Distance(e.Target.Position, p.Position) <= options.PositionTolerance
                            && Transforms.AngleDiff(e.Target.Yaw, p.Yaw) <= options.YawTolerance;
                e.State = close ? PieceState.InPlace : PieceState.Move;
            }
        }

        // 2–4. Stock, in order of preference. Quantities are shared out one piece at a time.
        var left = stock.Select(s => (Slot: s, Left: Math.Max(1, s.Quantity))).ToList();
        bool Take(PlanEntry e, Func<StockSlot, bool> fits, PieceState state)
        {
            for (var i = 0; i < left.Count; i++)
            {
                var (slot, n) = left[i];
                if (n <= 0 || slot.ItemId != e.Target.ItemId || !fits(slot)) continue;
                left[i] = (slot, n - 1);
                e.Stock = slot;
                e.State = state;
                return true;
            }
            return false;
        }

        var waiting = entries.Where(e => e.Placed == null).ToList();
        foreach (var e in waiting.ToList())
            if (Take(e, s => s.Source == StockSource.Storeroom && s.Stain == e.Target.Stain, PieceState.FromStoreroom)) waiting.Remove(e);
        foreach (var e in waiting.ToList())
            if (Take(e, s => s.Source == StockSource.Inventory, PieceState.FromInventory)) waiting.Remove(e);
        foreach (var e in waiting.ToList())
            if (Take(e, s => s.Source == StockSource.Storeroom, PieceState.FromStoreroom)) waiting.Remove(e);

        plan.Entries.AddRange(entries);
        plan.Extras.AddRange(placed.Where(p => !usedPlaced.Contains(p.Key)));
        return plan;
    }

    /// <summary>
    /// The order to work in: lower floors first; pieces standing on the floor before the things
    /// that sit on them or hang on walls; then low to high, then the design's order.
    /// </summary>
    public static IEnumerable<PlanEntry> WorkOrder(IEnumerable<PlanEntry> entries) =>
        entries.OrderBy(e => e.Target.Floor)
            .ThenBy(e => e.Target.Late)
            .ThenBy(e => MathF.Round(e.Target.Position.Y, 2))
            .ThenBy(e => e.Target.Key);
}
