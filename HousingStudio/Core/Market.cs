using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HousingStudio.Core;

/// <summary>One listing on the market board: a stack for sale at one price, on one world.</summary>
/// <param name="PricePerUnit">Gil for one, before the board's tax.</param>
/// <param name="Tax">The tax on the whole stack (Universalis says it per listing), 0 when not said.</param>
public sealed record MarketListing(uint PricePerUnit, int Quantity, string World, bool Hq, uint Tax = 0)
{
    /// <summary>What buying the whole stack costs, tax included.</summary>
    public long Total => (long)PricePerUnit * Quantity + Tax;
}

/// <summary>
/// What an item sells for on the market board of a data centre, as Universalis last heard (the
/// cheapest listings, cheapest first).
/// </summary>
/// <param name="Scope">The data centre (or world) asked about: "Light".</param>
/// <param name="FetchedAt">When this was asked (UTC).</param>
/// <param name="UnitsForSale">How many are for sale in all, beyond the listings kept, when Universalis says.</param>
public sealed record MarketQuote(uint ItemId, string Scope, DateTime FetchedAt, IReadOnlyList<MarketListing> Listings, int UnitsForSale = 0)
{
    /// <summary>The cheapest price for one, or null when nothing is listed.</summary>
    public uint? Cheapest => Listings.Count == 0 ? null : Listings.Min(l => l.PricePerUnit);

    /// <summary>
    /// What buying <paramref name="count"/> would cost: whole stacks (the board sells a listing whole),
    /// the cheapest for one first. <c>Covered</c> is how many those stacks hold (at least
    /// <paramref name="count"/> when enough is listed, more when the last stack is bigger than needed);
    /// <c>Short</c> how many are still wanted when the listings run out. Pure: tested.
    /// </summary>
    public MarketCost CostFor(int count)
    {
        if (count <= 0) return new MarketCost(0, 0, 0, Array.Empty<MarketListing>());
        var sorted = Listings.Where(l => l.Quantity > 0).OrderBy(l => l.PricePerUnit).ThenBy(l => l.Quantity).ToList();
        // The cheapest of: the first k stacks cheapest-each, then the one stack (of the rest) that covers
        // what's left for the least; or every stack cheapest-each until there are enough.
        MarketCost? best = null;
        long gil = 0;
        var covered = 0;
        for (var k = 0; k <= sorted.Count; k++)
        {
            var left = count - covered;
            if (left <= 0)
            {
                var all = new MarketCost(gil, covered, 0, sorted.Take(k).ToList());
                if (best == null || all.Gil < best.Gil) best = all;
                break;
            }
            MarketListing? finish = null;
            for (var j = k; j < sorted.Count; j++)
                if (sorted[j].Quantity >= left && (finish == null || sorted[j].Total < finish.Total)) finish = sorted[j];
            if (finish != null && (best == null || gil + finish.Total < best.Gil))
                best = new MarketCost(gil + finish.Total, covered + finish.Quantity, 0, sorted.Take(k).Append(finish).ToList());
            if (k == sorted.Count) break;
            gil += sorted[k].Total;
            covered += sorted[k].Quantity;
        }
        // Not enough listed: all of it, and how many are still wanted.
        return best ?? new MarketCost(gil, covered, Math.Max(0, count - covered), sorted);
    }
}

/// <summary>The estimate for buying a number of an item on the board.</summary>
/// <param name="Short">How many the listings can't cover.</param>
public sealed record MarketCost(long Gil, int Covered, int Short, IReadOnlyList<MarketListing> Listings)
{
    public bool Enough => Short == 0 && Covered > 0;
}

/// <summary>Reading Universalis's answers (https://docs.universalis.app). Pure, so it's tested.</summary>
public static class Universalis
{
    public const string Api = "https://universalis.app/api/v2/";

    /// <summary>How many items one request asks about (the API's limit).</summary>
    public const int MaxIdsPerRequest = 100;

    /// <summary>
    /// The request for some items' cheapest listings on a data centre (or world):
    /// <c>/api/v2/Light/5333,5334?listings=20&amp;entries=0</c>. The ids are sorted, so the same set
    /// asks the same thing.
    /// </summary>
    public static string Url(string scope, IEnumerable<uint> ids, int listings = 20)
    {
        var list = string.Join(",", ids.Distinct().OrderBy(i => i).Select(i => i.ToString(CultureInfo.InvariantCulture)));
        return $"{Api}{Uri.EscapeDataString(scope)}/{list}?listings={listings}&entries=0";
    }

    /// <summary>Ids in batches the API takes.</summary>
    public static IEnumerable<List<uint>> Batches(IEnumerable<uint> ids, int size = MaxIdsPerRequest) =>
        ids.Distinct().OrderBy(i => i).Select((id, n) => (id, n)).GroupBy(x => x.n / size).Select(g => g.Select(x => x.id).ToList());

    /// <summary>
    /// Reads an answer: for several ids, <c>{"items": {"5333": {...}}, "unresolvedItems": [...]}</c>;
    /// for one, the item itself (<c>{"itemID": 5333, "listings": [...]}</c>). Each item's
    /// <c>listings</c> carry <c>pricePerUnit</c>, <c>quantity</c>, <c>worldName</c>, <c>hq</c> and
    /// <c>tax</c>. Anything unreadable is left out (an answer that isn't JSON gives none).
    /// </summary>
    public static Dictionary<uint, MarketQuote> Parse(string json, string scope, DateTime fetchedAt)
    {
        var quotes = new Dictionary<uint, MarketQuote>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return quotes;
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in items.EnumerateObject())
                    if (uint.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && Item(p.Value, id, scope, fetchedAt) is { } q)
                        quotes[id] = q;
            }
            else if (root.TryGetProperty("itemID", out var idEl) && idEl.TryGetUInt32(out var id) && Item(root, id, scope, fetchedAt) is { } q)
            {
                quotes[id] = q;
            }
        }
        catch (JsonException)
        {
        }
        return quotes;
    }

    private static MarketQuote? Item(JsonElement e, uint id, string scope, DateTime fetchedAt)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var listings = new List<MarketListing>();
        if (e.TryGetProperty("listings", out var ls) && ls.ValueKind == JsonValueKind.Array)
            foreach (var l in ls.EnumerateArray())
            {
                if (l.ValueKind != JsonValueKind.Object) continue;
                var price = UInt(l, "pricePerUnit");
                var qty = (int)Math.Min(int.MaxValue, UInt(l, "quantity"));
                if (price == 0 || qty <= 0) continue;
                var world = l.TryGetProperty("worldName", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() ?? string.Empty : string.Empty;
                var hq = l.TryGetProperty("hq", out var h) && h.ValueKind == JsonValueKind.True;
                listings.Add(new MarketListing(price, qty, world, hq, UInt(l, "tax")));
            }
        listings.Sort((a, b) => a.PricePerUnit.CompareTo(b.PricePerUnit));
        var units = (int)Math.Min(int.MaxValue, UInt(e, "unitsForSale"));
        return new MarketQuote(id, scope, fetchedAt, listings, units);
    }

    private static uint UInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return 0;
        if (v.TryGetUInt32(out var u)) return u;
        return v.TryGetDouble(out var d) && d > 0 ? (uint)Math.Min(uint.MaxValue, Math.Round(d)) : 0;
    }
}
