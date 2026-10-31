using System.Collections.Generic;
using HousingStudio.Core;

namespace HousingStudio.Automation;

/// <summary>
/// ERRANDS (experimental): every game window and click "Get everything for me" uses, in one place, with
/// where each came from and how sure it is. None of these has been seen working with this plugin yet.
/// The ones marked unconfirmed (<see cref="UiCall.Verified"/> false) are always done step by step (the
/// run holds before the click until you press Continue) until you switch that off in the errands'
/// screen, having watched them work. To confirm or fix one: Settings → Something not working? → Start
/// recording, do the thing by hand (the recording each entry names), Stop recording, Copy recording +
/// log, and the window's name and the values it sent are in it.
///
/// Sources:
/// <list type="bullet">
/// <item>[ECommons] NightmareXIV/ECommons: SelectString and SelectIconString pick an entry with
/// (index); SelectYesno's yes is (0).</item>
/// <item>[AutoRetainer] NightmareXIV/AutoRetainer, from memory: RetainerList picks a retainer with
/// (2, index); the retainer menu is a SelectString.</item>
/// <item>[Guess] Written from what other plugins are known to send, not checked against a recording.</item>
/// </list>
/// Words are matched in English only so far: in another client's language a menu entry or object isn't
/// found, and the run stops and says which.
/// </summary>
public static class ErrandTable
{
    // ---------- vendors ----------

    /// <summary>A gil shop's window (the NPC's items, with Purchase). [ECommons: AddonMaster "Shop"]</summary>
    public const string Shop = "Shop";

    /// <summary>
    /// Buy <c>count</c> of the item in row <c>row</c> of the gil shop. [Guess: (0, row, count), as plugins that
    /// buy from vendors send.] Confirm with: a recording of buying one furnishing from the Material
    /// Supplier by hand (right-click → Purchase, or the quantity box and Purchase).
    /// </summary>
    public static readonly UiCall ShopBuy = new(Shop, "i:0 i:{row} i:{count}", false, "Guess", Verified: false);

    /// <summary>The menu some NPCs open first ("Purchase Furnishings (Tier 1)", "Purchase Items"). [ECommons]</summary>
    public const string SelectString = "SelectString";

    /// <summary>The same with icons (the housing vendors' menus). [ECommons]</summary>
    public const string SelectIconString = "SelectIconString";

    /// <summary>Picks a menu's entry (0 is the first). [ECommons: Callback.Fire(addon, true, index)]</summary>
    public static readonly UiCall MenuPick = new(SelectString, "i:{index}", true, "ECommons");

    public static readonly UiCall IconMenuPick = new(SelectIconString, "i:{index}", true, "ECommons");

    /// <summary>An NPC's words, which you click through. Not clicked by the plugin: it waits for you.</summary>
    public const string Talk = "Talk";

    // ---------- retainers ----------

    /// <summary>The summoning bell's list of retainers. [AutoRetainer]</summary>
    public const string RetainerList = "RetainerList";

    /// <summary>
    /// Picks the retainer in row <c>index</c> of the list. [AutoRetainer, from memory: (2, index)] Confirm
    /// with: a recording of opening the bell and choosing one retainer by hand.
    /// </summary>
    public static readonly UiCall RetainerPick = new(RetainerList, "i:2 i:{index}", false, "AutoRetainer (from memory)", Verified: false);

    /// <summary>The retainer's menu entry that opens their bags ("Entrust or withdraw items."), matched by its words (English).</summary>
    public static readonly string[] RetainerItemsWords = { "Entrust or withdraw items" };

    /// <summary>The retainer's menu entry that ends the talk ("Quit."), matched by its words.</summary>
    public static readonly string[] RetainerQuitWords = { "Quit" };

    /// <summary>The retainer's bag windows (as the plugin's own retainer record reads). [Retainers.cs]</summary>
    public static readonly string[] RetainerInventory = { "InventoryRetainer", "InventoryRetainerLarge" };

    /// <summary>
    /// The item menu's entry that takes a stack from the retainer to your bags, matched by its words: the
    /// whole stack ("Retrieve" for one, "Retrieve All" for a stack is the guess; never "Retrieve
    /// Quantity"). [Guess] Confirm with: a recording of right-clicking a furnishing in a retainer's bags
    /// and taking it.
    /// </summary>
    public static readonly string[] RetrieveWords = { "Retrieve All", "Retrieve" };

    /// <summary>The words of an entry that must never be chosen for a retrieve (it asks how many).</summary>
    public static readonly string[] RetrieveQuantityWords = { "Quantity" };

    // ---------- the market board ----------

    /// <summary>The board's search window, open once the board is used. [ECommons: "ItemSearch"]</summary>
    public const string ItemSearch = "ItemSearch";

    /// <summary>An item's listings on this world. [ECommons: "ItemSearchResult"]</summary>
    public const string ItemSearchResult = "ItemSearchResult";

    /// <summary>
    /// Buys the listing in row <c>index</c> of the listings shown (the board then asks yes/no). [Guess: (2, index)]
    /// Confirm with: a recording of buying one listing by hand, with the listings open.
    /// </summary>
    public static readonly UiCall ListingBuy = new(ItemSearchResult, "i:2 i:{index}", false, "Guess", Verified: false);

    // ---------- the house ----------

    /// <summary>
    /// The words of a house's front door (an event object), and of the inside's way out. [Guess] Confirm
    /// with: a recording standing at your door; the plugin logs the names of the objects near you when
    /// it can't find it.
    /// </summary>
    public static readonly string[] EntranceWords = { "Entrance" };

    // ---------- objects by name ----------

    /// <summary>EObjName rows: a summoning bell (2000401, as AutoRetainer looks for it) and the board's (a guess); their names in your client's language are read from these.</summary>
    public const uint SummoningBellRow = 2000401;

    public const uint MarketBoardRow = 2000402;

    public static readonly string[] SummoningBellWords = { "Summoning Bell" };

    public static readonly string[] MarketBoardWords = { "Market Board" };

    /// <summary>Every errand click, for Settings' list of windows and clicks.</summary>
    public static IReadOnlyList<UiCall> All => new[] { ShopBuy, MenuPick, IconMenuPick, RetainerPick, ListingBuy };
}
