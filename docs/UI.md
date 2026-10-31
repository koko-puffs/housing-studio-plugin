# The window (0.3.0)

The window is drawn from six mockups (Start, Ready, Running, Done, Pieces, Settings) and the
Housing Studio design system's tokens. This maps each part of them to the code that draws it.
Paths are under `HousingStudio/UI/`.

## How it's put together

| Part | File | What it does |
| --- | --- | --- |
| Routing | `MainWindow.cs` → `Draw` | Picks the view: Settings or Pieces when opened from a header, else `ViewModel.View` (Start → Ready → Running → Done). Pieces and Settings are hidden while a run goes. |
| View state | `ViewModel.cs` | The run's figure and piece count, whether Done is waiting, the step in words, missing dyes (cached per plan), the Recent list. |
| Game-free logic | `Model/*.cs` | Words (counts, home sizes, "about 25 minutes"), `PaceSetting`, `When` ("today 05.24"), `PieceRows` (stacking, chip counts), `StartSummary`, `FixtureTicks`, `StepWords`, `SvgPath`, `Dashes` (the dashed edge, with bounded loops). Tested in `HousingStudio.Tests/UiModelTests.cs`. |
| Tokens | `Kit/Palette.cs` | Every `hs-*` colour from tokens.json, radii, control heights. Translucent tokens keep their alpha; panel glass is flattened over ink (ImGui can't blur). |
| Fonts | `Kit/Fonts.cs` | Geist 400 (15/14/13/12), 500 (15/14/13), 600 (15/14); Geist Mono (13/12/11); Cormorant Garamond 500 (56/32/28/26/22) and 400 italic (32), through Dalamud's font atlas. CSS sizes are converted to ImGui's line-based sizes (Geist ×1.3, Cormorant ×1.211) so 14px is 14px. |
| Window frame | `Kit/Theme.cs` → `PushFrame` | Dalamud's title bar in walnut with ivory words, a 10px window radius and the 0.2 ivory edge. Only colours and the frame are pushed before `ImGui.Begin`: the title bar's font, height and ☰ ▼ × buttons are Dalamud's own, so they sit and hit as in every Dalamud window. |
| Layout | `Kit/Frame.cs` | Header (fixed), body (scrolls), footer (walnut, pinned, faint top line); `BeginBottom` is the mockups' `flex:1` spacer; toasts. |
| Text | `Kit/Txt.cs` | CSS line boxes (half-leading), letter-spacing glyph by glyph, ellipses, wrapped runs of mixed styles with inline links and dye swatches. |
| Icons | `Kit/Icons.cs` | Lucide icons parsed from the mockups' SVG path data, stroked at 1.8 in the 24-unit grid (1.6 at 24px), round caps. |
| Controls | `Kit/Kit.cs` | Buttons (primary, glass, quiet, danger, picked; 30/34/40/46 tall), icon buttons, switch (40×24, animated), segmented pill, filter chip, status pill, chip, state pill, swatch, round icon well, thumbnail, tick box, stepper, field, progress bar, eyebrow, dashed border, panels, row fills, tooltips, menus. Hover (edges to the 0.34 line), press, keyboard focus ring (2px brass-soft, 2px out) and disabled (50% buttons, 40% small controls). |
| Markers in the world | `Overlay.cs` | Markers, plus `Highlight` for Show where: a pulsing ring and label, or an arrow at the screen's edge when the spot is out of view. |

## Start (`Views/Start.cs`)

| Mockup | Code |
| --- | --- |
| "Housing *Studio*" | `DrawStart` → `MainWindow.Title(…, "Housing", "Studio")` |
| Where-you-are pill (green / danger / neutral) | `WherePill` → `Kit.StatusPill` |
| Settings icon button | `MainWindow.SettingsButton` |
| Dashed drop zone, round upload icon, "Load a design", note, Choose a file | `DrawDropZone` (`Kit.Dashed`, `Kit.Round`, `Kit.Button`); drag & drop via `DropTarget` (the zone and the whole body) |
| Recent eyebrow and rows (badge, name, "N pieces · House (medium) · today 05.24", MAKEPLACE / BUILD / BACKUP, chevron) | `DrawRecent`, rows from `ViewModel.Recent` (files read in the background); right-click → "Take it off this list" |
| "Looking for a design?" + Browse designs | `DrawBrowse` → `Util.OpenLink(https://www.kokos-tools.com/housing-studio/browse)` |

## Ready (`Views/Ready.cs`)

| Mockup | Code |
| --- | --- |
| Eyebrow "MAKEPLACE FILE · INSIDE", title, pill | `DrawReady` header; `ReadyPill` (fits / size mismatch / can't decorate / go home / garden) |
| Pieces, Settings, × | header buttons in `DrawReady` |
| "What Start does" rows (Move / Place / Dye; icon, serif number, line, note; hidden at 0) | `DrawStartSteps` → `StepRow`, counts from `Model/StartSummary` (dyes from `Core/DyeCounts.ToApply`) |
| "N dyes missing" + See which | a popover listing the dyes with Copy list (`DyesPopover`), from `LayoutSession.MissingDyes` |
| "2 pieces are already in place. 29 pieces…" | `SummaryLine` (also: missing pieces, Near me, floors left out, storeroom not read, the other side's pieces) |
| Fixtures disclosure with "x of 11 done" | `DrawFixtures` → `Disclosure`, `Kit.Chip`; ticks in `Configuration.FixturesDone` via `Model/FixtureTicks` |
| Info note | the bottom block of `DrawReady` |
| Clear out what isn't in the design (switch), Swap to this design | `Views/ClearOut.cs` → `DrawClearOut` (`SwitchRow`, `SetRow`); the pieces from `Core/ClearOut.Pick` (cached per plan and filters); Start placing asks first (`StartFromReady`), Swap asks (`AskSwap`) and starts with `keepAs` |
| Set them for me (Settings → Experimental) | `Views/SetFixtures.cs` → `DrawSetFixtures` under the fixtures' note: the plan's counts (`FixtureRunner.Plan`, read once a second), the button, and while it runs a brass panel with the step, Continue and Stop |
| Start placing / Preview first; "About N minutes at a quick pace"; "Preview marks each spot, moving nothing" | `DrawReadyFooter`; estimate from `Core/RunEstimate` (cached), words from `Words.About`. When Start can't run, it's at 50% and the left line says why (`runner.CanStart`). |

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
| More options (13) | `DrawMoreOptions`: place anywhere, pace variation (±5% steps), when to stop, chat, market prices (Universalis), Near me reach, storeroom / bags separately, floors to place, marker labels, placed markers, marker range, save what's placed as a build, good to know |
| Something not working? | `DrawTrouble`: step by step, move the preview before Place (old method), the recorder, the "Place" words, windows and clicks used |
| "Housing Studio 0.3.0" + What's new | the bottom block of `DrawSettings` |

## Looking after a home (`Views/Care.cs`, no mockup)

| Part | Code |
| --- | --- |
| Start: "312 of 450 pieces placed inside" | `DrawHomeCare`, the count from `Session/Care.Count` (read every 2 s while Start shows), words from `Model/CareWords.HowFull` |
| Start and Ready: "Since your last run", Check for changes, the changes, Show where they were, Put them back where they were | `DrawDrift` → `DrawDriftResult` → `AskPutBack` (`MainWindow.Ask`), from `Care.CheckForChanges` / `Care.PutBack` |
| Ready: "A run of this design was cut off…", Carry on from piece N of M, Forget it | `DrawCarryOn` (brass-wash panel), from `Care.Offer` |
| Ready: won't fit, pieces the game may refuse | `DrawReadyCare` → `AlertNote` (danger, or dim when Place anywhere takes them) |
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

## What the runner could add

`ViewModel.cs` keeps a few things the runner doesn't say yet (marked `TODO(runner)`):

- `Pieces`: the entries a run was given, so the figure's total doesn't depend on the window seeing
  the run start (a restore or retry started by the runner falls back to the plan).
- `StepText`: the step in plain words; the labels are written for the log, so
  `Model/StepWords` rewrites them ("Setting it at its spot").
- Pausing between pieces, so the footer can say what the mockup says.
