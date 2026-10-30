using System.Collections.Generic;
using Dalamud.Game;
using HousingStudio.Core;

namespace HousingStudio.Automation;

/// <summary>
/// Every game window and click the automation uses, in one place, with where each came from.
/// When the game changes (or a guess is wrong), a recording from Settings → Diagnostics shows the
/// real names and values: fix them here.
///
/// Sources:
/// <list type="bullet">
/// <item>[Recording 1] A user's recording with 0.2.0 (English client, game patch 7.5, October 2026):
/// them opening Indoor Furnishings and Furnishing Color, switching Move/Rotate, opening the Storeroom
/// tab, placing a storeroom piece, dyeing a piece, closing the editor. Values marked so are seen
/// working in the game.</item>
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

    /// <summary>Close the editor (back to the Housing menu). [Recording 1]</summary>
    public static readonly UiCall LayoutClose = new(LayoutBar, "i:-2", false, "Recording 1");

    /// <summary>Close the dye mode's list (back to the Housing menu). By analogy with the furnishing list's close; unconfirmed.</summary>
    public static readonly UiCall DyeListClose = new(DyeList, "i:-1", true, "same as HousingGoods' close", Verified: false);

    // ---------- the furnishing list ----------

    /// <summary>The Storeroom tab. [Recording 1]</summary>
    public static readonly UiCall ListStoreroomTab = new(FurnishingList, "i:11", true, "Recording 1");

    /// <summary>Pick the n-th row of the list (opens its menu). [Refurnish, Run 1]</summary>
    public static readonly UiCall ListSelect = new(FurnishingList, "i:1 i:{index}", false, "Refurnish, Run 1");

    /// <summary>Choose a menu entry ("Place" was entry 0 for a storeroom row). [Recording 1]</summary>
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
    /// The words of the "place" entry in an item's menu, by client language. English "Place" is
    /// confirmed (Recording 1: "0:Place | 1:Store | 2:Dye | …"); Settings → Diagnostics takes others.
    /// </summary>
    public static IReadOnlyList<string> PlaceLabels(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "配置する", "設置する", "配置" },
        ClientLanguage.German => new[] { "Aufstellen", "Platzieren", "Einrichten" },
        ClientLanguage.French => new[] { "Placer", "Installer", "Disposer" },
        _ => new[] { "Place" },
    };

    public static IEnumerable<UiCall> All() => new[]
    {
        MenuFurnishings, MenuDye, LayoutMove, LayoutRotate, LayoutClose, DyeListClose, ListStoreroomTab, ListSelect,
        MenuChoose, MenuCancel, YesNoYes, DyeShade, DyePick, DyeApply, DyeClose, DyeConfirmYes, DyeConfirmNo,
    };
}
