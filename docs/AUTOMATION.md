# How the automation works (0.3.2)

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
gets there). Each piece is a list of steps run by `Core/StepQueue.cs`: waits poll every frame and go on
the moment their condition holds (several checks can finish in one frame), at least the pace's click
gap between two game actions (window clicks and the editor's pick-up and set-down alike; 0.3 s at
Quick), each wait with a timeout, step-by-step mode holding before every click, and clean-up steps
(close the menu or dye window it opened) when a piece ends badly. Between pieces it waits the pace's
pause (± the jitter). The pace (`Core/Pacing.cs`, `PaceProfile`) sets the pause between pieces, the
click gap and a scale for the settle checks and short pauses (`Speed.Wait`), never the timeouts:
Steady 0.6 s / 0.45 s / ×1.5, Quick (the default since 0.3.3, 0.3.2's pace) 0.15 s / 0.3 s / ×1,
Daring 0.05 s / 0.2 s / ×0.65. Before 0.3.3 the pace was only the pause between pieces: Careful
(1.5 s) moves to Steady, the rest to Quick (`Configuration.Migrate`, version 5).

**Timing.** Every step is logged when it ends, with how long it took ("→ Pick row 165: … (12 ms)"),
and the end of a run (or a stop) logs a **Timing** summary: the average per piece and per phase, and
the steps that took the most time in all. Send that when something feels slow.

**Confirmed state, not the frame of the click** (0.3.1). 0.2.4 read the game's state in the same frame
as the click that changed it, and raced it: a just-placed piece "picked up" (in that frame only) that
the game then dropped, and a list "loaded" whose 32 rows were still blank, with a storeroom count still
climbing. Now (`Core/Settle.cs`), a state only counts once it has read the same over a few frames:
- **The list has filled in**: its rows have names, as many as the storeroom has pieces (up to the 200
  the component holds), and the storeroom's count, the list and the storeroom pages loaded read the
  same for 5 frames and 0.25 s. Waited for when the tab opens. After a placement it's watched every
  frame while the new piece is being moved (0.3.2), once the storeroom has one piece fewer, and its
  names are read again then, so the next piece rarely waits for it; after 4 s it goes on anyway.
- **The preview is the piece being placed**: after a storeroom row is picked, both the preview and the
  editor's active item, for 3 frames and 60 ms, before Place (0.3.0 chose Place for Doman Dogwood
  before the active item was set).
- **The placement has gone through** (0.3.2): after Place, the storeroom has one piece fewer (the
  server's answer; after 1.5 s it goes on without it), nothing is held, Rotate mode, and the new piece
  has its object in the furniture list, all steady for 4 frames and 0.12 s, before the new piece is
  picked up. 0.3.0 picked it up 50 ms after it appeared and the game let go 19 ms later. The object it
  picked was the preview's (the same address): the game keeps the preview's object for the new piece,
  and the log now says whether it did.
- **The piece is held**: the editor's active item is that piece (its object as the furniture list has
  it at the pick-up, not one remembered) for 3 frames running; 4 frames and 0.12 s for a piece just
  placed, after waiting until nothing is held (the game's own handling of a placement holds the piece
  for a moment). If the game lets go before the set-down, it's picked up again (up to three tries).
- A row search never gives up while a row could still be the piece: rows only guessed to belong to
  another piece's group were never read, so they're picked before giving up.

**Speed, roughly.** For a storeroom piece whose row is found by name: pick the row, the preview
settles (about 0.1 s), choose Place (0.3 s after the pick), the piece appears (about 0.2–0.5 s), the
placement goes through (about 0.15–0.3 s), pick it up, held (about 0.12 s), set it down (0.3 s after
the pick-up), it lands (0.1–0.5 s): about 1.8–2.2 s at Quick, plus the pause between pieces (0.15 s).
The list settling now overlaps the move. 0.3.1 measured about 2.4 s a piece found by name.

Every frame it checks: same home, permission, the editor open (or, for up to 8 s while it switches
modes, the Housing menu), not in combat/cutscene/zone change. A window that doesn't appear in time
stops the run (after cleaning up); a piece that doesn't land or take its dye is marked with why and
skipped; a few of those in a row stop the run.

## Getting to the right mode (`Core/ModeRoute.cs`)

- Nothing housing open: the Housing menu is opened by asking the housing agent to show itself
  (**unconfirmed**; you opened it through Main menu → Social → Housing in Recording 2, which isn't
  replayed). If it doesn't appear in 5 s, the run asks you to open it (only then: the "You:" line is
  no longer logged when the menu opened by itself).
- Housing menu → Indoor Furnishings `HousingMenu ← [i:0]` or Furnishing Color `[i:1]`. Indoor
  Furnishings opens the editor in Rotate **or Move** (0.3.0 opened in Move and the run, waiting for
  Rotate, stopped), so any layout mode counts as open, and Rotate (or Move) on the bar follows, pressed
  only if the editor isn't in it already.
- Move / Rotate: `HousingLayout ← [i:0]` / `[i:1]`.
- Between the dye mode and the others: close the editor `HousingLayout ← [i:-2]` or Furnishing
  Color's list `HousingGoodsStain ← [i:-1]` (**a guess**), then the other menu entry.

## Each kind of piece

**Move** (already placed): the editor's pick-up, set the transform, the editor's set-down, check it
landed. **Works in game.** Since 0.9.0 a piece placed before the run is picked, set and let go in one
step, as RemakePlace/plugin's `SetItemPosition` does (its select, write and place in one frame); only
when the editor doesn't hold it at once does the careful way follow (wait for the hold, the click gap,
then the set-down: about 0.3 s more). A piece the run has just placed, a retry and a rehearsal keep the
careful way (0.3.0 saw the game let go of a just-placed piece 19 ms after it was picked).

