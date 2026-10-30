# Housing Studio (Dalamud plugin)

Furnish your FFXIV house from a design on [Housing Studio](https://github.com/koko-puffs/kokos-tools):
load its `.build` (or any MakePlace `.json` layout), see every piece matched to what you have, and
press **Start placing**: the plugin moves what's placed, places what's in your bags and storeroom at
its exact spot, then dyes everything to match.

> **Status: 0.2.4, working in the game, being made quicker.** Moving placed pieces and placing from
> the storeroom (the game places the piece, then it's moved to its spot) work in game. 0.2.4 aims at
> about 2 s per storeroom piece: no fixed waits, the list's names read again after every placement,
> and a quicker default pace. Still unverified: finding rows past the list's first 200 names, setting a
> bag piece down without a click, and opening the Housing menu from nothing (see
> [docs/AUTOMATION.md](docs/AUTOMATION.md)). Keep the **backup** on, and if anything stops or feels
> slow, send the log: it ends with a **Timing** summary.

## What it does

- **Load** a `.build` or MakePlace `.json`: file picker, drag a file from Explorer onto the window,
  or pick from recent files. The last one reopens when the game starts.
- **Match** every piece to what you have, nearest first and the right dye first: **in place**,
  **to move**, **in the storeroom** (the design's dye preferred), **in your bags** (bag, row, slot),
  or **missing**, with a **copyable shopping list** (pieces, the dyes to apply and the dyes to buy)
  and a checklist.
- **Start placing** runs three phases, one piece at a time at the pace you set (Quick 0.15 s,
  Steady 0.4 s (the default) or Careful 1.5 s between pieces, ± 25 %; at least 0.25 s between any two
  clicks in the game's windows):
  1. **Move**: placed pieces are picked up and set at their spot with the editor's own functions.
  2. **Place**: storeroom pieces are picked in the furnishing list and placed where the game puts
     them, then moved to their exact spot; pieces from your bags are placed from the item's menu, the
     held piece is aimed and set down. Each is checked to sit exactly at its spot.
  3. **Dye**: every piece whose dye differs is dyed in the editor's dye mode with the game's dye
     window, using dyes from your bags. Pieces whose dye you don't have are skipped and listed.

  Floor pieces go first, then what sits on them or hangs on walls. Pause, resume, skip, stop
  (also `/hstudio pause|resume|stop`), and per-phase progress with the step being done.
- **The Housing menu, the editor's modes and the Storeroom tab are opened for you** (Rotate to
  place, Furnishing Color to dye, through the Housing menu as you would). Only if it can't find its
  way does the run ask you, in the window and in chat.
- **The storeroom list** isn't in storeroom order, so the run finds a piece's row by its name in the
  list when it can read the list, else picks rows (one per different piece, skipping the rest of
  each group); every row it learns is kept for the house until the storeroom changes.
- **When it's done**: what didn't work and why, the dyes you're short of, "try those again", and
  "restore the backup" to put everything back as it was before the run.
- **Safety**: only in a home you may decorate, with the furnishing editor open; it stops on leaving,
  a zone change, combat or a cutscene, losing permission, a window that doesn't appear in time, the
  furniture list changing under it, or a few pieces in a row failing; whatever a piece opened is
  closed when it stops. A backup `.build` of what's placed is saved before every run.
- **Inside and the garden separately** (garden coordinates are turned to match your plot).
- **Markers in the world** where each piece goes, which way it faces, and the one being worked on.
- **Filters** by floor, "within N yalms of you" (a room), state and name; placing follows them.
- **Dry run**: walks every piece, marking each spot and logging what it would do, touching nothing.
- **Activity log** of every step, with the windows and values used, copyable.

### What it doesn't do

- **Walls, floors, lights, the house's outside**: listed ("set these yourself").
- **Removing a dye** (a piece dyed in your house but undyed in the design): listed.
- **Pieces the game refuses** (floating, sunk or overlapping) unless **Place anywhere** is on in
  Settings, which turns off the game's placement check for the run (as MakePlace does).
- **The garden's storeroom**: you put those pieces down, the plugin sets them at their spot.
- **Paintings' and fish prints' pictures, aquarium fish, a partition's wall material**.

## Using it

1. `/hstudio`, then load a design (Design page).
2. Go inside your house or apartment (or to your plot for the garden), with permission to decorate.
3. Housing → **Indoor Furnishings** (or Outdoor), which opens the editor and the furnishing list.
   Press **Rotate**.
4. Place page → **Dry run** to see it, then **Start placing**. The first time, switch on **Step by
   step**: it holds before every click and shows what it's about to do; press **Continue**
   (or `/hstudio continue`).

If you'd rather put pieces down yourself, switch off "Place … for me" in Settings: the run then waits
for you to put each piece down anywhere and sets it at its spot.

## Sending diagnostics

If a run stops at a step, or does something unexpected:

1. Settings → Diagnostics → **Start recording**.
2. By hand: place one piece from your bags, one from the storeroom (pick it in the furnishing list's
   Storeroom tab, choose Place), switch the editor to its Dye mode and dye one piece. Clicking the
   editor's mode buttons and the Storeroom tab while recording helps too.
3. **Stop recording**, then **Copy recording + log**, and paste it in an issue or a message.

The recording lists every window that opened and closed, every click's values (yours and the
plugin's), the menus' entries and the housing state; that's what's needed to fix the table of
windows and clicks (`HousingStudio/Automation/UiTable.cs`). If your item menu's "place" entry has
other words, type them in Settings → Diagnostics. More in [docs/AUTOMATION.md](docs/AUTOMATION.md).

**Before you use it:** plugins that act in the game for you are against Square Enix's terms of
service; this one only works on your own furniture in a home you may decorate, through the game's
own windows, at a human pace, but the choice is yours. Placing binds a piece to you and dyeing uses
up the dye, as doing it by hand does. This plugin is installed from a custom repository, which
goatcorp doesn't review.

Install: see [docs/PUBLISHING.md](docs/PUBLISHING.md#3-install-it-in-the-game) (custom repository URL
`https://raw.githubusercontent.com/koko-puffs/housing-studio-plugin/main/repo.json`).

## Developing

Requirements: the .NET 10 SDK, and Dalamud's assemblies: either XIVLauncher with the game run once
(Windows: `%AppData%\XIVLauncher\addon\Hooks\dev`), or the release zip unpacked anywhere with
`DALAMUD_HOME` pointing at it:

```sh
curl -L https://goatcorp.github.io/dalamud-distrib/latest.zip -o dalamud.zip
unzip dalamud.zip -d ~/dalamud && export DALAMUD_HOME=~/dalamud/
dotnet test HousingStudio.Tests      # the game-free core, runs anywhere
dotnet build HousingStudio.sln -c Release
# → HousingStudio/bin/x64/Release/HousingStudio/latest.zip (and the DLL for a dev plugin)
```

It builds on Linux and macOS too (for checking); it only runs in the game on Windows (or Wine).

```
HousingStudio/
  Core/        game-free, unit-tested: reading .build/.json, axes and turns, plots, floors,
               dyes, matching, the shopping list, pacing, backups; and for the automation the
               step queue (StepQueue), the run's order and dye planning (RunSequence),
               callbacks as data (UiCall) and menu/list helpers (Menus).
  Automation/  UiTable (every window and click, with sources), GameUi (reading and clicking
               windows), UiRecorder (diagnostics recording).
  Game/        GameData (Lumina sheets), HousingContext (where you are), HousingEditor (the
               editor's state, pick up / set down), HousingActions (item menus, the dye window,
               aiming the held piece), FurnitureReader, InventoryReader, PlaceAnywhere.
  Session/     LayoutSession (the design matched to the game), PlacementRunner (the run: each
               piece's steps), Backups, ActivityLog.
  UI/          the site's look in ImGui; MainWindow (Design / Pieces / Place / Settings), Overlay.
HousingStudio.Tests/   xUnit tests for Core
tools/         make-plots.mjs, icon.svg + make-icon.mjs
docs/          AUTOMATION.md, RESEARCH.md, PUBLISHING.md
```

The coordinate conventions mirror the site's `src/features/housing-studio/makeplace.ts` and
`build/model.ts`: MakePlace stores a location as `[x, z, y]` of the game's, and the turn as a
quaternion about its own up axis the other way round (`yaw = -2·atan2(qz, qw)`); garden pieces are in
the plot's own coordinates.

After a game patch, see [docs/PUBLISHING.md](docs/PUBLISHING.md#4-things-to-know).

## Credits and licence

The housing editor's patterns and offsets were found by the MakePlace plugin (jawslouis) and are
kept current by its forks (RemakePlace, DisPlace); the furnishing dye flow and its values come from
RemakePlace; placing from the storeroom through the furnishing list and aiming its preview from
Refurnish; context-menu and callback conventions from ECommons. Fonts: Geist and Geist Mono (Vercel)
and Source Serif 4 (Adobe), under the SIL Open Font License 1.1 (`HousingStudio/Resources/Fonts`).

Licensed under the GNU AGPL-3.0 (`LICENSE`).
