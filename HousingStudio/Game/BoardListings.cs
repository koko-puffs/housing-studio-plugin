using System;
using System.Collections.Generic;
using Dalamud.Game.Network.Structures;

namespace HousingStudio.Game;

/// <summary>
/// The market board's listings for the item whose listings are open, as the game receives them
/// (Dalamud's market board service: each batch of up to ten, in the order the list shows them). 0.7.10
/// read them from the game's item search proxy, which never said they'd come (the errands waited 15 s
/// at every item). Each listing's row is its place in that order.
/// </summary>
public static class BoardListings
{
    private static readonly object Gate = new();
    private static readonly List<ErrandGame.BoardListing> Received = new();
    private static uint item;
    private static DateTime lastAt = DateTime.MinValue;
    private static uint askedItem;
    private static DateTime askedAt = DateTime.MaxValue;
    private static bool hooked;

    public static void Hook()
    {
        if (hooked) return;
        try
        {
            Services.MarketBoard.OfferingsReceived += OnOfferings;
            hooked = true;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Couldn't listen to the market board's listings");
        }
    }

    public static void Unhook()
    {
        if (!hooked) return;
        Services.MarketBoard.OfferingsReceived -= OnOfferings;
        hooked = false;
    }

    /// <summary>Listings of this item are about to be asked for: what came for another is forgotten.</summary>
    public static void Expect(uint itemId)
    {
        lock (Gate)
        {
            item = itemId;
            Received.Clear();
            lastAt = DateTime.MinValue;
            askedItem = itemId;
            askedAt = DateTime.UtcNow;
        }
    }

    /// <summary>When this item's listings were last asked for (<see cref="DateTime.MaxValue"/> if they weren't).</summary>
    public static DateTime AskedAt(uint itemId)
    {
        lock (Gate) return askedItem == itemId ? askedAt : DateTime.MaxValue;
    }

    /// <summary>
    /// The listings to choose from while the item's listings window is open: as received, or none when
    /// nothing came 6 s after they were asked for; null when the window isn't open (or they're coming).
    /// </summary>
    public static List<ErrandGame.BoardListing>? Open(uint itemId)
    {
        if (!Automation.GameUi.IsOpen(Automation.ErrandTable.ItemSearchResult)) return null;
        if (ErrandGame.Listings(itemId) is { } l) return l;
        return (DateTime.UtcNow - AskedAt(itemId)).TotalSeconds > 6 ? new List<ErrandGame.BoardListing>() : null;
    }

    /// <summary>
    /// The item's listings once none has come for <paramref name="settle"/> (they come in batches), or
    /// null while none has come (or they're another item's).
    /// </summary>
    public static List<ErrandGame.BoardListing>? For(uint itemId, TimeSpan settle)
    {
        lock (Gate)
        {
            if (item != itemId || Received.Count == 0 || DateTime.UtcNow - lastAt < settle) return null;
            return new List<ErrandGame.BoardListing>(Received);
        }
    }

    private static void OnOfferings(IMarketBoardCurrentOfferings offerings)
    {
        lock (Gate)
        {
            // A request's batches come together; one coming later is the list sent again (after a
            // purchase, say), whose rows have moved: it's taken afresh.
            if (DateTime.UtcNow - lastAt > TimeSpan.FromSeconds(1.5)) Received.Clear();
            foreach (var l in offerings.ItemListings)
            {
                if (l.ItemId != item)
                {
                    // Another item's listings (you opened another): they're what's open now.
                    item = l.ItemId;
                    Received.Clear();
                }
                if (l.ItemQuantity == 0 || Received.Exists(r => r.ListingId == l.ListingId)) continue;
                Received.Add(new ErrandGame.BoardListing(Received.Count, l.ListingId, l.PricePerUnit, (int)l.ItemQuantity, l.TotalTax, l.IsHq));
            }
            lastAt = DateTime.UtcNow;
        }
    }
}
