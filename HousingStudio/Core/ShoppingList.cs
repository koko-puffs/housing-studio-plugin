using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HousingStudio.Core;

/// <summary>
/// The plain-text list of what's still missing, for pasting anywhere (the site's
/// <c>shoppingList</c> reads the same way: "3 × Oak Table (Snow White ×2, undyed ×1)").
/// </summary>
public static class ShoppingList
{
    public static string Build(Plan plan, Func<uint, string> itemName, Func<byte, string> dyeName, string? title = null, Func<byte, int>? dyesOwned = null)
    {
        var missing = plan.Entries.Where(e => e.State == PieceState.Missing).ToList();
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title)) sb.Append(title).AppendLine(": shopping list").AppendLine();
        if (missing.Count == 0)
        {
            sb.AppendLine("Nothing missing: you have every piece.");
            AppendDyesToBuy(sb, plan, dyeName, dyesOwned);
            return sb.ToString().TrimEnd();
        }
        var groups = missing.GroupBy(e => e.Target.ItemId)
            .Select(g => (Id: g.Key, Name: itemName(g.Key), Count: g.Count(), Dyes: g.Where(e => e.Target.Stain != 0).GroupBy(e => e.Target.Stain).Select(d => (d.Key, d.Count())).OrderByDescending(d => d.Item2).ToList()))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);
        sb.AppendLine($"Missing ({missing.Count} {(missing.Count == 1 ? "piece" : "pieces")}, {missing.Select(e => e.Target.ItemId).Distinct().Count()} different)");
        foreach (var g in groups)
        {
            var parts = g.Dyes.Select(d => $"{dyeName(d.Key)} ×{d.Item2}").ToList();
            var undyed = g.Count - g.Dyes.Sum(d => d.Item2);
            if (parts.Count > 0 && undyed > 0) parts.Add($"undyed ×{undyed}");
            sb.Append(g.Count).Append(" × ").Append(g.Name);
            if (parts.Count > 0) sb.Append(" (").Append(string.Join(", ", parts)).Append(')');
            sb.AppendLine();
        }
        // Dyes for everything that'll need dyeing once it's in (missing pieces included).
        var dyeing = plan.Entries.Where(e => e.Target.Stain != 0 && (e.State == PieceState.Missing || e.DyeDiffers || e.State == PieceState.FromInventory))
            .GroupBy(e => e.Target.Stain).OrderByDescending(g => g.Count()).ToList();
        if (dyeing.Count > 0)
        {
            sb.AppendLine().AppendLine("Dyes to apply");
            foreach (var d in dyeing) sb.Append(d.Count()).Append(" × ").AppendLine(dyeName(d.Key));
        }
        AppendDyesToBuy(sb, plan, dyeName, dyesOwned);
        return sb.ToString().TrimEnd();
    }

    /// <summary>The dyes to buy: what the design needs less what's in your bags (when known).</summary>
    private static void AppendDyesToBuy(StringBuilder sb, Plan plan, Func<byte, string> dyeName, Func<byte, int>? dyesOwned)
    {
        if (dyesOwned == null) return;
        var stains = plan.Entries.Select(e => e.Target.Stain).Where(s => s != 0).Distinct();
        var owned = stains.ToDictionary(s => s, dyesOwned);
        var buy = DyePlanner.ToBuy(plan, owned);
        if (buy.Count == 0) return;
        sb.AppendLine().AppendLine("Dyes to buy (beyond what's in your bags)");
        foreach (var (stain, count) in buy) sb.Append(count).Append(" × ").AppendLine(dyeName(stain));
    }

    /// <summary>
    /// The list by where each thing comes from (<see cref="ShoppingPlanner"/>): what to take from your
    /// retainers, what to buy from which NPC where and for how much, the market board's estimate, what to
    /// craft and what's not known; then the dyes to apply and the total.
    /// </summary>
    /// <param name="seen">"today 05.24": when a retainer was seen (null when not known).</param>
    public static string WithSources(Plan plan, ShoppingPlan shop, Func<uint, string> itemName, Func<byte, string> dyeName, Func<DateTime?, string?> seen, string? title = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title)) sb.Append(title).AppendLine(": shopping list").AppendLine();
        if (shop.IsEmpty)
        {
            sb.AppendLine("Nothing to get: you have every piece and the dyes for them.");
            return sb.ToString().TrimEnd();
        }
        // A piece's dyes are said once, on its first line (they're for all its copies, wherever they come from).
        var described = new HashSet<uint>();
        string Name(ShoppingLine l)
        {
            var name = itemName(l.ItemId);
            if (l.Need.Kind == NeedKind.Piece && described.Add(l.ItemId))
            {
                var dyes = DyesOf(plan, l.ItemId, dyeName);
                if (dyes.Length > 0) name += $" ({dyes})";
            }
            return $"{l.Count} × {name}";
        }
        void Section(SourceKind source, string heading, Func<ShoppingLine, string?> detail)
        {
            var lines = shop.Of(source).ToList();
            if (lines.Count == 0) return;
            sb.AppendLine(heading);
            foreach (var l in lines.OrderBy(l => itemName(l.ItemId), StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(Name(l));
                if (detail(l) is { Length: > 0 } d) sb.Append(": ").Append(d);
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        Section(SourceKind.Retainer, "On your retainers", l =>
        {
            var when = seen(l.Retainer?.SeenAt);
            return $"on {l.Retainer?.Name ?? "a retainer"}" + (when != null ? $" (seen {when})" : string.Empty);
        });
        Section(SourceKind.Vendor, $"Buy from vendors ({Gil(shop.VendorGil)})", l => l.Vendor is { } v
            ? $"{Gil(v.Price)} each, {Gil(l.Gil)}, from {v.Where()}"
            : null);
        Section(SourceKind.Exchange, "For MGP and other currencies (what you have, not gil)", l => l.Exchange is { } x
            ? $"{l.CurrencyCost:N0} {x.CurrencyName}, from {x.Vendor.Where()}"
            : null);
        Section(SourceKind.Market, $"Market board (about {Gil(shop.MarketGil)})", l =>
        {
            var each = l.Count > 0 ? l.Gil / l.Count : 0;
            var scope = l.Market?.Scope is { Length: > 0 } sc ? $" on {sc}" : string.Empty;
            return $"about {Gil(l.Gil)}{scope} (about {Gil(each)} each)";
        });
        Section(SourceKind.Craft, "Craft (no vendor sells it, none listed)", l => l.Craft is { } c
            ? c.Level > 0 ? $"{c.Job}, level {c.Level}" : c.Job
            : null);
        Section(SourceKind.Unknown, "Not sold by vendors, none listed", _ => null);

        var dyeing = plan.Entries.Where(e => e.Target.Stain != 0 && (e.State == PieceState.Missing || e.DyeDiffers || e.State == PieceState.FromInventory))
            .GroupBy(e => e.Target.Stain).OrderByDescending(g => g.Count()).ToList();
        if (dyeing.Count > 0)
        {
            sb.AppendLine("Dyes to apply");
            foreach (var d in dyeing) sb.Append(d.Count()).Append(" × ").AppendLine(dyeName(d.Key));
            sb.AppendLine();
        }
        if (shop.TotalGil > 0) sb.Append("In all: ").AppendLine(TotalWords(shop));
        return sb.ToString().TrimEnd();
    }

    /// <summary>"about 98,400 gil (12,000 gil at vendors, about 86,400 gil on the market board)".</summary>
    public static string TotalWords(ShoppingPlan shop)
    {
        var (v, m) = (shop.VendorGil, shop.MarketGil);
        if (m == 0) return $"{Gil(v)} at vendors";
        if (v == 0) return $"about {Gil(m)} on the market board";
        return $"about {Gil(v + m)} ({Gil(v)} at vendors, about {Gil(m)} on the market board)";
    }

    /// <summary>"12,400 gil".</summary>
    public static string Gil(long gil) => gil.ToString("N0", CultureInfo.InvariantCulture) + " gil";

    /// <summary>A missing piece's dyes: "Snow White ×2, undyed ×1", or empty when all undyed.</summary>
    private static string DyesOf(Plan plan, uint itemId, Func<byte, string> dyeName)
    {
        var missing = plan.Entries.Where(e => e.State == PieceState.Missing && e.Target.ItemId == itemId).ToList();
        var dyes = missing.Where(e => e.Target.Stain != 0).GroupBy(e => e.Target.Stain).OrderByDescending(d => d.Count()).ToList();
        if (dyes.Count == 0) return string.Empty;
        var parts = dyes.Select(d => $"{dyeName(d.Key)} ×{d.Count()}").ToList();
        var undyed = missing.Count - dyes.Sum(d => d.Count());
        if (undyed > 0) parts.Add($"undyed ×{undyed}");
        return string.Join(", ", parts);
    }
}
