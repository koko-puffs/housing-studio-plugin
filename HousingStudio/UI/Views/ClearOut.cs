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
/// Clearing out and swapping, in Ready and Done. Ready: "Clear out what isn't in the design" (off
/// unless you turn it on for this design; the pieces go to the storeroom before the design is
/// placed) and "Swap to this design" (clears out, places, and keeps what was here under a name to
/// swap back to). Done: how many were put away, and Swap back after a swap.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The design (its file) clear out is turned on for; another design starts with it off.</summary>
    private string? clearOutFor;

    private bool ClearOutChosen => clearOutFor != null && clearOutFor == (session.FilePath ?? session.Title);

    private (Plan? Plan, bool B, bool G, bool U, bool Near, float Reach, DateTime Until, List<PlacedPiece> Pieces) clearOutCache;

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
        if (c.Plan == plan && c.B == config.ShowBasement && c.G == config.ShowGround && c.U == config.ShowUpper && c.Near == config.NearMe
            && c.Reach == config.NearMeRadius && (!config.NearMe || now < c.Until))
            return c.Pieces;
        var me = config.NearMe ? Services.Objects.LocalPlayer?.Position : null;
        var pieces = ClearOut.Pick(plan.Extras, session.PlanSide ?? Side.Interior, session.Context.HouseSize ?? session.Layout?.HouseSize, config.FloorShown, me,
            config.NearMe ? config.NearMeRadius : 0);
        clearOutCache = (plan, config.ShowBasement, config.ShowGround, config.ShowUpper, config.NearMe, config.NearMeRadius, now + TimeSpan.FromSeconds(0.5), pieces);
        return pieces;
    }

    /// <summary>What a run started from here clears out first: the pieces, when clear out is on for this design.</summary>
    private IReadOnlyList<PlacedPiece>? ChosenClearOut() => ClearOutChosen && ClearOutPieces() is { Count: > 0 } p ? p : null;

    /// <summary>Ready: clear out, and swap, when something placed here isn't in the design.</summary>
    private void DrawClearOut(Plan plan)
    {
        if (plan.Extras.Count == 0) return;
        var pieces = ClearOutPieces();
        var on = ClearOutChosen;
        var all = plan.Extras.Count;
        string note;
        if (pieces.Count == 0)
            note = $"{Words.Count(all, "piece")} in your home {(all == 1 ? "isn't" : "aren't")} in this design, none of {(all == 1 ? "it" : "them")} on the floors placed or near you, so nothing is put away.";
        else if (on)
            note = $"{Words.Count(pieces.Count, "piece")} in your home {(pieces.Count == 1 ? "isn't" : "aren't")} in this design: {(pieces.Count == 1 ? "it goes" : "they go")} to the storeroom first, with the editor's Store mode.";
        else
            note = $"{Words.Count(pieces.Count, "piece")} in your home {(pieces.Count == 1 ? "isn't" : "aren't")} in this design and {(pieces.Count == 1 ? "stays where it is" : "stay where they are")}. Turn this on to put {(pieces.Count == 1 ? "it" : "them")} away in the storeroom first.";

        Kit.Gap(18);
        Kit.BeginPanel("##clear-out", 0, 0, 0);
        var i = 0;
        if (SwitchRow(i++, "##clear-out-switch", "Clear out what isn't in the design", note, on))
            clearOutFor = on ? null : session.FilePath ?? session.Title;

        // A swap keeps what's here under a name; a backup loaded as the design is already such a one.
        if (pieces.Count > 0 && !(session.FilePath is { } path && ViewModel.IsBackup(path)))
        {
            var keep = KeepAsName();
            const string label = "Swap";
            var blocked = runner.CanStart(false);
            var swap = false;
            SetRow(i++, "Swap to this design", $"Clears out, places this design, and keeps what's here now as “{keep}”, to swap back to.",
                Kit.ButtonWidth(label, BtnSize.Sm, Icon.Refresh), Kit.ButtonHeight(BtnSize.Sm),
                p => swap = Kit.Button("##swap", p, label, Btn.Glass, BtnSize.Sm, Icon.Refresh, 0, blocked != null, blocked));
            if (swap) AskSwap(keep, pieces.Count);
        }
        Kit.EndPanel();
    }

    /// <summary>"Medium house in Mist, plot 12 before Winter Lodge".</summary>
    private string KeepAsName()
    {
        var ctx = session.Context;
        var side = session.PlanSide ?? ctx.Side ?? Side.Interior;
        var plot = side == Side.Garden ? ctx.Plot : FixtureReader.PlotOfInterior();
        var district = ctx.District != null ? Districts.Display(ctx.District) : null;
        return SwapNames.KeepAs(SwapNames.Home(side, ctx.HouseSize, district, plot), session.Title);
    }

    private void AskSwap(string keep, int count)
    {
        Ask("Swap to this design?",
            $"This keeps what's placed now as “{keep}” (in Recent), puts the {Words.Count(count, "piece")} that {(count == 1 ? "isn't" : "aren't")} in this design away in the storeroom, then places the design. "
            + "Swap back, when it's done or another day, puts it all back.",
            "Swap", "Not now", () =>
            {
                clearOutFor = session.FilePath ?? session.Title;
                StartRun(false, keepAs: keep);
            });
    }

    /// <summary>Ready's Start placing: with clear out on, asks first (it puts pieces away).</summary>
    private void StartFromReady(List<PlanEntry> entries)
    {
        var clear = ChosenClearOut();
        if (clear == null)
        {
            StartRun(false, entries);
            return;
        }
        Ask("Clear out first?",
            $"The {Words.Count(clear.Count, "piece")} in your home that {(clear.Count == 1 ? "isn't" : "aren't")} in this design {(clear.Count == 1 ? "goes" : "go")} to the storeroom first, then the design is placed."
            + (config.BackupBeforePlacing ? " A backup of what's placed is saved before." : " No backup is saved first (Settings)."),
            "Start placing", "Not now", () => StartRun(false, entries));
    }

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

    /// <summary>After a swap: what was here is kept, and Swap back puts it back (after asking).</summary>
    private void DrawSwapBack(DateTime savedAt)
    {
        var kept = runner.KeptAs!;
        Kit.BeginPanel("##swapback", 16, 20, 16);
        var at = Kit.At;
        var w = Kit.Width;
        const string label = "Swap back";
        var bw = Kit.ButtonWidth(label, BtnSize.Sm, null, Btn.Quiet);
        var x = at.X + S(16) + S(14);
        var tw = at.X + w - bw - S(14) - x;
        var titleH = Txt.Natural(Face.SansMedium, 14);
        var note = new[] { Run.Note($"What was here is kept as “{kept}” (saved at {Clock(savedAt)}, in Recent).") };
        var noteH = Txt.Flow(Vector2.Zero, tw, note, 19.5f, out _, draw: false);
        var wordsH = titleH + S(2) + noteH;
        var h = MathF.Max(S(34), wordsH);
        var mid = at.Y + h / 2;
        var dl = Kit.Dl;
        Icons.Draw(dl, Icon.Refresh, new Vector2(at.X, mid - S(8)), S(16), Palette.Dim);
        var top = mid - wordsH / 2;
        Txt.Line(dl, new Vector2(x, top), "Swap back?", Face.SansMedium, 14, Palette.Ivory);
        Txt.Flow(new Vector2(x, top + titleH + S(2)), tw, note, 19.5f, out _);
        var cannot = runner.CanRestore();
        if (Kit.Button("##swapback-btn", new Vector2(at.X + w - bw, mid - S(17)), label, Btn.Quiet, BtnSize.Sm, disabled: cannot != null, tip: cannot))
        {
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
        Kit.Reserve(at, w, h);
        Kit.EndPanel();
    }
}
