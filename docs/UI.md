# The window (0.3.0)

The window is drawn from six mockups (Start, Ready, Running, Done, Pieces, Settings) and the
Housing Studio design system's tokens. This maps each part of them to the code that draws it.
Paths are under `HousingStudio/UI/`.

## How it's put together

| Part | File | What it does |
| --- | --- | --- |
| Routing | `MainWindow.cs` → `Draw` | Picks the view: Settings (over the errands' estimate too), the errands' screens, or Pieces when opened from a header, else `ViewModel.View` (Start → Ready → Running → Done). Pieces and Settings are hidden while a run, the fixtures or the errands go. |
| View state | `ViewModel.cs` | The run's figure and piece count, whether Done is waiting, the step in words, missing dyes (cached per plan), the Recent list. |
| Game-free logic | `Model/*.cs` | Words (counts, home sizes, "about 25 minutes"), `PaceSetting`, `When` ("today 05.24"), `PieceRows` (stacking, chip counts), `StartSummary`, `FixtureTicks`, `ReadyWords` (Ready's button, Keep / Clear out / Swap, the estimate; `ReadyMode`), `DoneWords` (Done's line), `AfterPlacing` (when the fixtures follow a run), `CareWords.HouseLine` / `Changed` (This house), `StepWords`, `SvgPath`, `Dashes` (the dashed edge, with bounded loops). Tested in `HousingStudio.Tests/UiModelTests.cs`. |
| Tokens | `Kit/Palette.cs` | Every `hs-*` colour from tokens.json, radii, control heights. Translucent tokens keep their alpha; panel glass is flattened over ink (ImGui can't blur). |
| Fonts | `Kit/Fonts.cs` | Geist 400 (15/14/13/12), 500 (15/14/13), 600 (15/14); Geist Mono (13/12/11/10, 10 for the Beta tag); Cormorant Garamond 500 (56/32/28/26/22) and 400 italic (32), through Dalamud's font atlas. CSS sizes are converted to ImGui's line-based sizes (Geist ×1.3, Cormorant ×1.211) so 14px is 14px. |
| Window frame | `Kit/Theme.cs` → `PushFrame` | Dalamud's title bar in walnut with ivory words, a 10px window radius and the 0.2 ivory edge. Only colours and the frame are pushed before `ImGui.Begin`: the title bar's font, height and ☰ ▼ × buttons are Dalamud's own, so they sit and hit as in every Dalamud window. |
| Layout | `Kit/Frame.cs` | Header (fixed), body (scrolls), footer (walnut, pinned, faint top line); `BeginBottom` is the mockups' `flex:1` spacer; toasts. |
| Text | `Kit/Txt.cs` | CSS line boxes (half-leading), letter-spacing glyph by glyph, ellipses, wrapped runs of mixed styles with inline links and dye swatches. |
| Icons | `Kit/Icons.cs` | Lucide icons parsed from the mockups' SVG path data, stroked at 1.8 in the 24-unit grid (1.6 at 24px), round caps. The flow mock-up's (`docs/mockups/flow.html`) are there too: Bell, Store, Scale, Hammer, Flask, Brick, Sofa, Undo, Send, Save, File, Link, AlertTriangle. |
| Controls | `Kit/Kit.cs` | Buttons (primary, glass, quiet, danger, picked; 30/34/40/46 tall), icon buttons, switch (40×24, animated), segmented pill (items can be greyed out, and it shrinks its padding to fit a width; `SegmentedPress` reports any press, the item already picked too), filter chip, status pill, chip, state pill, swatch, round icon well, thumbnail, tick box, stepper, field, progress bar, eyebrow, dashed border, panels, row fills, tooltips, menus. Hover (edges to the 0.34 line), press, keyboard focus ring (2px brass-soft, 2px out) and disabled (50% buttons, 40% small controls). |
| The flow's controls | `Kit/Flow.cs` (`Kit` is partial) | From the 0.6 flow mock-up: `Wrap` (lays things left to right, wrapping), `CheckChip` / `CheckChips` (30px pill: an icon or a 64×6 `Meter`, its words; `ChipTone` Normal / Warn / Brass; the open one's edge lit; `IconInk` colours the icon alone, as the errands' plugins' green tick) with `BeginDetail` / `EndDetail` (the dashed detail under the row), `BeginStage` / `EndStage` (a glass card with a 34px number column, a 1px line joining it to the card above), `StageNumber` (`StageState` Todo / Now / Done), `StageHead` (mono label, serif 22 words, an aside on the right), `CountChip` ("4 retainers"), `BetaTag`, `LabeledSwitch` (a switch with its words and Beta tag), `MoreButton` (quiet, "See the list ›"), `Banner` (one brass line with up to two small buttons; returns which was pressed), `StatCells` (serif numbers over their words; 0 dim), `Journey` (stops in a row on a 2px track, brass up to the one in hand; for the errands' run). |
| Start, Done and Settings' controls | `Kit/Rows.cs` (`Kit` is partial) | `Section` (the mono label, "NEEDS YOU" with its count on the right), `Tabs` / `TabSpec` (small pills, the picked one in glass), `IconThumb` (the 34px thumb), `ActionCard` (a card with a thumb, a title, a line and a small button; Done's undo and fixtures), `BetaButton` (a small button with the Beta tag). |
| Markers in the world | `Overlay.cs` | Markers, plus `Highlight` for Show where: a pulsing ring and label, or an arrow at the screen's edge when the spot is out of view. |

## Start (`Views/Start.cs`, the 0.6 flow: `docs/mockups/flow.html`)

| Mockup | Code |
| --- | --- |
| Where-you-are pill (green / danger / neutral) above "Housing *Studio*" | `DrawStart` header: `WherePill` → `Kit.StatusPill`, then `MainWindow.Title(…, "Housing", "Studio")` |
| Settings icon button | `MainWindow.SettingsButton` (opens Settings on the tab it was last on) |
| The drop zone as one row: round upload well, "Load a design", "Drop a .build or MakePlace file here", Choose a file | `DrawDropZone` (`Kit.Dashed`, the 44px brass well, a small glass `Kit.Button`; the button goes under the words when they'd be narrower than 170px); pressing the zone chooses a file too; drag & drop via `DropTarget` (the zone and the whole body), "Let go to load it" while a file is dragged over |
| This house card: house thumb, "This house", "Medium house · 312 of 450 pieces placed inside", the meter | `DrawThisHouse` (only in a home you may decorate): `Kit.IconThumb`, words `Model/CareWords.HouseLine` (the count from `Session/Care.Count`, read every 2 s while Start shows; cached per count), `Kit.Meter` (how full, `ItemLimits.Of`) |
| The "3 changed" chip and the changes | `Kit.CheckChip` with a snapshot of this home (`Care.SnapshotAt`): "Check for changes" until checked, "Checking…" while `driftChecking`, then `CareWords.Changed` ("3 changed" brass, "Nothing changed" neutral); its tooltip `ChangesTip`. Pressing it runs `CheckForChanges` (`Care.CheckForChanges` on the framework thread) and opens `DrawChanges` (`Views/Care.cs`) under the card in `Kit.BeginDetail`: what moved, was taken away, dyed again or added, with Show where they were and Put them back where they were (`AskPutBack`); pressing it again closes it. It closes by itself (`changesFor`) in another home, once a run has started since (`runner.RunNumber`), or with no check to show and none under way |
| The home on Housing Studio: "On Housing Studio: Cozy Café · Verified in game · Open", Verify as …, "Not on Housing Studio yet · Post it", "Link your account to verify this home" (0.7) | `DrawSiteHome` and `DrawVerify` in `Views/GameLink.cs` (an icon and a line of runs with links, `IconLine`), from `Session/GameLink.Home` (`HomeLook`) |
| Save as a build, Send to my builds, Take a shot | small glass buttons in a `Kit.Wrap` row → `SaveDialog`; `SendHome`, `CantSend` and `DrawSendState` in `Views/SiteLink.cs`; `ShotButton` and `DrawShotHint` (the hint for a clean picture, or why the last shot didn't come out) in `Views/GameLink.cs` |
| From the site (0.7): badge, name, "Design · sent today 21:04", Open, × | `DrawFromSite` in `Views/GameLink.cs` (the newest five, `Kit.Section` with "5 of 12"), from `GameLink.Inbox`; Open → `GameLink.Open` → `OpenFromSite` → `OpenNow`; × → `GameLink.Remove`; how it went under the list (`AlertNote`) |
| Recent label and rows (badge, name, "447 pieces · House (medium) · today 05.24", MAKEPLACE / BUILD / BACKUP / KEPT, chevron) | `DrawRecent` (`Kit.Section`), rows from `ViewModel.Recent` (files read in the background); a swap's kept home (`Core/SwapNames.IsKept`: in the backups folder, not a backup's own name, named "… before …") is KEPT with "Kept from a swap · 391 pieces · yesterday"; right-click → "Take it off this list" |
| "Looking for a design? Browse designs" | `DrawBrowse` (a centred line with the link) → `Util.OpenLink(https://www.kokos-tools.com/housing-studio/browse)` |

## Ready (`Views/Ready.cs`, the 0.6 flow: `docs/mockups/flow.html`)

| Mockup | Code |
| --- | --- |
| Eyebrow "BUILD · INSIDE", title, pill | `DrawReady` header; `ReadyPill` (fits / size mismatch / can't decorate / go home / garden) |
| Pieces, Settings, × | header buttons in `DrawReady` |
| The plot's warnings | `AlertNote`s from `session.Warnings`, above the rest |
| Carry on banner: "A run was cut off at piece N of M." · Carry on · Forget | `DrawCarryOnBanner` (`Views/Care.cs`) → `Kit.Banner`, from `Care.Offer`; Carry on → `CarryOn` (the run's pieces as matched now; it never clears out or swaps: `StartRun`'s `clearOut` is null, and the tooltip says so while Clear out or Swap is picked) |
| Check chips and their detail (one open at a time; press again to close) | `DrawReadyChecks` → `ReadyChecks` (`Views/Care.cs`, made again only when what they're made from changes, `ChecksKey`) → `Kit.CheckChips`, the detail in `Kit.BeginDetail` with `CheckActionsRow` (Show where, Pieces, See which, Copy list). Chips: the item limit as a meter (`ItemLimits.Check`, a chosen clear out's pieces not counted; words `CareWords.LimitDetail`), "N may be refused" and "N can't go here" (`ClashesNow`), "N dyes short" (`vm.MissingDyes`, opens `DyesPopover`), "Storeroom not read", "Near me · N pieces", floors left out, the other side's pieces. None when there's nothing to say. |
| 1 · Get: "N missing", "about X gil", source chips, Get them for me first (Beta), See the list › | `DrawGetStage`: `ShopPlan()` (`Plugin.Shop`), words cached per plan in `GetWords`; `Kit.CountChip` per `SourceKind`; the switch only with `Errands.Enabled` (`Configuration.GetFirst`); See the list opens Shopping. Done ("You have everything") when nothing's missing; "Not known yet" away from home. |
| 2 · Place: "N to do", "N already in place", to move / to place / to dye | `DrawPlaceStage` (`Model/StartSummary`, words `ReadyWords.PlaceBig`), `Kit.StatCells`; a line when you put pieces down by hand (Settings) |
| Keep what's here / Clear out N / Swap, and its line | `DrawPlaceChoice` (`Views/ClearOut.cs`) → `Kit.SegmentedPress` (Clear out greyed with nothing to clear; Swap too, or for a backup; any press sets `Mode`, Keep what's here too), words `ReadyWords.ModeNote`. The choice is `Mode` (per design, Keep for another), `ModeNow` what it comes to (Keep when it can't; drawn, a choice that can't apply is set back to Keep so it doesn't return by itself), `SwapKeepAs` the swap's name (`KeepAsName`, read once a second), `ChosenClearOut()` the pieces put away first (Clear out or Swap). |
| 3 · Finish: "N fixtures", "x of y set", what they are, Set them for me after placing (Beta), Tick off | `DrawFinishStage`: `FinishLine` (`FixtureSummary` and, with the fixtures run on, `FixtureBagWords`; once a second), the switch with `Plugin.FixtureRun.Enabled` (`Configuration.FixturesAfterPlacing`); Tick off → `DrawFixtureTicks` (Set them for me, `DrawSetFixtures`, and the tick rows, `Configuration.FixturesDone` via `Model/FixtureTicks`). While it runs, `DrawFixtureRunPanel` (step, Continue, Stop) in its place. Hidden with no fixtures. |
| Stage numbers | `DrawStages`: the first stage not done is Now (brass), done ones a tick (`Kit.StageState`) |
| The brass button, Preview, the estimate | `DrawReadyFooter`: what it does is `ReadyGo()` (a `GoPlan`, shared with Done after a preview): Get first only when it's on and the errands' estimate gets something and `ErrandsBlocked` (cached) and the fixtures run allow it, else it places (`NotGetting` says why); words `ReadyWords.Go` (cached), tooltip `StartTip` (the old note: stay in the house, the editor, the backup). Pressed (`Go`): getting opens the errands (`errandsOpen`), Swap asks (`AskSwap`), else `StartFromReady` (asks before a clear out). Preview goes under it when the words don't fit beside it; it opens a menu (`DrawPreviewMenu`, `Views/Rehearsal.cs`, `Kit.BeginMenu`): **Mark the spots** (the preview, `StartRun(true, …)`) and **Rehearse the run** (`Rehearse`: as `Go` would, Get first opens the errands with `errandsRehearse`, else `runner.Rehearse` with Ready's clear out and swap name, `ArmFixturesAfterPlacing`), each dim with why as its tip when it can't go (Preview itself only when neither can). The line: `ReadyWords.Hint` (`Core/RunEstimate`, cached; with Get on, the errands' trips first; "Not getting first: why" when it can't). When Start can't run it's at 50% and the line says why (`runner.CanStart`). |
| Fixtures after placing | `ArmFixturesAfterPlacing` (Ready's starts, Carry on, a swap, Done's start after a preview: that run's `RunNumber`) and `ArmFixturesAfterErrands` (the errands once sent out), followed by `Model/AfterPlacing` (tested) from `MainWindow.Tick`, every framework tick from `Plugin`'s update, window open or not: when that run finishes (not stopped, failed or a preview; not another run), or the errands finish with every placing of theirs finished and their summary put away, with the switch on, fixtures left and the same design loaded, `Plugin.FixtureRun.Start()` once, opening the window (`Views/SetFixtures.cs`) |

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
| "PLACING <NAME>" / "PREVIEWING <NAME>" / "REHEARSAL · NOTHING IS COMMITTED · <NAME>" | `DrawRunning` header |
| Rehearsing pill (no mockup): "Rehearsing · about N minutes left", a brass outline, not filled | `RehearsalPill` (`Views/Rehearsal.cs`), while a rehearsal runs (waiting and paused use the usual pills); the errands' running header uses it too, with "Rehearsal · nothing is committed · trip N of M" and Would get / Would spend cards |
| The rehearsal's let-go held for Continue (only if `UiTable.LetGo` were unconfirmed again) | `DrawCallout`: with `runner.HoldingALetGo`, a note that the click isn't seen working yet and **Trust it from now on** (`config.RehearsalTrustLetGo`) |
| Live pill: "Running · about N minutes left", "Paused", "Waiting for you" | `DrawRunning` (`runner.TimeLeft`); the dot breathes while running |
| 56px figure, "of N pieces done", 8px bar | `DrawFigure` (`ViewModel.Figure`: in place at the start + moved + placed) |
| Three phase cards | `DrawPhases` (`runner.PhaseProgress`, `runner.Phase`) |
| Current piece card (icon, name, dye swatch in the stain's colour, floor, step text) | `DrawCurrent` (`Kit.Thumb` with the item's game icon, `ViewModel.StepText`) |
| "N skipped so far" | `DrawRunning` |
| Step by step (no mockup frame) | `DrawCallout`: a brass-edged panel above the log: "STEP BY STEP · NEXT", the step in words, the click itself in mono, Continue (the screen's brass button) and Skip this piece. "Waiting for you" uses the same callout without Continue. |
| "What it's doing" + Copy + last 3 lines | `DrawLog` |
| Centred note | the bottom block of `DrawRunning` |
| Pause / Resume, Stop, footer note | `DrawRunningFooter` |

## Done (`Views/Done.cs`, the 0.6 flow)

| Mockup | Code |
| --- | --- |
| Tick well, "<name> is *in*", one line of what happened | `DrawDone` header, centred: the 52px well (green; a neutral square when you stopped it, danger when stopped for safety), `CenteredTitle` ("Stopped at N of M", "Preview *done*"), `DoneLine` (cached): `Model/DoneWords.Line` ("445 of 447 pieces"; what the errands got is on their own summary, which comes in Done's place after them), the stop's reason, or what a preview found |
| Tally cells | `DrawTally` → `Kit.StatCells(…, 32)` (one array, filled in place) |
| "N pieces not in the design were put away" | `DrawPutAwayLine` (`Views/ClearOut.cs`), under the tally; the moved cell leaves them out (`runner.PutAwayDone`) |
| "Needs you 2" and its rows (thumb, name, one line of why, Show / Copy / Shopping list) | `Kit.Section("Needs you", count)`; `Issues` (made again only when the problems change) → `IssueRow`: each problem from `runner.Problems` (the whole reason is the tooltip); "N left undyed" grouped with Copy; "N pieces you don't have" grouped with Shopping list (opens the Shopping view) |
| One undo card: "Not what you wanted?" + Swap back / Put them back | `Kit.ActionCard`: after a swap `DrawSwapBack` (`Views/ClearOut.cs`, "What was here is kept as “X”") → `runner.SwapBack`; else `DrawPutBack` ("A backup of the 447 pieces you had, saved at 18.57") → `runner.RestoreBackup`; each asks first (`MainWindow.Ask`) |
| Fixtures card: "8 fixtures to set", what they are, Set them (Beta) | `DrawFixturesCard` (`Kit.ActionCard` with `Kit.BetaButton`): with `Plugin.FixtureRun.Enabled`, Set them asks (`AskSetFixtures`, as Ready's) and starts the fixtures run; else the line says to set them from Housing → Remodel Interior. Hidden after a preview, with none left, or while it runs |
| Setting the fixtures (step, Continue, Stop) | `DrawFixtureRunPanel` at the top of the body while `Plugin.FixtureRun.Active` |
| Try the N again / Carry on / Done | `DrawDoneFooter` (`runner.RetryProblems`; Carry on starts again with what's left, clearing out again only if the run stopped was clearing out (`runner.ClearOutCount`); nothing needs you → Done alone, in brass). After a preview the brass button is Ready's (`ReadyGo()`: "Get 12, then place", "Start placing", "Clear out 14, then place", "Swap and place", " · fixtures") and does what it does there (`Go`): the errands' estimate, Swap → `AskSwap` with `KeepAsName`, Clear out → `StartFromReady` (asks first) |

After a rehearsal (`runner.Rehearsal`, or the errands' `Rehearsal` in their summary's place), `DrawRehearsalResult` (`Views/Rehearsal.cs`) draws instead: the well (green, brass when something didn't work, neutral when you stopped it), "Rehearsal *done*" / "*stopped*" (`Model/RehearsalWords`), what it would have done (`Rehearsals.Report.CountsLine`, the purchase questions, the teleports), "What didn't work" with each issue (where, message, kind), the log, and **Copy report** (`Report.Text(log)`) and Done. No undo card, no Try again or Carry on; the fixtures panel shows at the top while they're rehearsed.

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

## Settings (`Views/Settings.cs`, the 0.6 flow)

| Mockup | Code |
| --- | --- |
| "Settings", back on the right | `DrawSettings` header (`Title`, `Kit.IconButton`) |
| Tabs: Placing, Getting 8, Beta 5, Help | `Kit.Tabs` with `SettingsTabs` (`TabSpec`), in the header; the tab is `settingsTab` (`SettingsTab`), kept while the window is open (`OnClose` sets Placing again). `ShowSettings()` opens Placing, `ShowSettings(SettingsTab)` a tab (the errands' "Limits · Change" opens Getting); `OpenSettings(SettingsTab)` inside the window. Each tab's body scrolls on its own. |
| A setting's row: name, one line, control on the right | `SetRow` (11px 14px inside; the name with an icon before it and the Beta tag after it when given; the line in 12px dim; the longer words as the tooltip over them, `tip:`; `below` for wide controls), `SwitchRow`, `StepperRow`, `ButtonRow`. The errands' screen uses them too. |
| Placing: Place pieces for me, Dye after placing, Keep a backup, Pace, Show where pieces go, More options › | `DrawPlacingTab`. Place pieces for me sets both `StoreroomAutomatic` and `BagsAutomatic`. Pace: `Kit.Segmented` (shrinks to fit, else under its words), its line `PaceSetting.Short` ("0.15 s between pieces"), its tooltip `PaceSetting.Note`. More options (12) opens `DrawMoreOptions` in the same panel: place anywhere, pace variation, when to stop, chat, Near me's reach, storeroom / bags apart, floors to place, marker names, placed markers, marker range, save what's placed as a build, and good to know |
| Getting: Market prices, Market ceiling, Other worlds, Spare bag slots, Way to your door, Housing Studio account, Check for things sent from the site, Show homes on Housing Studio when I visit | `DrawGettingTab`: `LookUpMarketPrices`; the errands' limits (`ErrandsMarketCeiling` stepped by `ErrandWords.StepCeiling`, `ErrandsOtherWorlds`, `ErrandsSpareSlots`); **Way to your door** (`DrawDoorWayRow`, 0.7.12): Record (`Errands.RecordWay`; refused inside or during the errands, said in chat), Stop (Danger, `StopWay`) while recording, Forget (`ForgetWays`) when a way is recorded in this zone; the note is `WayNote` or how many are recorded here; `DrawAccountRow` in `Views/SiteLink.cs` ("Linked · hsp_••••3f2a" with Unlink, "Not accepted by the site" once it's refused, or the masked token field with Link and Get a token); `DrawGameLinkRows` in `Views/GameLink.cs` (`CheckInbox`, `ShowHomesWhenVisiting`) |
| Beta: the features' switches with the Beta tag | `DrawBetaTab`: `Experimental` (the master switch), then Get everything for me (`ErrandsForYou`), Get things first from Ready (`GetFirst`, live with the errands on), Set the fixtures for me (`SetFixturesForYou`), Set fixtures after placing (`FixturesAfterPlacing`, live with the fixtures run on), Put back what changed (`PutBackChanges`); each greyed while what it needs is off |
| Help | `DrawHelpTab`: step by step, the old preview method, Record what I do (Start / Stop), Copy the recording and log, Write down the windows' values (while recording), the "Place" words (the built-in ones read once per client language), Trust the new clicks (`ErrandsTrustGuesses`), and the windows and clicks used (`UiTable.All`, read once) |
| "Housing Studio 0.6.0 · What's new" | the bottom block of `DrawSettings` (made once) |

## The small panels (0.7, no mockup)

| Part | Code |
| --- | --- |
| A panel of its own: glass, the line edge, radius 16, no title bar, sized to what it holds, 18px inside, first on the right of the screen | `SmallPanel` (`VisitorPanel.cs`): a Dalamud `Window` with `NoTitleBar`, `AlwaysAutoResize`, `NoFocusOnAppearing`, `NoNav`; no close on Esc, no sounds; `PreOpenCheck` opens it while `Wanted()`; `DrawConditions` hides it in combat, cutscenes and between zones; `Head` (eyebrow and ×), `Picture` (a texture cut from its middle, rounded) |
| The visitor panel: "This home is on Housing Studio", cover, name, by, ✓ Verified in game, Like · 12, Save · 3, Stamp · 9, Your design, "Link your account to like, save or stamp", Open its page, ‹ 1 of 3 ›, × | `VisitorPanel.Body`, from `GameLink.Here` (`Visit`); the three as `Btn.Picked` when on (`ActButton` → `GameLink.Act`) |
| A shot's preview: "Shot from the game", the picture, "For Cozy Café · 2560×1440 · 1.8 MB", Send to Cozy Café, Discard; once sent, Open its page, Close | `ShotPanel.Body`, from `GameLink.Taken` (`Shot`); "Taking the shot…" while `GameLink.Capturing` |

## Looking after a home (`Views/Care.cs`, no mockup)

| Part | Code |
| --- | --- |
| Start: This house, "312 of 450 pieces placed inside" | `DrawThisHouse` (`Views/Start.cs`), the count from `Session/Care.Count` (read every 2 s while Start shows), words from `Model/CareWords.HouseLine` |
| Start: the changes chip, the changes, Show where they were, Put them back where they were | `CheckForChanges` → `DrawChanges` → `AskPutBack` (`MainWindow.Ask`), from `Care.CheckForChanges` / `Care.PutBack` |
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
- Start, Done and Settings (the 0.6 flow mock-up): the changes chip reads "Check for changes" until a
  check has been made (checking reads what's placed and the snapshot, so it isn't done unasked);
  after one it says "3 changed" or "Nothing changed", and pressing it checks again and opens the
  changes. The mock's 40px title is 32 (the serif's sizes are 56/32/28/26/22), its 12.5px lines 13
  (Geist is loaded at 12 and 13), its 13.5px names 14, the Settings icon and the back arrow 40px
  (as on Ready). "This garden" in a garden; a garden's line is its count alone. The undo card
  says when the backup was saved (Put them back) or keeps the time in its tooltip (Swap back). Market
  prices moved from More options to Getting, so More options counts 12, as the mock does; Getting
  has the errands' spare bag slots too (the mock shows three of its rows), and Beta has Ready's two
  switches (Get things first from Ready, Set fixtures after placing) after the feature each needs,
  greyed while it's off. Help also keeps the old preview method, Copy the recording and log, Write
  down the windows' values, the "Place" words and the windows and clicks used.

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
