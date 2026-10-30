# Housing Studio (Dalamud plugin)

Furnish your FFXIV house from a design on [Housing Studio](https://github.com/koko-puffs/kokos-tools):
load its `.build` (or any MakePlace `.json` layout), see every piece matched to what you have, and let
the plugin set each one at its exact spot, including pieces still in your bags or the storeroom.

> **Status: early and untested in the game.** It compiles against Dalamud 15 (API 15, game patch
> 7.5) and its file reading, coordinate maths and matching are unit-tested, but nobody has run it in
> a house yet. Try the **Dry run** first, keep the **backup** option on, and expect rough edges.

## What it does

- **Load** a `.build` or MakePlace `.json`: file picker, drag a file from Explorer onto the window,
  or pick from recent files. The last one reopens when the game starts.
- **Match** every piece to what you have, nearest first and the right dye first:
  - **in place** (already there, turned the right way),
  - **to move** (placed somewhere else),
  - **in the storeroom** (a piece in the design's dye is preferred),
  - **in your bags** (with the bag, row and slot),
  - **missing**, with a **copyable shopping list** (counts and dyes) and a full checklist.
- **Place**, one piece at a time at the pace you set (default 1.5 s ± 25 %, never under 0.6 s):
  - placed pieces are picked up and set down at their spot by the game's own editor functions,
    and each is checked to have landed before the next;
  - for pieces in your bags or the storeroom, it tells you which one is next (in the window and in
    chat); you put it down **anywhere**, in any order, and it's set at its spot the moment it lands.
    For storeroom pieces, the game's preview is aimed at the spot as soon as you pick it, so "Place"
    puts it right there.
  - Pause, resume, skip a piece, stop (also `/hstudio pause|resume|stop`), and a progress bar.
- **Inside and the garden separately**: inside, in Housing → Indoor Furnishings; the garden from
  your plot in Housing → Outdoor Furnishings (garden coordinates are turned to match your plot).
- **Markers in the world**: a dot where each piece goes with a tick for which way it faces,
  coloured by what's to be done, a line from a placed piece to its spot, the current piece ringed in
  brass, names on hover.
- **Filters**: by floor (basement, ground, upper) and "within N yalms of you" (stand in a room to
  work on just that room), by state, and by name. Placing uses the same filters.
- **Dye notes**: pieces whose dye differs from the design's, and pieces that need dyeing after
  they're placed.
- **Dry run**: walks every step, marking each spot and logging what it would do, without touching
  anything.
- **Backups**: what's placed is saved as a `.build` before every run (and on demand); load one to
  put everything back. "Save what's placed as a build" also exports your current house for the site.
- **Activity log**, copyable, and in Dalamud's log (`/xllog`).

### What it doesn't do

- **Pick pieces up for you from your bags or the storeroom.** You choose them through the game's
  own menus; doing that for you means sending the game messages or clicking its menus, which other
  plugins report can break the client. See `docs/RESEARCH.md`.
- **Dyes, walls, floors, lights, the house's outside.** These are listed ("set these yourself").
- **Pieces the game refuses** (floating, sunk or overlapping) unless you switch on **Place anywhere**
  in Settings, which turns off the game's placement check for the run (as MakePlace does).
- **Paintings' and fish prints' pictures, aquarium fish, a partition's wall material**: placed as
  the piece itself only.

## Using it

1. `/hstudio`, then load a design (Design page).
2. Go inside your house or apartment (or to your plot for the garden). You need permission to
   decorate there.
3. Housing → Indoor (or Outdoor) Furnishings, and switch the editor to **Rotate** (the circular
   arrows).
4. Optional: open the Furnishings window's **Storeroom** tab once, so storeroom pieces are counted.
5. Place page → **Dry run** to see it, then **Start placing**.

It stops by itself if you leave, change zones, get into combat or a cutscene, lose permission, the
furniture list changes under it, or a few pieces in a row fail. It pauses (between pieces) if the
editor closes.

**Before you use it:** plugins that act in the game for you are against Square Enix's terms of
service; this one only moves your own furniture in a home you may decorate, at a human pace, but the
choice is yours. Placing binds a piece to you, as placing it by hand does. This plugin is installed
from a custom repository, which goatcorp doesn't review.

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
  Core/        game-free: reading .build/.json (LayoutReader), axes and turns (Transforms),
               plots (PlotTable + Data/plots.json), floors, dyes, matching, shopping list,
               pacing, writing backups (LayoutWriter). Unit-tested.
  Game/        the game: GameData (Lumina sheets), HousingContext (where you are),
               HousingEditor (the editor's state, select/place), FurnitureReader,
               InventoryReader, PlaceAnywhere.
  Session/     LayoutSession (the loaded design matched to the game), PlacementRunner (the
               step-by-step placer and its safety checks), Backups, ActivityLog.
  UI/          Palette, Theme, Fonts, Widgets (the site's look in ImGui), MainWindow
               (Design / Pieces / Place / Settings), Overlay (world markers).
  Resources/   fonts (OFL), images/icon.png
HousingStudio.Tests/   xUnit tests for Core, with a sample .build
tools/         make-plots.mjs (plot table from the site's exteriors.json), icon.svg + make-icon.mjs
docs/          RESEARCH.md (how it works and where that came from), PUBLISHING.md
```

The coordinate conventions mirror the site's `src/features/housing-studio/makeplace.ts` and
`build/model.ts`: MakePlace stores a location as `[x, z, y]` of the game's, and the turn as a
quaternion about its own up axis the other way round (`yaw = -2·atan2(qz, qw)`); garden pieces are in
the plot's own coordinates.

After a game patch, see [docs/PUBLISHING.md](docs/PUBLISHING.md#4-things-to-know).

## Credits and licence

The housing editor's patterns and offsets were found by the MakePlace plugin (jawslouis) and are
kept current by its forks (RemakePlace, DisPlace); placing from the storeroom by aiming the preview
comes from Refurnish. Fonts: Geist and Geist Mono (Vercel) and Source Serif 4 (Adobe), under the SIL
Open Font License 1.1 (`HousingStudio/Resources/Fonts`).

Licensed under the GNU AGPL-3.0 (`LICENSE`).
