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
/// <item>[Refurnish] Wyse2011/Refurnish (API 15): placing from the storeroom through the furnishing
/// window: list "HousingGoods" select = (int 1, int list index); "ContextMenu" choose = (int 0, int
/// entry, …), close = (int 0, int -1, …); the preview is <c>ObjectArray.TempObject</c>.</item>
/// <item>[ECommons] NightmareXIV/ECommons AddonMaster: ContextMenu entry select = (0, index, 0),
/// entries count in AtkValues[0], entry texts from AtkValues[8 + i]; SelectYesno.</item>
/// <item>[ReMakePlace] RemakePlace/plugin (API 15) furnishing dyes: editor in its dye mode (5),
/// select the piece, "interact" opens "ColorantColoring"; shade = (4, shade), dye = (5, SubOrder - 1,
/// Stain row, 0), apply = (0), close = (2); then "MiragePrismMiragePlateConfirm" yes = (0).</item>
/// </list>
/// </summary>
public static class UiTable
{
    // ---------- windows ----------

    /// <summary>The furnishing list (Housing → Indoor Furnishings): placed pieces and the storeroom tab. [Refurnish]</summary>
    public const string FurnishingList = "HousingGoods";

    /// <summary>The menu that opens on an item. [Refurnish, ECommons]</summary>
    public const string ContextMenu = "ContextMenu";

    /// <summary>The furnishing dye window. [ReMakePlace, ECommons]</summary>
    public const string DyeWindow = "ColorantColoring";

    /// <summary>"Dye this?" confirmation after applying. [ReMakePlace, ECommons]</summary>
    public const string DyeConfirm = "MiragePrismMiragePlateConfirm";

    /// <summary>The game's yes/no box (if placing asks anything, it's this). [ECommons]</summary>
    public const string YesNo = "SelectYesno";

    /// <summary>The bag windows, whichever layout you use; the item menu is opened "from" the open one.</summary>
    public static readonly string[] BagWindows = { "Inventory", "InventoryLarge", "InventoryExpansion" };

    // ---------- clicks ----------

    /// <summary>Pick the n-th piece in the furnishing list (opens its menu and the preview). [Refurnish, learned from a real click]</summary>
    public static readonly UiCall ListSelect = new(FurnishingList, "i:1 i:{index}", false, "Refurnish");

    /// <summary>
    /// Switch the furnishing list to its Storeroom tab. Not known yet: when null, the plugin asks you
    /// to click the tab (once per run). A recording of you clicking it gives the values.
    /// </summary>
    public static readonly UiCall? ListStoreroomTab = null;

    /// <summary>Choose a context-menu entry. [ECommons AddonMaster.ContextMenu.Entry.Select]</summary>
    public static readonly UiCall MenuChoose = new(ContextMenu, "i:0 i:{entry} i:0", true, "ECommons");

    /// <summary>Close a context menu without choosing. [Refurnish's learned cancel: entry -1]</summary>
    public static readonly UiCall MenuCancel = new(ContextMenu, "i:0 i:-1 i:0", true, "Refurnish");

    /// <summary>Press Yes. [ECommons/common use: SelectYesno callback 0]</summary>
    public static readonly UiCall YesNoYes = new(YesNo, "i:0", true, "ECommons");

    /// <summary>Pick a shade (the row of circles) in the dye window. [ReMakePlace StainCallbackHelper]</summary>
    public static readonly UiCall DyeShade = new(DyeWindow, "i:4 i:{shade}", true, "ReMakePlace");

    /// <summary>Pick a dye: its place in the shade (Stain.SubOrder - 1) and its Stain row. [ReMakePlace]</summary>
    public static readonly UiCall DyePick = new(DyeWindow, "i:5 i:{order} i:{stain} i:0", true, "ReMakePlace");

    /// <summary>The dye window's "Dye" button. [ReMakePlace]</summary>
    public static readonly UiCall DyeApply = new(DyeWindow, "i:0", true, "ReMakePlace");

    /// <summary>Close the dye window. [ReMakePlace]</summary>
    public static readonly UiCall DyeClose = new(DyeWindow, "i:2", true, "ReMakePlace");

    /// <summary>Yes on "dye this?". [ReMakePlace]</summary>
    public static readonly UiCall DyeConfirmYes = new(DyeConfirm, "i:0", true, "ReMakePlace");

    /// <summary>No on "dye this?" (clean-up). [ReMakePlace]</summary>
    public static readonly UiCall DyeConfirmNo = new(DyeConfirm, "i:-1", true, "ReMakePlace");

    /// <summary>
    /// Switch the furnishing editor's mode (Rotate for moving, Dye for dyeing). Not known yet: when
    /// null, the plugin asks you to press the mode's button and carries on when it changes.
    /// </summary>
    public static readonly UiCall? EditorSetMode = null;

    /// <summary>
    /// The shade circles in the dye window, by the Stain sheet's Shade column. [ReMakePlace
    /// StainCallbackHelper.GetShadeCallbackValue]
    /// </summary>
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
    /// The words of the "place" entry in an item's menu, by client language. Unconfirmed: the plugin
    /// writes every menu it opens to the activity log, and Settings → Diagnostics lets you type the
    /// exact words if these don't match.
    /// </summary>
    public static IReadOnlyList<string> PlaceLabels(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "配置する", "設置する", "配置" },
        ClientLanguage.German => new[] { "Aufstellen", "Platzieren", "Einrichten" },
        ClientLanguage.French => new[] { "Placer", "Installer", "Disposer" },
        _ => new[] { "Place", "Place Furnishing", "Place Item" },
    };
}
