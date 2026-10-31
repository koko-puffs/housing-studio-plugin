using System.Collections.Generic;
using Dalamud.Game;
using HousingStudio.Core;

namespace HousingStudio.Automation;

/// <summary>
/// Every game window and click the automation uses, in one place, with where each came from.
/// When the game changes (or a guess is wrong), a recording from Settings → Something not working? shows the
/// real names and values: fix them here.
///
/// Sources:
/// <list type="bullet">
/// <item>[Recording 1] A user's recording with 0.2.0 (English client, game patch 7.5, October 2026):
/// them opening Indoor Furnishings and Furnishing Color, switching Move/Rotate, opening the Storeroom
/// tab, placing a storeroom piece, dyeing a piece, closing the editor. Values marked so are seen
/// working in the game.</item>
/// <item>[Recording 2] The same user, with 0.2.1: placing from the bags and from the storeroom,
/// moving, leaving the editor, then dyeing through Furnishing Color's list (right-click → Paint).
/// Opening the Housing menu itself went through the main menu (Social → Housing), which isn't replayed:
/// the plugin asks the housing agent to show it instead.</item>
/// <item>[Run 1] The same user's run of 0.2.0: picking furnishing-list rows with (1, row) opened their
/// menus and picked storeroom slots, in no slot order.</item>
/// <item>[Refurnish] Wyse2011/Refurnish (API 15): the furnishing list's row pick (1, row) and the menu's
/// close (0, -1, …).</item>
/// <item>[ECommons] NightmareXIV/ECommons AddonMaster: menu entries at AtkValues[8 + i], SelectYesno.</item>
/// <item>[ReMakePlace] RemakePlace/plugin (API 15): the dye window's shade pick (4, shade) and the
/// "open the dye window on the picked piece" functions.</item>
/// </list>
/// </summary>
public static class UiTable
{
    // ---------- windows ----------

    /// <summary>The Housing menu: Indoor Furnishings, Furnishing Color, … [Recording 1]</summary>
    public const string HousingMenu = "HousingMenu";

    /// <summary>The furnishing editor's bar (Move, Rotate, Store, …). [Recording 1]</summary>
    public const string LayoutBar = "HousingLayout";

    /// <summary>The furnishing list (Indoor Furnishings): placed pieces, and the Storeroom tab. [Recording 1, Refurnish]</summary>
    public const string FurnishingList = "HousingGoods";

    /// <summary>The furnishing list in Furnishing Color (dye) mode. [Recording 1]</summary>
    public const string DyeList = "HousingGoodsStain";

    /// <summary>The menu that opens on an item. [Recording 1, Refurnish, ECommons]</summary>
    public const string ContextMenu = "ContextMenu";

    /// <summary>The dye window. [Recording 1, ReMakePlace]</summary>
    public const string DyeWindow = "ColorantColoring";

    /// <summary>"Dye the selected item with the listed colors?" [Recording 1]</summary>
    public const string DyeConfirm = "MiragePrismMiragePlateConfirm";

    /// <summary>The game's yes/no box (if placing asks anything, it's this). [ECommons]</summary>
    public const string YesNo = "SelectYesno";

    /// <summary>The bag windows, whichever layout you use; the item menu is opened "from" the open one.</summary>
    public static readonly string[] BagWindows = { "Inventory", "InventoryLarge", "InventoryExpansion" };

    // ---------- the editor's modes ----------

    /// <summary>Housing menu → Indoor Furnishings: opens the editor (in Rotate) and the furnishing list. [Recording 1]</summary>
    public static readonly UiCall MenuFurnishings = new(HousingMenu, "i:0", true, "Recording 1");

    /// <summary>Housing menu → Furnishing Color: the editor in its dye mode. [Recording 1]</summary>
    public static readonly UiCall MenuDye = new(HousingMenu, "i:1", true, "Recording 1");

    /// <summary>The editor's Move button. [Recording 1]</summary>
    public static readonly UiCall LayoutMove = new(LayoutBar, "i:0", true, "Recording 1");

    /// <summary>The editor's Rotate button. [Recording 1]</summary>
    public static readonly UiCall LayoutRotate = new(LayoutBar, "i:1", true, "Recording 1");

    /// <summary>
    /// The editor's Store button ("Place selected item into your storeroom"): in Store mode, the piece
    /// picked goes to the storeroom. [0.3.3's recording: HousingLayout [i:2], then mode Store]
    /// </summary>
    public static readonly UiCall LayoutStore = new(LayoutBar, "i:2", true, "0.3.3 recording");

