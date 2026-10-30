using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

public sealed partial class MainWindow
{
    private bool confirmStart;

    private void DrawPlace()
    {
        if (runner.Active || runner.State is RunState.Finished or RunState.Stopped && runner.Total > 0)
            DrawRun();
        if (!runner.Active)
            DrawReady();
        Widgets.Gap(10);
        DrawLog();
    }

    private void DrawReady()
    {
        var ctx = session.Context;
        var plan = session.Plan;

        Widgets.BeginPanel();
        Widgets.Eyebrow("Before you start");
        Check(session.Layout != null, "A design is loaded", "Load one on the Design page.");
        Check(ctx.Side != null, "You're in a home (inside, or on your plot)", "Go to your house, apartment or plot.");
        Check(ctx.HasPermission, "You're allowed to decorate here", "Only your own home, or one whose owner gave you permission.");
        Check(ctx.EditorOpen, "The furnishing editor is open", ctx.Kind == PlaceKind.Ward ? "Housing → Outdoor Furnishings." : "Housing → Indoor Furnishings (it opens the furnishing list too).");
        Check(ctx.Mode == EditorMode.Rotate, "It's in Rotate mode", "Press Rotate (the circular arrows); the run asks if it needs another mode.", optional: true);
        Check(session.Warnings.Count == 0, "The design fits this home", session.Warnings.FirstOrDefault() ?? string.Empty);
        if (ctx.Side == Side.Interior)
            Check(session.StoreroomLoaded, "The storeroom is loaded", "Open the furnishing list's Storeroom tab once so its pieces count.", optional: true);
        Widgets.EndPanel();
        Widgets.Gap(10);

        if (plan == null) return;
        var entries = Filtered().ToList();
        var jobs = RunSequence.Build(entries);
        var moves = jobs.Count(j => j.Kind == JobKind.Move);
        var storeroom = jobs.Count(j => j.Kind == JobKind.PlaceFromStoreroom);
        var bags = jobs.Count(j => j.Kind == JobKind.PlaceFromBags);
        var missing = entries.Count(e => e.State == PieceState.Missing);
        var dyes = entries.Count(e => e.State != PieceState.Missing && e.Target.Stain != 0 && e.Target.Stain != e.CurrentStain);
        var floorsOff = new[] { (config.ShowBasement, "basement"), (config.ShowGround, "ground floor"), (config.ShowUpper, "upper floor") }.Where(f => !f.Item1).Select(f => f.Item2).ToList();

        Widgets.BeginPanel();
        Widgets.Eyebrow("What happens");
        Phase("1", "Move", $"{moves} placed piece{(moves == 1 ? "" : "s")} picked up and set at {(moves == 1 ? "its" : "their")} spot.");
        Phase("2", "Place", $"{storeroom} from the storeroom ({(config.StoreroomAutomatic && ctx.Side != Side.Garden ? "picked in the furnishing list for you" : "you put them down")}), "
                          + $"{bags} from your bags ({(config.BagsAutomatic ? "picked from the item's menu for you" : "you put them down")}); each set exactly at its spot.");
        Phase("3", "Dye", config.DyeAfterPlacing ? $"About {dyes} piece{(dyes == 1 ? "" : "s")} dyed in the editor's dye mode, with dyes from your bags (pieces whose dye you don't have are skipped and listed)." : "Off (Settings).");
        if (missing > 0) Widgets.Muted($"{missing} missing piece{(missing == 1 ? " is" : "s are")} skipped (Pieces → Copy shopping list).");
        if (floorsOff.Count > 0 || config.NearMeRadius > 0)
            Widgets.Muted($"Only what the Pieces filters show: {(floorsOff.Count > 0 ? $"not the {string.Join(" or ", floorsOff)}" : "every floor")}{(config.NearMeRadius > 0 ? $", within {config.NearMeRadius:0} yalms of you" : "")}.");
        Widgets.Muted("Where the game's own button has to be pressed (the editor's modes, the Storeroom tab), the run asks you and carries on once it's done.");
        Widgets.Gap(6);

        var seconds = config.IntervalMs / 1000f;
        ImGui.SetNextItemWidth(S(240));
        if (ImGui.SliderFloat("##pace", ref seconds, Pacing.MinimumMs / 1000f, 5f, $"{PaceWord(seconds)}: %.1f s between pieces"))
        {
            config.IntervalMs = (int)MathF.Round(seconds * 1000);
            config.Save();
        }
        ImGui.SameLine();
        var stepMode = config.StepByStep;
        if (Widgets.Toggle("Step by step", ref stepMode, "Holds before every click and shows what it's about to do, until you press Continue (Settings → Diagnostics).")) { config.StepByStep = stepMode; config.Save(); }
        Widgets.Gap(6);

        var blocked = runner.CanStart(false);
        var dryBlocked = runner.CanStart(true);
        if (confirmStart)
        {
            Widgets.Wrapped($"Start? {moves + storeroom + bags} pieces to set{(config.DyeAfterPlacing ? ", then dyes" : string.Empty)}. {(config.BackupBeforePlacing ? "A backup of what's placed now is saved first." : "No backup will be saved (Settings).")}", Palette.BrassSoft);
            if (Widgets.Button("Yes, start", ButtonStyle.Brass, disabled: blocked != null))
            {
                confirmStart = false;
                Services.Framework.RunOnFrameworkThread(() => runner.Start(false, Filtered().ToList()));
            }
            ImGui.SameLine();
            if (Widgets.Button("Not yet", ButtonStyle.Ghost)) confirmStart = false;
        }
        else
        {
            if (Widgets.Button("Start placing", ButtonStyle.Brass, disabled: blocked != null, tooltip: blocked)) confirmStart = true;
            ImGui.SameLine();
            if (Widgets.Button("Dry run", ButtonStyle.Ghost, disabled: dryBlocked != null,
                    tooltip: dryBlocked ?? "Walks through every piece, marking each spot in the world and logging what it would do, without touching anything."))
                Services.Framework.RunOnFrameworkThread(() => runner.Start(true, Filtered().ToList()));
            ImGui.SameLine();
            if (Widgets.Button("Save a backup", ButtonStyle.Quiet, disabled: ctx.Side == null))
                Services.Framework.RunOnFrameworkThread(() =>
                {
                    if (Backups.Save(session, data, log, "by hand") != null) Notify("Backup saved");
                });
        }
        if (blocked != null && !confirmStart) Widgets.Muted(blocked);
        Widgets.EndPanel();
    }

    private void Phase(string number, string title, string text)
    {
        using (theme.Fonts.Mono.Push())
            ImGui.TextColored(Palette.Brass, number);
        ImGui.SameLine();
        ImGui.TextColored(Palette.Ivory, title);
        ImGui.SameLine();
        Widgets.Wrapped(text, Palette.Text);
    }

    private static string PaceWord(float seconds) => seconds switch
    {
        < 1f => "Brisk",
        < 2f => "Steady",
        < 3.5f => "Careful",
        _ => "Slow",
    };

    private void Check(bool ok, string label, string fix, bool optional = false)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var r = S(7);
        var c = pos + new Vector2(r, ImGui.GetTextLineHeight() / 2);
        var color = ok ? Palette.Success : optional ? Palette.Dim : Palette.Danger;
        if (ok)
        {
            dl.AddCircleFilled(c, r, Palette.U32(Palette.Mix(Palette.Panel, color, 0.25f)));
            dl.AddLine(c + new Vector2(-S(3), 0), c + new Vector2(-S(1), S(2.5f)), Palette.U32(color), S(1.6f));
            dl.AddLine(c + new Vector2(-S(1), S(2.5f)), c + new Vector2(S(3.5f), -S(2.5f)), Palette.U32(color), S(1.6f));
        }
        else
        {
            dl.AddCircle(c, r, Palette.U32(color), 0, S(1.4f));
        }
        ImGui.Dummy(new Vector2(r * 2 + S(8), ImGui.GetTextLineHeight()));
        ImGui.SameLine();
        ImGui.TextColored(ok ? Palette.Text : Palette.Ivory, label);
        if (!ok && fix.Length > 0)
        {
            ImGui.SameLine();
            using (theme.Fonts.Small.Push())
                ImGui.TextColored(Palette.Dim, fix);
        }
    }

    private void DrawRun()
    {
        Widgets.BeginPanel();
        var title = runner.State switch
        {
            RunState.Running => runner.DryRun ? "Dry run" : "Placing",
            RunState.Paused => "Paused",
            RunState.Finished => runner.DryRun ? "Dry run finished" : "Finished",
            _ => "Stopped",
        };
        Widgets.Eyebrow(title, runner.State == RunState.Stopped ? Palette.Danger : Palette.Brass);
        var done = runner.Done + runner.Failed + runner.Skipped;
        using (theme.Fonts.Heading.Push())
            ImGui.TextColored(Palette.Ivory, $"{runner.Done} of {runner.Total}");
        if (runner.Failed + runner.Skipped > 0)
        {
            ImGui.SameLine();
            Widgets.Muted($"{runner.Failed} didn't work · {runner.Skipped} skipped", false);
        }
        Widgets.Progress(runner.Total == 0 ? 0 : done / (float)runner.Total);
        Widgets.Gap(6);

        // The three phases, each with its own count.
        var current = runner.Current?.Phase;
        var gap = S(8);
        var w = (Widgets.Avail() - gap * 2) / 3;
        foreach (var (phase, i) in new[] { RunPhase.Move, RunPhase.Place, RunPhase.Dye }.Select((p, i) => (p, i)))
        {
            if (i > 0) ImGui.SameLine(0, gap);
            var total = runner.Count(phase);
            var finished = runner.Count(phase, JobOutcome.Done);
            var label = phase == RunPhase.Dye && !runner.DyesPlanned ? "after placing" : $"{finished} / {total}";
            PhaseTile(PlacementRunner.PhaseWord(phase), label, current == phase, w);
        }
        Widgets.Gap(8);

        if (runner.Current is { } job)
        {
            Widgets.ItemIcon(data.ItemIcon(job.Target.ItemId), 40);
            ImGui.SameLine(0, S(10));
            ImGui.BeginGroup();
            ImGui.TextColored(Palette.Ivory, $"{PlacementRunner.PhaseWord(job.Phase)} {data.ItemName(job.Target.ItemId)}");
            using (theme.Fonts.Small.Push())
            {
                var p = job.Target.Position;
                ImGui.TextColored(Palette.Dim, $"{Floors.Name(job.Target.Floor)} · ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}) · facing {Transforms.TidyYaw(job.Target.Yaw) * 180 / MathF.PI:0}°");
                if (job.Target.Stain != 0) ImGui.TextColored(Palette.Ash, $"Dye: {data.DyeName(job.Target.Stain)}");
                if (runner.StepLabel is { } step)
                {
                    var (n, count) = runner.StepProgress;
                    ImGui.TextColored(runner.WaitingForYou ? Palette.BrassSoft : Palette.Text, $"Step {n} of {count}: {step}");
                }
            }
            ImGui.EndGroup();
        }
        if (runner.AwaitingContinue)
        {
            Widgets.Gap(4);
            Widgets.Wrapped($"Step by step: about to {runner.StepLabel}. Press Continue to do it (or /hstudio continue).", Palette.BrassSoft);
        }
        else if (runner.WaitingForYou)
        {
            Widgets.Gap(4);
            Widgets.Wrapped("Waiting for you: " + runner.StepLabel!["You: ".Length..], Palette.BrassSoft);
        }
        else if (runner.StatusText != null && !runner.Active)
        {
            Widgets.Wrapped(runner.StatusText, runner.State == RunState.Stopped ? Palette.Danger : Palette.BrassSoft);
        }
        Widgets.Gap(6);

        if (runner.Active)
        {
            if (runner.AwaitingContinue)
            {
                if (Widgets.Button("Continue", ButtonStyle.Brass)) Services.Framework.RunOnFrameworkThread(runner.Continue);
                ImGui.SameLine();
            }
            if (runner.State == RunState.Running)
            {
                if (Widgets.Button("Pause", ButtonStyle.Ghost)) Services.Framework.RunOnFrameworkThread(() => runner.Pause("Paused by you."));
            }
            else if (Widgets.Button("Resume", ButtonStyle.Brass))
            {
                Services.Framework.RunOnFrameworkThread(runner.Resume);
            }
            ImGui.SameLine();
            if (Widgets.Button("Skip this piece", ButtonStyle.Quiet)) Services.Framework.RunOnFrameworkThread(runner.SkipCurrent);
            ImGui.SameLine();
            if (Widgets.Button("Stop", ButtonStyle.Danger)) Services.Framework.RunOnFrameworkThread(() => runner.Stop("Stopped by you."));
        }
        Widgets.EndPanel();
        Widgets.Gap(10);
    }

    private void PhaseTile(string title, string value, bool current, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        var h = S(46);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Palette.U32(current ? Palette.BrassWash : Palette.Field), S(Palette.RadiusMd));
        dl.AddRect(pos, pos + new Vector2(width, h), Palette.U32(current ? Palette.Brass : Palette.Line), S(Palette.RadiusMd));
        using (theme.Fonts.Mono.Push())
            dl.AddText(pos + new Vector2(S(12), S(6)), Palette.U32(current ? Palette.Brass : Palette.Dim), title.ToUpperInvariant());
        dl.AddText(pos + new Vector2(S(12), S(22)), Palette.U32(Palette.Ivory), value);
        ImGui.Dummy(new Vector2(width, h));
    }

    private void DrawLog()
    {
        Widgets.Eyebrow("Activity");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - S(90));
        if (Widgets.Button("Copy log", ButtonStyle.Quiet, S(90), small: true))
        {
            ImGui.SetClipboardText(log.AsText());
            Notify("Log copied");
        }
        var height = Math.Max(S(120), ImGui.GetContentRegionAvail().Y - S(4));
        if (Widgets.BeginCard("##log", height, Palette.Walnut))
        {
            using var font = theme.Fonts.Small.Push();
            if (log.Lines.Count == 0) ImGui.TextColored(Palette.Dim, "Nothing yet.");
            foreach (var line in log.Lines)
            {
                ImGui.TextColored(Palette.Dim, line.At.ToString("HH:mm:ss"));
                ImGui.SameLine();
                var color = line.Kind switch
                {
                    LogKind.Done => Palette.Success,
                    LogKind.Warn => Palette.BrassSoft,
                    LogKind.Error => Palette.Danger,
                    _ => Palette.Text,
                };
                Widgets.Wrapped(line.Text, color);
            }
            if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - S(20)) ImGui.SetScrollHereY(1f);
        }
        Widgets.EndCard();
    }
}
