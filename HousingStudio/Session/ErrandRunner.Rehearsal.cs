using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// The errands rehearsed (Ready's Preview → Rehearse the run, with "Get them for me first"): the
/// same trips, in the same order, with the same travel, walking, talking and windows, and every
/// commit point backed out:
/// <list type="bullet">
/// <item>a vendor's purchase: the shop's row found and the purchase sent, the game's question read and
/// checked against the price (<see cref="MarketRule.QuestionMatches"/>), then Cancel (never yes);</item>
/// <item>the market board: searched, the listings opened, the listing chosen and sent, its question
/// checked, then Cancel;</item>
/// <item>a retainer: the bell, the retainer, their bags, the item's menu, "Retrieve from Retainer"
/// found, then the menu closed without choosing.</item>
/// </list>
/// Teleports and walking happen for real (the fees are the only gil spent). Nothing is got, so each
/// trip is made once as planned, home between them as usual, and placing is rehearsed after the last
/// (on what's owned already). Gil and bag checks are noted where a real run would stop, and it goes on.
/// What it would get and spend counts as Got and Spent here (for the window's cards and the agreed-gil
/// check), and in the report.
/// </summary>
public sealed partial class ErrandRunner
{
    /// <summary>A rehearsal stops after this many steps that didn't work.</summary>
    private const int MaxRehearsalFailures = 6;

    /// <summary>These errands are a rehearsal: nothing bought or taken.</summary>
    public bool Rehearsal { get; private set; }

    private int rehearsalFailures;
    private Func<bool>? rehearsingNow;

    /// <summary>For the guard (<see cref="Rehearsals"/>): these errands are rehearsing now.</summary>
    private Func<bool> rehearsing => rehearsingNow ??= () => Active && Rehearsal;

    private void RehearsalStarted(ErrandPlan plan)
    {
        log.Info($"A rehearsal: {Words(plan.Trips.Count, "trip")} walked as planned, each purchase asked for and cancelled, nothing taken from retainers; "
                 + "then placing rehearsed on what you have. Teleports are real (their fees are the only gil spent).");
        var gil = ErrandGame.Gil();
        if (gil < plan.TotalGil)
            Rehearsals.Report.Issue("Before setting out", $"You have {gil:N0} gil; the errands need about {plan.TotalGil:N0}: a real run wouldn't start.", RehearsalTrouble.WouldStop);
    }

    /// <summary>
    /// A segment of the rehearsal didn't work: it's noted, and the rehearsal goes on with the next stop
    /// (the rest of this one skipped), unless it was going home, going in or placing (the next trip
    /// would start from the wrong place), or too many steps haven't worked. False: stop.
    /// </summary>
    private bool RehearsalCarriesOn(string message)
    {
        if (segment < 0 || segment >= segments.Count) return false;
        var seg = segments[segment];
        Rehearsals.Report.Issue(seg.Title, message, message.StartsWith("Timed out", StringComparison.Ordinal) || message.StartsWith("Didn't", StringComparison.Ordinal) ? RehearsalTrouble.TimedOut : RehearsalTrouble.Failed);
        if (++rehearsalFailures >= MaxRehearsalFailures)
        {
            log.Warn($"  {rehearsalFailures} steps of the rehearsal haven't worked: it stops.");
            return false;
        }
        if (seg.Leg is ErrandLeg.Home or ErrandLeg.Place) return false;
        var skipped = 0;
        while (segment + 1 < segments.Count && !segments[segment + 1].Going && segments[segment + 1].Leg is not (ErrandLeg.Home or ErrandLeg.Place))
        {
            segment++;
            skipped++;
        }
        // Travel left running (a teleport or world visit that didn't finish) is stopped first.
        Nav.Stop();
        walkTarget = null;
        if (travelling || Travel.Busy) Travel.Abort();
        travelling = false;
        lastTerritory = ErrandGame.Territory;
        log.Warn($"  ✕ {seg.Title}: {message} The rehearsal notes it and goes on{(skipped > 0 ? $" with the next stop ({skipped} step{(skipped == 1 ? "" : "s")} of this one left out)" : string.Empty)}.");
        CloseWindows();
        return true;
    }