**Dyes that ran out** (0.9.0, as RemakePlace/plugin's `MissingDyes`): when Dye gets no confirmation
(the button greyed out), the dye counts as out for the run once that happens with none of its own dye
items in your bags, or twice; later pieces needing it are skipped at once ("Out of …") rather than
each waiting. The wait for the confirmation is 3 s (it's the game's own window: 30–200 ms in 0.8.6's
run), not 6.

**From the storeroom**, using only what has been seen to work:
1. Rotate; the furnishing list `HousingGoods`; its Storeroom tab `HousingGoods ← [i:11]` (clicked only
   when it isn't already showing).
2. Find the piece's row (below), and pick it: `HousingGoods ← [i:1, u:row]`. The game opens the
   row's menu and shows a **preview** of the piece in the room, in front of you. The plugin reads
   which storeroom slot the game picked; if it's another piece, it closes the menu and tries the next row.
3. Choose "Place": `ContextMenu ← [i:0, i:0, u:0, empty, empty]`, **without touching the preview**:
   Recording 2 placed the piece where the game's preview was. (0.2.2 moved the preview to the spot
   first, and the game then didn't place anything; that's now an off-by-default option,
   `MovePreviewBeforePlace`.)
4. Watch the furniture list for a new piece of that item (a furniture slot that wasn't there before,
   nearest the preview; the preview itself is never in that list). A yes/no that comes up is answered.
5. Move the new piece to its exact spot with the move that works in game (pick up, set the transform,
   set down, check). If the game doesn't pick it up (a piece just placed sometimes isn't ready), it
   tries twice more; after that the piece is listed as a problem, and "Try again" moves it later.
6. If nothing appears in 6 s, it tries once more (logging how many pieces were placed before and after,
   and the game's red error message if there was one); then the piece is skipped as "didn't land".
   Stand somewhere open: the game puts a storeroom piece in front of you.

**Which storeroom piece next** (0.3.2). The list lets only its first 200 rows' names be read, but every
placement takes a row out, so rows past 200 move up into them as a run goes. So the next storeroom
piece is the one, among those still to place on the same floor and in the same pass (floor pieces
before tabletop and wall-mounted ones, which still come after what they sit on), whose name is read
nearest the top of the list (`StoreroomOrder.FirstReadable`). Only when none is readable is a row
searched for (below). Moves and bag pieces keep their places in the run.

**The list's order** (`Core/StoreroomOrder.cs`, 0.3.2). The Storeroom tab lists one row per piece,
grouped by item, sorted by the item's **UI category order** (ItemUICategory OrderMajor, OrderMinor:
Furnishing 20, Table 21, Tabletop 22, Wall-mounted 23, Rug 24), then its **SubcategorySort** (the Item
sheet's column between LevelItem and ItemAction; `Unknown4` in Lumina's stable sheet), then its
**item id**. All 36 rows a user's 0.3.1 runs learnt fit this order (Simple Hingan Pillar 8230 at row
0; SubcategorySort 10000 from row 3 to 165, by item id; … Hingan Bookshelf, Table 4230, row 272). So a
piece's row is predicted as **how many storeroom pieces sort before its item** (the storeroom's own
inventory says what they are), then checked by picking it. Every row whose item is known (read by
name, or picked) bounds where the piece can be; a miss steps away from the prediction 1, 3, 7… rows in
the direction it showed, then halves what's left: one pick when the prediction is right, two or three
when it's a little off, under 20 for 445 rows when it's far off. If the known rows contradict the
order (an item that sorts differently), the search hands over to trying rows in turn. The log says,
once a run, how many known rows the order predicts.

A pick that turns out to be another piece (a probe) closes its menu in the same step: only the next
pick waits the gap between actions (0.3.1 spent 0.6 s a row, closing with a second paced click).

**0.3.5: every pick counts.** Within a group of one item in several dyes, the list's order changes
after each placement (0.3.4's log: a slot moved from row 27 to 28 as row 28's piece left), so a
particular dye has to be looked for. A row that turns out to hold another dye is placed for a piece
later in the run, of the same item and in the same pass, that wants that very dye (`RunJob.SwapTargets`:
the two swap where they go and which copy), and the first piece comes later. Rows known to hold the
piece in another dye are passed over by the order search as well.

