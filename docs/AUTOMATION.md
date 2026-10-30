# How the automation works (0.2)

"Start placing" drives the game's own windows the way established automation plugins do: by firing
the callbacks a window fires when you click in it (`AtkUnitBase.FireCallback`, what ECommons'
`Callback.Fire` does), by calling the furnishing editor's own pick-up/set-down functions, and by
waiting for windows to appear. It never sends hand-made network messages (Refurnish found that
placing that way breaks the client).

**None of this has been run in the game yet.** Everything below says where it came from and how
sure it is.

## The run

`Session/PlacementRunner.cs` builds a list of pieces (`Core/RunSequence.cs`): moves, then pieces from
the storeroom and bags, floor pieces before what sits on them, and the dyes last (planned when the run
gets there, from what's placed by then and the dyes in your bags, `Core/DyePlanner`). Each piece is
a list of steps run by `Core/StepQueue.cs`: one step per frame at most, at least 0.3 s between two
clicks, each wait with a timeout, step-by-step mode holding before every click, and clean-up steps
(close the menu or dye window it opened) when a piece ends badly. Between pieces it waits the
interval set on the Place page (± the jitter).

Every frame it checks: same home (territory and house id), permission, the editor open, not in
combat/cutscene/zone change. A window that doesn't appear in time stops the run (after cleaning
up); a piece that doesn't land or take its dye is marked and skipped, and a few of those in a row
stop the run.

## Each kind of piece

**Move** (already placed). Source: MakePlace and its API 15 forks (RemakePlace, DisPlace), Refurnish.
Confidence: high, the same technique those plugins use today.
1. The editor must be in Rotate mode: if not, you're asked to press Rotate.
2. Re-read the piece by its furniture slot; stop if the slot now holds another item.
3. The editor's pick-up function; wait until it reports the piece picked.
4. Write the position and turn into the piece's layout instance; the editor's set-down function.
5. Wait until the furniture list stores it at the spot (5 cm, 0.03 rad).

**From the storeroom** (inside only). Source: Refurnish (list select, menu, preview), ECommons (menu
choose). Confidence: medium.
1. Wait for the furnishing list window, `HousingGoods`.
2. Its Storeroom tab: the click isn't known, so you're asked to click the tab (once per run).
3. Find the piece: the list position is guessed from the storeroom's order
   (`Menus.GuessStoreroomIndex`); pick it with `HousingGoods ← [i:1, i:index]`; when its menu opens,
   read which storeroom slot the game picked (`IndoorTerritory.StoreroomItemInventoryType/Slot`); if
   it's another piece, close the menu (`ContextMenu ← [i:0, i:-1, i:0]`) and try the next position
   outwards, up to 12.
4. Wait for the preview (`ObjectArray.TempObject`), move it to the spot.
5. Choose the "place" entry: find it by its words in the menu (`Menus.FindEntry`), then
   `ContextMenu ← [i:0, i:entry, i:0]`.
6. If a `SelectYesno` appears, its question is logged and Yes pressed.
7. Wait for a new piece of that item in the furniture list; if it isn't exactly at the spot, Move it.

**From your bags.** Source: AgentInventoryContext (used by item automation plugins), ECommons (menu),
MakePlace (set-down). Confidence: **low** — this is the step most likely to need fixing.
1. Open the item's menu on its bag slot: `AgentInventoryContext.OpenForItemSlot(container, slot, 0,
   bag window id)`.
2. Choose the entry whose words match "Place" (by client language, editable in Settings →
   Diagnostics); the menu's entries are logged either way.
3. Wait for the editor to be placing (mode 4) or the preview to show.
4. Aim the held piece at the spot and call the editor's set-down function on it.
   **Unverified**: the game may only set a held piece down on a real mouse click. If nothing lands in
   6 s, the run stops and asks you to click (or press Escape). Then switch "Place pieces from my bags
   for me" off: the run waits for you to put each piece down anywhere and sets it at its spot.
5. As 6–7 above.

**Dye.** Source: RemakePlace (`ReMakePlacePlugin.cs` `SetItemDye`, `StainsUtils.cs`), ECommons
(`ColorantColoring`, `MiragePrismMiragePlateConfirm`). Confidence: medium-high, RemakePlace ships it.
1. The editor must be in its dye mode (mode 5): you're asked to press it.
2. Check you have the dye (`InventoryManager.GetInventoryItemCount` over the Stain's items); skip if not.
3. Pick the piece (the editor's pick-up function); open the dye window on it with the game's
   "picked housing item" getter and the housing agent's "interact" function (RemakePlace's patterns).
4. `ColorantColoring ← [i:4, i:shade]`, then `[i:5, i:SubOrder-1, i:stain, i:0]`, then Dye `[i:0]`.
5. If `MiragePrismMiragePlateConfirm` appears, Yes `[i:0]`.
6. Wait for the piece's stored dye to change; close the dye window `[i:2]`.

## What's most likely to need fixing

1. **Setting a piece from the bags down** (bags step 4).
2. **The item menu's "place" words** (and that the entry exists in the editor's item menu): the log
   shows the menu's entries, and Settings → Diagnostics takes the right words.
3. **The editor mode and Storeroom tab clicks**: unknown, so you're asked to press them. A recording
   of you pressing them gives the values to automate them (`UiTable.EditorSetMode`,
   `UiTable.ListStoreroomTab`).
4. **The storeroom list's order** (if the guess is off, it searches outwards; if a list position
   picks nothing, the menu doesn't open and the run stops with a timeout).
5. Byte patterns after a game patch (`HousingEditor`, `HousingActions`, `FurnitureReader`).

## Sending diagnostics

Settings → Diagnostics → **Start recording**; do one placement from the bags, one from the storeroom
and one dye by hand; **Stop recording**; **Copy recording + log**. The text has, with times:
- `OPEN name …` / `CLOSE name`: every window, with its first text values;
- `CALLBACK (you|plugin) name close=… [i:1, i:12 …]`: every click's values, and a menu's entries;
- `STATE mode=… active=… preview=(x, y, z) storeroomTab=… storeroomPick=container/slot placed=n`;
- `ERROR TOAST …`: the game's red error messages.

The activity log below it has every step the plugin took, with the windows and values it used, and
what it saw. With those, the values in `HousingStudio/Automation/UiTable.cs` (or the menu words) can
be fixed in a few lines.
