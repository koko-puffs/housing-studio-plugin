# How the automation works (0.2.1)

"Start placing" drives the game's own windows the way established automation plugins do: by firing
the callbacks a window fires when you click in it (`AtkUnitBase.FireCallback`, what ECommons'
`Callback.Fire` does), by calling the furnishing editor's own pick-up/set-down functions, and by
waiting for windows to appear. It never sends hand-made network messages (Refurnish found that
placing that way breaks the client).

Since 0.2.1 most clicks are the ones a user recorded doing it by hand with 0.2.0 ("Recording 1" in
`HousingStudio/Automation/UiTable.cs`). What's still a guess is marked below.

## The run

`Session/PlacementRunner.cs` builds a list of pieces (`Core/RunSequence.cs`): moves, then pieces from
the storeroom and bags, floor pieces before what sits on them, and the dyes last (planned when the run
gets there, from what's placed by then). Each piece is a list of steps run by `Core/StepQueue.cs`: one
step per frame at most, at least 0.3 s between two clicks, each wait with a timeout, step-by-step mode
holding before every click, and clean-up steps (close the menu or dye window it opened) when a piece
ends badly. Between pieces it waits the interval set on the Place page (± the jitter).

Every frame it checks: same home (territory and house id), permission, the editor open (or, for up to
8 s while it switches modes, the Housing menu), not in combat/cutscene/zone change. A window that
doesn't appear in time stops the run (after cleaning up); a piece that doesn't land or take its dye
is marked and skipped, and a few of those in a row stop the run.

## Switching modes (automatic since 0.2.1)

`Core/ModeRoute.cs`, with the clicks from Recording 1:
- Move / Rotate: the editor bar `HousingLayout ← [i:0]` / `[i:1]`.
- To the dye mode: close the editor `HousingLayout ← [i:-2]` (the Housing menu comes back), then
  `HousingMenu ← [i:1]` (Furnishing Color).
- Back from it: close its list `HousingGoodsStain ← [i:-1]` (**a guess**, by analogy with the
  furnishing list's close; if that window isn't there the plugin closes it directly), then
  `HousingMenu ← [i:0]` (Indoor Furnishings, which opens in Rotate).
- With only the Housing menu open, Start works too: the first piece opens Indoor Furnishings.
- If there's no known way (nothing open, or a piece is held), it asks you.

## Each kind of piece

**Move** (already placed). Source: MakePlace and its API 15 forks, Refurnish. **Confirmed in game**
(0.2.0 run: Minimalist Poplar Partition, Summoning Bell).

**From the storeroom** (inside only). Sources: Recording 1, Run 1, Refurnish.
1. Rotate mode; the furnishing list (`HousingGoods`) open; its Storeroom tab `HousingGoods ← [i:11]`.
2. Find the piece's row. The list isn't in storeroom slot order (Run 1: rows 26–37 held slots of
   three containers in no order), so 0.2.1 learns it: it picks rows (`HousingGoods ← [i:1, i:row]`),
   reads which storeroom slot the game picked (`IndoorTerritory.StoreroomItemInventoryType/Slot`), and
   remembers every row it has seen for the rest of the run (`Core/StoreroomRows.cs`). A piece whose
   row is known goes straight to it; otherwise rows are picked from the first unknown one down until
   it turns up. Each row is picked at most once per run (when a piece is placed, the rows under it
   move up one). Every row learnt is logged ("row 31 → storeroom 27008/49: …"), and the whole list at
   the end of the run.
3. Choose "Place": `ContextMenu ← [i:0, i:entry, u:0, empty, empty]` (Recording 1: entry 0 "Place").
4. The editor now holds the piece (mode Place; no preview). Aim it at its spot and call the editor's
   set-down function on it. **Unverified**: the game may only set a held piece down on a real mouse
   click. If nothing lands in 6 s, the run stops and says so (click or press Escape to finish that
   piece). Switching "Place … for me" off in Settings makes the run wait for you to put each piece
   down anywhere and then set it at its spot (that part works: it's a Move).
5. A new piece of that item appears; if it isn't exactly at the spot, it's moved there.

**From your bags.** As above from step 3, but the menu is opened on the bag slot with
`AgentInventoryContext.OpenForItemSlot`. Opening it that way is unconfirmed.

**Dye.** Sources: Recording 1, RemakePlace.
1. Furnishing Color mode (switched automatically, above).
2. Pick the piece with the editor's pick-up function. In this mode picking a piece opens the dye
   window (`ColorantColoring`); if it doesn't within 0.8 s, it's opened with RemakePlace's "interact
   with the picked item" function.
3. Find the dye's row in the window's list: the list's names start at AtkValue 203 with "No Color",
   so Soot Black is row 6 (Recording 1). If the dye isn't in the list shown, the shade is picked first
   (`[i:4, i:shade]`, RemakePlace, **unconfirmed**) and the list read again; if it still isn't there,
   the dye's place in the Stain sheet is used.
4. `ColorantColoring ← [i:5, i:row, i:stain, i:0]` (Recording 1: `[5, 6, 6, 0]` for Soot Black), Dye
   `[i:0]`, confirm `MiragePrismMiragePlateConfirm ← [i:0]`, close `ColorantColoring ← [i:-2]`.
5. Check the piece's stored dye changed.

Dyes: the window chooses which dye item to use (Recording 1 offered "Standard Spectrum Dye"), so the
run doesn't skip a piece just because the Stain's own dye items aren't in your bags; it tries, and a
piece the game won't dye is skipped with a note.

## What's most likely to need fixing next

1. **Setting a held piece down** (storeroom and bags step 4).
2. **Picking another shade** in the dye window, for dyes not in the first shade (0.2.1 picks the
   shade, then reads the list; if the list holds every dye at once, no shade click is needed).
3. **Closing Furnishing Color** to go back to Rotate (`HousingGoodsStain ← [i:-1]`).
4. **Scanning the storeroom list** takes one pick per row the first time; for a big storeroom that's
   minutes. A recording with "Write down the windows' values" (with the Storeroom tab open) may show
   the rows' items in the list's own values, so they can be read without picking.

## Sending diagnostics

Settings → Diagnostics → **Start recording**; do the thing that went wrong by hand (and, with the
Storeroom tab open, press **Write down the windows' values**); **Stop recording**; **Copy recording +
log**. The text has, with times:
- `OPEN name …` / `CLOSE name`: every window, with its words (all of them for the furnishing list,
  the dye window and menus);
- `CALLBACK (you|plugin) name close=… [i:1, i:12 …]`: every click's values, and a menu's entries;
- `STATE mode=… active=… preview=(x, y, z) storeroomTab=… storeroomPick=container/slot placed=n`;
- `VALUES name (n values): [0] … [1] …`: every value a window holds, when asked;
- `ERROR TOAST …`: the game's red error messages.

The activity log below it has every step the plugin took, with the windows and values it used, what
it saw, and the storeroom rows it learnt.
