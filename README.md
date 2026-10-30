# Housing Studio (Dalamud plugin)

Furnish your FFXIV house from a design on [Housing Studio](https://github.com/koko-puffs/kokos-tools):
load its `.build` (or any MakePlace `.json` layout), see every piece matched to what you have, and
press **Start placing**: the plugin moves what's placed, places what's in your bags and storeroom at
its exact spot, then dyes everything to match.

> **Status: 0.3.2, working in the game, being made quicker.** 0.3.2 fixes a freeze on closing a design and the title bar's buttons;
> 0.3.0 is a new window (see
> [The window](#the-window)). Moving placed pieces and placing from the storeroom (the game places the
> piece, then it's moved to its spot) work in game. 0.2.4 removed too many waits and raced the game;
> 0.3.1 waits for confirmed state instead (the list filled in, the placement settled, the piece held
> for a few frames), with 0.3 s between game actions. Still unverified: finding rows past the list's
> first 200 names, setting a bag piece down without a click, and opening the Housing menu from nothing
> (see [docs/AUTOMATION.md](docs/AUTOMATION.md)). Keep the **backup** on, and if anything stops or feels
> slow, send the log: it ends with a **Timing** summary.

## What it does

- **Load** a `.build` or MakePlace `.json`: file picker, drag a file from Explorer onto the window,
  or pick from Recent (your files and the backups runs saved). The last one reopens when the game starts.
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
- **When it's done**: what didn't work and why (with "Show where" lighting its spot), the dyes
  you're short of, "Try the N again", and "Put them back" to restore the backup the run saved.
- **Safety**: only in a home you may decorate, with the furnishing editor open; it stops on leaving,
  a zone change, combat or a cutscene, losing permission, a window that doesn't appear in time, the
  furniture list changing under it, or a few pieces in a row failing; whatever a piece opened is
  closed when it stops. A backup `.build` of what's placed is saved before every run.
- **Inside and the garden separately** (garden coordinates are turned to match your plot).
- **Markers in the world** where each piece goes, which way it faces, and the one being worked on.
- **Near me** (only pieces within a few yalms of you: a room) and the floors to place; placing
  follows them. The Pieces list also filters by state and name.
- **Preview first**: walks every piece, marking each spot and logging what it would do, touching nothing.
- **Activity log** of every step, with the windows and values used, copyable.

### What it doesn't do

- **Walls, floors, lights, the house's outside**: listed ("set these yourself").
- **Removing a dye** (a piece dyed in your house but undyed in the design): listed.
- **Pieces the game refuses** (floating, sunk or overlapping) unless **Place anywhere** is on in
  Settings, which turns off the game's placement check for the run (as MakePlace does).
- **The garden's storeroom**: you put those pieces down, the plugin sets them at their spot.
- **Paintings' and fish prints' pictures, aquarium fish, a partition's wall material**.

## The window

One window, no tabs: what it shows follows what you're doing.

- **Start** (nothing loaded): where you are ("In your house (medium) · you can decorate"), a drop
  zone with **Choose a file**, **Recent** (your files and the backups runs saved; right-click a file
  to take it off the list) and **Browse designs** (opens Housing Studio's Browse page).
- **Ready** (a design loaded): whether it fits your home, **What Start does** (how many pieces it
  moves, places from your storeroom and bags, and dyes, with the dyes you're missing under
  **See which**), the **Fixtures to set yourself** (tick them as you set them; the ticks are kept per
  design), then **Start placing** or **Preview first**, with about how long it takes at your pace.
  The header has **Pieces**, **Settings** and **×** (close this design).
- **Running** (a run or a preview): the pieces done, the three phases, the piece in hand with its dye
  and the step being done, what it's doing (the last lines of the log, with Copy), **Pause** /
  **Resume** and **Stop**. In step-by-step mode a brass callout says what the next click does, with
  **Continue**; when the run needs you to do something, the same callout says what.
- **Done** (until you press Done): how many pieces are where the design has them, what was moved,
  placed and dyed, what **needs you** (each with why, and **Show where** or **Copy list**), **Put them
  back** (restores the backup the run saved, after asking), and **Try the N again** (or **Carry on**
  when you stopped it).
- **Pieces** (from Ready): every piece by floor, the same item in the same dye stacked
  ("Hingan Bookshelf ×9"), filtered by **To do / In place / Missing / All**, **Find a piece** and
  **Near me**; each with its state and a **Show where** button that lights its spot in your home (and
  points at it when it's off screen). **Copy shopping list** and **Copy checklist** at the foot.
- **Settings** (from the header, or Dalamud's settings button): what Start does (place for me, dye,
  backup, pace: Careful 1.5 s, Steady 0.4 s, Quick 0.15 s between pieces), markers in the world,
  **More options** (place anywhere, pace variation, when to stop, chat, how far Near me reaches,
  storeroom and bags separately, floors to place, marker options, save what's placed as a build) and
  **Something not working?** (step by step, the old preview method, the recorder, the "Place" menu
  words, the windows and clicks used).

While a run goes, Pieces and Settings are hidden, so nothing that steers the run changes under it.
[docs/UI.md](docs/UI.md) maps each part of the design to the code that draws it.

## Using it

1. `/hstudio`, then load a design (drop it on the window, **Choose a file**, or pick it from Recent).
2. Go inside your house or apartment (or to your plot for the garden), with permission to decorate.
   The pill under the title turns green: "Fits your house (medium) · you can decorate".
3. Housing → **Indoor Furnishings** (or Outdoor), which opens the editor and the furnishing list
   (the run can also open the Housing menu for you).
4. **Preview first** to see it, then **Start placing**. The first time, switch on **Step by step**
   (Settings → Something not working?): it holds before every click and shows what it's about to
   do; press **Continue** (or `/hstudio continue`).

If you'd rather put pieces down yourself, switch off **Place pieces for me** in Settings: the run
then waits for you to put each piece down anywhere and sets it at its spot.

## Sending diagnostics

If a run stops at a step, or does something unexpected:

1. Settings → Something not working? → **Start recording**.
2. By hand: place one piece from your bags, one from the storeroom (pick it in the furnishing list's
   Storeroom tab, choose Place), switch the editor to its Dye mode and dye one piece. Clicking the
   editor's mode buttons and the Storeroom tab while recording helps too.
3. **Stop recording**, then **Copy recording and log**, and paste it in an issue or a message.

The recording lists every window that opened and closed, every click's values (yours and the
plugin's), the menus' entries and the housing state; that's what's needed to fix the table of
windows and clicks (`HousingStudio/Automation/UiTable.cs`). If your item menu's "place" entry has
other words, type them in Settings → Something not working?. More in [docs/AUTOMATION.md](docs/AUTOMATION.md).

**Before you use it:** plugins that act in the game for you are against Square Enix's terms of
service; this one only works on your own furniture in a home you may decorate, through the game's
own windows, at a human pace, but the choice is yours. Placing binds a piece to you and dyeing uses
up the dye, as doing it by hand does. This plugin is installed from a custom repository, which
goatcorp doesn't review.

Install: see [docs/PUBLISHING.md](docs/PUBLISHING.md#3-install-it-in-the-game) (custom repository URL
`https://www.kokos-tools.com/dalamud/repo.json`, Koko's repository, which lists every plugin Koko
makes).

## Developing

Requirements: the .NET 10 SDK, and Dalamud's assemblies: either XIVLauncher with the game run once
(Windows: `%AppData%\XIVLauncher\addon\Hooks\dev`), or the release zip unpacked anywhere with
`DALAMUD_HOME` pointing at it:

```sh
curl -L https://goatcorp.github.io/dalamud-distrib/latest.zip -o dalamud.zip
unzip dalamud.zip -d ~/dalamud && export DALAMUD_HOME=~/dalamud/
dotnet test HousingStudio.Tests      # the game-free core and the window's logic, runs anywhere
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
  UI/          the window: Kit/ (the design system's tokens, fonts, icons and controls drawn with
               ImGui's draw lists), Model/ (game-free, unit-tested: words, the Pieces list, pace,
               fixture ticks, SVG icon paths), Views/ (Start, Ready, Running, Done, Pieces,
               Settings), ViewModel, MainWindow, Overlay (markers in the world). See docs/UI.md.
HousingStudio.Tests/   xUnit tests for Core and UI/Model
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
and Cormorant Garamond (the Cormorant Project), under the SIL Open Font License 1.1
(`HousingStudio/Resources/Fonts`). Icons: drawn from Lucide's paths (ISC licence).

Licensed under the GNU AGPL-3.0 (`LICENSE`).
