# Research notes

What was looked at before building Housing Studio's plugin, and what was taken from where. Written
October 2026. Everything about the game's memory is from other plugins' work and the
FFXIVClientStructs that ships with Dalamud; none of it has been checked in the game by this project
yet.

## 1. Dalamud: API level, SDK, template, manifest, custom repos

**Newest API level: 15.**

- NuGet `Dalamud.NET.Sdk` versions: 9.0.0 … 14.0.0–14.0.2 (Dec 2025–Feb 2026), **15.0.0
  (29 April 2026)**, nothing newer as of 30 October 2026
  (`https://api.nuget.org/v3-flatcontainer/dalamud.net.sdk/index.json`, and
  [nuget.org/packages/Dalamud.NET.Sdk](https://www.nuget.org/packages/Dalamud.NET.Sdk)).
  `DalamudPackager` is also 15.0.0 ([nuget.org/packages/DalamudPackager](https://www.nuget.org/packages/dalamudpackager)).
- Dalamud's own release channel: `goatcorp/dalamud-distrib` `version` file reads
  `{"AssemblyVersion":"15.0.3.9"}` (staging: `15.0.3.9-16-g…`). The plugin was compiled against
  that release's assemblies (the same `latest.zip` the CI downloads).
- API 15 arrived with game patch 7.5: plugins' release notes from 30 April 2026 describe moving to
  the 15 SDK and bumping `DalamudApiLevel` 14 → 15 "for patch 7.5"
  ([MasterEvent v1.4.1](https://github.com/Ashfall-Codex/MasterEvent/releases/tag/v1.4.1),
  [AutoWeeklyCap releases](https://github.com/Senither/AutoWeeklyCap/releases)). The maintained
  MakePlace forks declare `"DalamudApiLevel": 15` too.
- `Dalamud.NET.Sdk/15.0.0` (read from the package's `Sdk.props`): targets **`net10.0-windows`**,
  C# 14, x64, nullable, unsafe allowed, lock files on; references `Dalamud`,
  `Dalamud.Bindings.ImGui`/`ImPlot`/`ImGuizmo` (the Hexa-based bindings that replaced ImGui.NET),
  `FFXIVClientStructs`, `InteropGenerator.Runtime`, `Lumina`, `Lumina.Excel`, `Newtonsoft.Json`,
  `Serilog`, all from the Dalamud install (`%AppData%\XIVLauncher\addon\Hooks\dev\`,
  `~/.xlcore/dalamud/Hooks/dev/`, or `DALAMUD_HOME`), and adds `DalamudPackager`.
- Template: [goatcorp/SamplePlugin](https://github.com/goatcorp/SamplePlugin) — `Plugin : IDalamudPlugin`
  with `[PluginService]` static properties (`IDalamudPluginInterface`, `ICommandManager`,
  `IClientState`, `IPlayerState`, `IDataManager`, `ITextureProvider`, `IPluginLog`…), a
  `WindowSystem` drawn from `UiBuilder.Draw`, `OpenMainUi`/`OpenConfigUi`, a `Configuration :
  IPluginConfiguration` saved with `SavePluginConfig`. Its README links the plugin docs
  (dalamud.dev) and the official repo's AI usage policy (relevant if this is ever submitted there).
- **Manifest**: DalamudPackager writes `<AssemblyName>.json` next to the DLL from a hand-written
  `<AssemblyName>.json` in the project (or csproj properties): `Author`, `Name`, `Punchline`,
  `Description`, `ApplicableVersion`, `RepoUrl`, `Tags`, `CategoryTags`, `IconUrl`, `ImageUrls`,
  `Changelog`, `AcceptsFeedback`, `LoadPriority`, `LoadSync`, `LoadRequiredState`, … and fills
  `InternalName`, `AssemblyVersion` and `DalamudApiLevel` itself (15 here). In Release it zips the
  output to `bin/x64/Release/<Name>/latest.zip`.
- **Custom repository** (`repo.json`): a JSON *array* of manifests, each with the fields above plus
  `AssemblyVersion`, `TestingAssemblyVersion`, `DalamudApiLevel`, `DownloadLinkInstall`,
  `DownloadLinkUpdate`, `DownloadLinkTesting`, `IconUrl`, `LastUpdate` (unix seconds),
  `IsHide`, `IsTestingExclusive`. Seen in the wild: MakePlace's, ReMakePlace's, DisPlace's and
  Refurnish's own `repo.json`/`*.json` (links point at GitHub release assets; Refurnish uses
  `releases/latest/download/latest.zip`).

## 2. MakePlace plugin and its forks (ReMakePlace, DisPlace)

- [jawslouis/MakePlacePlugin](https://github.com/jawslouis/MakePlacePlugin) (API 11, unmaintained;
  **no licence file**, so no code was copied — only facts like patterns and offsets),
  [RemakePlace/plugin](https://github.com/RemakePlace/plugin) (API 15, 7.5.1, uses ECommons, adds
  auto-dyeing), [Drakansoul/DisPlace](https://github.com/Drakansoul/DisPlace) (API 15 fork, "copy
  other housing layouts"). "Displace" is DisPlace.
- **Reading layouts**: MakePlace JSON: `interiorFurniture`/`exteriorFurniture`, each `{ itemId,
  name, transform: { location, rotation, scale }, properties: { color: "RRGGBBAA", material },
  attachments: [...] }`, `interiorScale`/`exteriorScale`, `interiorFixture`/`exteriorFixture`.
  Location is `[x, z, y]` of the game's divided by the scale; the turn is
  `-ComputeZAngle(quaternion)`. Housing Studio's `makeplace.ts` does the same (`yaw = -2·atan2(qz,
  qw)`), and the plugin's `Core/Transforms.cs` is tested against both formulas. Dye: nearest
  `Stain` row with the housing flag (now `Stain.IsHousingApplicable`).
- **Matching**: they list placed objects (`HousingObjectManager` objects, `housingRowId` →
  `HousingFurniture`/`HousingYardObject` sheet → `Item`), first pairing same item **and dye** to the
  nearest layout piece, then same item any dye; unmatched layout pieces are greyed out. Floors split
  at y < 0 (basement), < 7 (ground), else upper.
- **Moving**: in **Rotate** mode only. The housing editor's state is a pointer at `LayoutWorld + 0x40`
  (mode at +0x0: None, Move, Rotate, Store, Place, Dye=5, Remove=6; active item at +0x18). They call
  the game's "select item" function (`48 85 D2 0F 84 ?? ?? ?? ?? 53 41 56 48 83 EC ?? 48 89 6C 24`),
  write the active item's position (+0x50) and rotation quaternion (+0x60) — that's the
  `SharedGroupLayoutInstance.Transform` — and call the "place item" function (`48 89 5C 24 10 48
  89 74 24 18 57 48 83 EC 20 4C 8B 41 18 33 FF 0F B6 F2`). Tabletop/wall-mounted
  (`ItemUICategory` 78/79) go last.
- **Outdoors**: only your plot's items, via the `HousingExteriorPlacedItems` container and a game
  function mapping (plot, slot) → furniture index (`E8 ?? ?? ?? ?? 44 0F B7 D8`). Garden
  coordinates in the file are plot-local, turned by a hard-coded plot table (`Plots.cs`). Housing
  Studio's own `public/housing/exteriors.json` has the same plots (with the opposite sign of turn),
  so the plugin's `Data/plots.json` is generated from the site's data instead.
- **Rate limit**: a fixed "placement interval" between items (`Framework.RunOnTick(…, interval)`);
  their README warns ~200 ms misses placements.
- **Place anywhere**: three one-byte patches in the game's code (patterns in `Game/PlaceAnywhere.cs`)
  turn off the floating/overlap check; restored afterwards.
- **None of them place from the inventory or storeroom**: their READMEs all say furniture must
  already be placed.

## 3. Placing new pieces: how the game does it, and what's workable

- In the game, a new piece comes from the furnishing editor: from the **storeroom** (Furnishings
  window, Storeroom tab → pick an item → a context menu, and a **preview** of the item spawns →
  choose "Place") or from your **bags** (pick the item → it follows the cursor → click to set it
  down). FFXIVClientStructs has the pieces of this: `IndoorTerritory.StoreroomItemInventoryType /
  Slot` (the storeroom slot picked), `StoreroomItemPlacePosition/Rotation`,
  `HousingTemporaryObject` (kind, model, stain, `InventoryItem*`), and the preview object
  `FurnitureManager.ObjectManager.ObjectArray.TempObject`.
- **[Wyse2011/Refurnish](https://github.com/Wyse2011/Refurnish)** (API 15) is the only plugin found
  that places new pieces. Its notes are the most useful evidence:
  - Sending the placement *message* directly (skipping the game's "holding an item" step) "leaves the
    game client in a broken state (hidden UI, locked commands, crashes)", so it's disabled.
  - From the **storeroom** it clicks through the game's own window: it learns the Furnishings
    list's and the context menu's callback values from you doing it once (per game version), then
    replays them; before choosing Place it **moves the preview** (`TempObject->SetPosition/
    SetRotation`) to the layout spot, and Place puts the item there. It verifies by watching the
    storeroom slot empty and the placed count rise, stops on errors/toasts, leaving, or ten failures.
  - **Inventory pieces must be placed by hand** in Refurnish.
  - Fine-tuning afterwards uses the same select → write transform → place as MakePlace ("Lifter").
- [LeonBlade/BDTHPlugin](https://github.com/LeonBlade/BDTHPlugin) (Burning Down The House) edits the
  active item's position/rotation while you hold it, by the same editor state.

**What this plugin does** (robust parts only, everything through the game's own flow):

1. Already-placed pieces: MakePlace's select → set transform → place, Rotate mode only, one at a
   time, each checked to have landed (the furniture list's stored position within 5 cm and 0.03 rad)
   before the next.
2. New pieces (bags or storeroom): **you** pick each up through the game's menus and put it down
   anywhere; the plugin notices the new piece (a new slot in the furniture list, same item) and
   sets it at its exact spot with step 1. Any order works. For storeroom pieces it also aims the
   game's preview at the spot while the menu is open (Refurnish's technique), so Place usually puts
   it there directly.
3. Not done: clicking menus for you (Refurnish's learned-callback replay) and sending placement
   messages. The first is plausible as a later, opt-in step once someone can test it in game; the
   second is known to break the client.

**Safety** (`Session/PlacementRunner.cs`): only with `HousingManager.HasHousePermissions()`, only in
the editor's Rotate mode for moves, stops on leaving the home/zone (house id and territory checked
every frame), combat/cutscene/zone changes, a furniture slot holding a different item than planned,
or N failures in a row; pauses (between pieces, never mid-piece) if the editor closes; pace never
below 600 ms (default 1.5 s ± 25 %); a dry run that touches nothing; a backup `.build` of what's
placed before starting.

## 4. Custom ImGui looks: Lightless and others

- Lightless Sync's client (mirror: [CakeAndBanana/LightlessClient](https://github.com/CakeAndBanana/LightlessClient),
  `LightlessSync/UI/UIColors.cs`, `UISharedService.cs`, `TopTabMenu.cs`): a named palette of hex
  colours turned into `Vector4`s; extra fonts through Dalamud's font atlas
  (`UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudAssetFont(…,
  new() { SizePx = 35 })))`, `NewGameFontHandle`, `IconFontFixedWidthHandle`), pushed with
  `using (font.Push())`; custom widgets drawn with `ImGui.GetWindowDrawList()` (rounded outlines
  around grouped content, outlined text, a tab bar with drawn underlines, icon+text buttons laid out
  by hand).
- The same approach here: `UI/Palette.cs` (the site's `--hs-*` tokens from `studio.css`, translucent
  ones flattened over the ink), `UI/Theme.cs` (style colours and vars pushed in `Window.PreDraw` and
  popped in `PostDraw`), `UI/Fonts.cs` (TTFs from embedded resources via
  `IFontAtlasBuildToolkitPreBuild.AddFontFromMemory`, merged with game symbols and Dalamud's language
  glyphs), `UI/Widgets.cs` (brass pills, segmented control, chips, switches, auto-sized panels drawn
  behind content with draw-list channels, progress bar, stat tiles, icon wells).
- **Fonts**: Geist and Geist Mono (Vercel, OFL 1.1, from the `geist` npm package 1.8.2) and Source
  Serif 4 Subhead (Adobe, OFL 1.1 with Reserved Font Name "Source", from the official
  `source-serif` npm release 4.5.1). Bundled unmodified, licences shipped in the zip. On a Japanese
  client the body font falls back to Dalamud's (Geist has no kana/kanji for item names).

## 5. Sources not reachable from here

hgxiv.com and thegamer.com (housing guides) were blocked in this environment, so the exact wording of
the game's menus ("Place", "Display Preview") wasn't checked. Since 0.2 the automation does choose a
menu entry by its words, so they're editable in Settings → Diagnostics and logged on every run.

## 6. Automation (0.2): driving the game's windows

Added after the first in-game feedback ("place everything for me, then dye"). How each part is done
is in [AUTOMATION.md](AUTOMATION.md); the sources:

- **Clicking windows**: firing `AtkUnitBase.FireCallback(count, AtkValue*, close)` with the values the
  window itself sends, as ECommons' `Callback.Fire` does (`NightmareXIV/ECommons`,
  `ECommons/Automation/Callback.cs`), never hand-made network messages.
- **Context menus** (`ContextMenu`): entry count in `AtkValues[0]` (uint), entry texts from
  `AtkValues[8 + i]`, choose = `(0, index, 0)` (ECommons
  `UIHelpers/AddonMasterImplementations/ContextMenu.cs`); close = `(0, -1, …)` (Refurnish's learned
  cancel). `SelectYesno` Yes = callback `0`.
- **Storeroom placing** (Refurnish `Automation/UiPlacer.cs`, `UiPlaceLearner.cs`,
  `UiPlaceProfile.cs`): the furnishing list is the `HousingGoods` window; picking a row is
  `(1, list index)` and opens the row's `ContextMenu` and a preview (`ObjectArray.TempObject`); the
  picked storeroom slot is `IndoorTerritory.StoreroomItemInventoryType/Slot`; `ShowStoreroomTab` says
  which tab is shown; moving the preview (`SetPosition/SetRotation`) before choosing Place puts the
  piece there. The tab switch's values weren't found (Refurnish asks you to open the tab too).
- **Bag items' menus**: `AgentInventoryContext.OpenForItemSlot(type, slot, 0, owner addon id)`
  (FFXIVClientStructs), as item automation plugins open an item's menu. The "place" entry's words and
  what the game does after it's chosen (a held piece, editor mode 4) weren't confirmed by any source;
  Refurnish leaves bag items to you. Setting the held piece down with the editor's set-down function
  is this plugin's own, unverified, attempt.
- **Dyeing** (RemakePlace `ReMakePlacePlugin.cs` `SetItemDye`/`DyeAllItems`, `Util/StainsUtils.cs`;
  ECommons `ColorantColoring`, `MiragePrismMiragePlateConfirm`): editor mode 5, pick the piece,
  `InteractWithHousingItem(agentHousing, GetSelectedHousingItemAddress(housingManager))` (patterns in
  `Game/HousingActions.cs`) opens `ColorantColoring`; shade `(4, circle 0–7 from Stain.Shade)`, dye
  `(5, Stain.SubOrder - 1, Stain row, 0)`, Dye `(0)`, close `(2)`; `MiragePrismMiragePlateConfirm`
  Yes `(0)`, No `(-1)`. RemakePlace also reads the Dye button's state and the "not enough dye" mark
  from node ids; here the dyes you own are counted beforehand (`InventoryManager.GetInventoryItemCount`
  over `Stain.Item`) and the dye change is checked in the furniture list.
- **Diagnostics**: the recorder hooks `FireCallback` (as Refurnish's `CallbackTap`/`PlacementRecorder`
  and ECommons' debug hook do) and listens to `IAddonLifecycle` PostSetup/PreFinalize for every window.
- **Editor modes**: no source had the furnishing bar's callbacks; the run asks you to press Rotate or
  Dye when it needs them.
- **No new dependencies**: ECommons would have given the same helpers, but it's a large shared library
  that also hooks the game on its own; the few helpers needed (firing a callback, reading a menu) are
  written here in `Automation/GameUi.cs`.