    /// <summary>Close the editor (back to the Housing menu). [Recording 1]</summary>
    public static readonly UiCall LayoutClose = new(LayoutBar, "i:-2", false, "Recording 1");

    /// <summary>Close the dye mode's list (back to the Housing menu). By analogy with the furnishing list's close; unconfirmed.</summary>
    public static readonly UiCall DyeListClose = new(DyeList, "i:-1", true, "same as HousingGoods' close", Verified: false);

    // ---------- the furnishing list ----------

    /// <summary>The Storeroom tab. [Recording 1]</summary>
    public static readonly UiCall ListStoreroomTab = new(FurnishingList, "i:11", true, "Recording 1");

    /// <summary>
    /// Pick the n-th row of the list: opens its menu and shows a preview of the piece in the room
    /// (mode stays Rotate). [Recording 2: HousingGoods [i:1, u:3] — the row is unsigned, close true]
    /// </summary>
    public static readonly UiCall ListSelect = new(FurnishingList, "i:1 u:{index}", true, "Recording 2");

    /// <summary>
    /// Filter the list to a category (Core.ListCategories): not known yet, so not used. A recording of
    /// choosing a category in the furnishing list (Display All / Indoor Furnishings / Tables / Tabletop /
    /// Wall-mounted / Rugs) gives the values; the window's words for them are its values [10]–[15],
    /// which aren't the callback's values (the Storeroom tab, whose words are [19], is [i:11]).
    /// </summary>
    public static readonly UiCall? ListCategory = null;

    /// <summary>Leave the editor: the furnishing list's close (sent with the bar's [-2]). [Recording 2]</summary>
    public static readonly UiCall ListClose = new(FurnishingList, "i:-2", false, "Recording 2");

    // ---------- the dye mode's list ----------

    /// <summary>Pick the n-th piece in Furnishing Color's list (the editor's active item becomes it). [Recording 2: HousingGoodsStain [i:0, u:1]]</summary>
    public static readonly UiCall DyeListSelect = new(DyeList, "i:0 u:{index}", true, "Recording 2");

    /// <summary>Open the n-th piece's menu in Furnishing Color's list (its entry 0 is "Paint"). [Recording 2: HousingGoodsStain [i:1, u:1]]</summary>
    public static readonly UiCall DyeListMenu = new(DyeList, "i:1 u:{index}", true, "Recording 2");

    /// <summary>Close the Housing menu. [Recording 2: HousingMenu [i:-32]]</summary>
    public static readonly UiCall MenuClose = new(HousingMenu, "i:-32", true, "Recording 2");

    /// <summary>Choose a menu entry ("Place" is entry 0 for a storeroom row and a bag item, "Paint" for a dye-list row). [Recordings 1 and 2]</summary>
    public static readonly UiCall MenuChoose = new(ContextMenu, "i:0 i:{entry} u:0 empty empty", true, "Recording 1");

    /// <summary>Close a menu without choosing. [Refurnish's learned cancel]</summary>
    public static readonly UiCall MenuCancel = new(ContextMenu, "i:0 i:-1 u:0 empty empty", true, "Refurnish", Verified: false);

    /// <summary>Press Yes. [ECommons]</summary>
    public static readonly UiCall YesNoYes = new(YesNo, "i:0", true, "ECommons", Verified: false);

    // ---------- the dye window ----------

    /// <summary>Pick a shade (the row of circles). [ReMakePlace; not needed in Recording 1, which stayed on the first shade]</summary>
    public static readonly UiCall DyeShade = new(DyeWindow, "i:4 i:{shade}", true, "ReMakePlace", Verified: false);

    /// <summary>
    /// Pick a dye: (5, its row in the window's list, its Stain row, 0). Recording 1 picked Soot Black
    /// (Stain 6) with (5, 6, 6, 0): the list starts with "No Color", so Soot Black is row 6. The row is
    /// read from the window's own list (<see cref="DyeListFirstValue"/>); the Stain's SubOrder is the
    /// fallback (ReMakePlace used SubOrder - 1, before the window had "No Color").
    /// </summary>
    public static readonly UiCall DyePick = new(DyeWindow, "i:5 i:{row} i:{stain} i:0", true, "Recording 1");

