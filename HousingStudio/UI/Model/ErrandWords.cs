using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>The errands' screens in words: a stop, a reason, the gil. Pure, so it's tested.</summary>
public static class ErrandWords
{
    public static string Gil(long gil) => gil.ToString("N0", CultureInfo.InvariantCulture) + " gil";

    /// <summary>"Momo (retainer)", "Material Supplier, Mist (10.9, 11.4)", "The market board on Lich".</summary>
    public static string Where(ErrandStop stop) => stop.Kind switch
    {
        StopKind.Retainer => $"{stop.Retainer?.Name ?? "A retainer"} (retainer)",
        StopKind.Vendor => stop.Vendor?.Where() ?? "A vendor",
        _ => $"The market board on {stop.World}",
    };

    /// <summary>"2 × Hingan Bookshelf, Oasis Doormat" and the gil: "3,000 gil", "about 5,250 gil".</summary>
    public static string What(ErrandStop stop, Func<uint, string> name)
    {
        var items = string.Join(", ", stop.Items.Select(i => i.Count == 1 ? name(i.ItemId) : $"{i.Count} × {name(i.ItemId)}"));
        return stop.Kind switch
        {
            StopKind.Vendor => $"{items} · {Gil(stop.Gil)}",
            StopKind.Market => $"{items} · at most {Gil(stop.Gil)}, none above {Gil(stop.Items.Max(i => i.MaxUnitPrice))} each",
            _ => items,
        };
    }

    /// <summary>"Trip 2 · 14 things · 30 bag slots · 4,500 gil".</summary>
    public static string Trip(ErrandTrip trip) =>
        $"Trip {trip.Number} · {Words.Count(trip.Count, "thing")} · {Words.Count(trip.Slots, "bag slot")}" + (trip.Gil > 0 ? $" · {Gil(trip.Gil)}" : string.Empty);

    public static string Reason(LeftReason reason) => reason switch
    {
        LeftReason.Craft => "crafted: no vendor sells it, none listed",
        LeftReason.NoVendorReachable => "its vendors are where the errands don't go (another district, or an aetheryte you haven't)",
        LeftReason.OverCeiling => "past your market ceiling",
        LeftReason.NotListedHere => "not listed on the worlds the errands buy on",
        LeftReason.NoRoom => "no room in your bags",
        _ => "no way to get it is known",
    };

    /// <summary>What's left out, grouped by why: ("past your market ceiling", "2 × Oasis Doormat, …").</summary>
    public static List<(string Why, string What)> Left(IReadOnlyList<ErrandLeft> left, Func<uint, string> name) =>
        left.GroupBy(l => l.Reason)
            .OrderBy(g => g.Key)
            .Select(g => (Reason(g.Key), string.Join(", ", g.GroupBy(l => l.Need.ItemId)
                .Select(i => (Count: i.Sum(l => l.Count), Name: name(i.Key)))
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i => i.Count == 1 ? i.Name : $"{i.Count} × {i.Name}"))))
            .ToList();

    /// <summary>The confirmation's words, naming the gil.</summary>
    public static string Confirm(ErrandPlan plan)
    {
        var parts = new List<string>();
        if (plan.VendorGil > 0) parts.Add($"{Gil(plan.VendorGil)} at vendors");
        if (plan.MarketGil > 0) parts.Add($"up to {Gil(plan.MarketGil)} on the market board (your ceiling is {Gil(plan.MarketCeiling)})");
        var spend = parts.Count == 0 ? "It spends no gil." : $"It spends up to {Gil(plan.TotalGil)}: {string.Join(", and ", parts)}.";
        return $"{spend} It moves your character: {Words.Count(plan.Trips.Count, "trip")}, each ending at home to place what it got. "
               + "Stop works at any time. This is experimental: watch it, and keep the backup on.";
    }

    /// <summary>The ceilings the stepper goes through.</summary>
    public static readonly long[] Ceilings = { 0, 10_000, 25_000, 50_000, 100_000, 250_000, 500_000, 1_000_000, 2_500_000, 5_000_000, 10_000_000 };

    /// <summary>The next ceiling up or down from <paramref name="now"/>.</summary>
    public static long StepCeiling(long now, int delta)
    {
        if (delta > 0) return Ceilings.FirstOrDefault(c => c > now, Ceilings[^1]);
        if (delta < 0) return Ceilings.LastOrDefault(c => c < now, 0);
        return now;
    }
}
