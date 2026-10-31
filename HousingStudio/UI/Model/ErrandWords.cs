using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>One line of a trip's route: where, what's got there, and the gil.</summary>
public sealed record RouteLeg(ErrandLeg Kind, string Name, string What, string Gil);

/// <summary>A trip on the Get screen: its label ("TRIP 1", drawn as given), the words on the right, its legs.</summary>
public sealed record RouteTrip(string Label, string Rest, IReadOnlyList<RouteLeg> Legs);

/// <summary>A reason things aren't got this way: "1 to craft", the things, why in a sentence, and the items (for Teamcraft).</summary>
public sealed record LeftRow(LeftReason Reason, string Title, string What, string Why, IReadOnlyList<(uint ItemId, int Count)> Items);

/// <summary>The errands' screens in words: a stop, a reason, the gil. Pure, so it's tested.</summary>
public static class ErrandWords
{
    public static string Gil(long gil) => gil.ToString("N0", CultureInfo.InvariantCulture) + " gil";

    public static string Reason(LeftReason reason) => reason switch
    {
        LeftReason.Craft => "crafted: no vendor sells it, none listed",
        LeftReason.NoVendorReachable => "its vendors are where the errands don't go (another district, or an aetheryte you haven't)",
        LeftReason.OverCeiling => "past your market ceiling",
        LeftReason.NotListedHere => "not listed on the worlds the errands buy on",
        LeftReason.NoRoom => "no room in your bags",
        _ => "no way to get it is known",
    };

    // ---------------------------------------------------------------------------------------------
    // The Get screen (the 0.6 flow's "Get, before setting out").

    /// <summary>"8,400": gil in mono on a route leg, without the word.</summary>
    public static string Number(long gil) => gil.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"12 things," and "2 trips": the title's plain and italic parts; nothing to fetch says so.</summary>
    public static (string Plain, string? Italic) Title(ErrandPlan plan) =>
        plan.IsEmpty ? ("Nothing to fetch", null) : ($"{Words.Count(plan.Count, "thing")},", Words.Count(plan.Trips.Count, "trip"));

    /// <summary>The brass button, naming the most it spends: "Set out · up to 61,400 gil", or "Set out" when it spends none.</summary>
    public static string SetOut(long totalGil) => totalGil > 0 ? $"Set out · up to {Gil(totalGil)}" : "Set out";

    /// <summary>"Glade Shelf ×2, Oasis Rug": what a stop gets.</summary>
    public static string Items(IEnumerable<ErrandItem> items, Func<uint, string> name) =>
        string.Join(", ", items.Select(i => i.Count == 1 ? name(i.ItemId) : $"{name(i.ItemId)} ×{i.Count}"));

    /// <summary>"11 pieces go in", "11 pieces go in · 2 dyes", "2 dyes for the pieces here".</summary>
    public static string GoIn(ErrandTrip trip)
    {
        var pieces = trip.Stops.SelectMany(s => s.Items).Where(i => i.Need.Kind == NeedKind.Piece).Sum(i => i.Count);
        var dyes = trip.Stops.SelectMany(s => s.Items).Where(i => i.Need.Kind == NeedKind.Dye).Sum(i => i.Count);
        if (pieces == 0) return dyes > 0 ? $"{Words.Count(dyes, "dye")} for the pieces here" : "What's in your bags goes in";
        var words = $"{Words.Count(pieces, "piece")} go{(pieces == 1 ? "es" : string.Empty)} in";
        return dyes > 0 ? $"{words} · {Words.Count(dyes, "dye")}" : words;
    }

    /// <summary>
    /// A trip as its legs, one line each: the bell (every retainer of the trip at one bell), each
    /// vendor, each world's market board, then home to place. Its words: where ("Bell in your house",
    /// "Mist · Material Supplier", "Limsa · Market board", with the world when it isn't yours), what's
    /// got there, and the gil ("free", "8,400", "≤ 53,000").
    /// </summary>
    /// <param name="houseBell">A summoning bell in the home you're in (else the market city's are used).</param>
    /// <param name="city">The market city's short name ("Limsa", "Ul'dah").</param>
    public static List<RouteLeg> Legs(ErrandTrip trip, Func<uint, string> name, bool houseBell, string homeWorld, string city = "Limsa")
    {
        var legs = new List<RouteLeg>();
        var retainers = trip.Stops.Where(s => s.Kind == StopKind.Retainer).ToList();
        if (retainers.Count > 0)
        {
            var what = string.Join("; ", retainers.Select(r => $"{r.Retainer?.Name ?? "A retainer"} · {Items(r.Items, name)}"));
            legs.Add(new RouteLeg(ErrandLeg.Bell, houseBell ? "Bell in your house" : $"{city} · Summoning bell", what, "free"));
        }
        foreach (var stop in trip.Stops)
        {
            switch (stop.Kind)
            {
                case StopKind.Vendor:
                    var v = stop.Vendor;
                    var where = v == null ? "A vendor" : v.PlaceName.Length > 0 ? $"{v.PlaceName} · {v.NpcName}" : v.NpcName;
                    legs.Add(new RouteLeg(ErrandLeg.Vendor, where, Items(stop.Items, name), Number(stop.Gil)));
                    break;
                case StopKind.Market:
                    var world = !string.IsNullOrEmpty(stop.World) && stop.World != homeWorld ? $" · {stop.World}" : string.Empty;
                    legs.Add(new RouteLeg(ErrandLeg.Market, $"{city} · Market board" + world, Items(stop.Items, name), "≤ " + Number(stop.Gil)));
                    break;
            }
        }
        legs.Add(new RouteLeg(ErrandLeg.Home, "Home, and place", GoIn(trip), string.Empty));
        return legs;
    }