**0.4.0: the scan.** Timing: a pause holds the step it paused (`StepQueue.Hold`, steps time
themselves with `queue.Elapsed`), so resuming carries on the wait instead of timing it out. A bag
piece's "goes into the storeroom" checks for the store first (one that settles after 3.5 s still
counts) and its backstop skips the piece rather than calling it done. Yes/no dialogs are answered
once a click gap (`AnswerYes`), not every frame. The new piece after a placement is told from the
slots read just before it (not the session's list, a second old); a picked storeroom row is
followed by its container and slot, as rows above it may come out before it's placed. A piece that
ends badly while the editor holds something sets it down (Rotate/Move) or says to press Esc
(Place). The set-down function gets 1 as its flag (it reads only `dl`). Put them back remembers
where its backup was taken (kind, territory, house) and refuses anywhere else, and puts nothing
away when the backup has no pieces for the side. The live furniture is read once a frame, the
matching only when targets, placed pieces or stock change.

**0.4.0: saving a whole home.** Saving reads, besides the furniture (`FurnitureReader`): the
interior's fixtures from `LayoutWorld.ActiveLayout->IndoorAreaData` (the interior territory's own
copy when that's missing): `Floor0` ground, `Floor1` upper, `Floor2` basement and `Exterior`, the
house's own walls, window and door ("Main", as MakePlace writes it), each with parts wall, window,
door, floor, light. A part's key is an Item's `AdditionalData` row (items of ItemUICategory 65-75,
the fixtures). The outside, from `OutdoorAreaData->Plots[plot].Fixture[0..7]` (roof, walls, window,
door, roof decoration, wall decoration, placard, fence: each a `FixtureId` and `StainId`), only in
the ward: every plot with a house is read there every 5 s and kept for the session, so a home saved
from inside has it too (its plot is the interior's `HouseId`, its ward `GetCurrentWard()`, in
`GetOriginalHouseTerritoryTypeId()`). A themed house is one whose roof or walls key belongs to a
`HousingUnitedExterior` row; it's written as one "Walls" entry with that item, dyed as its walls,
plus any part of the outside that isn't its own. A piece's material (a partition's wallpaper, a
frame's painting, an angler's canvas's fish print) is the ushort at +0xCC of its
`SharedGroupLayoutInstance.StainInfo` (MakePlace's `MaterialSlot1`), a VaseFlower number (rows
2001-2999 wallpapers, 1001-1999 paintings, 3001-6999 the four sizes of fish print), written as
`properties.material` {name, itemId}. The style comes from the interior's territory name: s1, f1,
w1, e1, r1 the districts, h1 "Minimalist", h2 "Minimalist II". From 0.7.8, inside, the lighting
level: `HousingManager.GetBrightness()` (`5 - GetInvertedBrightness()`; ClientStructs' data.yml has
the inverted one as "0-5, higher is darker", and Brio, Ktisis and others light a house at
`1 - SavedInvertedBrightness * 0.2`, six steps down to 0), only with `IndoorTerritory` set and not
between areas, written as `properties.light` 0–5 as it is (`Core/Lighting.FromGame`: anything
over 5, a wrapped byte, left out; the site lights a room at level × 20%). Unverified in game: all of it, the
+0xCC offset and the outside's table most of all.

**0.3.5: putting things back.** A restore's run moves the backup's pieces back, places what's
missing, dyes, and then puts away what's placed now and isn't in the backup (`Plan.Extras`): the
editor's Store button (`HousingLayout [i:2]`, from 0.3.3's recording), each piece picked with the
editor's own pick, a yes/no answered, and the piece watched leaving the furniture list (4 s, else
it's skipped to be put away by hand). Last, because whether storing renumbers the furniture slots
the other steps use isn't known; the editor is put back in Rotate when the run ends.

**Clearing out** (chosen in Ready). The same Store steps as putting things back, but first: a run
with clear out starts with only the Store jobs; once they're done it waits a moment, reads the
session afresh (`LayoutSession.Refresh`), matches the design again and adds its moves and placements
(`PlacementRunner.AfterClearOut`), so no furniture slot planned before a piece was stored is used
after it. The storeroom's rows are learnt again and the list made again before it's read. A swap is
the same run with its backup kept under a name (`Backups.SaveKept`); Swap back is a restore whose
put-away comes first.

**Setting fixtures** (experimental, `Session/FixtureRunner`, `Automation/FixtureTable`). Untested in
the game; it goes step by step. The windows `HousingEditInterior` and `HousingEditExterior` and their
edit containers (InventoryType 2012 and 2011) are from FFXIVClientStructs. The Housing menu's
fixtures entry ("Remodel Interior", recorded as [i:2]) is fired as `HousingMenu ← [i:entry]`, its
place among the menu's text values, only when the first two read as Indoor Furnishings and
Furnishing Color (the recorded [i:0] and [i:1]); a fixture is put in its slot with
`InventoryManager.MoveItemSlot` (bag → edit container), only when the container's slots hold what's
set in the guessed order (floors in the game's order, five parts each). The game's own way, recorded,
is a slot clicked (`HousingEditInterior ← [i:2, u:0, empty, empty, u:1]`) and a fixture chosen from
its icons (`ContextIconMenu ← [i:0, i:0, u:51218, u:0, empty]`), not decoded yet. The apply is
`HousingEditInterior ← [i:0, empty, empty, empty, empty]` (recorded), then yes to "Proceed with
fixture changes?". Everything it can't do, it asks you for, and it stops if a window doesn't appear.
Still to record: the same on a plot (Remodel Exterior), and which slot and fixture were used.

**0.3.4: making the list again.** The furnishing list doesn't show a piece that came into the
storeroom while it was open (0.3.3's log: a bag piece stored, then 170 rows searched for it one by
one). So after a bag piece is stored, and whenever neither a piece's name nor the list's order puts
it in a row, the list is made again once for that piece: its Placed tab and then its Storeroom tab
([i:11] switches between them, as the 0.3.3 recording showed), then it's read afresh. After that,
rows are tried in turn 60 at most (0.3.3: up to 600), and the piece is skipped with why. A row
already picked and found to hold the piece in another dye isn't picked again for it while the
storeroom has one in the dye wanted (0.3.3 picked the same six black partitions for every undyed one).

**0.3.3's storeroom fixes.** A row is taken only in the dye the plan chose the piece in, while the
storeroom has one in that dye (0.3.2 took any of the item, so an undyed table placed for a black one
used up a dye and the black one was left over). A placed piece's row comes out of the rows once its
slot is empty, even when the storeroom's count lags the new piece (0.3.2 kept it, and every later row
was off by one); rows are saved only while they line up with the storeroom. A bag piece stored to be
placed gets its row where the sort order puts it, the rows after it moving down (0.3.2 forgot every
learnt row each time). After reading further rows by scrolling, the list is scrolled back to the top,
since names are read again from row 0. And the sort-order search steps out from the prediction after
a miss, not from the bound the miss moved: a prediction one row off now takes two picks, not three.

**Finding a storeroom row quickly** (`Core/StoreroomRows.cs`). The list isn't in storeroom order, it
shifts and re-sorts as pieces leave it, and it's grouped by item.
- The list's row names are read without clicking (`Automation/ListReader.cs`): every row's from the
  window's own values when they hold exactly one name per storeroom piece (**unconfirmed**), else the
  200 the list component holds (0.2.3). They're **read again after every placement** (cheap: no
  clicks), and remembered rows that no longer match the name now shown there are forgotten.
- A row is only picked if its name is the piece's (or its name hasn't been read): never a row read to
  hold another piece. If a row that should have held the piece doesn't, the names are read again
  rather than scanning.
- For a piece past the names read, the list is scrolled and read again once (0.3.1's scrolling never
  showed new rows; after one such try it isn't scrolled again that run), then the row is found from the
  list's order (above).
- Last of all (the order contradicted), rows past the names are picked, skipping the rest of each item's group.
- Rows picked are saved per house between runs while the storeroom holds exactly the same pieces.

**From your bags** (0.3.2, `Core/PlacingHelpers.cs` `BagRoute`). Placing straight from the bag got
"Unable to place item." in 0.3.0 (the editor held the piece, the set-down was refused, nothing
placed), while placing from the storeroom works. So inside a house:
1. The item's menu (opened with `AgentInventoryContext`, which worked in 0.3.0), **"Store"** (entry 1
   in "0:Place | 1:Store | 2:Dye | …"; in other languages the game's own words, below, untried there;
   a menu without it goes to step 3).