    /// <summary>The dye window's Dye button. [Recording 1]</summary>
    public static readonly UiCall DyeApply = new(DyeWindow, "i:0", true, "Recording 1");

    /// <summary>Close the dye window. [Recording 1: the game sent this right after the confirmation]</summary>
    public static readonly UiCall DyeClose = new(DyeWindow, "i:-2", false, "Recording 1");

    /// <summary>Yes on "dye the selected item?". [Recording 1]</summary>
    public static readonly UiCall DyeConfirmYes = new(DyeConfirm, "i:0", true, "Recording 1");

    /// <summary>No on "dye the selected item?" (clean-up). [ReMakePlace]</summary>
    public static readonly UiCall DyeConfirmNo = new(DyeConfirm, "i:-1", true, "ReMakePlace", Verified: false);

    /// <summary>The dye window's list of dye names starts at this AtkValue ("No Color"). [Recording 1: values 203–209 were No Color … Soot Black]</summary>
    public const int DyeListFirstValue = 203;

    /// <summary>The shade circles in the dye window, by the Stain sheet's Shade column. [ReMakePlace]</summary>
    public static int? ShadeIndex(byte shade) => shade switch
    {
        2 => 0,
        4 => 1,
        5 => 2,
        6 => 3,
        7 => 4,
        8 => 5,
        9 => 6,
        10 => 7,
        _ => null,
    };

    /// <summary>
    /// The words of the "place" entry in an item's menu, in the client's language: the game's own
    /// (<see cref="MenuWords.Place"/>), else the built-in ones. English "Place" is confirmed (Recording 1:
    /// "0:Place | 1:Store | 2:Dye | …"); Settings → Something not working? takes others.
    /// </summary>
    public static IReadOnlyList<string> PlaceLabels(ClientLanguage language) => Game.GameWords.Place(language, BuiltInPlaceLabels(language));

    /// <summary>The words of the "Store" entry in a bag item's menu (sends it to the storeroom): the game's own, else the built-in ones.</summary>
    public static IReadOnlyList<string> StoreLabels(ClientLanguage language) => Game.GameWords.Store(language, BuiltInStoreLabels(language));

    /// <summary>The words of the "Paint" entry in a Furnishing Color list row's menu: the game's own, else the built-in ones.</summary>
    public static IReadOnlyList<string> PaintLabels(ClientLanguage language) => Game.GameWords.Paint(language, BuiltInPaintLabels(language));

    /// <summary>
    /// The "place" entry's words when the game's own can't be read: English "Place" is confirmed; the
    /// others were guesses before the game's words were read (German's is Platzieren there).
    /// </summary>
    public static IReadOnlyList<string> BuiltInPlaceLabels(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "配置する", "設置する", "配置" },
        ClientLanguage.German => new[] { "Platzieren", "Aufstellen", "Einrichten" },
        ClientLanguage.French => new[] { "Placer", "Installer", "Disposer" },
        _ => new[] { "Place" },
    };

    /// <summary>
    /// The "Store" entry's words when the game's own can't be read. English "Store" is confirmed
    /// (0.3.0: "0:Place | 1:Store | 2:Dye | …"); the others are the game's (Addon row 6840, 2026.10).
    /// </summary>
    public static IReadOnlyList<string> BuiltInStoreLabels(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "倉庫に入れる" },
        ClientLanguage.German => new[] { "Ins Lagerhaus legen" },
        ClientLanguage.French => new[] { "Ranger dans l'entrepôt" },
        _ => new[] { "Store" },
    };

    /// <summary>The "Paint" entry's words when the game's own can't be read. English confirmed (Recording 2); the others the game's (Addon row 6272).</summary>
    public static IReadOnlyList<string> BuiltInPaintLabels(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "染色する", "染色" },
        ClientLanguage.German => new[] { "Färben" },
        ClientLanguage.French => new[] { "Teindre" },
        _ => new[] { "Paint" },
    };

    public static IEnumerable<UiCall> All() => new[]
    {
        MenuFurnishings, MenuDye, MenuClose, LayoutMove, LayoutRotate, LayoutStore, LayoutClose, ListClose, DyeListClose, ListStoreroomTab, ListSelect,
        DyeListSelect, DyeListMenu,
        MenuChoose, MenuCancel, YesNoYes, DyeShade, DyePick, DyeApply, DyeClose, DyeConfirmYes, DyeConfirmNo,
    };
}