    /// <summary>A trip's label's right side: "28 of 32 bag slots"; a later one is worked out again after the one before.</summary>
    public static string TripRest(ErrandTrip trip, int freeSlots) =>
        trip.Number <= 1 ? $"{trip.Slots} of {Words.Count(freeSlots, "bag slot")}" : $"worked out again after trip {trip.Number - 1}";

    /// <summary>The trips as the Get screen lists them.</summary>
    public static List<RouteTrip> Route(ErrandPlan plan, Func<uint, string> name, bool houseBell, string homeWorld, string city = "Limsa") =>
        plan.Trips.Select(t => new RouteTrip($"TRIP {t.Number}", TripRest(t, plan.FreeSlots), Legs(t, name, houseBell, homeWorld, city))).ToList();

    /// <summary>The market city's short name for the route ("Limsa", "Ul'dah"), by Settings → Getting → Market city.</summary>
    public static string City(int choice) => choice == 1 ? "Ul'dah" : "Limsa";

    /// <summary>What's left out, one row per reason: ("1 to craft", "Oasis Lamp"), ("2 past the market ceiling", …).</summary>
    public static List<LeftRow> LeftRows(IReadOnlyList<ErrandLeft> left, Func<uint, string> name) =>
        left.GroupBy(l => l.Reason)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var n = g.Sum(l => l.Count);
                var title = g.Key switch
                {
                    LeftReason.Craft => $"{n} to craft",
                    LeftReason.NoVendorReachable => $"{n} from vendors it doesn't go to",
                    LeftReason.OverCeiling => $"{n} past the market ceiling",
                    LeftReason.NotListedHere => $"{n} not listed where it buys",
                    LeftReason.NoRoom => $"{n} with no room in your bags",
                    _ => $"{n} not known where from",
                };
                var what = string.Join(", ", g.GroupBy(l => l.Need.ItemId)
                    .Select(i => (Count: i.Sum(l => l.Count), Name: name(i.Key)))
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => i.Count == 1 ? i.Name : $"{i.Name} ×{i.Count}"));
                return new LeftRow(g.Key, title, what, Words.Capital(Reason(g.Key)) + ".",
                    g.GroupBy(l => l.Need.ItemId).Select(i => (i.Key, i.Sum(l => l.Count))).ToList());
            })
            .ToList();

    /// <summary>The Limits row: "Market up to 100,000 · home world only · 2 slots spare".</summary>
    public static string Limits(long ceiling, bool otherWorlds, int spare) =>
        $"{(ceiling > 0 ? $"Market up to {Number(ceiling)}" : "Nothing from the market")} · {(otherWorlds ? "other worlds too" : "home world only")} · {Words.Count(spare, "slot")} spare";

    /// <summary>What the errands' first placing does with what's here, as Ready chose: "Then clears out 14 before placing", "Then swaps, keeping what's here as “X”".</summary>
    public static string? Then(ReadyMode mode, int clearOut, string? keepAs) => mode switch
    {
        ReadyMode.Swap when keepAs != null => $"Then swaps, keeping what's here as “{keepAs}”",
        ReadyMode.ClearOut or ReadyMode.Swap when clearOut > 0 => $"Then clears out {clearOut} before placing",
        _ => null,
    };

    /// <summary>"Already here: 312 placed, 4 in your storeroom, 2 in your bags · 3 on your retainers, fetched first".</summary>
    public static string Have(int placed, int storeroom, int bags, int onRetainers)
    {
        var words = $"Already here: {placed} placed, {storeroom} in your storeroom, {bags} in your bags";
        return onRetainers > 0 ? $"{words} · {onRetainers} on your retainers, fetched first" : words;
    }

    // ---------------------------------------------------------------------------------------------
    // Running: the journey, where it is in words, what it's doing.

    /// <summary>The journey bar's stops for a trip: Bell, Vendors, Market (those it has), then Home and Place always.</summary>
    public static List<ErrandLeg> Journey(ErrandTrip? trip)
    {
        var stops = new List<ErrandLeg>();
        if (trip != null)
        {
            if (trip.Stops.Any(s => s.Kind == StopKind.Retainer)) stops.Add(ErrandLeg.Bell);
            if (trip.Stops.Any(s => s.Kind == StopKind.Vendor)) stops.Add(ErrandLeg.Vendor);
            if (trip.Stops.Any(s => s.Kind == StopKind.Market)) stops.Add(ErrandLeg.Market);
        }
        stops.Add(ErrandLeg.Home);
        stops.Add(ErrandLeg.Place);
        return stops;
    }

    /// <summary>The stop in hand: the leg's own, else the first after where it would be (a leg the trip hasn't is passed).</summary>
    public static int JourneyAt(IReadOnlyList<ErrandLeg> stops, ErrandLeg leg)
    {
        for (var i = 0; i < stops.Count; i++)
            if (stops[i] >= leg) return i;
        return stops.Count - 1;
    }

    /// <summary>The journey's words under each stop.</summary>
    public static string JourneyLabel(ErrandLeg leg) => leg switch
    {
        ErrandLeg.Bell => "Bell",
        ErrandLeg.Vendor => "Vendors",
        ErrandLeg.Market => "Market",
        ErrandLeg.Home => "Home",
        _ => "Place",
    };

    /// <summary>
    /// Where it is, in plain words, as the title's plain and italic parts: "At the" "summoning bell",
    /// "On the way to" "Material Supplier", "At the" "market board", "On the way" "home", "Placing"
    /// "what it got".
    /// </summary>
    /// <param name="going">On the way there (else there).</param>
    /// <param name="who">The vendor's name, at a vendor.</param>
    /// <param name="replanning">After placing: working out what's still missing.</param>
    public static (string Plain, string Italic) Where(ErrandLeg leg, bool going, string? who, bool replanning = false) => leg switch
    {
        ErrandLeg.Bell => going ? ("On the way to a", "summoning bell") : ("At the", "summoning bell"),
        ErrandLeg.Vendor => going ? ("On the way to", who ?? "a vendor") : ("At", who ?? "the vendor"),
        ErrandLeg.Market => going ? ("On the way to the", "market board") : ("At the", "market board"),
        ErrandLeg.Home => going ? ("On the way", "home") : ("Going", "inside"),
        _ => replanning ? ("Seeing what's", "still missing") : ("Placing", "what it got"),
    };

    /// <summary>"Buying Riviera Table ×2", "Taking Oasis Rug from Momo"; null when it isn't getting a thing (the segment's words do).</summary>
    public static string? Doing(ErrandLeg leg, ErrandItem? item, string itemName, string? who)
    {
        if (item == null) return null;
        var thing = item.Count == 1 ? itemName : $"{itemName} ×{item.Count}";
        return leg switch
        {
            ErrandLeg.Bell => who != null ? $"Taking {thing} from {who}" : $"Taking {thing}",
            ErrandLeg.Vendor or ErrandLeg.Market => $"Buying {thing}",
            _ => null,
        };
    }

    /// <summary>
    /// The dim line under it: the price ("2,000 gil each", "none above 21,000 gil each"), and when the
    /// game's yes/no is in hand, what's done with it ("the game asked, and the price matched").
    /// </summary>
    /// <param name="answering">The step in hand answers the game's yes/no.</param>
    public static string? DoingNote(ErrandLeg leg, ErrandItem? item, bool answering)
    {
        if (item == null) return null;
        var price = leg switch
        {
            ErrandLeg.Vendor => $"{Gil(item.Count > 0 ? item.Gil / item.Count : item.Gil)} each",
            ErrandLeg.Market => $"none above {Gil(item.MaxUnitPrice)} each",
            _ => null,
        };
        if (price == null) return null;
        if (!answering) return price;
        // A shop's yes is only asked for once its price matched; the board's is checked in the same step.
        return leg == ErrandLeg.Vendor ? $"{price} · the game asked, and the price matched" : $"{price} · the game asked: its price is checked before yes";
    }

    /// <summary>"6" "of 12": a Got or Spent card's figure and its words.</summary>
    public static (string Value, string Of) Of(long value, long of, bool gil) =>
        gil ? (Number(value), $"of {Number(of)}") : (value.ToString(CultureInfo.InvariantCulture), $"of {of.ToString(CultureInfo.InvariantCulture)}");

    // ---------------------------------------------------------------------------------------------
    // The summary.

    /// <summary>"12 got on 2 trips for 58,920 gil"; nothing got: "Nothing got, 0 gil spent".</summary>
    public static string Summary(int got, int trips, long spent) =>
        got == 0 ? $"Nothing got, {Gil(spent)} spent" : $"{got} got on {Words.Count(Math.Max(1, trips), "trip")} for {Gil(spent)}";

    /// <summary>What was got, by name: ("Oasis Rug ×2", item).</summary>
    public static List<(uint ItemId, string Words)> GotList(IReadOnlyDictionary<uint, int> got, Func<uint, string> name) =>
        got.Where(kv => kv.Value > 0)
            .Select(kv => (kv.Key, Name: name(kv.Key), kv.Value))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Value == 1 ? g.Name : $"{g.Name} ×{g.Value}"))
            .ToList();

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