2. Wait until the storeroom holds one more of it and your bags one fewer (3 frames, 0.1 s), then the
   storeroom's steps above place it and move it to its spot. Since 0.9.0 the next bag pieces of the
   same floor and pass are stored right after it (`StoreAhead`, up to 10, while the storeroom has room
   for more than one), so the list is made again once for them all; each is marked `StoredAhead` and
   placed with the storeroom's steps in its turn. One that can't be stored ahead (gone from the bags,
   no "Store", refused) is simply placed from the bags in its turn, as before.
3. If the game won't store it (an error message, such as a full storeroom, or no change in 3.5 s):
   the bag's own way, "Place" `[i:0, i:0, u:0, empty, empty]`, the editor holds it, the plugin aims it
   and calls the editor's set-down with **place anywhere switched on for that one set-down** (put back
   after).
4. If that's refused too, you put it down; the plugin then sets it at its spot.

In a garden (no Storeroom tab to place from) it starts at step 3. With "Place pieces from my bags for
me" off, the run waits for you to put each down and then sets it.

**Dye**, as Recordings 1 and 2:
1. Furnishing Color mode.
2. Open the dye window on the piece (0.9.0, as RemakePlace/plugin's `SetItemDye`): the editor's
   pick-up on it, then, once the editor's active item is this piece (1 s at most), the housing agent's
   "interact" with the picked item (`HousingActions.OpenDyeWindow`: the "selected housing item" getter
   and `AgentHousing`'s interact, RemakePlace's patterns), which opens the window on it. 0.8.6 waited
   a second for the pick alone to open it (it doesn't in Furnishing Color) and then walked the list,
   20 s a piece. If the window still isn't open after 1.5 s, through Furnishing Color's list as in
   Recording 2: rows with the piece's name are picked (`HousingGoodsStain ← [i:0, u:row]`) until the
   editor's active item is this piece (a row that picks another piece is passed at once, not after
   1.5 s), then its menu `[i:1, u:row]` and "Paint".
3. Find the dye's row in the window's list: its names start at value 203 "No Color" (row 0) and end
   where "No Color" comes round again, so Snow White is row 1 and Soot Black row 6. If the dye isn't
   in the list shown, the shade is picked first (`[i:4, i:shade]`, RemakePlace, **unconfirmed**).
4. `ColorantColoring ← [i:5, i:row, i:stain, i:0]` (recorded: Snow White [5, 1, 1, 0], Soot Black
   [5, 6, 6, 0]; the third value matches both row and Stain id there), Dye `[i:0]`, confirm
   `MiragePrismMiragePlateConfirm ← [i:0]`, close `ColorantColoring ← [i:-2]`; check the dye changed.
   While the window is open the game writes its preview into the piece's stored dye
   (`HousingFurniture.Stain`): the dye chosen, or a shade's first dye when the shade is switched (0.8.2's
   rehearsal read Sunset Orange there just after switching to the browns), and puts the piece's own back when the
   window closes without Dye. So the dye before is read with the window closed, and the dye after only
   counts with the window closed and the reading steady for 0.5 s (`DyeHeld`).

**How many to dye** is one calculation (`DyeCounts.Needs`) for the run's first line and the dye phase:
each piece you have, placed (the dye it has now) or still to place (the dye it will have: storeroom
pieces keep theirs, bag pieces are undyed), counted when that differs from the design's. A dry run's
dye phase predicts, as the first line does (0.3.0 said "then 9 to dye" and then "0 pieces to dye"); a
real run's counts only pieces that got placed, and says so when that's fewer.

The dye window chooses which dye item it uses (both recordings used "Standard Spectrum Dye"), so a
piece isn't skipped because the Stain's own dye items aren't in your bags; a piece the game won't dye
is skipped as "dye missing".

## The menus' words in other languages

