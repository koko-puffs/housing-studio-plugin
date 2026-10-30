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
        Check(ctx.EditorOpen, "The furnishing editor is open", ctx.Kind == PlaceKind.Ward ? "Housing → Outdoor Furnishings." : "Housing → Indoor Furnishings.");
        Check(ctx.Mode == EditorMode.Rotate, "It's in Rotate mode", "Press Rotate (the circular arrows) in the editor's bar.");
        Check(session.Warnings.Count == 0, "The design fits this home", session.Warnings.FirstOrDefault() ?? string.Empty);
        if (ctx.Side == Side.Interior)
            Check(session.StoreroomLoaded, "The storeroom is loaded", "Open Furnishings → Storeroom once so its pieces count.", optional: true);
        Widgets.EndPanel();
        Widgets.Gap(10);

        if (plan == null) return;
        var entries = Filtered().ToList();
        var moves = entries.Count(e => e.State == PieceState.Move);
        var bags = entries.Count(e => e.State == PieceState.FromInventory);
        var storeroom = entries.Count(e => e.State == PieceState.FromStoreroom);
        var missing = entries.Count(e => e.State == PieceState.Missing);
        var floorsOff = new[] { (config.ShowBasement, "basement"), (config.ShowGround, "ground floor"), (config.ShowUpper, "upper floor") }.Where(f => !f.Item1).Select(f => f.Item2).ToList();

        Widgets.BeginPanel();
        Widgets.Eyebrow("What happens");
        Widgets.Wrapped($"{moves} placed piece{(moves == 1 ? "" : "s")} will be picked up and set at {(moves == 1 ? "its" : "their")} spot, one at a time.", Palette.Text);
        if (bags + storeroom > 0)
            Widgets.Wrapped($"Then it's your turn for {bags + storeroom} more ({bags} from your bags, {storeroom} from the storeroom): put each down anywhere through the game's menus, in any order, and it's set at its spot the moment it lands.", Palette.Text);
        if (missing > 0) Widgets.Muted($"{missing} missing piece{(missing == 1 ? " is" : "s are")} skipped (Pieces → Copy shopping list).");
        if (floorsOff.Count > 0 || config.NearMeRadius > 0)
            Widgets.Muted($"Only what the Pieces filters show: {(floorsOff.Count > 0 ? $"not the {string.Join(" or ", floorsOff)}" : "every floor")}{(config.NearMeRadius > 0 ? $", within {config.NearMeRadius:0} yalms of you" : "")}.");
        Widgets.Gap(6);

        // Pace.
        var seconds = config.IntervalMs / 1000f;
        ImGui.SetNextItemWidth(S(240));
        if (ImGui.SliderFloat("##pace", ref seconds, Pacing.MinimumMs / 1000f, 5f, $"{PaceWord(seconds)}: %.1f s between pieces"))
        {
            config.IntervalMs = (int)MathF.Round(seconds * 1000);
            config.Save();
        }
        ImGui.SameLine();
        Widgets.Muted("Slower is safer: the game has to answer each one.", false);
        Widgets.Gap(6);

        var blocked = runner.CanStart(false);
        var dryBlocked = runner.CanStart(true);
        if (confirmStart)
        {
            Widgets.Wrapped($"Start placing {moves + bags + storeroom} pieces here? {(config.BackupBeforePlacing ? "A backup of what's placed now is saved first." : "No backup will be saved (Settings).")}", Palette.BrassSoft);
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
                    tooltip: dryBlocked ?? "Walks through every step, marking each spot in the world and logging what it would do, without touching anything."))
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
            Widgets.Muted($"{runner.Failed} couldn't be set · {runner.Skipped} skipped", false);
        }
        Widgets.Progress(runner.Total == 0 ? 0 : done / (float)runner.Total);
        Widgets.Gap(6);

        if (runner.Current is { } job)
        {
            Widgets.ItemIcon(data.ItemIcon(job.Target.ItemId), 40);
            ImGui.SameLine(0, S(10));
            ImGui.BeginGroup();
            ImGui.TextColored(Palette.Ivory, data.ItemName(job.Target.ItemId));
            using (theme.Fonts.Small.Push())
            {
                var p = job.Target.Position;
                ImGui.TextColored(Palette.Dim, $"{Floors.Name(job.Target.Floor)} · ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}) · facing {Transforms.TidyYaw(job.Target.Yaw) * 180 / MathF.PI:0}°");
                if (job.Target.Stain != 0) ImGui.TextColored(Palette.Ash, $"Dye: {data.DyeName(job.Target.Stain)}");
            }
            ImGui.EndGroup();
        }
        if (runner.StatusText != null) Widgets.Wrapped(runner.StatusText, runner.State == RunState.Stopped ? Palette.Danger : Palette.BrassSoft);
        Widgets.Gap(6);

        if (runner.State == RunState.Running)
        {
            if (Widgets.Button("Pause", ButtonStyle.Ghost)) Services.Framework.RunOnFrameworkThread(() => runner.Pause("Paused by you."));
            ImGui.SameLine();
            if (Widgets.Button("Skip this piece", ButtonStyle.Quiet)) Services.Framework.RunOnFrameworkThread(runner.SkipCurrent);
            ImGui.SameLine();
            if (Widgets.Button("Stop", ButtonStyle.Danger)) Services.Framework.RunOnFrameworkThread(() => runner.Stop("Stopped by you."));
        }
        else if (runner.State == RunState.Paused)
        {
            if (Widgets.Button("Resume", ButtonStyle.Brass)) Services.Framework.RunOnFrameworkThread(runner.Resume);
            ImGui.SameLine();
            if (Widgets.Button("Skip this piece", ButtonStyle.Quiet)) Services.Framework.RunOnFrameworkThread(runner.SkipCurrent);
            ImGui.SameLine();
            if (Widgets.Button("Stop", ButtonStyle.Danger)) Services.Framework.RunOnFrameworkThread(() => runner.Stop("Stopped by you."));
        }
        Widgets.EndPanel();
        Widgets.Gap(10);
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
