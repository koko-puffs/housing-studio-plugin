# The window (0.3.0)

The window is drawn from six mockups (Start, Ready, Running, Done, Pieces, Settings) and the
Housing Studio design system's tokens. This maps each part of them to the code that draws it.
Paths are under `HousingStudio/UI/`.

## How it's put together

| Part | File | What it does |
| --- | --- | --- |
| Routing | `MainWindow.cs` → `Draw` | Picks the view: Settings or Pieces when opened from a header, else `ViewModel.View` (Start → Ready → Running → Done). Pieces and Settings are hidden while a run goes. |
| View state | `ViewModel.cs` | The run's figure and piece count, whether Done is waiting, the step in words, missing dyes (cached per plan), the Recent list. |
| Game-free logic | `Model/*.cs` | Words (counts, home sizes, "about 25 minutes"), `PaceSetting`, `When` ("today 05.24"), `PieceRows` (stacking, chip counts), `StartSummary`, `FixtureTicks`, `ReadyWords` (Ready's button, Keep / Clear out / Swap, the estimate; `ReadyMode`), `StepWords`, `SvgPath`, `Dashes` (the dashed edge, with bounded loops). Tested in `HousingStudio.Tests/UiModelTests.cs`. |
| Tokens | `Kit/Palette.cs` | Every `hs-*` colour from tokens.json, radii, control heights. Translucent tokens keep their alpha; panel glass is flattened over ink (ImGui can't blur). |
| Fonts | `Kit/Fonts.cs` | Geist 400 (15/14/13/12), 500 (15/14/13), 600 (15/14); Geist Mono (13/12/11/10, 10 for the Beta tag); Cormorant Garamond 500 (56/32/28/26/22) and 400 italic (32), through Dalamud's font atlas. CSS sizes are converted to ImGui's line-based sizes (Geist ×1.3, Cormorant ×1.211) so 14px is 14px. |
| Window frame | `Kit/Theme.cs` → `PushFrame` | Dalamud's title bar in walnut with ivory words, a 10px window radius and the 0.2 ivory edge. Only colours and the frame are pushed before `ImGui.Begin`: the title bar's font, height and ☰ ▼ × buttons are Dalamud's own, so they sit and hit as in every Dalamud window. |
| Layout | `Kit/Frame.cs` | Header (fixed), body (scrolls), footer (walnut, pinned, faint top line); `BeginBottom` is the mockups' `flex:1` spacer; toasts. |
| Text | `Kit/Txt.cs` | CSS line boxes (half-leading), letter-spacing glyph by glyph, ellipses, wrapped runs of mixed styles with inline links and dye swatches. |
| Icons | `Kit/Icons.cs` | Lucide icons parsed from the mockups' SVG path data, stroked at 1.8 in the 24-unit grid (1.6 at 24px), round caps. The flow mock-up's (`docs/mockups/flow.html`) are there too: Bell, Store, Scale, Hammer, Flask, Brick, Sofa, Undo, Send, Save, File, Link, AlertTriangle. |
| Controls | `Kit/Kit.cs` | Buttons (primary, glass, quiet, danger, picked; 30/34/40/46 tall), icon buttons, switch (40×24, animated), segmented pill (items can be greyed out, and it shrinks its padding to fit a width), filter chip, status pill, chip, state pill, swatch, round icon well, thumbnail, tick box, stepper, field, progress bar, eyebrow, dashed border, panels, row fills, tooltips, menus. Hover (edges to the 0.34 line), press, keyboard focus ring (2px brass-soft, 2px out) and disabled (50% buttons, 40% small controls). |
| The flow's controls | `Kit/Flow.cs` (`Kit` is partial) | From the 0.6 flow mock-up: `Wrap` (lays things left to right, wrapping), `CheckChip` / `CheckChips` (30px pill: an icon or a 64×6 `Meter`, its words; `ChipTone` Normal / Warn / Brass; the open one's edge lit; `IconInk` colours the icon alone, as the errands' plugins' green tick) with `BeginDetail` / `EndDetail` (the dashed detail under the row), `BeginStage` / `EndStage` (a glass card with a 34px number column, a 1px line joining it to the card above), `StageNumber` (`StageState` Todo / Now / Done), `StageHead` (mono label, serif 22 words, an aside on the right), `CountChip` ("4 retainers"), `BetaTag`, `LabeledSwitch` (a switch with its words and Beta tag), `MoreButton` (quiet, "See the list ›"), `Banner` (one brass line with up to two small buttons; returns which was pressed), `StatCells` (serif numbers over their words; 0 dim), `Journey` (stops in a row on a 2px track, brass up to the one in hand; for the errands' run). |
| Markers in the world | `Overlay.cs` | Markers, plus `Highlight` for Show where: a pulsing ring and label, or an arrow at the screen's edge when the spot is out of view. |

## Start (`Views/Start.cs`)

| Mockup | Code |
| --- | --- |
| "Housing *Studio*" | `DrawStart` → `MainWindow.Title(…, "Housing", "Studio")` |
| Where-you-are pill (green / danger / neutral) | `WherePill` → `Kit.StatusPill` |
| Settings icon button | `MainWindow.SettingsButton` |
| Dashed drop zone, round upload icon, "Load a design", note, Choose a file | `DrawDropZone` (`Kit.Dashed`, `Kit.Round`, `Kit.Button`); drag & drop via `DropTarget` (the zone and the whole body) |
| Save this house as a build, Send to my builds (side by side, stacked when narrow), and how the send went | `DrawSaveHome` → `SaveDialog`; `SendHome`, `CantSend` and `DrawSendState` in `Views/SiteLink.cs` (`Core/SiteLink` sends) |
| Recent eyebrow and rows (badge, name, "N pieces · House (medium) · today 05.24", MAKEPLACE / BUILD / BACKUP, chevron) | `DrawRecent`, rows from `ViewModel.Recent` (files read in the background); right-click → "Take it off this list" |
| "Looking for a design?" + Browse designs | `DrawBrowse` → `Util.OpenLink(https://www.kokos-tools.com/housing-studio/browse)` |

## Ready (`Views/Ready.cs`, the 0.6 flow: `docs/mockups/flow.html`)

| Mockup | Code |
| --- | --- |
| Eyebrow "BUILD · INSIDE", title, pill | `DrawReady` header; `ReadyPill` (fits / size mismatch / can't decorate / go home / garden) |
| Pieces, Settings, × | header buttons in `DrawReady` |
| The plot's warnings | `AlertNote`s from `session.Warnings`, above the rest |
| Carry on banner: "A run was cut off at piece N of M." · Carry on · Forget | `DrawCarryOnBanner` (`Views/Care.cs`) → `Kit.Banner`, from `Care.Offer`; Carry on → `CarryOn` (the run's pieces as matched now) |
| Check chips and their detail (one open at a time; press again to close) | `DrawReadyChecks` → `ReadyChecks` (`Views/Care.cs`, made again only when what they're made from changes, `ChecksKey`) → `Kit.CheckChips`, the detail in `Kit.BeginDetail` with `CheckActionsRow` (Show where, Pieces, See which, Copy list). Chips: the item limit as a meter (`ItemLimits.Check`, a chosen clear out's pieces not counted; words `CareWords.LimitDetail`), "N may be refused" and "N can't go here" (`ClashesNow`), "N dyes short" (`vm.MissingDyes`, opens `DyesPopover`), "Storeroom not read", "Near me · N pieces", floors left out, the other side's pieces. None when there's nothing to say. |
| 1 · Get: "N missing", "about X gil", source chips, Get them for me first (Beta), See the list › | `DrawGetStage`: `ShopPlan()` (`Plugin.Shop`), words cached per plan in `GetWords`; `Kit.CountChip` per `SourceKind`; the switch only with `Errands.Enabled` (`Configuration.GetFirst`); See the list opens Shopping. Done ("You have everything") when nothing's missing; "Not known yet" away from home. |
| 2 · Place: "N to do", "N already in place", to move / to place / to dye | `DrawPlaceStage` (`Model/StartSummary`, words `ReadyWords.PlaceBig`), `Kit.StatCells`; a line when you put pieces down by hand (Settings) |
| Keep what's here / Clear out N / Swap, and its line | `DrawPlaceChoice` (`Views/ClearOut.cs`) → `Kit.Segmented` (Clear out greyed with nothing to clear; Swap too, or for a backup), words `ReadyWords.ModeNote`. The choice is `Mode` (per design, Keep for another), `ModeNow` what it comes to (Keep when it can't), `SwapKeepAs` the swap's name (`KeepAsName`, read once a second), `ChosenClearOut()` the pieces put away first (Clear out or Swap). |
| 3 · Finish: "N fixtures", "x of y set", what they are, Set them for me after placing (Beta), Tick off | `DrawFinishStage`: `FinishLine` (`FixtureSummary` and, with the fixtures run on, `FixtureBagWords`; once a second), the switch with `Plugin.FixtureRun.Enabled` (`Configuration.FixturesAfterPlacing`); Tick off → `DrawFixtureTicks` (Set them for me, `DrawSetFixtures`, and the tick rows, `Configuration.FixturesDone` via `Model/FixtureTicks`). While it runs, `DrawFixtureRunPanel` (step, Continue, Stop) in its place. Hidden with no fixtures. |
| Stage numbers | `DrawStages`: the first stage not done is Now (brass), done ones a tick (`Kit.StageState`) |
| The brass button, Preview, the estimate | `DrawReadyFooter`: words `ReadyWords.Go` (cached), tooltip `StartTip` (the old note: stay in the house, the editor, the backup); with Get on it opens the errands (`errandsOpen`), Swap asks (`AskSwap`), else `StartFromReady` (asks before a clear out). Preview goes under it when the words don't fit beside it. The line: `ReadyWords.Hint` (`Core/RunEstimate`, cached; with Get on, the errands' trips first). When Start can't run it's at 50% and the line says why (`runner.CanStart`). |
| Fixtures after placing | `ArmFixturesAfterPlacing` (Ready's starts, Carry on, a swap, the errands once sent out) and `FixturesAfterPlacing` (each frame from `DrawBody`): when that run finishes (not a preview), with the switch on and fixtures left, `Plugin.FixtureRun.Start()` once, after the errands too (`Views/SetFixtures.cs`) |

## Errands (`Views/Errands.cs`, experimental; the 0.6 flow's "Get, before setting out" and "Running")

Get, before setting out (opened by Ready's brass button with Get them for me first on, and by Shopping's
Get everything for me):

| Mockup | Code |
| --- | --- |
| Eyebrow "KOKOMED2 · GET" + Beta, "12 things, *2 trips*", Back | `DrawErrandsEstimate` header (`Kit.Eyebrow`, `Kit.BetaTag`, `FitTitle`; words `ErrandWords.Title`); Back on the right as the mock has it |
| The plugins as check chips | `ErrandChips` (made again only when one comes or goes): vnavmesh and Lifestream with a green tick (`CheckChipSpec.IconInk`), or a warn chip "… · not installed"; AutoRetainer only when the trips use retainers, neutral when missing (it's optional). Pressing one opens `ErrandChipDetail` (what it does, Its page) |
| What you already have | `DimLine` with `ErrandWords.Have` ("Already here: … · 3 on your retainers, fetched first") |
| "TRIP 1 · 28 of 32 bag slots" and its legs | `SectionLabel` + `LegRow` per `RouteLeg` from `ErrandWords.Route` / `Legs` / `TripRest`: the pin (bell / store / scale / house; home's in brass), where ("Bell in your house", "Mist · Material Supplier", "Limsa · Market board · Lich" off your world), what's got there (cut to one line, all of it as a tooltip), the gil in mono ("free", "8,400", "≤ 53,000"), a 1px line to the next pin; "Home, and place" with "11 pieces go in" (`GoIn`). Later trips: "worked out again after trip 1" |
| Ready's choice | `ErrandThen` → `ErrandWords.Then`: "Then clears out 14 before placing" / "Then swaps, keeping what's here as “X”" (from `ModeNow`, `ChosenClearOut()`, `SwapKeepAs`) |
| "1 to craft · Teamcraft" | `LeftRowDraw`, one row per reason (`ErrandWords.LeftRows`): its icon, "N to craft" / "N past the market ceiling" / …, the things; Teamcraft (`Core/Teamcraft.ImportUrl`) on the crafted row; why as a tooltip |
| Limits row + Change | `DrawErrandLimits` (`ErrandWords.Limits`); Change opens `DrawErrandLimitRows` in the same panel: market ceiling, other worlds, home is, bag slots to keep free, trust the new clicks |
| "Set out · up to 61,400 gil" + the hint | footer: `ErrandWords.SetOut(plan.TotalGil)`, disabled with `ErrandsBlocked` (`ErrandRunner.CanStart`, every half second) as its tooltip and the line under it; else "You can stop at any time. Nothing is bought above the price shown." beside it (under it when narrow) |

Everything drawn from one estimate is made once per plan (`GetScreen`); the estimate itself is worked
out every 2 s or when what it's built from changes, and keeps its object while it says the same
(`ErrandPlanner.Same`). **The button is the confirmation** (no pop-up): `SetOut` starts the very plan
drawn (its total is the gil agreed), and on the framework thread first works the estimate out once more:
if it doesn't say the same (`ErrandPlanner.Same`: trips, stops, items, counts, gil, the market's dearest
price for one, what's left out), nothing starts and the screen shows the new one.

Running (the errands, `DrawErrandsRunning`):

| Mockup | Code |
| --- | --- |
| "GETTING KOKOMED2 · TRIP 1 OF 2", the live pill | header, eyebrow left and `Kit.StatusPill` right (Running / Paused / Waiting for you, as the placing run's) |
| "At the *market board*" | `ErrandWords.Where(runner.Leg, Going, Who, Replanning)`, from the runner's segment |
| The journey | `Kit.Journey`: `ErrandWords.Journey(CurrentTrip)` (Bell, Vendors, Market as the trip has them, then Home and Place), the stop in hand `JourneyAt` |
| The current card | `DrawErrandCurrent`: the item's icon (or the stop's), "Buying Riviera Table ×2" (`ErrandWords.Doing`), one dim line (`DoingNote`: the price, "the game asked, and the price matched", or the step) |
| Got / Spent | `DrawErrandCards` → `KvCard`: "6 of 12" (`GotCount` of `CountAtStart`), "4,000 of 61,400" (`Spent` of `AgreedGil`), each with its bar |
| Step by step, Waiting for you | `DrawErrandCallout` (the click it holds before with Continue, or what you do) |
| "What it's doing" | `DrawLog` (Running's) |
| Placing | the journey on Place over Running's `DrawFigure`, `DrawPhases` and `DrawCurrent` |
| Pause / Resume, Stop, the hint | footer: "About N minutes left" while it places (`runner.TimeLeft`), else what Pause does |

Its words are made once per segment and step (`ErrandRunWords`), the cards' once per change of the
numbers. The summary (`DrawErrandsSummary`) is as Done: the mark (tick, a neutral square when you stopped
it, danger when it stopped for safety), "Errands *done*" / "*stopped*", one line
(`ErrandWords.Summary`: "12 got on 2 trips for 58,920 gil", and why it stopped), Got (icon and name
×count per thing, `ErrandWords.GotList`), Not got (the reasons' rows), the log, and Done.

## Running (`Views/Running.cs`)

| Mockup | Code |
| --- | --- |
| "PLACING <NAME>" / "PREVIEWING <NAME>" | `DrawRunning` header |
| Live pill: "Running · about N minutes left", "Paused", "Waiting for you" | `DrawRunning` (`runner.TimeLeft`); the dot breathes while running |
| 56px figure, "of N pieces done", 8px bar | `DrawFigure` (`ViewModel.Figure`: in place at the start + moved + placed) |
| Three phase cards | `DrawPhases` (`runner.PhaseProgress`, `runner.Phase`) |
| Current piece card (icon, name, dye swatch in the stain's colour, floor, step text) | `DrawCurrent` (`Kit.Thumb` with the item's game icon, `ViewModel.StepText`) |
| "N skipped so far" | `DrawRunning` |
| Step by step (no mockup frame) | `DrawCallout`: a brass-edged panel above the log: "STEP BY STEP · NEXT", the step in words, the click itself in mono, Continue (the screen's brass button) and Skip this piece. "Waiting for you" uses the same callout without Continue. |
| "What it's doing" + Copy + last 3 lines | `DrawLog` |
| Centred note | the bottom block of `DrawRunning` |
| Pause / Resume, Stop, footer note | `DrawRunningFooter` |

## Done (`Views/Done.cs`)

| Mockup | Code |
| --- | --- |
| Check mark, "<name> is *in*", sentence | `DrawDone` header; "Stopped at N of M" (neutral square, or danger when stopped for safety); "Preview *done*" after a preview |
| Tally cells | `DrawTally` |
| Needs you (icon, name, reason, Show where / Copy list) | `Issues` → `IssueRow`: each problem from `runner.Problems`; "N pieces left undyed" grouped with Copy list; "N pieces you don't have" grouped with Shopping list (opens the Shopping view) |
| "N pieces not in the design were put away" | `DrawPutAwayLine` (`Views/ClearOut.cs`), under the tally; the moved cell leaves them out (`runner.PutAwayDone`) |
| After a swap: "Swap back?" + Swap back | `DrawSwapBack` (`Views/ClearOut.cs`) in place of Put them back → `runner.SwapBack` |
| "Not what you wanted?" + Put them back | `DrawPutBack` → a confirmation (`MainWindow.Ask`) → `runner.RestoreBackup` |
| Setting the fixtures (step, Continue, Stop) | `DrawFixtureRunPanel` at the top of the body while `Plugin.FixtureRun.Active` (after placing with Set them for me after placing on) |
| Fixtures note | the bottom block of `DrawDone` (hidden when all are ticked) |
| Try the N again / Carry on / Done | `DrawDoneFooter` (`runner.RetryProblems`; Carry on starts again with what's left; a preview offers Start placing; nothing needs you → Done alone, in brass) |

## Pieces (`Views/Pieces.cs`)

| Mockup | Code |
| --- | --- |
| Back arrow, eyebrow, "447 pieces" | `DrawPieces` header |
| Chips To do / In place / Missing / All (0 → 40%) | `Kit.FilterChip`, counts from `PieceRows.Counts` |
| Find a piece, Near me | `Kit.Field`; Near me toggles `Configuration.NearMe` (reach in Settings → More options) |
| Floor groups, stacked rows, notes, state pills, Show where | `FloorHeader`, `PieceRow`, `PieceRows.Build`; the floor you're on opens first |
| Shopping list + summary, Copy checklist | `DrawPiecesFooter`; Shopping list opens the Shopping view. A missing piece on a retainer: `RetainerNote` and a neutral RETAINER pill (`Views/Shopping.cs`). |

## Shopping (`Views/Shopping.cs`, no mockup)

| Part | Code |
| --- | --- |
| Back arrow, eyebrow, "N things to get" | `DrawShopping` header; back goes where it was opened from (`shoppingFrom`: Pieces or the main view) |
| Status ("Reading the game's vendors…", "Asking Universalis…", why there are no prices) | `Shopping.Status` as an `AlertNote` |
| Groups: On your retainers, Buy from vendors, Market board, Craft, Not known (eyebrow with gil and count) | `ShopGroups`, from `ShoppingPlan` (`ShopPlan()`, cached in `Session/Shopping.cs`) |
| Row: icon (a dye's swatch), name ×count, where from, gil, flag on the map | `ShopRow`, `ShopNote`, `FlagOnMap` |
| Open in Teamcraft | `TeamcraftRow` (`Core/Teamcraft.ImportUrl`) |
| Copy list + the total | `DrawShoppingFooter`, `CopyShoppingList` (`ShoppingList.WithSources`) |

## Settings (`Views/Settings.cs`)

| Mockup | Code |
| --- | --- |
| Start does: Place pieces for me, Dye, Backup, Pace | `DrawSettings` → `SwitchRow`, `SetRow` + `Kit.Segmented`. Place pieces for me sets both `StoreroomAutomatic` and `BagsAutomatic`. Pace: Steady, Quick (the default) or Daring, the `Pacing.Profiles` (`Model/PaceSetting`), with a line under it saying what the one chosen does. |
| In the world: Show where pieces go | `ShowMarkers` |
| Housing Studio: Link to Housing Studio (token field, masked; Link, Get a token) or Linked (masked token, Unlink) | `DrawSiteLink` in `Views/SiteLink.cs`; `Configuration.SiteToken` |
| More options (13) | `DrawMoreOptions`: place anywhere, pace variation (±5% steps), when to stop, chat, market prices (Universalis), Near me reach, storeroom / bags separately, floors to place, marker labels, placed markers, marker range, save what's placed as a build, good to know |
| Something not working? | `DrawTrouble`: step by step, move the preview before Place (old method), the recorder, the "Place" words, windows and clicks used |
| "Housing Studio 0.3.0" + What's new | the bottom block of `DrawSettings` |

## Looking after a home (`Views/Care.cs`, no mockup)

| Part | Code |
| --- | --- |
| Start: "312 of 450 pieces placed inside" | `DrawHomeCare`, the count from `Session/Care.Count` (read every 2 s while Start shows), words from `Model/CareWords.HowFull` |
| Start: "Since your last run", Check for changes, the changes, Show where they were, Put them back where they were | `DrawDrift` → `DrawDriftResult` → `AskPutBack` (`MainWindow.Ask`), from `Care.CheckForChanges` / `Care.PutBack` |
| Ready: "A run was cut off at piece N of M.", Carry on, Forget | `DrawCarryOnBanner` (`Kit.Banner`), from `Care.Offer` |
| Ready: the item limit, pieces the game may refuse, and the other chips | `DrawReadyChecks` (see Ready) |
| Pieces: "Pieces the game may refuse" with Show where | `DrawPiecesClashes` → `Disclosure` + Done's `IssueRow` |

## Decisions and differences from the mockups

- During a run, Pieces and Settings are hidden (not read-only): the run uses the settings it started
  with, and nothing in them should look as if it changes the run in hand.
- "See which" opens a popover (the dyes, how many, Copy list) rather than Pieces, so you don't lose
  your place on Ready.
- The mockups' footer line "Pause finishes the piece in hand first" isn't what the runner does (it
  holds before its next step), so it says "Pause holds before its next step".
- "N need you" counts every problem plus the pieces you don't have, and "Try the N again" retries
  every problem (the runner's `RetryProblems`); the mockup's numbers are illustrative.
- Panels are solid glass over ink (no blur); the glass's lit top edge is a 1px line.
- Icons are stroked at Lucide's 1.8 in the 24-unit grid, as the mockups' SVG renders them.
- The title bar is Dalamud's own (its font and height), in the mockup's walnut; the mockup's 32px bar
  with 13px Geist would mean changing its frame padding, which moves the title bar buttons' hit boxes.
- Paused is neutral, waiting for you is brass-picked, running is brass-wash with a breathing dot.
- The step-by-step callout, the empty and not-at-home states, toasts and the confirmation dialog
  aren't in the mockups; they're built from the same tokens.
- Ready (the 0.6 flow mock-up): the brass button's words follow the brief's shorter forms ("Get 12,
  then clear out 14" rather than "…, then clear out 14, then place"), so they fit beside Preview at
  520px; Preview drops under it when they still don't. The estimate with Get on says the errands'
  trips come first ("2 trips first, then about 25 minutes at a quick pace"): the errands' own time
  isn't known. The mock's 36×22 switch is the kit's 40×24, its 12.5px words 13px, and the Beta tag's
  9.5px mono 10px. Tick off shows a chevron, as it opens and closes the tick list. With the fixtures
  run on, Tick off also has Set them for me (now, without placing), which the mock leaves out.

- Errands (the 0.6 flow mock-up): Back is an arrow (the kit's), on the right as the mock has it. The
  route's later trips are drawn in full (the estimate knows them) with "worked out again after trip 1"
  instead of the mock's single "Whatever's left" leg. A missing plugin's chip opens a detail with its
  page rather than an Install button (vnavmesh and Lifestream come from their own repositories, which
  Dalamud's installer can't open to). The Limits' Change opens the rows in place and also holds Trust the
  new clicks. Running's title names the vendor as the game does ("At *Material Supplier*") rather than
  lower-casing it. The Got and Spent figures are serif 22 (the kit's nearest to the mock's 20).

## What the runner could add

`ViewModel.cs` keeps a few things the runner doesn't say yet (marked `TODO(runner)`):

- `Pieces`: the entries a run was given, so the figure's total doesn't depend on the window seeing
  the run start (a restore or retry started by the runner falls back to the plan).
- `StepText`: the step in plain words; the labels are written for the log, so
  `Model/StepWords` rewrites them ("Setting it at its spot").
- Pausing between pieces, so the footer can say what the mockup says.