The entries chosen by their words ("Place", "Store", "Paint") come from the game's own text, the
Addon sheet in the client's language (`Core/MenuWords.cs`, `Game/GameWords.cs`, used by
`UiTable.PlaceLabels`, `StoreLabels` and `PaintLabels`): rows 1751 (a bag item's menu) and 6229 (the
housing windows') for Place, 6840 for Store, 6272 for Paint, each checked against its English words
first, so rows moved by a patch fall back to the built-in words. Settings' "Place" words override.
A recording from a German, French or Japanese client (placing from the bags and the storeroom, and
dyeing through Furnishing Color's list) would confirm them: its "menu entries" lines show the words.

## What's most likely to need fixing next

1. **The list's order** for categories no log has shown yet (tabletop, wall-mounted, rugs, paintings):
   the search notices a contradiction and falls back, and the log's check line says how well it predicts.
2. **"Store" from a bag item's menu** and the storeroom's steps after it (unverified in game).
3. **The pace**: whether the shorter settle after a placement holds everywhere.
4. **Opening the Housing menu** from nothing (Indoor Furnishings now accepts Move).
5. **Picking another dye shade**, and closing Furnishing Color.

## Rehearsal

A rehearsal (Ready's Preview → Rehearse the run; [DETAILS.md](../DETAILS.md#rehearsal)) runs the
same scripts with every commit point backed out, in `Session/PlacementRunner.Rehearsal.cs`,
`ErrandRunner.Rehearsal.cs` and `FixtureRunner.Rehearsal.cs`:
- **Moves**: `MoveSteps` as usual; at "Picked up?" `RehearseHold` replaces `SetDownSteps`: the held
  piece isn't shifted, and `UiTable.LetGo` (`HousingLayout ← [i:0]`, the Move button) lets go of it.
  By hand, Move while holding drops what was changed, as Esc does. 0.7.0–0.7.1 set the transform to
  the spot and wrote the old one back before letting go, and the check found a piece away from where it
  was; nothing is shifted now. Still held → you press Esc.
  Its stored spot and turn (`FurnitureReader.Stored`) are checked against what they were.
- **Storeroom**: `StoreroomAttempt` up to the preview, then `RehearsePlace`: `AimPreview` at the spot,
  "Place" found in the menu, `MenuCancel`, and a check that the menu closed and nothing was placed (the
  game keeps the row's preview, as the editor's active item, until another row is picked: recorded).
- **Bags**: `RehearseBag`: the item's menu, "Store" (or "Place") found, `MenuCancel`.
- **Clear out**: `RehearseStore`: the storeroom button ([i:3]) pressed, the piece not picked (that mode's pick stores it).
  With a storeroom short of room, the pieces it has no room for are listed last (`ClearOutSplit`), as a run does.
- **Dyes**: `DyeSteps` up to `DyePick`, then `RehearseDyeEnd`: `DyeClose` (and `DyeConfirmNo` if it
  asks), the stain checked unchanged; dyes of pieces not placed are only counted.
- **Errands**: `RehearseBuy` / `RehearseListing`: the purchase sent, the yes/no read and checked with
  `MarketRule.QuestionMatches`, closed (`GameUi.Close(SelectYesno)`, as a mismatched question is), and
  bags and gil checked unchanged; `RehearseWithdraw`: "Retrieve from Retainer" found, `MenuCancel`.
  Trips as planned (`NextTripSteps`), placing rehearsed after the last (`runner.Rehearse`).
- **Fixtures**: `PutInSlot` as usual (`MoveItemSlot` into the edit container), then `RehearseApply`:
  the window closed (`GameUi.Close`, held for Continue as every fixtures step), a question left to you,
  and the fixtures and your bags checked.

The guard (`Core/Rehearsal.cs`): `GameUi.Fire` refuses what `RehearsalRules.Commits` names (SelectYesno
[i:0], ColorantColoring [i:0], MiragePrismMiragePlateConfirm [i:0], HousingEditInterior/Exterior
[i:0, …]) and `HousingEditor.Place` refuses the set-down while any runner rehearses
(`Rehearsals.Register`); `Rehearsals.Allow()` lets the door's yes through. What it would have done
and what didn't work go in `Rehearsals.Report`. Care, backups, swaps, place anywhere and fixture
ticks are skipped for a rehearsal.

**The let-go** (`HousingLayout ← [i:0]` while holding): by hand, a piece turned while held and then
Move (or Esc) went back as it was. A recording of an unshifted piece showed mode Move, nothing held,
the piece where it was. 0.7.1's rehearsal of a shifted one (old transform written back first) read
the piece away from where it was after; why isn't known, so a rehearsal never shifts what it holds.

**Getting about** (0.7.6, `Game/Movement.cs`): before a walk of `RideFrom` (35) yalms or more,
`Movement.SpeedUp`: the mount roulette (GeneralAction 9) where `TerritoryType.Mount` and not in combat,
else Sprint (4); Sprint again while walking on foot every 3 s if ready; `GetDown` (Dismount, 23) at
the end of `WalkSteps`. Each only when `ActionManager.GetActionStatus` is 0; the GeneralAction row's name
is logged on first use. Switch: `ErrandsRide`.

**The market city** (0.7.9, 0.7.10): `ErrandGame.Hub(config.ErrandsHub)` → `MarketCity`: Limsa (aetheryte
and boards in 129) or Ul'dah (aetheryte in 130, boards at the Sapphire Avenue Exchange in 131, an
aethernet hop: `AethernetSteps`, `Lifestream.Aethernet`). `HubSteps(near)` skips it all when
`AtABoardCity` with a board or bell in sight; for a world visit, when `AtAWorldVisitAetheryte`. `TeleportSteps` waits for `Settled` and `!Travel.Busy`, and asks
Lifestream again every 2 s for 20 s. `WalkSteps`' last step waits for `Movement.Landed` (not mounted,
mounting or jumping) 0.9 s after Dismount; a vendor's `Opens` talks to them again (twice) when nothing
opened in 2.5 s. A vendor's question with another price: no, and `Finished()` for that item. The
board's results: `SearchResultIndex` can't read them (the names aren't in the window's values), so
index 0, checked by `ListingsItem()`. Doors: `ErrandGame.InFrontOf(door, before)`: straight out along the
door's yaw (`Rotation`), on your side, walked all the way (within 1 yalm); the chambers' door at 1.5,
then 0.8 if it doesn't answer in 2.5 s.

**The board's listings** (0.7.11): `BoardListings` listens to `IMarketBoard.OfferingsReceived` (each
batch's `ItemListings`: `PricePerUnit`, `ItemQuantity`, `TotalTax`, `ListingId`, `IsHq`), rows in the order
received; `Expect(item)` before the result is opened; a batch more than 1.5 s after the last starts the
list afresh. `ErrandGame.Listings` uses them once settled (0.6 s), else the item search proxy;
`BoardListings.Open` gives none 6 s after asking when nothing came. A vendor's other price:
`MarketRule.AskedCost(question, n)` (the largest number besides the quantity), yes while `Spent + asked <=
agreedGil`. The front door: `ErrandGame.OutsideOf(door)`, the nearest of four spots around it (0.7.12:
only when no way to it is recorded).

**The way to the front door** (0.7.12, `Core/DoorWays.cs`, `Session/ErrandRunner.DoorWay.cs`): a ward's
navmesh has no houses (vnavmesh builds it from the zone's collision; the houses are placed later), so
`PathfindAndMoveTo` to a door runs into walls. Settings → Getting → Way to your door records one:
`RecordWay` (outside), `TickWay` each frame keeps a spot every `DoorWays.Spacing` (0.75) yalms, up to
`MostSpots` (400), and notes the "Entrance" within `EndsAtDoor` (5); the zone changing (going in) saves
it if it ended at a door (`DoorWays.Keep`, replacing one to the same door), as does Stop beside one.
Kept in `Configuration.ErrandsDoorWays` by ward zone; the end's spot finds it again
(`DoorWays.For(ways, territory, door)`), so a plot's way serves every ward. `EnterSteps` →
`ApproachSteps`: with a way, `Vnavmesh.Follow` (`vnavmesh.Path.MoveTo(List<Vector3>, false)`, no
pathfinding) from the spot after the nearest (`DoorWays.From`; a resume after a pause follows on from
where you are); else `MoveTo` the spot out front. No nearer by 0.5 yalm in 4 s, or stopped short: once
more, then `HandOver` (a "You: Walk to your door and go in" step, the rest skipped). Within reach of the
door counts as there. "Use your door" waits for `Movement.Landed` (getting down first), uses the door,
and again after 2.5 s with no question (up to 3). `WalkSteps`' last step waits for `Landed` even when
not ridden. A board found without an item: `emptyBoards` (item, world) → `ErrandOptions.ListingAllowed`;
`emptiedThisTrip` keeps a trip that got nothing but those from stopping the run.

**Fresh listings** (0.7.12): `MarketListing.SeenAt` is Universalis's `lastReviewTime`; `MarketUnits`
chooses from those within `MarketListing.TrustedFor` (12 h), all of them when those aren't enough.
`ErrandGame.Listings` uses only `BoardListings` when hooked (an answer with nothing in it is none
listed); the item search proxy only when it isn't.

**The editor's bar** (0.7.12): `EnsureMode`'s bar press waits (5 s) for `HousingLayout` to be open,
and opening the furnishing editor waits for its bar too: the editor opens in the mode last used
(0.7.11's recording: mode Store at 403.85 s, `HousingLayout` open at 404.02).

**0.8.0**: `ErrandRunner.UseUntil(what, find, window, opened)` (bell, board): `FreeToUse()` (landed, no
Occupied*/OccupiedSummoningBell/OccupiedInEvent/Casting), then `Interact`, again every 2.5 s, 4 times.
`SearchSteps`' "listings come": the window's title ([2]) or the proxy's item tells another item's
results apart (closed, searched again after 0.6 s, 3 searches); no window: the pick again every 2.5 s
(3 picks), then a search again. `ListingSteps`: `BoardListings.Gone(listingId)` after a purchase (rows
after it move up); `Missed(why)` (no question in 5 s, another row's question, nothing in the bags in
10 s) → the next listing after this try's last step (`retryPending`), 3 misses then left.
`BoardListings.Open` gives none after 10 s. Travel: the asking steps time out at 25 s (their own 20 s
decide); `Again(ref lastTry, ref asks, ask)` in each arrival asks Lifestream again when settled, idle
and not there after 12 s (25 s for a world), twice. `Vnavmesh.Stop` also calls
`vnavmesh.Nav.PathfindCancelAll`. `EnterSteps(looked)` waits 5 s for the door; with no way known,
`StartRecordingWay(errands: true)` and a "You: walk to your door" step. `FurnitureReader.ReadInterior`
adds `FurnitureVector` slots with no spawned object (row 0x20000 | Id, no instance) and returns null when
something's placed but nothing reads. `UiRecorder`: 60 min, 8,000 lines, noise filtered
(`IsNoise`), words cut at 160, the state written only when it changes, `spawned=` in it.

**Currency vendors** (0.7.14): `VendorData` reads `SpecialShop` (`Item` entries: `ReceiveItems[0].Item`,
`ReceiveCount`; one `ItemCosts` entry with `ItemCost` and `CurrencyCost`); a cost item below 8 is resolved
by `UseCurrencyType` (4: `TomestonesItem`), otherwise skipped; the currency must be an Item of UI
category 100. NPCs reach them through ENpcData 0x1B…, TopicSelect, PreHandler and CustomTalk as gil
shops do (`NpcDataKind.SpecialShop`). `ExchangeOffer` (the vendor, currency, cost, gives);
`ShoppingPlanner.Build(..., exchanges, balance, currencyAllowed)` after gil vendors, before the board,
sharing out each balance (`ErrandGame.CurrencyCount`: the item count or the Currency page);
`SourceKind.Exchange` / `StopKind.Exchange`; `ErrandPlan.CurrencyCosts`, agreed at Start
(`currencyAgreed`). At the vendor: `OpenShopSteps(v, ErrandTable.ExchangeWindows)`, `ErrandGame.ExchangeRow`
(the window's texts against `VendorData.ExchangeShopItems`), `ErrandTable.ExchangeCurrencyBuy` /
`ExchangeItemBuy` `[i:0, i:row, i:count]` (Guess, unverified: `Key = Guess`), a yes/no naming the cost
answered yes, `ShopExchangeCurrencyDialog` left for you. `RehearseExchange` finds the row only.
`ErrandRunner.KindOf`: the home's own kind when `Owned`. `PaceProfile.ErrandGapMs` sets the errands'
`ActionGap`; the board's results wait `max(0.6 s, 1.5 s × WaitScale)`.

**0.8.1**: `ErrandPlanner`'s vendor key is `v:{territory}:{district}:{npc}` (no shop): one stop per NPC,
each `ErrandItem.Vendor` its own offer (shop, price). `AddVendor` groups a stop's items by shop: the
first by `OpenShopSteps(offer)`, the next by `OpenShopSteps(offer, [Shop], another: true)` (close
`Shop`; wait until it's gone and `FreeToUse()` for `max(0.3 s, 0.6 s × WaitScale)`, or the NPC's menu
comes back; talk again unless it did; `Opens()` picks the shop by name). `BuySteps` / `RehearseBuy`
take the item's offer. Stop order (`KindOrder`): vendors, exchanges, retainers, the market; retainers
first with `ErrandOptions.BellAtHome`; the market by `HomeWorld` first, then by name.
`ErrandWords.Legs` puts the bell's line where the trip's first retainer stop is. The board's results:
`BoardResultsWait` (1.5 s, not paced) before the first pick and after a search again; a pick that
opened nothing is tried again after 1.2 s. NPC walks end within `NpcReach` (2 yalms). `You(...)` logs
"You: …" the first time it waits, "done, after" only past 0.25 s. `LearnPrice` returns whether it was
new; the rehearsal's note says "Learnt" only then. Placing: `InventoryReader.StoreroomLoaded` is true
once the loaded page count has held 300 ms; "It lands at its spot" times out at 5 s; `BagSteps` goes
to `HeldAttempt` when `StoreroomAutomatic` is off; `ListSettles` and `WatchList` read every 40 ms.
`FurnitureReader.ReadGarden` takes the plot from `EditingFurnishingsOfPlot` else `HouseUnit.PlotIndex`
(null when not 0-59) and checks the list against the spawned objects' `BaseId`;
`GameData.ItemOfSlot` tries the memory Id with 0x20000 and 0x30000, then as it is. `PlacementRunner.Tick`
stops a garden run when `ctx.Plot` is another real plot. Fixtures: the wait after Apply ends only on
the yes/no; "The fixtures are set" answers a late "Proceed…" question yes (once a second).

**0.8.2**: `HousingContext.Read` (inside): the district from the interior's name, else `HouseDistrict(id)`
(the house id's bits 32-47, the ward's TerritoryType, "s1h1"; else `GetOriginalHouseTerritoryTypeId`),
kept per house: "h2i2" (a remodelled interior) names none. `MarketRule.WhyNot` allows `Leeway` (25%)
over the planned price for one and over the item's gil left; the ceiling and the gil agreed still bound
it. `ShoppingLine.Exchangeable` (Craft/Unknown lines: the best currency vendor, any currency) →
`LeftReason.Currency` with `ErrandLeft.Exchange`. `PlacementRunner.EditorCloses()` after
`ModeClick.CloseEditor`: the Housing menu back, else after 1.5 s `HousingGoods` closed and `LayoutClose`
again (as by hand: HousingGoods [i:-1], HousingLayout [i:-2]), then `HousingLayout` closed outright;
closed with no menu after 0.8 s: `HousingActions.OpenHousingMenu()`. 8 s in all.

**0.8.3**: other districts' vendors. `Reachable`: a district vendor not of your home's district (or
from an apartment) is reachable when `ErrandGame.Attuned(Residential.CityAetheryte(district))`
(8 Mist, 2 Lavender Beds, 9 Goblet, 111 Shirogane, 70 Empyreum: the Aetheryte rows, which are
Lifestream's `ResidentialAetheryteKind`). `VendorTravelSteps` → `DistrictSteps(district)`:
`Lifestream.GoToHousingAddress` with its `AddressBookEntryTuple` (name "", world = current, city =
the aetheryte, ward 1, House, plot 1, …) via `Lifestream.GoToWard`; arrived when
`ErrandGame.WardDistrict()` is the district and settled for 1.5 s, then `Lifestream.Abort` (it walks on
to the plot) and the nearest NPC of the vendor's name is walked to. Asked again after 20 s (twice).
`ErrandPlanner.PlaceOrder`: vendors of `PreferDistrict` first, then other districts', then by zone.

**0.8.4**: the dye window's preview. `DyeSteps` keeps the piece's dye from before the window opened
(read again just before the pick that opens it); `RehearseDyeEnd(job, key, before)` logs what the dye
reads with the window open (the preview) and checks with `DyeHeld` that it's `before` again once the
window is closed (0.5 s steady, 3 s at most). A real run's confirm step no longer ends on the dye
reading the target while the window is open (that's the preview): it waits for the question (Yes),
or the window closed by the game with the dye in place; then the window is closed and `DyeHeld` waits
for the dye (4 s), skipping as `DyeFailed` after a Yes, else `DyeMissing`. `SayStillHeld` ignores
Furnishing Color's active item (the piece selected for dyeing, not held).

**0.8.6**: `ErrandRunner.guessByHand`: set before a held `Guess` click (the retainer's item menu opened,
or a baseline step before the exchange); while `AwaitingContinue` on it, the item's bag count going up
calls `queue.Continue()`. The retainer's choose step returns Done when the count went up (the next step
counts it), and opens the menu again once when it closed with nothing taken (8 s limit). The exchange step
counts what came by hand (bags and currency against its baseline) before working out the rest.
Also 0.8.6: `RunSequence.Build` places `FromStoreroom` before `FromInventory` in each late group;
`BagSteps` goes to `HeldAttempt` while `StoreroomRefuses()` (a refused store sets `refusedAtRoom`);
`heldRefused` (set when a held set-down is refused, reset at Start) sends later bag pieces to
`ByHandSteps`. `StoreSteps(job, toBags)`: with `StoreroomRefuses()` and free bag slots, or after the
storeroom refuses, `EditorMode.ToBags` and `UiTable.LayoutToBags` (0.7.2's recording) instead of Store.
`Update` restores with `session.Plan.Extras` whatever the backup's count. `ReadAgainSteps` waits for
`session.StoreroomLoaded` inside (15 s, then a warning; 45 s limit).

**0.8.5**: `DyeShownCheck` between the pick and Dye: `HousingEditor.ActiveItem` must be the piece and its
stored dye the target (the window's preview); after 1 s still its own dye → Dye anyway (no preview seen),
another dye → `Skip(DyeFailed)`. The already-dyed test is in "Pick" (window closed, item checked). The
confirm step skips (`DyeMissing`) unless Yes was pressed or the game closed the window with the dye in
place; "The confirmation closes" skips after 4 s; `DyeHeld` settles 1 s and never gives up while the
reading is right; `FireOrSkip` for the dye window's clicks; `DyeCleanup` says no, then closes the window,
and waits (`CloseDyeWindowAgain` every 1.5 s). Pick-up and store pick skip when `HousingEditor.Mode` isn't
Rotate/Move (Store); `HeldCleanup` fires `UiTable.LetGo` for a placed piece (`editor.Place` only without
the bar); `StoreCleanup` closes a yes/no left up; "Check placed" waits 4 s for the counts (8 s limit,
SkipJob). `MarketRule.BoardQuestionMatches` (largest number = cost ±1 or cost without tax; the quantity
in it when over 1) and `MarketRule.NamesItem` (each name word's first four letters) in ErrandRunner; the
board's buy step closes a yes/no left up; "comes to your bags" waits while the gil has gone, and
`GilLate` (1.5 s) for the gil after the item. `HousingEditor.State` reads only once both patterns were
found. `Plugin.OnUpdate` reads the context in its own try (`StopEverything` on failure).

**0.7.13**: `ErrandPlanner.Pack` cuts each stop's units (by `Key`) into pieces that fit a trip, then
packs the pieces biggest first into the first trip with room (first-fit decreasing); a trip keeps the
plan's order. `NeedKind.Fixture`: `Shopping.FixturesToGet` (the plugin sets it to `FixtureRun.Plan()`'s
`NotOwned` items, read at most every 2 s, part of the plan's cache key); `VendorData` keeps gil shops'
fixture items too (`GameData.IsFixtureCategory`, item UI categories 65-75). `VendorOffer.UnlockQuests`
(GilShopItem's QuestRequired) and `ErrandGame.QuestsDone` (QuestManager) in `Reachable`. A shop's
question with another price (`MarketRule.AskedCost`, divisible by the quantity): `Shopping.LearnPrice`
→ `Configuration.ErrandsVendorPrices`, applied by `Shopping.Offers`. `DoorWay.Plot` is read inside
after recording (`TickWay` waits up to 15 s for `HomeHere`); `DoorWays.HasFor(ways, wardTerritory,
plot)` (a way with plot 0 counts for any plot until `LearnWayPlot`). The rehearsal's dye stand-in takes
a dyed placed piece first, then `GameData.Dyeable`.

**Vendors in a ward's divisions**: `VendorData` keeps every spot of a selling NPC
(`SpotsOf(name, territory)`), not only the offer's (the first per territory). `VendorTravelSteps` walks to
the NPC in sight (`ErrandGame.NpcOrNamed`: by id, else by name), else the nearest such spot.

**The bar's put-away buttons** (0.7.2's chamber recording): `HousingLayout ← [i:2]` is "Place selected
object in your inventory" (mode 3, `EditorMode.ToBags`, `UiTable.LayoutToBags`); `[i:3]` is "Place
selected item into your storeroom" (mode 6, `EditorMode.Store`, `UiTable.LayoutStore`). Until 0.7.3
`LayoutStore` was [i:2].

**A rehearsal's dye stand-in**: `RehearseDyeOncePlaced` (a dye job with no slot): the first time per
dye, a live piece with `GameData.Dyeable` (the Item row's `DyeCount`), not in that dye, dyed ones
first, gets `DyeSteps` as a stand-in `RunJob` (in `standIns`: its `Would` isn't counted again).

**Chambers for the errands**: `ErrandGame.HomeHere` marks a room in a house (`RoomNumber > 0`, not an
apartment or the workshop) as `HomeRef.Chamber`, yours (`CompanyHouse`) when the company's estate has
its ward and plot and you may decorate. `HomeRef.WardTerritory` is the house id's bits 32-47 (0.7.5; the game's original house territory, read in chambers, wasn't the ward's), and `ErrandGame.WardCheck` says what "outside your home" compared when it waits over 20 s. `EnterSteps` then waits for the house's hall
(`ErrandGame.InHouseOf`), and `ChamberSteps` goes up (0.7.4, from 0.7.2's chamber recording): the
door found by `ErrandTable.ChamberDoorWords` in its name ("Entrance to Additional Chambers";
`ErrandGame.NearestContaining`), walked to (vnavmesh), used; its SelectString's `ChamberMenuWords`
("Move to your private chambers", [i:1]: in with no question, 0.7.5's recording); a question that does
come is answered yes only for `ChamberYesWords` ("Retire to your own chambers?", through
`Rehearsals.Allow()` as the front door's). Anything else hands over to you (logged), and the run waits for you to be in.

**Clear out and the storeroom's room**: `InventoryReader.StoreroomRoom(side)` (the loaded pages' `Size`
less the occupied slots), `ClearOutSplit.Split` at the start, `PutOffClearOut` before each first store
(room 0, or a refusal at this room → the rest wait), `PlanClearOutLater` at the end (after dyes and
Put them back's pieces; found again by item and position within 0.25). `StoreSteps` decides in its
wait: still placed after a toast and 1 s, or after 3.5 s → skipped; with a toast or room 0 it records
`refusedAtRoom`, and later stores aren't picked while the room is unchanged. Since 0.9.0 a refusal
while the storeroom has room isn't taken for a full one: it's the piece ("Unable to remove. Another
furnishing has been placed on that item."), and the piece is moved after the other pieces put away in
that stretch (`DeferStore`/`MoveLater`, 3 times at most, not counted as a failure). Clear out and Put
them back's pieces are put away tabletop and wall-mounted first (`GameData.GoesOnSomethingElse`), and
a piece whose furniture slot changed is found again by item and spot within 0.25.

**A storeroom pick the game refuses** (0.9.0): the "Pick row" click clears the last error message; if
the row then reads empty and "Unable to place item." comes within 0.4 s (or comes with no preview while
waiting for it), the piece is skipped at once with that message (`Refused`). 0.8.6's Showcase waited
6 s for a preview, and a rerun took the empty row for a changed list. The fixtures window's close: a recording of putting a fixture in its slot and closing the window
without applying, saying whether it asked anything and whether the fixture went back to the bags.

## For the window (the run's state)

`PlacementRunner`: `State`, `DryRun`, `Phase`, `PhaseProgress(phase)` → (done, problems, total),
`Current` (the piece), `StepLabel` and `StepProgress`, `WaitingForYou`, `AwaitingContinue` /
`Continue()`, `TimeLeft`, `Problems` (piece, name, kind, reason), `DyeShortfall`, `RetryProblems()`,
`LastBackupPath` / `RestoreBackup()`, `StoreroomRowsKnown`, `Pause`/`Resume`/`Stop`/`SkipCurrent`,
`Rehearsal` / `Rehearse(…)` / `HoldingALetGo`.
`LayoutSession.MissingDyes()` lists the dyes to buy before a run.

## Sending diagnostics

Settings → Help → Record what I do → **Start**; do the thing that went wrong by hand (and, with the
Storeroom tab open, press **Write down the windows' values**); **Stop recording**; **Copy recording +
log**.

## Errands (experimental)

"Get everything for me" (`Session/ErrandRunner.cs`) uses the same step queue for its trips: teleports
and world visits through Lifestream's IPC, walking through vnavmesh's, talking to NPCs and using bells,
boards and doors with the game's `TargetSystem.InteractWithObject`, and the windows' callbacks as
above. Every window, callback and word it uses is in `Automation/ErrandTable.cs`. Recorded by hand:
a gil shop's purchase (`Shop ← [i:0, row, count, empty]`), choosing a retainer (`RetainerList ←
[i:2, u:row, empty, empty]`), "Retrieve from Retainer", buying a listing (`ItemSearchResult ← [i:2,
row]`), and each one's question; a purchase's question is checked against its price before yes. The
ones still guessed (opening a retainer's item menu, the board's result row, the door's and the
board's names) wait for Continue until you trust them. NPCs' words are clicked through with `Talk ←
[]` (recorded); the board's search is `ItemSearch ← [i:9, empty, empty, s:name, s:name, empty,
empty, empty]` (recorded), sent by `GameUi.FireValues` (it holds text), and the listings opened are
checked to be the item's before anything is bought. The market board's listings are
read from the game's own record of them (`InfoProxyItemSearch`), not the window. See
[DETAILS.md](../DETAILS.md#getting-everything-for-you-experimental) for what it does and what would
confirm each guess.
