using System;
using System.Collections.Generic;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Clearing out and swapping, in Ready and Done. Ready's Place stage chooses what happens to the pieces
/// placed here that aren't in the design (<see cref="ReadyMode"/>): Keep what's here (the default for each
/// design), Clear out (they go to the storeroom before the design is placed), or Swap (clears out, places,
/// and keeps what was here under a name to swap back to). Done: how many were put away, and Swap back
/// after a swap.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The design (its file) a choice other than Keep was made for, and the choice; another design starts with Keep.</summary>
    private (string? For, ReadyMode Mode) readyMode;

    private string DesignId => session.FilePath ?? session.Title;

    /// <summary>
    /// Ready's choice for the pieces here that aren't in the design, as picked for this design (the
    /// errands' screen reads it too). <see cref="ModeNow"/> is what it comes to here and now.
    /// </summary>
    private ReadyMode Mode
    {
        get => readyMode.For != null && readyMode.For == DesignId ? readyMode.Mode : ReadyMode.Keep;
        set => readyMode = (value == ReadyMode.Keep ? null : DesignId, value);
    }

    private (string? Path, bool Allowed) swapAllowed;

    /// <summary>A swap keeps what's here under a name; a backup loaded as the design is already such a one (asked once per design file).</summary>
    private bool SwapAllowed
    {
        get
        {
            if (session.FilePath is not { } path) return true;
            if (swapAllowed.Path != path) swapAllowed = (path, !ViewModel.IsBackup(path));
            return swapAllowed.Allowed;
        }
    }

    /// <summary>The choice as it stands: Clear out and Swap need pieces to clear, and Swap a design that isn't a backup; else Keep.</summary>
    private ReadyMode ModeNow
    {
        get
        {
            var mode = Mode;
            if (mode == ReadyMode.Keep || session.Plan == null || ClearOutPieces().Count == 0) return ReadyMode.Keep;
            return mode == ReadyMode.Swap && !SwapAllowed ? ReadyMode.Keep : mode;
        }
    }

    /// <summary>A swap's name for what's here now ("Medium house in Mist, plot 12 before Winter Lodge"), or null when the choice isn't Swap.</summary>
    private string? SwapKeepAs => ModeNow == ReadyMode.Swap ? KeepAsName() : null;

    private (Plan? Plan, DateTime Until, List<PlacedPiece> Pieces) clearOutCache;

    private (bool B, bool G, bool U, bool Near, float Reach) clearOutFilters;

    private static readonly List<PlacedPiece> NoPieces = new();

    /// <summary>
    /// The pieces placed here that the design doesn't use, less those the run's filters leave out
    /// (floors, Near me). Worked out again when the plan or the filters change, and with Near me on
    /// every half second (you move).
    /// </summary>
    private List<PlacedPiece> ClearOutPieces()
    {
        var plan = session.Plan;
        if (plan == null || plan.Extras.Count == 0) return NoPieces;
        var c = clearOutCache;
        var now = DateTime.UtcNow;
        var filters = (config.ShowBasement, config.ShowGround, config.ShowUpper, config.NearMe, config.NearMeRadius);
        if (c.Plan == plan && clearOutFilters == filters && (!config.NearMe || now < c.Until))
            return c.Pieces;
        var me = config.NearMe ? Services.Objects.LocalPlayer?.Position : null;
        var pieces = ClearOut.Pick(plan.Extras, session.PlanSide ?? Side.Interior, session.Context.HouseSize ?? session.Layout?.HouseSize, config.FloorShown, me,
            config.NearMe ? config.NearMeRadius : 0);
        clearOutCache = (plan, now + TimeSpan.FromSeconds(0.5), pieces);
        clearOutFilters = filters;
        return pieces;
    }

    /// <summary>What a run started from here clears out first: the pieces, when the choice is Clear out or Swap.</summary>
    private IReadOnlyList<PlacedPiece>? ChosenClearOut() => ModeNow != ReadyMode.Keep && ClearOutPieces() is { Count: > 0 } p ? p : null;

    private (int Pieces, int Extras, bool Swap, string Keep, ReadyMode Mode, string[]? Items, string?[] Tips, string Note) placeChoice =
        (0, 0, false, string.Empty, ReadyMode.Keep, null, Array.Empty<string?>(), string.Empty);

    /// <summary>Ready's Place stage: Keep what's here / Clear out N / Swap, and a line saying what the one picked does.</summary>
    private void DrawPlaceChoice(Plan plan)
    {
        var pieces = ClearOutPieces().Count;
        var canSwap = pieces > 0 && SwapAllowed;
        var keep = canSwap ? KeepAsName() : string.Empty;
        var mode = ModeNow;
        // A choice that can't apply here (nothing to clear out, a swap of a backup) shows as Keep, and is
        // Keep from now on: it doesn't come back by itself when pieces come in reach again.
        if (Mode != mode) Mode = mode;
        var c = placeChoice;
        if (c.Items == null || c.Pieces != pieces || c.Extras != plan.Extras.Count || c.Swap != canSwap || c.Keep != keep || c.Mode != mode)
        {
            // Made again only when what they say changes, not every frame.
            var items = new[] { "Keep what's here", ReadyWords.ClearOutLabel(pieces), "Swap" };
            var tips = new string?[]
            {
                null,
                pieces == 0 ? "Nothing placed here (on the floors placed, near you) is outside the design." : "The pieces not in the design go to the storeroom first, with the editor's Store mode.",
                canSwap ? $"Clears out, places this design, and keeps what's here now as “{keep}” (in Recent), to swap back to."
                : pieces == 0 ? "Nothing to swap out: what's placed here is all in the design."
                : "This design is a backup: what's here is kept already. Load the design you want to swap to.",
            };
            c = placeChoice = (pieces, plan.Extras.Count, canSwap, keep, mode, items, tips, ReadyWords.ModeNote(mode, plan.Extras.Count, pieces, keep));
        }
        Kit.Gap(12);
        var at = Kit.At;
        var w = Kit.Width;
        var mask = (pieces == 0 ? 1 << 1 : 0) | (canSwap ? 0 : 1 << 2);
        // Any press counts, the one already picked too (Keep what's here sets Keep).
        var pressed = Kit.SegmentedPress("##ready-mode", at, c.Items!, (int)mode, mask, w, c.Tips);
        if (pressed >= 0) Mode = (ReadyMode)pressed;
        Kit.Reserve(at, w, S(40));
        Kit.Gap(8);
        modeNote[0] = new Run(c.Note, Face.Sans, 13, Palette.Dim);
        Txt.Para(modeNote);
    }

    private readonly Run[] modeNote = new Run[1];

    private (Plan? Plan, string Title, DateTime Until, string Name) keepAs = (null, string.Empty, DateTime.MinValue, string.Empty);

    /// <summary>"Medium house in Mist, plot 12 before Winter Lodge" (read again once a second: it reads where you are).</summary>
    private string KeepAsName()
    {
        var now = DateTime.UtcNow;
        if (keepAs.Plan == session.Plan && keepAs.Title == session.Title && now < keepAs.Until) return keepAs.Name;
        var ctx = session.Context;
        var side = session.PlanSide ?? ctx.Side ?? Side.Interior;
        var plot = side == Side.Garden ? ctx.Plot : FixtureReader.PlotOfInterior();
        var district = ctx.District != null ? Districts.Display(ctx.District) : null;
        var name = SwapNames.KeepAs(SwapNames.Home(side, ctx.HouseSize, district, plot), session.Title);
        keepAs = (session.Plan, session.Title, now + TimeSpan.FromSeconds(1), name);
        return name;
    }

    private void AskSwap(string keep, int count)
    {
        Ask("Swap to this design?",
            $"This keeps what's placed now as “{keep}” (in Recent), puts the {Words.Count(count, "piece")} that {(count == 1 ? "isn't" : "aren't")} in this design away in the storeroom, then places the design."
            + ReadyWords.StoreroomRoomNote(count, StoreroomRoomHere())
            + " Swap back, when it's done or another day, puts it all back.",
            "Swap", "Not now", () =>
            {
                Mode = ReadyMode.Swap;
                StartRun(false, null, ChosenClearOut(), ArmFixturesAfterPlacing, keep);
            });
    }

    /// <summary>Ready's start (Keep or Clear out): with clear out chosen, asks first (it puts pieces away).</summary>
    private void StartFromReady(List<PlanEntry> entries)
    {
        var clear = ChosenClearOut();
        if (clear == null)
        {
            StartRun(false, entries, null, ArmFixturesAfterPlacing);
            return;
        }
        Ask("Clear out first?",
            $"The {Words.Count(clear.Count, "piece")} in your home that {(clear.Count == 1 ? "isn't" : "aren't")} in this design {(clear.Count == 1 ? "goes" : "go")} to the storeroom first, then the design is placed."
            + ReadyWords.StoreroomRoomNote(clear.Count, StoreroomRoomHere())
            + (config.BackupBeforePlacing ? " A backup of what's placed is saved before." : " No backup is saved first (Settings)."),
            "Start placing", "Not now", () => StartRun(false, entries, ChosenClearOut(), ArmFixturesAfterPlacing));
    }

    /// <summary>How many more pieces the storeroom of the side being placed takes (asked once, when Start is pressed).</summary>
    private int? StoreroomRoomHere() => InventoryReader.StoreroomRoom(session.PlanSide ?? session.Context.Side ?? Side.Interior);

    // ---------------------------------------------------------------------------------------------
    // Done.

    /// <summary>Done: how many pieces were put away (the ones Store mode wouldn't take are under Needs you).</summary>
    private void DrawPutAwayLine(bool preview)
    {
        var count = runner.ClearOutCount;
        if (count == 0) return;
        Kit.Gap(12);
        if (preview)
        {
            Txt.Note($"Start puts {Words.Count(count, "piece")} that {(count == 1 ? "isn't" : "aren't")} in the design away in the storeroom first.");
            return;
        }
        var done = runner.PutAwayDone;
        var left = count - done;
        Txt.Note(left > 0
            ? $"{done} of {Words.Count(count, "piece")} not in the design {(done == 1 ? "was" : "were")} put away in the storeroom."
            : $"{Words.Capital(Words.Count(done, "piece"))} not in the design {(done == 1 ? "was" : "were")} put away in the storeroom.");
    }

    private (string? Kept, string Line) swapBackLine = (null, string.Empty);

    /// <summary>The undo card after a swap: "Not what you wanted?", what was here is kept as “…”, and Swap back (after asking).</summary>
    private void DrawSwapBack(DateTime savedAt)
    {
        var kept = runner.KeptAs!;
        if (swapBackLine.Kept != kept) swapBackLine = (kept, $"What was here is kept as “{kept}”");
        var cannot = runner.CanRestore();
        if (!Kit.ActionCard("##swapback", Icon.Undo, null, "Not what you wanted?", swapBackLine.Line, "Swap back", null, false, cannot != null,
                cannot ?? $"Kept at {Clock(savedAt)}, and in Recent. Puts away what isn't in it, then puts everything back where it was (asks first)."))
            return;
        Ask("Swap back?",
            $"This loads “{kept}” as the design and starts a run that puts away what isn't in it first, then puts everything back where it was.",
            "Swap back", "Keep this design", () =>
            {
                OnFramework(() =>
                {
                    if (runner.SwapBack(out var why))
                    {
                        vm.Dismiss();
                        session.Refresh(HousingContext.Read(Plugin.Plots));
                        Notify("Swapping back…");
                    }
                    else Notify(why);
                });
            });
    }
}
