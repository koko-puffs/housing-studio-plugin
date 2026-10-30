# How the automation works (0.2.2)

"Start placing" drives the game's own windows the way established automation plugins do: by firing
the callbacks a window fires when you click in it (`AtkUnitBase.FireCallback`, what ECommons'
`Callback.Fire` does), by calling the furnishing editor's own pick-up/set-down functions, and by
waiting for windows to appear. It never sends hand-made network messages (Refurnish found that
placing that way breaks the client).

The clicks come from two recordings a user made doing everything by hand ("Recording 1" with 0.2.0,
"Recording 2" with 0.2.1), listed with their sources in `HousingStudio/Automation/UiTable.cs`.

## The run

`Session/PlacementRunner.cs` builds a list of pieces (`Core/RunSequence.cs`): moves, then pieces from
the storeroom and bags, floor pieces before what sits on them, and the dyes last (planned when the run
gets there). Each piece is a list of steps run by `Core/StepQueue.cs`: one step per frame at most, at
least 0.3 s between two clicks, each wait with a timeout, step-by-step mode holding before every
click, and clean-up steps (close the menu or dye window it opened) when a piece ends badly. Between
pieces it waits the interval set on the Place page (± the jitter).

Every frame it checks: same home, permission, the editor open (or, for up to 8 s while it switches
modes, the Housing menu), not in combat/cutscene/zone change. A window that doesn't appear in time
stops the run (after cleaning up); a piece that doesn't land or take its dye is marked with why and
skipped; a few of those in a row stop the run.

## Getting to the right mode (`Core/ModeRoute.cs`)

- Nothing housing open: the Housing menu is opened by asking the housing agent to show itself
  (**unconfirmed**; you opened it through Main menu → Social → Housing in Recording 2, which isn't
  replayed). If it doesn't appear in 5 s, the run asks you to open it.
- Housing menu → Indoor Furnishings `HousingMenu ← [i:0]` (opens in Rotate) or Furnishing Color `[i:1]`.
- Move / Rotate: `HousingLayout ← [i:0]` / `[i:1]`.
- Between the dye mode and the others: close the editor `HousingLayout ← [i:-2]` or Furnishing
  Color's list `HousingGoodsStain ← [i:-1]` (**a guess**), then the other menu entry.

## Each kind of piece

**Move** (already placed): the editor's pick-up, set the transform, the editor's set-down, check it
landed. **Works in game.**

**From the storeroom**, as Recording 2 showed it done by hand:
1. Rotate; the furnishing list `HousingGoods`; its Storeroom tab `HousingGoods ← [i:11]`.
2. Find the piece's row (below), and pick it: `HousingGoods ← [i:1, u:row]` (the row is unsigned).
   The game opens the row's menu, shows a **preview** of the piece in the room, and stays in Rotate.
   The plugin reads which storeroom slot the game picked; if it's another piece, it closes the menu
   and tries the next row.
3. Move the preview to the piece's exact spot and turn (Refurnish's technique); check it stays there.
4. Choose "Place": `ContextMenu ← [i:0, i:0, u:0, empty, empty]`. The piece is placed where the preview
   is (Recording 2: placed 33 → 34, no Place mode, no click).
5. The new piece appears; if it isn't exactly at its spot, it's moved there.

**Finding a storeroom row quickly** (`Core/StoreroomRows.cs`). The list isn't in storeroom order but
it's grouped by item.
- First, the list's own row texts are read without clicking (`Automation/ListReader.cs`: the list
  components' rows, a tree list's items or a list's labels). If the furnishing list has as many rows
  as the storeroom has pieces, rows are found by the piece's name: usually one pick.
- Rows already picked are known (this run, and saved per house between runs while the storeroom holds
  exactly the same pieces; a fingerprint of the storeroom decides).
- Otherwise rows are picked from the top, and when a row starts a group, the storeroom's count of that
  piece says how long the group is, so the rest of it is skipped: about one pick per different piece.
- When a piece leaves the list, the rows below move up.
- The log says what was read from the list ("The furnishing list's own rows: node …") and every row
  picked ("row 31 → storeroom 27008/49: …").

**From your bags**, as Recording 2: the item's menu (opened with `AgentInventoryContext`,
**unconfirmed**), "Place" `[i:0, i:0, u:0, empty, empty]`; the editor then holds the piece (mode Place).
In the recording a real click set it down; the plugin aims the held piece and calls the editor's
set-down function on it (**unverified**). If nothing lands in 6 s, the run stops and says so; with
"Place pieces from my bags for me" off, the run waits for you to put each down and then sets it.

**Dye**, as Recordings 1 and 2:
1. Furnishing Color mode.
2. Open the dye window on the piece: first the editor's pick-up on it (clicking the piece in the room
   opened the window in Recording 1); if the window doesn't open in a second, through Furnishing
   Color's list as in Recording 2: rows with the piece's name are picked (`HousingGoodsStain ←
   [i:0, u:row]`) until the editor's active item is this piece, then its menu `[i:1, u:row]` and
   "Paint"; if the list's names can't be read, RemakePlace's "interact with the picked item" function.
3. Find the dye's row in the window's list: its names start at value 203 "No Color" (row 0) and end
   where "No Color" comes round again, so Snow White is row 1 and Soot Black row 6. If the dye isn't
   in the list shown, the shade is picked first (`[i:4, i:shade]`, RemakePlace, **unconfirmed**).
4. `ColorantColoring ← [i:5, i:row, i:stain, i:0]` (recorded: Snow White [5, 1, 1, 0], Soot Black
   [5, 6, 6, 0]; the third value matches both row and Stain id there), Dye `[i:0]`, confirm
   `MiragePrismMiragePlateConfirm ← [i:0]`, close `ColorantColoring ← [i:-2]`; check the dye changed.

The dye window chooses which dye item it uses (both recordings used "Standard Spectrum Dye"), so a
piece isn't skipped because the Stain's own dye items aren't in your bags; a piece the game won't dye
is skipped as "dye missing".

## What's most likely to need fixing next

1. **Reading the list's rows**: if the log says the rows couldn't be read, the scan (one pick per
   different piece) is used. A recording with "Write down the windows' values" while the Storeroom tab
   is open shows where the names are.
2. **Setting a held bag piece down.**
3. **Opening the Housing menu** from nothing.
4. **Picking another dye shade**, and closing Furnishing Color.

## For the window (the run's state)

`PlacementRunner`: `State`, `DryRun`, `Phase`, `PhaseProgress(phase)` → (done, problems, total),
`Current` (the piece), `StepLabel` and `StepProgress`, `WaitingForYou`, `AwaitingContinue` /
`Continue()`, `TimeLeft`, `Problems` (piece, name, kind, reason), `DyeShortfall`, `RetryProblems()`,
`LastBackupPath` / `RestoreBackup()`, `StoreroomRowsKnown`, `Pause`/`Resume`/`Stop`/`SkipCurrent`.
`LayoutSession.MissingDyes()` lists the dyes to buy before a run.

## Sending diagnostics

Settings → Diagnostics → **Start recording**; do the thing that went wrong by hand (and, with the
Storeroom tab open, press **Write down the windows' values**); **Stop recording**; **Copy recording +
log**.