    /// <summary>After a trip's way home: the next trip as planned, or the end (a rehearsal doesn't work out what's missing again).</summary>
    private List<Step> NextTripSteps() => new()
    {
        Step.Act("Next, as planned (a rehearsal got nothing, so nothing is worked out again)", () =>
        {
            if (Trip < Plan.Trips.Count) pendingTrip = Plan.Trips[Trip];
            else finishPending = true;
            return StepOutcome.Done;
        }),
    };

    /// <summary>Notes where a real run would stop, and goes on.</summary>
    private void WouldStop(string where, string why)
    {
        log.Warn($"  A real run would stop here: {why}");
        Rehearsals.Report.Issue(where, $"{why} A real run would stop here.", RehearsalTrouble.WouldStop);
    }

    /// <summary>Reads the game's yes/no, checks it names the price planned, and says Cancel (closes it): never yes.</summary>
    private void CheckAndCancel(string where, string? question, uint unit, int quantity, long cost, bool board = false, uint itemId = 0)
    {
        // A vendor's price the game's data has wrong: learnt, so the next estimate has it right.
        var learnt = !board && itemId != 0 && !MarketRule.QuestionMatches(question, unit, quantity, cost)
                     && MarketRule.AskedCost(question, quantity) is { } askedSum && askedSum > 0 && askedSum % quantity == 0
                     && LearnPrice(itemId, (uint)(askedSum / quantity), unit);
        // A vendor asking another price: a real run says yes while the run stays within the gil agreed.
        var then = board ? "say no and stop"
            : MarketRule.AskedCost(question, quantity) is { } asked && Spent + asked <= agreedGil ? $"buy it at {asked:N0} gil, still within the {agreedGil:N0} agreed"
            : "say no, and leave it";
        // The board's as a real run checks it (its whole cost and quantity, 0.8.5).
        var matched = board ? MarketRule.BoardQuestionMatches(question, quantity, cost, (long)unit * quantity) : MarketRule.QuestionMatches(question, unit, quantity, cost);
        Rehearsals.Report.Question(matched);
        log.Info($"  The game asks: \"{question}\": {(matched ? "it names the price planned" : $"it doesn't name {quantity} for {cost:N0} gil (a real run would {then})")} → Cancel (a rehearsal never says yes).");
        // A vendor's other price that a real run would pay (within the gil agreed) isn't a failure: a note,
        // and the price is learnt for the next estimate (0.7.12's Stage Panel and White Screen).
        var paid = !board && MarketRule.AskedCost(question, quantity) is { } wouldPay && Spent + wouldPay <= agreedGil;
        if (!matched && paid) Rehearsals.Report.Note($"{where}: the shop asks another price than the game's data (\"{question}\"); a real run would {then}.{(learnt ? " Learnt for the next estimate." : string.Empty)}");
        else if (!matched) Rehearsals.Report.Issue(where, $"The game asked \"{question}\", which doesn't name {quantity} for {cost:N0} gil: a real run would {then}.", RehearsalTrouble.NoMatch);
        GameUi.Close(UiTable.YesNo);
    }

    /// <summary>Waits for the yes/no to go, then checks nothing came to your bags and no gil went.</summary>
    private List<Step> NothingBought(string where, uint itemId, Func<int> before, Func<long> gilBefore) => new()
    {
        WaitFor("The question closes", () => GameUi.YesNoText() == null, TimeSpan.FromSeconds(5), "The purchase's question didn't close after Cancel."),
        new Step
        {
            Label = "Check nothing was bought",
            Delay = TimeSpan.FromMilliseconds(500),
            Run = () =>
            {
                var now = ErrandGame.BagCount(itemId);
                var gil = ErrandGame.Gil();
                if (now != before() || gil != gilBefore())
                {
                    log.Warn($"  After Cancel: {now} in your bags (was {before()}), {gil:N0} gil (was {gilBefore():N0}).");
                    Rehearsals.Report.Issue(where, $"After Cancel your bags hold {now} (were {before()}) and you have {gil:N0} gil (had {gilBefore():N0}).", RehearsalTrouble.Failed);
                }
                return Finished();
            },
        },
    };

    /// <summary>A vendor's purchase rehearsed: sent, its question checked, Cancel.</summary>
    private List<Step> RehearseBuy(VendorOffer v, ErrandItem item)
    {
        var name = Name(item.ItemId);
        var where = $"Buying {Thing(item)} from {v.NpcName}";
        var before = 0;
        long gilBefore = 0;
        var n = 0;
        var stack = ErrandGame.StackSize(item.ItemId);
        return new List<Step>
        {
            new Step
            {
                Label = $"Ask to buy {name} from {v.NpcName} ({ErrandTable.ShopBuy.Describe()}), then Cancel its question",
                IsAction = true,
                Run = () =>
                {
                    if (!GameUi.IsOpen(ErrandTable.Shop)) return StepOutcome.Halt($"{v.NpcName}'s shop closed.");
                    n = Math.Min(item.Count, Math.Min(MaxPerPurchase, stack));
                    var cost = (long)v.Price * n;
                    var gil = ErrandGame.Gil();
                    if (gil < cost) WouldStop(where, $"Not enough gil: {cost:N0} for {n} {name}, and you have {gil:N0}.");
                    if (Spent + (long)v.Price * item.Count > agreedGil) WouldStop(where, $"That would spend more than the {agreedGil:N0} gil agreed to.");
                    var room = ErrandGame.FreeSlots();
                    if (room < ErrandPlanner.Stacks(item.Count, stack)) WouldStop(where, $"Your bags are full ({room} free slots).");
                    var row = ErrandGame.ShopRow(v.ShopId, item.ItemId);
                    if (row < 0)
                    {
                        log.Warn($"  {name} isn't in {v.NpcName}'s list (it may need a quest first): a real run leaves it.");
                        Rehearsals.Report.Issue(where, $"{name} isn't in {v.NpcName}'s list (it may need a quest first): a real run leaves it.", RehearsalTrouble.NoMatch);
                        return Finished();
                    }
                    before = ErrandGame.BagCount(item.ItemId);
                    gilBefore = gil;
                    return GameUi.Fire(ErrandTable.ShopBuy, UiCallValues.Of(("row", row), ("count", n))) ? StepOutcome.Done : StepOutcome.Halt($"{v.NpcName}'s shop closed.");
                },
            },
            new Step
            {
                Label = "The shop asks to confirm",
                Run = () =>
                {
                    if (GameUi.YesNoText() != null) return StepOutcome.Done;
                    if (ErrandGame.BagCount(item.ItemId) > before)
                    {
                        var spent = Math.Max(0, gilBefore - ErrandGame.Gil());
                        Rehearsals.Report.Issue(where, $"The shop sold it without asking ({ErrandGame.BagCount(item.ItemId) - before} came to your bags, {spent:N0} gil spent): a rehearsal can't stop that.", RehearsalTrouble.Failed);
                        return Finished();
                    }
                    return StepOutcome.Waiting;
                },
                Timeout = TimeSpan.FromSeconds(6),
                TimeoutMessage = "The shop didn't ask to confirm the purchase (nothing was bought).",
            },
            new Step
            {
                Label = $"Check the question, then Cancel (never yes in a rehearsal)",
                IsAction = true,
                Run = () =>
                {
                    CheckAndCancel(where, GameUi.YesNoText(), v.Price, n, (long)v.Price * n, itemId: item.ItemId);
                    var gil = (long)v.Price * item.Count;
                    Rehearsals.Report.Would(RehearsalAct.Buy, item.Count, gil);
                    VendorSpent += gil;
                    Count(item.ItemId, item.Count);
                    log.Info($"  Would buy {Thing(item)} from {v.NpcName} for {gil:N0} gil (a rehearsal: not bought).");
                    return StepOutcome.Done;
                },
            },
        }.Concat(NothingBought(where, item.ItemId, () => before, () => gilBefore)).ToList();
    }

    /// <summary>A market board purchase rehearsed: the search and listings as a real run, a listing chosen and sent, its question checked, Cancel.</summary>
    private List<Step> RehearseListing(ErrandItem item)
    {
        var name = Name(item.ItemId);
        var where = $"Buying {Thing(item)} on the market board";
        var before = 0;
        long gilBefore = 0;
        long cost = 0;
        uint unit = 0;
        var quantity = 0;
        var asked = false;
        var steps = SearchSteps(item.ItemId, name);
        steps.Add(new Step
        {
            Label = $"Choose a listing of {name} ({ErrandTable.ListingBuy.Describe()}), then Cancel its question",
            IsAction = true,
            Run = () =>
            {
                if (BoardListings.Open(item.ItemId) is not { } listings) return StepOutcome.Halt($"{name}'s listings closed.");
                var gil = ErrandGame.Gil();
                string? firstWhy = null;
                foreach (var l in listings.OrderBy(l => l.UnitPrice))
                {
                    // As a real run chooses (the budgets as it would have them), but gil on hand only noted.
                    var why = MarketRule.WhyNot(l.UnitPrice, l.Quantity, l.TotalTax, item.MaxUnitPrice, item.Count, item.Gil,
                        Math.Min(ceiling - MarketSpent, agreedGil - Spent), long.MaxValue);
                    if (why != null)
                    {
                        firstWhy ??= why;
                        continue;
                    }
                    cost = MarketRule.Cost(l.UnitPrice, l.Quantity, l.TotalTax);
                    unit = l.UnitPrice;
                    quantity = l.Quantity;
                    if (gil < cost) WouldStop(where, $"Not enough gil: {cost:N0} for this listing, and you have {gil:N0}.");
                    if (ErrandGame.FreeSlots() < ErrandPlanner.Stacks(l.Quantity, ErrandGame.StackSize(item.ItemId)))
                        WouldStop(where, $"Your bags are full ({ErrandGame.FreeSlots()} free slots).");
                    before = ErrandGame.BagCount(item.ItemId);
                    gilBefore = gil;
                    asked = true;
                    log.Info($"  Choosing {l.Quantity} × {name} at {l.UnitPrice:N0} gil each ({cost:N0} with tax), row {l.Row}.");
                    return GameUi.Fire(ErrandTable.ListingBuy, UiCallValues.Of(("index", l.Row))) ? StepOutcome.Done : StepOutcome.Halt($"{name}'s listings closed.");
                }
                // A real run leaves the item for now and plans another world for it: not a stop, a note.
                log.Warn($"  No listing of {name} a real run would buy here ({firstWhy ?? "none listed"}): it would be left for now and looked for on another world.");
                Rehearsals.Report.Note($"{where}: no listing a real run would buy here ({firstWhy ?? "none listed"}); it would look on another world.");
                if (emptyBoards.Add((item.ItemId, ErrandGame.CurrentWorld()))) emptiedThisTrip++;
                return Finished();
            },
        });
        steps.Add(WaitFor("The board asks to confirm", () => !asked || GameUi.YesNoText() != null, TimeSpan.FromSeconds(5), "The board didn't ask to confirm the purchase (nothing was bought)."));
        steps.Add(new Step
        {
            Label = $"Check the question, then Cancel (never yes in a rehearsal)",
            IsAction = true,
            Run = () =>
            {
                CheckAndCancel(where, GameUi.YesNoText(), unit, quantity, cost, board: true);
                var gil = Math.Max(cost, item.Gil);
                Rehearsals.Report.Would(RehearsalAct.Buy, item.Count, gil);
                MarketSpent += gil;
                Count(item.ItemId, item.Count);
                log.Info($"  Would buy {Thing(item)} on the market board for about {gil:N0} gil (a rehearsal: not bought).");
                return StepOutcome.Done;
            },
        });
        steps.AddRange(NothingBought(where, item.ItemId, () => before, () => gilBefore));
        return steps;
    }

    /// <summary>A retainer's item rehearsed: its menu opened as a real run opens it, "Retrieve from Retainer" found, the menu closed without choosing.</summary>
    private List<Step> RehearseWithdraw(RetainerStock r, ErrandItem item)
    {
        var name = Name(item.ItemId);
        var where = $"Taking {Thing(item)} from {r.Name}";
        var before = 0;
        var stack = ErrandGame.StackSize(item.ItemId);
        return new List<Step>
        {
            new Step
            {
                Label = $"Open the menu of {Thing(item)} in {r.Name}'s bags",
                IsAction = true,
                Run = () =>
                {
                    if (!ErrandGame.RetainerBagsOpen) return StepOutcome.Halt($"{r.Name}'s bags closed.");
                    if (ErrandGame.RetainerSlot(item.ItemId) is not { } slot)
                    {
                        log.Warn($"  {r.Name} has no {name}: a real run leaves it.");
                        Rehearsals.Report.Issue(where, $"{r.Name} has no {name}: a real run leaves it.", RehearsalTrouble.NoMatch);
                        return Finished();
                    }
                    var room = ErrandGame.FreeSlots();
                    if (room < ErrandPlanner.Stacks(slot.Quantity, stack)) WouldStop(where, $"Your bags are full ({room} free slots; the stack of {slot.Quantity} needs more).");
                    before = ErrandGame.BagCount(item.ItemId);
                    return ErrandGame.OpenRetainerItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
                },
            },
            Step.Wait("The item's menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), TimeSpan.FromSeconds(3)),
            new Step
            {
                Label = $"Find \"{ErrandTable.RetrieveWords[0]}\" in the item's menu (not chosen: a rehearsal)",
                Run = () =>
                {
                    var entries = GameUi.MenuEntries();
                    if (entries == null) return StepOutcome.Halt("The item's menu closed.");
                    log.Info($"  menu entries: {string.Join(" | ", entries.Select((e, i) => $"{i}:{e}"))}");
                    var i = ErrandGame.Find(entries, ErrandTable.RetrieveWords, ErrandTable.RetrieveQuantityWords);
                    if (i < 0)
                    {
                        Rehearsals.Report.Issue(where, $"The item's menu has no \"{ErrandTable.RetrieveWords[0]}\" (it shows: {string.Join(" | ", entries)}): a real run would stop here.", RehearsalTrouble.NoMatch);
                        return StepOutcome.Done;
                    }
                    Rehearsals.Report.Would(RehearsalAct.Take, item.Count);
                    Count(item.ItemId, item.Count);
                    log.Info($"  Would choose “{entries[i]}” (entry {i}) to take {Thing(item)} (a rehearsal: not chosen).");
                    return StepOutcome.Done;
                },
            },
            Step.Act($"Close the menu without choosing: {UiTable.MenuCancel.Describe()}", () =>
            {
                if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                return StepOutcome.Done;
            }),
            Step.Wait("The item's menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), TimeSpan.FromSeconds(3), StepStatus.Done),
            new Step
            {
                Label = "Check nothing came to your bags",
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.ContextMenu))
                        Rehearsals.Report.Issue(where, $"The item's menu didn't close with {UiTable.MenuCancel.Describe()}.", RehearsalTrouble.NoMatch);
                    var now = ErrandGame.BagCount(item.ItemId);
                    if (now != before) Rehearsals.Report.Issue(where, $"Your bags hold {now} {name} (held {before}): something was taken.", RehearsalTrouble.Failed);
                    return Finished();
                },
            },
        };
    }
}
