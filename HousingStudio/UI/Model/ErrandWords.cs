using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>One line of a trip's route: where, what's got there, and the gil.</summary>
public sealed record RouteLeg(ErrandLeg Kind, string Name, string What, string Gil);

/// <summary>A trip on the Get screen: its label ("TRIP 1", drawn as given), the words on the right, its legs.</summary>
public sealed record RouteTrip(string Label, string Rest, IReadOnlyList<RouteLeg> Legs);

/// <summary>A reason things aren't got this way: "1 to craft", the things, why in a sentence, and the items (for Teamcraft).</summary>
public sealed record LeftRow(LeftReason Reason, string Title, string What, string Why, IReadOnlyList<(uint ItemId, int Count)> Items);

/// <summary>
/// One step of the running screen's journey (0.9.0): the trip's stops of one kind in a row (its shops,
/// its bell, its boards), in the trip's own order, then Home and Place. <see cref="FirstStop"/> to
/// <see cref="LastStop"/> are the trip's stops it covers (-1 for Home and Place).
/// </summary>
/// <param name="Label">Its words: "Shops", "Bell", "Board · 7", "Home", "Place".</param>
/// <param name="Short">Its words when they don't fit: "Board" for "Board · 7".</param>
public sealed record JourneyStep(ErrandLeg Leg, string Label, string Short, int FirstStop, int LastStop);

/// <summary>A thing the errands didn't get, with why (the summary's Not got, 0.9.0): "Wine Glass ×2", the reason, "board".</summary>
public sealed record NotGotRow(uint ItemId, int Count, LeftReason Reason, string Name, string Why, string Kind);

/// <summary>The errands' screens in words: a stop, a reason, the gil. Pure, so it's tested.</summary>
public static class ErrandWords
{
    public static string Gil(long gil) => gil.ToString("N0", CultureInfo.InvariantCulture) + " gil";

