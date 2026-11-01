using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HousingStudio.Automation;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// A rehearsal (Ready's Preview → Rehearse the run): the same plan, order, windows, clicks and waits
/// as a real run, with every commit point done up to the moment before it and then backed out:
/// <list type="bullet">
/// <item>a move: picked up and let go as it is (<see cref="UiTable.LetGo"/>; never shifted while
/// held), and checked unmoved;</item>
/// <item>a storeroom piece: its row found and picked, the game's preview shown at its spot, "Place"
/// found in the menu, and the menu closed instead (the preview goes with it);</item>
/// <item>a bag piece: its menu opened, "Store" (or "Place") found, and the menu closed;</item>
/// <item>clear out: the editor put in its storeroom mode, and the piece not picked (in that mode the
/// pick itself stores it);</item>
/// <item>a dye: the dye window opened on the piece and the dye chosen, then closed without Dye;</item>
/// <item>pieces placed by hand: not waited for.</item>
/// </list>
/// Each would-be commit goes in <see cref="Rehearsals.Report"/> ("Would place …"), with what didn't
/// work. No backup, no swap kept, no snapshot or resume.json (Care leaves a rehearsal alone), place
/// anywhere left off; the editor's set-down is refused while a rehearsal goes (<see cref="Rehearsals"/>).
/// </summary>
public sealed unsafe partial class PlacementRunner
{
    /// <summary>The step key of the rehearsal's let-go, held for Continue until trusted.</summary>
    private const string LetGoKey = "let go";

    /// <summary>The next <see cref="Start(bool, IEnumerable{PlanEntry}, bool, IReadOnlyList{PlacedPiece})"/> is a rehearsal.</summary>
    private bool rehearseNext;

    /// <summary>The run going (or the last) is a rehearsal: every step a real run does, nothing committed.</summary>
    public bool Rehearsal { get; private set; }

    /// <summary>Step by step is holding before the rehearsal's let-go (a click not yet seen working).</summary>
    public bool HoldingALetGo => AwaitingContinue && Rehearsal && queue.Current?.Key == LetGoKey;

    /// <summary>A placed piece the rehearsal holds (picked up as a move does; never shifted), by name for what's said if it's left held.</summary>
    private (nint Instance, string Name)? rehearsalHeld;

    private Func<bool>? rehearsingNow;

    /// <summary>For the guard (<see cref="Rehearsals"/>): this runner is rehearsing now.</summary>
    private Func<bool> rehearsing => rehearsingNow ??= () => Active && Rehearsal;

    /// <summary>
    /// Rehearses a run on these pieces as Ready's choice has it (<paramref name="clearOut"/> for Clear out
    /// or Swap; <paramref name="keepAsName"/> is only said: nothing is kept). The report isn't started
    /// here: the window starts it (a rehearsal's errands and fixtures add to the same one).
    /// </summary>
    public void Rehearse(IEnumerable<PlanEntry> shown, IReadOnlyList<PlacedPiece>? clearOut, string? keepAsName = null)
    {
        if (keepAsName != null) log.Info($"Would keep what's here as “{keepAsName}” (a swap): not in a rehearsal.");
        rehearseNext = true;
        try
        {
            Start(false, shown, clearOut);
        }
        finally
        {
            rehearseNext = false;
        }
    }

    private static RehearsalTrouble TroubleOf(string? message) =>
        message != null && message.StartsWith("Timed out", StringComparison.Ordinal) ? RehearsalTrouble.TimedOut : RehearsalTrouble.Failed;

    private static RehearsalAct ActOf(RunJob job) => job.Kind switch
    {
        JobKind.Move => RehearsalAct.Move,
        JobKind.Store => RehearsalAct.Store,
        JobKind.Dye => RehearsalAct.Dye,
        _ => RehearsalAct.Place,
    };

    /// <summary>The rehearsal reached this piece's commit point: noted for the piece, the log and the report.</summary>
    private void Would(RunJob job, string words)
    {
        job.Note = $"Rehearsed: would {words}.";
        log.Info($"  Would {words} (a rehearsal: not done).");
        // A stand-in's dye is a try of the dye path, counted once as the piece it stands in for.
        if (!standIns.Contains(job)) Rehearsals.Report.Would(ActOf(job));
    }

    /// <summary>Stand-in dye jobs (a placed piece the dye path is tried on, for one that isn't placed).</summary>
    private readonly HashSet<RunJob> standIns = new();

