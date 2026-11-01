using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>One place you buy at: an NPC where they stand, and what's got from them there.</summary>
/// <param name="Vendor">The NPC (one whose spot is known, when any of the lines has one): its name, place and map flag.</param>
public sealed record ShopPlace(VendorOffer Vendor, IReadOnlyList<ShoppingLine> Lines)
{
    /// <summary>How many things (counting each one) come from here.</summary>
    public int Things => Lines.Sum(l => l.Count);
}

/// <summary>
/// A section of the Shopping list: its label ("From shops"), what's beside it ("149,000 gil · 30"),
/// its lines sorted by name, and for the shops (gil and currency) the same lines by place.
/// </summary>
/// <param name="Priced">The aside carries a price (drawn in brass).</param>
public sealed record ShopSection(SourceKind Source, string Title, string Aside, bool Priced, IReadOnlyList<ShoppingLine> Lines, IReadOnlyList<ShopPlace> Places);

/// <summary>
/// The Shopping list's and the Pieces footer's words (0.9.0): "102 to get", "about 2,098,460 gil in
/// all", the sections by where things come from, the shops grouped by place, and each row's words.
/// Gil is said one way everywhere: "about" for an estimate (the market board), "≈" before an estimated
/// row's total, a plain number for a fixed shop price. Pure, so it's tested.
/// </summary>
public static class ShoppingScreen
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The sections in their order: free first, then fixed prices, then estimates, then the rest.</summary>
    public static readonly (SourceKind Source, string Title)[] Order =
    {
        (SourceKind.Retainer, "On your retainers"),
        (SourceKind.Vendor, "From shops"),
        (SourceKind.Exchange, "For MGP and other currencies"),
        (SourceKind.Market, "On the market board"),
        (SourceKind.Craft, "Craft"),
        (SourceKind.Unknown, "Not known"),
    };

    /// <summary>"12,400".</summary>
    public static string Number(long n) => n.ToString("N0", Invariant);

    /// <summary>Everything to get, counting each one (pieces, fixtures and dyes): Ready's "102 to get".</summary>
    public static int Things(ShoppingPlan shop) => shop.Lines.Sum(l => l.Count);

    /// <summary>The Shopping list's title: "102 to get", or "Nothing to get".</summary>
    public static string Title(ShoppingPlan shop) => Things(shop) is > 0 and var n ? $"{n} to get" : "Nothing to get";

    /// <summary>What's to get, by kind (each one counted): the pieces, the fixtures and the dyes.</summary>
    public static (int Pieces, int Fixtures, int Dyes) Counts(ShoppingPlan shop)
    {
        var (pieces, fixtures, dyes) = (0, 0, 0);
        foreach (var l in shop.Lines)
            switch (l.Need.Kind)
            {
                case NeedKind.Fixture:
                    fixtures += l.Count;
                    break;
                case NeedKind.Dye:
                    dyes += l.Count;
                    break;
                default:
                    pieces += l.Count;
                    break;
            }
        return (pieces, fixtures, dyes);
    }

    /// <summary>
    /// The line under the Shopping list's title: "about 2,098,460 gil in all" (an estimate when any of it
    /// is the market board's), "149,000 gil in all" (shop prices only), with the currencies after
    /// ("… and 45,000 MGP in all"); null when nothing has a price.
    /// </summary>
    public static string? Total(ShoppingPlan shop)
    {
        var parts = new List<string>();
        if (shop.TotalGil > 0) parts.Add((shop.MarketGil > 0 ? "about " : string.Empty) + ShoppingList.Gil(shop.TotalGil));
        foreach (var (currency, cost) in shop.CurrencyCosts)
            if (cost > 0) parts.Add($"{Number(cost)} {currency}");
        return parts.Count == 0 ? null : Words.List(parts) + " in all";
    }

    /// <summary>
    /// The Pieces footer's line: "99 pieces and 3 fixtures to get · no dyes to buy" (the same things as
    /// Ready's "102 to get", the dyes said apart), or "Nothing to get".
    /// </summary>
    public static string PiecesFoot(int pieces, int fixtures, int dyes)
    {
        if (pieces + fixtures + dyes == 0) return "Nothing to get";
        var get = (pieces, fixtures) switch
        {
            (0, 0) => "No pieces to get",
            (_, 0) => $"{Words.Count(pieces, "piece")} to get",
            (0, _) => $"{Words.Count(fixtures, "fixture")} to get",
            _ => $"{Words.Count(pieces, "piece")} and {Words.Count(fixtures, "fixture")} to get",
        };
        return get + " · " + (dyes == 0 ? "no dyes to buy" : $"{Words.Count(dyes, "dye")} to buy");
    }

    /// <summary>The sections to draw, in <see cref="Order"/>, each sorted by name; the empty ones left out.</summary>
    public static List<ShopSection> Sections(ShoppingPlan shop, Func<uint, string> itemName)
    {
        var sections = new List<ShopSection>();
        foreach (var (source, title) in Order)
        {
            var lines = shop.Of(source).OrderBy(l => itemName(l.ItemId), StringComparer.OrdinalIgnoreCase).ToList();
            if (lines.Count == 0) continue;
            var n = lines.Sum(l => l.Count);
            var gil = lines.Sum(l => l.Gil);
            var money = source switch
            {
                SourceKind.Vendor when gil > 0 => ShoppingList.Gil(gil),
                SourceKind.Exchange => CurrencyWords(lines),
                SourceKind.Market when gil > 0 => "about " + ShoppingList.Gil(gil),
                _ => null,
            };
            // By place only when every line has its NPC (a shop's always does).
            var places = source is SourceKind.Vendor or SourceKind.Exchange ? Places(lines) : new List<ShopPlace>();
            if (places.Sum(p => p.Lines.Count) != lines.Count) places = new List<ShopPlace>();
            sections.Add(new ShopSection(source, title, money != null ? $"{money} · {n}" : n.ToString(Invariant), money != null, lines, places));
        }
        return sections;
    }

    /// <summary>"45,000 MGP + 300 Wolf Marks", or null when there's none.</summary>
    private static string? CurrencyWords(IEnumerable<ShoppingLine> lines)
    {
        var costs = lines.Where(l => l.Exchange != null && l.CurrencyCost > 0).GroupBy(l => l.Exchange!.CurrencyName)
            .Select(g => $"{Number(g.Sum(l => l.CurrencyCost))} {g.Key}").ToList();
        return costs.Count == 0 ? null : string.Join(" + ", costs);
    }

    /// <summary>The NPC a line is bought from (a gil shop's or a currency vendor's), or null.</summary>
    public static VendorOffer? VendorOf(ShoppingLine line) => line.Vendor ?? line.Exchange?.Vendor;

    /// <summary>
    /// Lines by the place you buy at: one per NPC where they stand (the same NPC's shops together), the
    /// places whose spot is known first, then by place and name. The lines keep their order.
    /// </summary>
    public static List<ShopPlace> Places(IEnumerable<ShoppingLine> lines) =>
        lines.Where(l => VendorOf(l) != null)
            .GroupBy(l =>
            {
                var v = VendorOf(l)!;
                return (v.NpcId, v.TerritoryId, v.NpcName, v.PlaceName);
            })
            .Select(g =>
            {
                var list = g.ToList();
                var vendor = list.Select(l => VendorOf(l)!).FirstOrDefault(v => v.Located) ?? VendorOf(list[0])!;
                return new ShopPlace(vendor, list);
            })
            .OrderBy(p => p.Vendor.Located ? 0 : 1)
            .ThenBy(p => p.Vendor.PlaceName.Length == 0 ? 1 : 0)
            .ThenBy(p => p.Vendor.PlaceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Vendor.NpcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Vendor.NpcId)
            .ToList();

    /// <summary>
    /// The ward to name for a vendor (1 on), when it stands in a residential district and that's the
    /// district of the home you're in (<paramref name="homeWard"/> is that home's ward, 1 on); else null.
    /// </summary>
    public static int? WardFor(VendorOffer v, string? homeDistrict, int? homeWard) =>
        v.District != null && homeDistrict != null && v.District == homeDistrict && homeWard is > 0 ? homeWard : null;

    /// <summary>
    /// A place's second line: "The Lavender Beds · ward 1 · 8 things"; without a ward, where on the map
    /// ("Ul'dah - Steps of Thal · (10.9, 11.4) · 2 things").
    /// </summary>
    public static string PlaceLine(VendorOffer v, int things, int? ward)
    {
        var parts = new List<string> { v.PlaceName.Length > 0 ? v.PlaceName : "Somewhere not known" };
        if (ward is > 0) parts.Add($"ward {ward.Value.ToString(Invariant)}");
        else if (v.MapPosition is { } m) parts.Add(string.Format(Invariant, "({0:0.0}, {1:0.0})", m.X, m.Y));
        parts.Add(Words.Count(things, "thing"));
        return string.Join(" · ", parts);
    }

    /// <summary>"Fixture", "Dye", or empty for a piece.</summary>
    public static string KindWord(NeedKind kind) => kind switch
    {
        NeedKind.Fixture => "Fixture",
        NeedKind.Dye => "Dye",
        _ => string.Empty,
    };

    /// <summary>
    /// A shop row's second line under its place, or null when there's nothing to say (a piece at a
    /// plain price): "Fixture", "Dye", "after a quest", and for a currency "300 MGP for 3".
    /// </summary>
    public static string? PlaceRowNote(ShoppingLine line)
    {
        var parts = new List<string>();
        if (KindWord(line.Need.Kind) is { Length: > 0 } kind) parts.Add(kind);
        if (line.Exchange is { Gives: > 1 } x) parts.Add($"{Number(x.Cost)} {x.CurrencyName} for {x.Gives.ToString(Invariant)}");
        if (VendorOf(line)?.NeedsUnlock == true) parts.Add("after a quest");
        return parts.Count == 0 ? null : Words.Capital(string.Join(" · ", parts));
    }

    /// <summary>A market row's second line: "about 50,400 each · Light" (with "Fixture · " or "Dye · " first, and whole stacks said).</summary>
    public static string MarketNote(ShoppingLine line)
    {
        var each = line.Count > 0 ? line.Gil / line.Count : 0;
        var kind = KindWord(line.Need.Kind);
        var scope = line.Market?.Scope is { Length: > 0 } s ? " · " + s : string.Empty;
        var extra = line.MarketCost is { } cost && cost.Covered > line.Count ? $" · whole stacks: {cost.Covered.ToString(Invariant)} bought" : string.Empty;
        return (kind.Length > 0 ? kind + " · " : string.Empty) + $"about {Number(each)} each" + scope + extra;
    }

    /// <summary>
    /// A row's right side: "free" from a retainer, a shop's fixed price ("30,000"), the board's estimate
    /// ("≈ 100,800"), a currency's cost ("45,000 MGP"); empty otherwise.
    /// </summary>
    public static string RowPrice(ShoppingLine line) => line.Source switch
    {
        SourceKind.Retainer => "free",
        SourceKind.Vendor => Number(line.Gil),
        SourceKind.Market => "≈ " + Number(line.Gil),
        SourceKind.Exchange when line.Exchange != null => $"{Number(line.CurrencyCost)} {line.Exchange.CurrencyName}",
        _ => string.Empty,
    };
}
