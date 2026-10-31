# Housing Studio plugin: the details

The full reference for the plugin (it used to be the README; [README.md](README.md) is the short
version): what every view and setting does, the pace table, diagnostics, the code's layout.

Furnish your FFXIV house from a design on [Housing Studio](https://github.com/koko-puffs/kokos-tools):
load its `.build` (or any MakePlace `.json` layout), see every piece matched to what you have, and
press **Start placing**: the plugin moves what's placed, places what's in your bags and storeroom at
its exact spot, then dyes everything to match.

> **Status: 0.4.3, working in the game.** 0.4.3: a scan's small fixes. Stopping a Put them
> back run by a zone change or a logout no longer reads the editor or clicks its Rotate (the
> editor may be half torn down then, as with a held piece in 0.4.1); a diagnostics recording
> left running out of the house reads the editor's state only in a home; one piece reads as
> one piece (the shopping list's Missing line, "1 needs you", Near me's count, the other
> side's pieces).
> 0.4.2: a check of 0.4.1. The outside kept for saving
> is told apart by the world you're on (0.4.1 read it from the game's house ids, which may not
> carry it in the ward, and a save from inside could then have lost its outside); a held piece
> isn't set down while a zone change or logout is under way (it showed as that before the
> territory changed); Try again for a garden also needs its plot.
> 0.4.1: a scan's fixes. A piece the editor holds when
> a run stops is only set down while you're still in that home (not after a zone change or a
> logout); the dye window's fallback only opens on the piece being dyed; Put them back for a
> garden is tied to its plot; Try again only runs in the home the run was in; the outside kept
> for saving is told apart by world too; a save writes beside the file and then replaces it, so
> a failed write never leaves it cut short; the release workflow takes the typed version through
> the environment, and the build workflow's token only reads.
> 0.4.0: **Save this house as a build** saves the whole
> home: its size and style (Minimalist II too), every floor's walls, flooring and light, the house's
> window and door, its outside with dyes (an all-in-one house as one), partition wallpapers and framed
> paintings, and the plot; the file always ends in `.build`. A deep scan fixed: Put them back only
> runs in the home (and on the side) its backup is of, so it can't put a whole house away; a pause no
> longer counts against the step it paused (a piece was skipped on resume); a bag piece the game
> stored late is placed, not given up on; a piece put down by hand just before a run isn't taken for
> the one placed; a piece the editor still holds when a run stops is set down; matching gives undyed
> spots the undyed copies (dyes can't be taken off) and no longer slows the game on designs with
> hundreds of one item; the game's "set down" gets a fixed flag; place anywhere and unloading are
> safer; your own garden saves aren't pruned with the backups; files with impossible numbers are
> read safely. Not yet seen in the game: the fixtures, outside and wallpaper reads.
> 0.3.5: a storeroom row that holds the copy (the dye)
> another piece of the same item needs is placed for that piece instead of passed over, so mixed-dye
> groups (a dozen partitions in three dyes) take far fewer picks; **Put them back** also puts away
> the pieces placed since the backup; and the header's "you can decorate" pill isn't cut short. 0.3.4 turns **Place anywhere** on by default, makes the
> furnishing list again when a piece isn't in it (a bag piece just stored wasn't), never re-picks a
> storeroom row already seen to hold the piece in another dye, and gives up on a piece the list won't
> show after 60 rows instead of 600. **Save this house as a build** is on the Start view, and works
> in any home you may decorate without the editor open. 0.3.3 made Quick (0.3.2's pace) the default, with Steady
> for a slow connection and Daring for the impatient, and fixes the storeroom picking a piece in the
> wrong dye, a garden or house whose placed furniture couldn't be read being planned as empty (every
> piece placed again), and a few slow spots in the window (see [Pace](#pace)). 0.3.2 also fixes a freeze on closing a
> design and the title bar's buttons; 0.3.0 is a new window (see [The window](#the-window)). Moving placed pieces and placing from
> the storeroom (the game places the piece, then it's moved to its spot) work in game, at about 2.4 s
> a piece in 0.3.1. 0.3.2 stops scanning the storeroom list row by row: storeroom pieces whose names
> the list shows go first (each placement brings more rows into view), and a row it can't read is
> predicted from the list's sort order and found in a pick or a few. Bag pieces are stored first and
> placed from the storeroom (placing from the bag was refused in 0.3.0). Opening the editor from
> nothing now accepts it opening in Move. Still unverified: the sort order for categories no log has
> shown, "Store" from a bag item's menu, and opening the Housing menu from nothing (see
> [docs/AUTOMATION.md](docs/AUTOMATION.md)). Keep the **backup** on, and if anything stops or feels
> slow, send the log: it ends with a **Timing** summary.

## What it does

- **Load** a `.build` or MakePlace `.json`: file picker, drag a file from Explorer onto the window,
  or pick from Recent (your files and the backups runs saved). The last one reopens when the game starts.
- **Match** every piece to what you have, nearest first and the right dye first: **in place**,
  **to move**, **in the storeroom** (the design's dye preferred), **in your bags** (bag, row, slot),
  or **missing**, with a **copyable shopping list** (pieces, the dyes to apply and the dyes to buy)
  and a checklist.
- **Start placing** runs three phases, one piece at a time at the [pace](#pace) you set (Quick, the
  default: 0.15 s between pieces ± 25 % and 0.3 s between any two clicks in the game's windows):
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
  you're short of, "Try the N again", and "Put them back" to restore the backup the run saved: the
  pieces you had go back to their spots, then the pieces placed since (not in the backup) are put
  away in the storeroom with the editor's Store mode (new in 0.3.5, not yet seen in the game: a piece
  Store mode won't take is listed, to put away by hand).
- **Safety**: only in a home you may decorate, with the furnishing editor open; it stops on leaving,
  a zone change, combat or a cutscene, losing permission, a window that doesn't appear in time, the
  furniture list changing under it, or a few pieces in a row failing; whatever a piece opened is
  closed when it stops. A backup `.build` of what's placed is saved before every run (the newest 50 are kept; files you save in the backups folder yourself stay).
- **Inside and the garden separately** (garden coordinates are turned to match your plot).
- **Markers in the world** where each piece goes, which way it faces, and the one being worked on.
- **Near me** (only pieces within a few yalms of you: a room) and the floors to place; placing
  follows them. The Pieces list also filters by state and name.
- **Preview first**: walks every piece, marking each spot and logging what it would do, touching nothing.
- **Activity log** of every step, with the windows and values used, copyable.

### Looking after a home

- **How full it is**: Start shows how many pieces are placed where you are against what the home
  holds ("312 of 450 pieces placed inside"). The numbers are the site's (`Core/ItemLimits.cs`, from
  `ITEM_LIMITS` and `OUTDOOR_LIMIT`): inside an apartment 150, a cottage 300, a house 450, a mansion
  600; a garden 40, 60 or 80. Ready says when a design won't fit: the design alone ("This design has
  480 pieces inside; a house (medium) holds 450."), or with the pieces already there that aren't in
  it (they stay, and count), and what Start does then: it places pieces until the home is full, the
  game refuses the rest, and after a few refused in a row the run stops. Near me and the floors
  leave pieces out, and the count follows them.
- **Pieces the game may refuse** (`Core/Clashes.cs`): what can be told without the game's own check
  and without the pieces' sizes (the plugin has none): copies of one item at the same spot (within
  0.02 yalms), spots below their floor or above its ceiling (the basement at -7 up to -0.5, -0.6 in a
  mansion; the ground floor 0 to 6.5; the upper floor 7 to 13.5; Shirogane's ceilings 0.3 lower;
  from the site's `interiors.json`, read from the game's models; 0.05 of slack), spots outside the
  house's walls (the box its model fills, by size), and an indoor furnishing in the garden or an
  outdoor one inside (the game's HousingFurniture and HousingYardObject sheets). Ready says so in a
  line (in danger with Place anywhere off; a quiet note with it on, as the game takes them then;
  always for the other side's furnishings), and Pieces lists each under **Pieces the game may
  refuse**, with Show where.
- **Carry on after a crash or a disconnect** (`Core/RunProgress.cs`, `Session/Care.cs`): while a run
  goes, its progress is kept in `resume.json` in the plugin's folder (the design file, the home, the
  design's pieces it was given, how many are through and the phase), written beside it and moved
  over it at most twice a second. When it finishes, or you stop it, the file goes. Otherwise (the
  game closed, you were disconnected or changed zones, or the run stopped for safety), the next time
  that design is loaded in that home Ready offers **Carry on from piece N of M** or **Forget it**.
  Carrying on starts a run on those pieces as they're matched now, so the ones done are in place and
  nothing is done twice; its numbers go on from the run cut off. A file that's cut short or edited
  reads as none (and is dropped). A restore's pieces to put away aren't carried on.
- **Check for changes** (`Core/Drift.cs`): after every run (not a preview), what's placed is kept as
  a snapshot of the home (`snapshots/<home>.build`, the same file a backup is; the home is told by
  side, world, territory, house and plot). It's dropped when the next run starts and made again when
  it ends in the home, so a run that ended away (a zone change) leaves none rather than a stale one.
  On Start or Ready, in a home you may decorate with a snapshot, **Since your last run** has **Check
  for changes**: what's placed now against the snapshot, paired as the matching pairs pieces (the
  same item, the same dye, the nearest; within 0.05 yalms and 0.03 radians counts as unmoved): moved,
  added, taken away, in another dye, with **Show where they were**. Nothing is touched by checking.
- **Put them back where they were** (Settings → Experimental → Put back what changed since a run;
  off by default): after asking, the snapshot (a copy of it) is loaded as the design and a run
  started on it once it's matched: the moved pieces are moved back, the ones taken away placed again
  from your storeroom or bags, and the ones dyed since dyed back (when Dye after placing is on). The
  pieces added since stay where they are (it doesn't put anything away). Not yet tried in the game.
- **Other client languages** (`Core/MenuWords.cs`, `Game/GameWords.cs`): the menu entries the run
  chooses by their words ("Place" in a bag item's menu and a storeroom row's, "Store", Furnishing
  Color's "Paint") are read from the game's own text, the Addon sheet in the client's language
  (rows 1751 and 6229 Place, 6840 Store, 6272 Paint: Platzieren / Placer / 配置する, Ins Lagerhaus
  legen / Ranger dans l'entrepôt / 倉庫に入れる, Färben / Teindre / 染色する). Each row's English
  words are checked first; if a patch moved the rows, the built-in words are used (logged). The
  "Place" words typed in Settings still override them. Everything else the run matches by words is
  the game's own text already (item and dye names from the sheets in the client's language, the dye
  window's "No Color" read from the window). Not yet tried on a German, French or Japanese client.

### What it doesn't do

- **Walls, floors, lights, the house's outside**: listed ("set these yourself").
- **Removing a dye** (a piece dyed in your house but undyed in the design): listed.
- **Pieces the game refuses** (floating, sunk or overlapping) if **Place anywhere** is switched off
  in Settings → More options. It's on by default: the game's placement check is off while a run
  lasts (as MakePlace does), and back on when it ends.
- **The garden's storeroom**: you put those pieces down, the plugin sets them at their spot.
- **Paintings' and fish prints' pictures, aquarium fish, a partition's wall material**.

## The window

One window, no tabs: what it shows follows what you're doing.

- **Start** (nothing loaded): where you are ("In your house (medium) · you can decorate"), a drop
  zone with **Choose a file**; in a home you may decorate, **Save this house as a build**: the whole
  home as it is (no design, and no need for the furnishing editor or Rotate), the same file the site
  and MakePlace open. It holds the house's size and style (a district's, Minimalist or Minimalist II),
  every floor's walls, flooring and light, the house's window and door, its outside (roof, walls or
  an all-in-one house, window, door, decorations, placard and fence, with their dyes; seen in the
  ward, so walk past your house once before saving from inside), every piece with its dye, the
  wallpaper on partitions and the paintings in frames, and the plot. A garden needs Outdoor
  Furnishings opened once, as the game loads its list only then. The name you type gets `.build`; **Recent** (your files and the backups runs saved; right-click a file
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
  backup, [pace](#pace): Steady, Quick or Daring), markers in the world,
  **More options** (place anywhere, pace variation, when to stop, chat, how far Near me reaches,
  storeroom and bags separately, floors to place, marker options, save what's placed as a build) and
  **Something not working?** (step by step, the old preview method, the recorder, the "Place" menu
  words, the windows and clicks used).

While a run goes, Pieces and Settings are hidden, so nothing that steers the run changes under it.
[docs/UI.md](docs/UI.md) maps each part of the design to the code that draws it.

## Pace

Settings → Pace sets how fast a run goes. Each piece is many small steps; a pace sets the pause
between pieces, the least time between two clicks, and how long the plugin waits for the game to
settle (its "the same for a few frames" checks). Timeouts aren't touched: they only decide when to
give up.

| Pace | Between pieces | Between clicks | Waits for the game |
|---|---|---|---|
| Steady | 0.6 s | 0.45 s | half as long again |
| **Quick** (default) | 0.15 s | 0.3 s | as 0.3.2 |
| Daring | 0.05 s | 0.2 s | two thirds |

Quick is 0.3.2's pace, as tested in the game. Steady is for a slow connection or a busy house, or if
pieces get skipped. Daring is for the daring: every step still waits for the game to say it's done,
but with less to spare; if pieces get skipped, go back to Quick. Before 0.3.3 the pace was only the
pause between pieces: Careful (1.5 s) becomes Steady, the rest Quick.

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
               aiming the held piece), FurnitureReader, FixtureReader (the home's fixtures, inside and out, for saving it), InventoryReader, PlaceAnywhere.
  Session/     LayoutSession (the design matched to the game), PlacementRunner (the run: each
               piece's steps), Care (snapshots, the run's progress, putting back), Backups, ActivityLog.
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