    /// <summary>The dyes this rehearsal has tried on a stand-in already (each dye once).</summary>
    private readonly HashSet<byte> dyesTriedOnStandIn = new();

    /// <summary>
    /// A dye for a piece not placed yet (a rehearsal places nothing): counted, and, the first time a
    /// rehearsal comes to each dye, the dye path tried on a stand-in, a placed piece that takes a dye
    /// (Furnishing Color, the piece picked, the dye window, the dye found and chosen, then closed
    /// without Dye and the stand-in checked to have kept its own). Without a stand-in, only counted.
    /// </summary>
    private (List<Step>, List<Step>) RehearseDyeOncePlaced(RunJob job)
    {
        var name = Name(job);
        var stain = job.Target.Stain;
        var dye = data.DyeName(stain);
        Rehearsals.Report.WouldDyeOncePlaced();
        LivePiece? standIn = null;
        string? none = null;
        if (!dyesTriedOnStandIn.Contains(stain))
        {
            if (ReadLive() is not { } live) none = "what's placed here couldn't be read";
            else
            {
                // A piece dyed already surely takes a dye (0.7.12's chamber found no stand-in among 100
                // placed pieces going by the item sheet alone); then those the sheet says take one.
                var loaded = live.Values.Where(p => p.Instance != 0 && p.Stain != stain).ToList();
                standIn = loaded.Where(p => p.Stain != 0).Concat(loaded.Where(p => p.Stain == 0 && data.Dyeable(p.ItemId)))
                    .Select(p => (LivePiece?)p)
                    .FirstOrDefault();
                if (standIn == null)
                    none = $"none of the {live.Count} pieces placed here is dyed or takes a dye by the game's data ({live.Values.Count(p => p.Instance != 0)} loaded, "
                           + $"{live.Values.Count(p => data.Dyeable(p.ItemId))} dyeable, {live.Values.Count(p => p.Stain != 0)} dyed)";
            }
        }
        if (standIn is not { } s)
        {
            var why = dyesTriedOnStandIn.Contains(stain) ? $"{dye} was tried on a stand-in already" : none ?? "nothing placed here takes a dye to try it on";
            return (new List<Step>
            {
                Step.Act($"Would dye {name} {dye} once placed (it isn't placed in a rehearsal; {why})", () =>
                {
                    job.Note = $"Rehearsed: would dye it once placed (not tried on it: it isn't placed; {why}).";
                    return StepOutcome.Done;
                }),
            }, new List<Step>());
        }
        dyesTriedOnStandIn.Add(stain);
        var stand = new RunJob { Kind = JobKind.Dye, Target = job.Target with { ItemId = s.ItemId, Key = s.Key }, Key = s.Key };
        standIns.Add(stand);
        var standName = data.ItemName(s.ItemId);
        var steps = new List<Step>
        {
            Step.Act($"Try {dye} on {standName} instead ({name} isn't placed in a rehearsal): every dye step but Dye", () =>
            {
                job.Note = $"Rehearsed: would dye it once placed; {dye} was tried on {standName} (a stand-in), every step but Dye.";
                log.Info($"  {name} isn't placed in a rehearsal: {dye} is tried on {standName} (slot {s.Key}), placed here, and the window is closed without Dye.");
                return StepOutcome.Done;
            }),
        };
        steps.AddRange(DyeSteps(stand));
        return (steps, DyeCleanup());
    }

    /// <summary>The steps of a piece in a rehearsal: as <see cref="Script"/>, backed out at each commit point.</summary>
    private (List<Step> Steps, List<Step> Cleanup) RehearsalScript(RunJob job) => job.Kind switch
    {
        JobKind.Move => (MoveSteps(job, job.Key!.Value), RehearsalHeldCleanup()),
        JobKind.PlaceFromStoreroom when side == Side.Interior && config.StoreroomAutomatic => (StoreroomSteps(job), MenuCleanup().Concat(RehearsalHeldCleanup()).ToList()),
        JobKind.PlaceFromBags when config.BagsAutomatic => (RehearseBag(job), MenuCleanup().Concat(RehearsalHeldCleanup()).ToList()),
        JobKind.Dye when job.Key == null => RehearseDyeOncePlaced(job),
        JobKind.Dye => (DyeSteps(job), DyeCleanup()),
        JobKind.Store => (RehearseStore(job), new List<Step>()),
        _ => (new List<Step>
        {
            Step.Act($"Would wait for you to put {Name(job)} down anywhere, then set it at its spot (not in a rehearsal)", () =>
            {
                Would(job, $"wait for you to put {Name(job)} down, then set it at its spot");
                return StepOutcome.Done;
            }),
        }, new List<Step>()),
    };