    public static string Reason(LeftReason reason) => reason switch
    {
        LeftReason.Craft => "crafted: no vendor sells it, none listed",
        LeftReason.NoVendorReachable => "its vendors are where the errands don't go (a city or zone whose aetheryte you haven't attuned to, or a shop that wants a quest you haven't done)",
        LeftReason.OverCeiling => "past your market ceiling",
        LeftReason.NotListedHere => "not listed on the worlds the errands buy on",
        LeftReason.NoRoom => "no room in your bags",
        LeftReason.Currency => "sold for a currency the errands may not spend, or you haven't enough of: allow it in Settings → Getting → Currencies",
        LeftReason.NoneListed => "sold on the market board, but none is listed on your data centre just now (as Universalis has it)",
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

    /// <summary>The brass button with the currency vendors' part too: "Set out · up to 58,920 gil + 45,000 MGP".</summary>
    public static string SetOut(long totalGil, IReadOnlyList<(string Currency, uint CurrencyItem, long Cost)> currencies)
    {
        var spent = currencies.Where(c => c.Cost > 0).Select(c => $"{Number(c.Cost)} {c.Currency}").ToList();
        if (spent.Count == 0) return SetOut(totalGil);
        return totalGil > 0 ? $"Set out · up to {Gil(totalGil)} + {string.Join(" + ", spent)}" : $"Set out · {string.Join(" + ", spent)}";
    }

    /// <summary>
    /// The currencies a plan spends, against what you have: "Also spends 45,000 MGP (you have 120,000)".
    /// Null when it spends none.
    /// </summary>
    public static string? CurrencyLine(IReadOnlyList<(string Currency, uint CurrencyItem, long Cost)> currencies, Func<uint, long> have)
    {
        var parts = currencies.Where(c => c.Cost > 0).Select(c => $"{Number(c.Cost)} {c.Currency} (you have {Number(have(c.CurrencyItem))})").ToList();
        return parts.Count == 0 ? null : $"Also spends {string.Join(", ", parts)}: only what you have, after any gil vendor";
    }

    /// <summary>"Glade Shelf ×2, Oasis Rug": what a stop gets.</summary>
    public static string Items(IEnumerable<ErrandItem> items, Func<uint, string> name) =>
        string.Join(", ", items.Select(i => i.Count == 1 ? name(i.ItemId) : $"{name(i.ItemId)} ×{i.Count}"));

    /// <summary>"11 pieces go in", "11 pieces go in · 2 dyes", "2 dyes for the pieces here".</summary>
    public static string GoIn(ErrandTrip trip)
    {
        var pieces = trip.Stops.SelectMany(s => s.Items).Where(i => i.Need.Kind == NeedKind.Piece).Sum(i => i.Count);
        var dyes = trip.Stops.SelectMany(s => s.Items).Where(i => i.Need.Kind == NeedKind.Dye).Sum(i => i.Count);
        var fixtures = trip.Stops.SelectMany(s => s.Items).Where(i => i.Need.Kind == NeedKind.Fixture).Sum(i => i.Count);
        var parts = new List<string>();
        if (pieces > 0) parts.Add($"{Words.Count(pieces, "piece")} go{(pieces == 1 ? "es" : string.Empty)} in");
        if (dyes > 0) parts.Add(pieces > 0 ? Words.Count(dyes, "dye") : $"{Words.Count(dyes, "dye")} for the pieces here");
        if (fixtures > 0) parts.Add($"{Words.Count(fixtures, "fixture")} to set");
        return parts.Count == 0 ? "What's in your bags goes in" : string.Join(" · ", parts);
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
        var bellSaid = false;
        foreach (var stop in trip.Stops)
        {
            switch (stop.Kind)
            {
                case StopKind.Retainer when !bellSaid:
                    // Every retainer of the trip at one bell, where the trip comes to the first.
                    bellSaid = true;
                    var what = string.Join("; ", retainers.Select(r => $"{r.Retainer?.Name ?? "A retainer"} · {Items(r.Items, name)}"));
                    legs.Add(new RouteLeg(ErrandLeg.Bell, houseBell ? "Bell in your house" : $"{city} · Summoning bell", what, "free"));
                    break;
                case StopKind.Vendor:
                    var v = stop.Vendor;
                    var where = v == null ? "A vendor" : v.PlaceName.Length > 0 ? $"{v.PlaceName} · {v.NpcName}" : v.NpcName;
                    legs.Add(new RouteLeg(ErrandLeg.Vendor, where, Items(stop.Items, name), Number(stop.Gil)));
                    break;
                case StopKind.Exchange:
                    var x = stop.Vendor;
                    var at = x == null ? "A vendor" : x.PlaceName.Length > 0 ? $"{x.PlaceName} · {x.NpcName}" : x.NpcName;
                    var costs = stop.Items.Where(i => i.Exchange != null).GroupBy(i => i.Exchange!.CurrencyName)
                        .Select(g => $"{Number(g.Sum(i => i.CurrencyCost))} {g.Key}");
                    legs.Add(new RouteLeg(ErrandLeg.Vendor, at, Items(stop.Items, name), string.Join(" + ", costs)));
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
                    LeftReason.NoVendorReachable => $"{n} from shops it doesn't go to",
                    LeftReason.OverCeiling => $"{n} past the market ceiling",
                    LeftReason.NotListedHere => $"{n} not listed where it buys",
                    LeftReason.NoRoom => $"{n} with no room in your bags",
                    LeftReason.Currency => $"{n} for {string.Join(", ", g.Select(l => l.Exchange?.CurrencyName).Where(c => !string.IsNullOrEmpty(c)).Distinct())}",
                    LeftReason.NoneListed => $"{n} not on the board just now",
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

    /// <summary>
    /// "Already here" as four small figures (0.9.0; the one line was cut off): in your storeroom, in your
    /// bags, on a retainer ("on retainers" for more), placed.
    /// </summary>
    public static (int Value, string Label)[] HaveFigures(int placed, int storeroom, int bags, int onRetainers) => new[]
    {
        (storeroom, "in storeroom"),
        (bags, "in your bags"),
        (onRetainers, onRetainers == 1 ? "on a retainer" : "on retainers"),
        (placed, "placed"),
    };

    /// <summary>
    /// A trip's label's right side (0.9.0): "fills 28 bag slots, 6 spare", the spare being the slots still
    /// empty after it (those kept free in Limits too); a later one is worked out again after the one before.
    /// </summary>
    /// <param name="freeSlots">The slots a trip may fill (<see cref="ErrandPlan.FreeSlots"/>: your empty ones less those kept free).</param>
    /// <param name="keptFree">The slots kept free (Limits' "2 slots spare").</param>
    public static string TripSlots(ErrandTrip trip, int freeSlots, int keptFree) =>
        trip.Number <= 1
            ? $"fills {Words.Count(trip.Slots, "bag slot")}, {Math.Max(0, freeSlots - trip.Slots) + Math.Max(0, keptFree)} spare"
            : $"worked out again after trip {trip.Number - 1}";

    /// <summary>
    /// The gil the errands are expected to spend, as the shopping list says it (Ready's "about"): the
    /// vendors' fixed prices, and the shopping list's market estimate for what the trips buy on the board.
    /// </summary>
    public static long Expected(ShoppingPlan shop, ErrandPlan plan)
    {
        // How many of each the trips buy on the board: a line the ceiling cut short counts only that part.
        var bought = new Dictionary<Need, int>();
        foreach (var item in plan.Stops(StopKind.Market).SelectMany(s => s.Items))
            bought[item.Need] = bought.GetValueOrDefault(item.Need) + item.Count;
        long market = 0;
        foreach (var line in shop.Lines)
        {
            if (line.Count <= 0 || !bought.TryGetValue(line.Need, out var n)) continue;
            if (line.Source == SourceKind.Market) market += line.Gil * Math.Min(n, line.Count) / line.Count;
            // A vendor's (or a currency's) out of reach, bought on the board instead (0.9.4): the board's estimate.
            else if (line.Source != SourceKind.Retainer && line.Board is { Listings.Count: > 0 } board) market += board.CostFor(Math.Min(n, line.Count)).Gil;
        }
        return plan.VendorGil + market;
    }

    /// <summary>
    /// The line under Set out (0.9.0): the shopping list's estimate when it's below the button's "up to"
    /// ("Expected about 2,098,460 by the shopping list; it never spends more than “up to”. You can stop at
    /// any time."). "Up to" is the listings the trips picked (dearer ones on your world alone, or with
    /// older listings left out); a listing that moved is bought only within it. Without the board, or
    /// with the estimate at the cap, as before.
    /// </summary>
    /// <param name="market">The trips buy on the market board.</param>
    public static string SetOutHint(long expected, long cap, bool market)
    {
        const string stop = "You can stop at any time.";
        if (!market || expected <= 0 || expected >= cap) return $"Nothing is bought above the price shown. {stop}";
        return $"Expected about {Number(expected)} by the shopping list; it never spends more than “up to”. {stop}";
    }

    // ---------------------------------------------------------------------------------------------
    // Running: the journey, where it is in words, what it's doing.

    /// <summary>
    /// The journey bar's steps for a trip, in the trip's own order (0.9.0; since 0.8.1 a trip goes to the
    /// shops first and the bell second, unless the bell is in your home, but the bar kept Bell, Vendors,
    /// Market, so Bell showed done on the way to the shops): each run of stops of one kind is one step
    /// ("Shops", "Bell", "Board", "Board · 3" for three worlds' boards), then Home and Place always.
    /// </summary>
    public static List<JourneyStep> JourneySteps(ErrandTrip? trip)
    {
        var steps = new List<JourneyStep>();
        if (trip != null)
        {
            for (var i = 0; i < trip.Stops.Count; i++)
            {
                var leg = trip.Stops[i].Kind switch
                {
                    StopKind.Retainer => ErrandLeg.Bell,
                    StopKind.Market => ErrandLeg.Market,
                    _ => ErrandLeg.Vendor,
                };
                if (steps.Count > 0 && steps[^1].Leg == leg)
                {
                    steps[^1] = steps[^1] with { LastStop = i };
                    continue;
                }
                var label = leg switch { ErrandLeg.Bell => "Bell", ErrandLeg.Market => "Board", _ => "Shops" };
                steps.Add(new JourneyStep(leg, label, label, i, i));
            }
            for (var s = 0; s < steps.Count; s++)
            {
                if (steps[s].Leg != ErrandLeg.Market) continue;
                var boards = steps[s].LastStop - steps[s].FirstStop + 1;
                if (boards > 1) steps[s] = steps[s] with { Label = $"Board · {boards}" };
            }
        }
        steps.Add(new JourneyStep(ErrandLeg.Home, "Home", "Home", -1, -1));
        steps.Add(new JourneyStep(ErrandLeg.Place, "Place", "Place", -1, -1));
        return steps;
    }

    /// <summary>
    /// The step in hand, by where the runner is: the step holding the trip's stop it's at
    /// (<paramref name="stop"/>, its index in the trip), else the first of its leg (Home, Place), else the first.
    /// </summary>
    public static int JourneyIndex(IReadOnlyList<JourneyStep> steps, ErrandLeg leg, int stop)
    {
        if (stop >= 0)
            for (var i = 0; i < steps.Count; i++)
                if (steps[i].FirstStop >= 0 && stop >= steps[i].FirstStop && stop <= steps[i].LastStop) return i;
        for (var i = 0; i < steps.Count; i++)
            if (steps[i].Leg == leg) return i;
        return 0;
    }

    /// <summary>
    /// Where it is, for the current card's first line (0.9.0, no map coordinates): "Housing Merchant · The
    /// Lavender Beds", "Bell in your house", "Ul'dah · Summoning bell", "Ul'dah · Market board · Phoenix",
    /// "Home"; null when it's placing or between stops.
    /// </summary>
    /// <param name="stop">The trip's stop it's at (null for Home and Place).</param>
    public static string? Place(ErrandStop? stop, ErrandLeg leg, bool houseBell, string homeWorld, string city)
    {
        if (stop != null)
        {
            switch (stop.Kind)
            {
                case StopKind.Retainer:
                    return houseBell ? "Bell in your house" : $"{city} · Summoning bell";
                case StopKind.Vendor or StopKind.Exchange:
                    var v = stop.Vendor;
                    return v == null ? null : v.PlaceName.Length > 0 ? $"{v.NpcName} · {v.PlaceName}" : v.NpcName;
                case StopKind.Market:
                    var world = !string.IsNullOrEmpty(stop.World) && stop.World != homeWorld ? $" · {stop.World}" : string.Empty;
                    return $"{city} · Market board{world}";
            }
        }
        return leg == ErrandLeg.Home ? "Home" : null;
    }

    /// <summary>A map position as the game's words give it, in either culture's decimals: " (10.9, 11.4)", " (11,8, 8,4)".</summary>
    private static readonly Regex MapPosition = new(@"\s*\(\d+[.,]\d,\s*\d+[.,]\d\)", RegexOptions.CultureInvariant);

    /// <summary>The words without a map position ("Housing Merchant, Mist (11,8, 8,4)" → "Housing Merchant, Mist").</summary>
    public static string NoMapPosition(string text) => text.IndexOf('(') < 0 ? text : MapPosition.Replace(text, string.Empty);

    /// <summary>The steps' first words that start a sentence (a name doesn't: "Momo's bags" keeps its capital).</summary>
    private static readonly HashSet<string> StepWords = new(StringComparer.Ordinal)
    {
        "Aethernet", "Answer", "Arrive", "Ask", "Away", "Buy", "Check", "Choose", "Click", "Close", "Enter", "Exchange", "Find", "Get", "Go",
        "In", "Its", "Leave", "Mount", "Note", "On", "Open", "Placing", "Press", "Remember", "Say", "Search", "Start", "Take", "Talk",
        "Teleport", "The", "To", "Use", "Wait", "Walk", "Work", "Your",
    };

    /// <summary>The current card's second line: the step in hand, in words ("Now: teleport to Mist (Lifestream, aetheryte 72)").</summary>
    public static string Now(string step)
    {
        var s = NoMapPosition(step).Trim();
        if (s.Length == 0) return s;
        var space = s.IndexOf(' ');
        if (StepWords.Contains(space < 0 ? s : s[..space])) s = char.ToLowerInvariant(s[0]) + s[1..];
        return "Now: " + s;
    }

    /// <summary>The journey bar's stops for a trip in the old fixed order: Bell, Vendors, Market (those it has), then Home and Place. The window uses <see cref="JourneySteps"/>.</summary>
    public static List<ErrandLeg> Journey(ErrandTrip? trip)
    {
        var stops = new List<ErrandLeg>();
        if (trip != null)
        {
            if (trip.Stops.Any(s => s.Kind == StopKind.Retainer)) stops.Add(ErrandLeg.Bell);
            if (trip.Stops.Any(s => s.Kind is StopKind.Vendor or StopKind.Exchange)) stops.Add(ErrandLeg.Vendor);
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

    /// <summary>"4,000" "of up to 61,400": the Spent card's figure and its words (0.9.0: set against the cap agreed to).</summary>
    public static (string Value, string Of) SpentOf(long spent, long agreed) =>
        agreed <= 0 && spent <= 0 ? ("0", "no gil to spend") : (Number(spent), $"of up to {Number(agreed)}");

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

    /// <summary>
    /// What wasn't got, a row per thing with why (the summary's Not got, 0.9.0): "Wine Glass ×2", "Past your
    /// market ceiling.", and where it would have come from ("board", "shop", "craft", "bags"); by reason, then name.
    /// </summary>
    /// <param name="boardWhy">Why a board didn't sell it, the first time, by item (the run's own words), for board rows.</param>
    public static List<NotGotRow> NotGot(IReadOnlyList<ErrandLeft> left, Func<uint, string> name, IReadOnlyDictionary<uint, string>? boardWhy = null) =>
        left.Where(l => l.Count > 0)
            .GroupBy(l => (l.Need.ItemId, l.Reason))
            .Select(g =>
            {
                var n = g.Sum(l => l.Count);
                var thing = name(g.Key.ItemId);
                var currency = g.Select(l => l.Exchange?.CurrencyName).FirstOrDefault(c => !string.IsNullOrEmpty(c));
                var why = g.Key.Reason == LeftReason.Currency && currency != null
                    ? $"Sold for {currency}, which the errands may not spend or you haven't enough of: allow it in Settings → Getting → Currencies."
                    : g.Key.Reason is LeftReason.NotListedHere or LeftReason.OverCeiling && boardWhy != null && boardWhy.TryGetValue(g.Key.ItemId, out var said)
                        ? Words.Capital(said) + "."
                        : Words.Capital(Reason(g.Key.Reason)) + ".";
                return new NotGotRow(g.Key.ItemId, n, g.Key.Reason, n == 1 ? thing : $"{thing} ×{n}", why, NotGotKind(g.Key.Reason));
            })
            .OrderBy(r => r.Reason)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Where a thing not got would have come from, in a word: "board", "shop", "craft", "bags".</summary>
    public static string NotGotKind(LeftReason reason) => reason switch
    {
        LeftReason.Craft => "craft",
        LeftReason.OverCeiling or LeftReason.NotListedHere or LeftReason.NoneListed => "board",
        LeftReason.NoVendorReachable or LeftReason.Currency => "shop",
        LeftReason.NoRoom => "bags",
        _ => string.Empty,
    };

    /// <summary>The line under the summary's buttons: "The 2 not got stay on the shopping list."; null when everything was got.</summary>
    public static string? NotGotHint(int notGot) =>
        notGot <= 0 ? null : notGot == 1 ? "The 1 not got stays on the shopping list." : $"The {notGot} not got stay on the shopping list.";

    /// <summary>The most gil a character can hold, so the most a ceiling can be.</summary>
    public const long GilCap = 999_999_999;

    /// <summary>The ceilings the stepper goes through (any other can be typed in).</summary>
    public static readonly long[] Ceilings =
    {
        0, 10_000, 25_000, 50_000, 100_000, 250_000, 500_000, 1_000_000, 2_500_000, 5_000_000, 10_000_000,
        25_000_000, 50_000_000, 100_000_000, 250_000_000, 500_000_000, GilCap,
    };

    /// <summary>
    /// A ceiling typed in: "2500000", "2,500,000 gil", "2.500.000", "2.5m", "750k", "1b" (k, m and b
    /// may have a decimal part, with a point or a comma); kept between 0 and <see cref="GilCap"/>. Null
    /// when there's no number in it.
    /// </summary>
    public static long? ParseGil(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant().Replace("gil", string.Empty).Replace(" ", string.Empty).Replace("\u00a0", string.Empty);
        if (t.Length == 0) return null;
        var scale = t[^1] switch { 'k' => 1_000L, 'm' => 1_000_000L, 'b' => 1_000_000_000L, _ => 1L };
        if (scale > 1) t = t[..^1];
        double value;
        if (scale > 1)
        {
            // One separator with a short tail is a decimal point ("2.5m", "2,5m"); more are groupings.
            var seps = t.Count(c => c is '.' or ',');
            var last = t.LastIndexOfAny(new[] { '.', ',' });
            var digits = new string(t.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;
            if (seps == 1 && t.Length - last - 1 is >= 1 and <= 2)
            {
                var whole = new string(t[..last].Where(char.IsDigit).ToArray());
                var part = new string(t[(last + 1)..].Where(char.IsDigit).ToArray());
                value = double.Parse((whole.Length == 0 ? "0" : whole) + "." + part, CultureInfo.InvariantCulture);
            }
            else
            {
                value = double.Parse(digits, CultureInfo.InvariantCulture);
            }
        }
        else
        {
            var digits = new string(t.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;
            if (digits.Length > 12) return GilCap;
            value = double.Parse(digits, CultureInfo.InvariantCulture);
        }
        return (long)Math.Clamp(Math.Round(value * scale), 0, GilCap);
    }

    /// <summary>The next ceiling up or down from <paramref name="now"/>.</summary>
    public static long StepCeiling(long now, int delta)
    {
        if (delta > 0) return Ceilings.FirstOrDefault(c => c > now, Ceilings[^1]);
        if (delta < 0) return Ceilings.LastOrDefault(c => c < now, 0);
        return now;
    }
}