    /// <summary>
    /// A move's commit point: the piece is picked up as a real move does, and let go as it is with
    /// <see cref="UiTable.LetGo"/>. It isn't shifted to its spot while held (0.7.1 shifted it and wrote
    /// its old spot back first, and its check read an Icebox away from where it was after). Its spot and
    /// turn are checked after; if the editor still holds it, you press Esc.
    /// </summary>
    private List<Step> RehearseHold(RunJob job, int key, nint instance)
    {
        var name = Name(job);
        (Vector3 Position, float Yaw)? was = null;
        return new List<Step>
        {
            new Step
            {
                Label = $"Hold {name} where it is (a rehearsal doesn't take it to its spot)",
                Key = "hold at spot",
                IsAction = true,
                Run = () =>
                {
                    if (HousingEditor.ActiveItem != instance) return StepOutcome.Skip("The game let go of it before the rehearsal could look at it.", ProblemKind.DidNotLand);
                    was = FurnitureReader.Stored(side == Side.Garden, key);
                    rehearsalHeld = (instance, name);
                    var t = job.Target.Position;
                    Would(job, $"set {name} down at ({t.X:0.00}, {t.Y:0.00}, {t.Z:0.00}), turned {job.Target.Yaw * 180 / MathF.PI:0}°");
                    return StepOutcome.Done;
                },
            },
            Step.Pause("Hold it a moment", Speed.Wait(200)),
            new Step
            {
                Label = $"Let go of {name} where it was: {UiTable.LetGo.Describe()}",
                Key = LetGoKey,
                IsAction = true,
                Run = () =>
                {
                    // Pressing Move takes the editor out of Rotate for a moment: that isn't "the editor closed".
                    editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                    if (HousingEditor.ActiveItem == 0) return StepOutcome.Done;
                    return GameUi.Fire(UiTable.LetGo) ? StepOutcome.Done : StepOutcome.Halt($"{UiTable.LetGo.Addon} isn't open, so the editor couldn't let go of {name}.");
                },
            },
            new Step
            {
                Label = "The editor lets go",
                Key = "lets go",
                Timeout = TimeSpan.FromSeconds(2),
                OnTimeout = StepStatus.Done, // decided next
                Run = () => HousingEditor.ActiveItem == 0 ? StepOutcome.Done : StepOutcome.Waiting,
            },
            new Step
            {
                Label = $"Did the editor let go of {name}?",
                Run = () =>
                {
                    if (HousingEditor.ActiveItem != 0)
                    {
                        log.Warn($"  The editor still holds {name} after {UiTable.LetGo.Describe()}: you let go (Esc). A rehearsal never sets a piece down.");
                        Rehearsals.Report.Issue(name, $"The editor didn't let go of it after {UiTable.LetGo.Describe()} (pressing Move while holding a piece): you were asked to press Esc.", RehearsalTrouble.NoMatch);
                        if (config.ChatMessages) Services.Chat.Print($"Press Esc to let go of {name} (a rehearsal never sets it down).", "Housing Studio");
                        queue.InsertNext(new[]
                        {
                            new Step
                            {
                                Label = $"You: press Esc to let go of {name} (a rehearsal never sets it down)",
                                Timeout = YouWait,
                                TimeoutMessage = $"The editor still holds {name}: it's left for you (press Esc).",
                                Run = () =>
                                {
                                    editorMayCloseUntil = DateTime.UtcNow + ModeSwitchGrace;
                                    return HousingEditor.ActiveItem == 0 ? StepOutcome.Done : StepOutcome.Waiting;
                                },
                            },
                        });
                    }
                    else log.Info($"  Let go of {name}: the editor holds nothing (mode {ModeWord(HousingEditor.Mode)}).");
                    rehearsalHeld = null;
                    return StepOutcome.Done;
                },
            },
            new Step
            {
                Label = $"Check {name} is where it was",
                Delay = Speed.Wait(300),
                Run = () =>
                {
                    var now = FurnitureReader.Stored(side == Side.Garden, key);
                    if (was is { } w && now is { } n
                        && (Vector3.Distance(w.Position, n.Position) > LandedDistance || Transforms.AngleDiff(w.Yaw, n.Yaw) > LandedYaw))
                    {
                        var line = $"After letting go it's at ({n.Position.X:0.00}, {n.Position.Y:0.00}, {n.Position.Z:0.00}) turned {n.Yaw * 180 / MathF.PI:0}°, "
                                   + $"not where it was ({w.Position.X:0.00}, {w.Position.Y:0.00}, {w.Position.Z:0.00}) turned {w.Yaw * 180 / MathF.PI:0}°: put it back by hand.";
                        log.Warn($"  {name}: {line}");
                        Rehearsals.Report.Issue(name, line);
                    }
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>
    /// A storeroom piece's commit point: its row is picked and the game shows its preview (as a real
    /// run); here the preview is shown at the piece's spot, "Place" is found in the row's menu, and the
    /// menu is closed instead (as a real run closes a row that isn't the piece). The game keeps showing the
    /// preview after that (the row stays picked until another is), so only the menu and the hand are waited for.
    /// </summary>
    private List<Step> RehearsePlace(RunJob job, int[] chosen, Func<int> placedBefore)
    {
        var name = Name(job);
        return new List<Step>
        {
            Step.Act($"Show {name} at its spot (the game's preview moved there; nothing is placed)", () =>
            {
                if (!HousingActions.AimPreview(job.Target.Position, job.Target.Yaw)) log.Info("  The game shows no preview to move.");
                return StepOutcome.Done;
            }, Speed.Wait(150)),
            Step.Pause("Show it there a moment", Speed.Wait(400)),
            new Step
            {
                Label = $"Find \"{PlaceLabels().FirstOrDefault()}\" in the storeroom row's menu (not chosen: a rehearsal)",
                Run = () =>
                {
                    var menu = GameUi.MenuEntries();
                    if (menu == null) return StepOutcome.Skip("The row's menu closed before \"Place\" could be found.", ProblemKind.Other);
                    log.Info($"  menu entries: {string.Join(" | ", menu.Select((e, i) => $"{i}:{e}"))}");
                    var entry = Menus.FindEntry(menu, PlaceLabels());
                    if (entry < 0)
                        return StepOutcome.Skip($"The menu has no entry like \"{string.Join("\" / \"", PlaceLabels())}\" (it has: {string.Join(", ", menu)}): a real run would stop here.", ProblemKind.Other);
                    Would(job, $"choose “{menu[entry]}” (entry {entry}) to place {name} from row {chosen[0]}, then set it at its spot");
                    return StepOutcome.Done;
                },
            },
            Step.Act($"Close the menu instead: {UiTable.MenuCancel.Describe()}", () =>
            {
                if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                return StepOutcome.Done;
            }),
            new Step
            {
                Label = "The menu closes",
                Timeout = WindowWait,
                OnTimeout = StepStatus.Done, // checked next
                // Only the menu: the game keeps the row's preview shown, as the editor's active item, until
                // another row is picked (a recording shows it), and that's nothing placed.
                Run = () => !GameUi.IsOpen(UiTable.ContextMenu) ? StepOutcome.Done : StepOutcome.Waiting,
            },
            new Step
            {
                Label = $"Check {name} wasn't placed",
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.ContextMenu) || HousingEditor.Mode == EditorMode.Place)
                    {
                        var what = GameUi.IsOpen(UiTable.ContextMenu) ? "the menu stayed open" : "the editor went on to place it";
                        log.Warn($"  After closing the menu, {what}.");
                        Rehearsals.Report.Issue(name, $"After closing the storeroom row's menu ({UiTable.MenuCancel.Describe()}), {what}.", RehearsalTrouble.NoMatch);
                    }
                    var count = FurnitureReader.PlacedCount(side == Side.Garden);
                    if (placedBefore() >= 0 && count > placedBefore())
                        Rehearsals.Report.Issue(name, $"The pieces placed went from {placedBefore()} to {count}: something was placed.", RehearsalTrouble.Failed);
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>
    /// A bag piece's commit point: its menu is opened as a real run opens it, the entry a real run would
    /// choose is found ("Store", then it's placed from the storeroom; in a garden, or without "Store",
    /// "Place"), and the menu is closed instead.
    /// </summary>
    private List<Step> RehearseBag(RunJob job)
    {
        var name = Name(job);
        var stock = job.Entry?.Stock;
        var storeFirst = BagRoute.First(side == Side.Interior) == BagWay.Store;
        var storeWords = UiTable.StoreLabels(Services.ClientState.ClientLanguage);
        return new List<Step>
        {
            EnsureMode(EditorMode.Rotate),
            Step.Act($"Open {name}'s menu ({(stock != null ? InventoryReader.Describe(stock) : "bags")})", () =>
            {
                var slot = FindInBags(job.Target.ItemId, stock);
                if (slot == null) return StepOutcome.Skip("It's not in your bags any more.", ProblemKind.NotFound);
                log.Info($"  {name}: container {slot.Container}, slot {slot.Slot}; menu opened from window id {GameUi.BagWindowId()}.");
                return HousingActions.OpenBagItemMenu(slot.Container, slot.Slot) ? StepOutcome.Done : StepOutcome.Halt("Couldn't open the item's menu.");
            }),
            Step.Wait("Its menu opens", () => GameUi.IsOpen(UiTable.ContextMenu), WindowWait),
            new Step
            {
                Label = $"Find \"{(storeFirst ? storeWords.FirstOrDefault() : PlaceLabels().FirstOrDefault())}\" in the item's menu (not chosen: a rehearsal)",
                Run = () =>
                {
                    var menu = GameUi.MenuEntries();
                    if (menu == null) return StepOutcome.Skip("The item's menu closed before its entry could be found.", ProblemKind.Other);
                    log.Info($"  menu entries: {string.Join(" | ", menu.Select((e, i) => $"{i}:{e}"))}");
                    var store = storeFirst ? Menus.FindEntry(menu, storeWords) : -1;
                    if (store >= 0)
                    {
                        Would(job, $"choose “{menu[store]}” (entry {store}) to send {name} to the storeroom, then place it from there and set it at its spot");
                        return StepOutcome.Done;
                    }
                    if (storeFirst) log.Warn($"  The menu has no entry like \"{string.Join("\" / \"", storeWords)}\": a real run places it from the bag instead.");
                    var place = Menus.FindEntry(menu, PlaceLabels());
                    if (place < 0)
                        return StepOutcome.Skip($"The item's menu has neither \"{storeWords.FirstOrDefault()}\" nor \"{PlaceLabels().FirstOrDefault()}\" (it has: {string.Join(", ", menu)}).", ProblemKind.Other);
                    Would(job, $"choose “{menu[place]}” (entry {place}) and set {name} down at its spot with place anywhere on");
                    return StepOutcome.Done;
                },
            },
            Step.Act($"Close the menu instead: {UiTable.MenuCancel.Describe()}", () =>
            {
                if (GameUi.IsOpen(UiTable.ContextMenu)) GameUi.Fire(UiTable.MenuCancel);
                return StepOutcome.Done;
            }),
            Step.Wait("Its menu closes", () => !GameUi.IsOpen(UiTable.ContextMenu), WindowWait, StepStatus.Done),
        };
    }

    /// <summary>
    /// Clear out's commit point: the editor is put in Store mode (its Store button, as a real run), and
    /// the piece is checked to be there; it isn't picked, as in Store mode the pick itself may put it
    /// away (whether the game asks first isn't known).
    /// </summary>
    private List<Step> RehearseStore(RunJob job)
    {
        var name = Name(job);
        var key = job.Key!.Value;
        var steps = StoreSteps(job).Take(2).ToList();
        steps.Add(Step.Act($"Would put {name} away (picked in Store mode): not picked in a rehearsal, as the pick itself may store it", () =>
        {
            var live = ReadLive();
            if (live == null || !live.TryGetValue(key, out var piece) || piece.ItemId != job.Target.ItemId)
                return StepOutcome.Skip("It's no longer where it was (moved or put away already).", ProblemKind.NotFound);
            if (piece.Instance == 0) return StepOutcome.Skip("The game hasn't loaded it (walk closer to it).", ProblemKind.NotLoaded);
            Would(job, $"pick {name} in Store mode to put it away in the storeroom (answering yes if the game asks)");
            return StepOutcome.Done;
        }));
        return steps;
    }

    /// <summary>
    /// A dye's commit point: the dye window is open on the piece and the dye chosen (as a real run);
    /// here Dye isn't pressed, the window is closed (a "dye it?" question is answered no), and the
    /// piece is checked to have kept its dye. Ends the piece (the real run's Dye steps after it don't run).
    /// </summary>
    private List<Step> RehearseDyeEnd(RunJob job, int key)
    {
        var name = Name(job);
        var dye = data.DyeName(job.Target.Stain);
        var before = StainOf(key);
        var saidNo = false;
        return new List<Step>
        {
            Step.Act($"Would press Dye: {UiTable.DyeApply.Describe()} (not pressed: a rehearsal)", () =>
            {
                Would(job, $"dye {name} {dye}");
                return StepOutcome.Done;
            }, Speed.Wait(300)),
            Step.Act($"Close the dye window without dyeing: {UiTable.DyeClose.Describe()}", () =>
            {
                if (GameUi.IsOpen(UiTable.DyeWindow)) GameUi.Fire(UiTable.DyeClose);
                return StepOutcome.Done;
            }),
            new Step
            {
                Label = "The dye window closes",
                Timeout = WindowWait,
                OnTimeout = StepStatus.Done, // checked next
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.DyeConfirm) && !saidNo)
                    {
                        saidNo = true;
                        log.Info($"  The game asks: {string.Join(" ", GameUi.TextValues(UiTable.DyeConfirm).Values.Where(v => v.Length > 0).Take(4))} → {UiTable.DyeConfirmNo.Describe()}");
                        GameUi.Fire(UiTable.DyeConfirmNo);
                    }
                    return !GameUi.IsOpen(UiTable.DyeWindow) && !GameUi.IsOpen(UiTable.DyeConfirm) ? StepOutcome.Done : StepOutcome.Waiting;
                },
            },
            new Step
            {
                Label = $"Check {name} kept its dye",
                Run = () =>
                {
                    if (GameUi.IsOpen(UiTable.DyeWindow))
                        Rehearsals.Report.Issue(name, $"The dye window didn't close with {UiTable.DyeClose.Describe()}.", RehearsalTrouble.NoMatch);
                    var now = StainOf(key);
                    if (now != before)
                        Rehearsals.Report.Issue(name, $"Its dye changed ({DyeWord(before)} → {DyeWord(now)}) though Dye wasn't pressed.", RehearsalTrouble.Failed);
                    queue.FinishAfterThis();
                    return StepOutcome.Done;
                },
            },
        };
    }

    /// <summary>
    /// When a rehearsal's piece ends badly with the editor holding something: it's left for you to let
    /// go (Esc); nothing is set down.
    /// </summary>
    private List<Step> RehearsalHeldCleanup() => new()
    {
        new Step
        {
            Label = "Leave what the editor holds to you (a rehearsal never sets it down)",
            Run = () =>
            {
                if (!StillInStartHome()) return StepOutcome.Done;
                var held = HousingEditor.ActiveItem;
                if (held == 0)
                {
                    rehearsalHeld = null;
                    return StepOutcome.Done;
                }
                SayStillHeld();
                return StepOutcome.Done;
            },
        },
    };

    private void SayStillHeld()
    {
        // The last storeroom row's preview: the game keeps it as its active item until another row is
        // picked. Nothing is placed; it isn't a piece held.
        if (HousingEditor.Mode != EditorMode.Place && FurnitureReader.Preview() != null && rehearsalHeld == null)
        {
            log.Info("  The game still shows the last storeroom row's preview (nothing was placed): picking another row, or Esc, clears it.");
            return;
        }
        var what = rehearsalHeld is { } h && h.Instance == HousingEditor.ActiveItem ? $"{h.Name} (where it was)" : "a piece";
        var line = HousingEditor.Mode == EditorMode.Place
            ? "The editor is still holding a piece to place: press Esc to put it back. A rehearsal never sets a piece down."
            : $"The editor still holds {what}: press Esc to let go. A rehearsal never sets a piece down.";
        log.Warn($"  {line}");
        Rehearsals.Report.Issue("The editor", line, RehearsalTrouble.Failed);
        if (config.ChatMessages) Services.Chat.Print(line, "Housing Studio");
    }

    /// <summary>The rehearsal finished: if the editor still holds something, you're told (it's never set down).</summary>
    private void RehearsalEnded()
    {
        if (!StillInStartHome()) return;
        if (HousingEditor.ActiveItem != 0) SayStillHeld();
        rehearsalHeld = null;
    }
}
