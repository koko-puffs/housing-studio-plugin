using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

public sealed partial class MainWindow
{
    private static readonly string[] StateFilters = { "All", "To do", "Missing", "Done" };
    private int stateFilter;
    private string search = string.Empty;

    /// <summary>The plan's pieces the filters let through (floors, near me), for the list and for placing.</summary>
    private IEnumerable<PlanEntry> Filtered()
    {
        var plan = session.Plan;
        if (plan == null) return Enumerable.Empty<PlanEntry>();
        var me = Services.Objects.LocalPlayer?.Position;
        return plan.Entries.Where(e =>
            config.FloorShown(e.Target.Floor)
            && (config.NearMeRadius <= 0 || me == null || Vector3.Distance(me.Value, e.Target.Position) <= config.NearMeRadius));
    }

    private void DrawPieces()
    {
        var plan = session.Plan;
        if (plan == null)
        {
            Widgets.Muted(session.Layout == null ? "Load a layout on the Design page first." : "Go inside your home (or to your plot) to match this design against what you have.");
            return;
        }

        DrawFilters();
        Widgets.Gap(6);

        var rows = Filtered()
            .Where(e => stateFilter switch
            {
                1 => e.State is PieceState.Move or PieceState.FromInventory or PieceState.FromStoreroom,
                2 => e.State == PieceState.Missing,
                3 => e.State == PieceState.InPlace,
                _ => true,
            })
            .Where(e => search.Length == 0 || data.ItemName(e.Target.ItemId).Contains(search, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (e.Target.ItemId, e.State))
            .Select(g => (Item: g.Key.ItemId, State: g.Key.State, Entries: g.ToList()))
            .OrderBy(g => g.State == PieceState.Missing ? 0 : g.State == PieceState.InPlace ? 2 : 1)
            .ThenBy(g => data.ItemName(g.Item), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var listHeight = ImGui.GetContentRegionAvail().Y - S(Palette.ControlSm + 20);
        if (Widgets.BeginCard("##pieces", Math.Max(S(120), listHeight)))
        {
            if (rows.Count == 0) Widgets.Muted("Nothing here with these filters.");
            foreach (var row in rows) DrawPieceRow(row.Item, row.State, row.Entries);
        }
        Widgets.EndCard();

        Widgets.Gap(4);
        if (Widgets.Button("Copy shopping list", ButtonStyle.Brass, small: true))
        {
            ImGui.SetClipboardText(ShoppingList.Build(plan, data.ItemName, data.DyeName, session.Title));
            Notify("Shopping list copied");
        }
        ImGui.SameLine();
        if (Widgets.Button("Copy checklist", ButtonStyle.Ghost, small: true))
        {
            ImGui.SetClipboardText(Checklist(plan));
            Notify("Checklist copied");
        }
    }

    private void DrawFilters()
    {
        if (session.PlanSide == Side.Interior && session.Context.HouseSize != "Apartment")
        {
            var b = config.ShowBasement;
            if (Widgets.Toggle("Basement##fb", ref b)) { config.ShowBasement = b; config.Save(); }
            ImGui.SameLine(0, S(16));
            var g = config.ShowGround;
            if (Widgets.Toggle("Ground##fg", ref g)) { config.ShowGround = g; config.Save(); }
            if (session.Context.HouseSize is "Medium" or "Large")
            {
                ImGui.SameLine(0, S(16));
                var u = config.ShowUpper;
                if (Widgets.Toggle("Upper##fu", ref u)) { config.ShowUpper = u; config.Save(); }
            }
        }
        Widgets.Segmented("##state", StateFilters, ref stateFilter);
        ImGui.SameLine(0, S(12));
        ImGui.SetNextItemWidth(Math.Max(S(120), ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("##search", "Search pieces", ref search, 64);
        var near = config.NearMeRadius;
        ImGui.SetNextItemWidth(S(220));
        if (ImGui.SliderFloat("##near", ref near, 0, 30, near <= 0 ? "Anywhere in the home" : "Within %.0f yalms of you"))
        {
            config.NearMeRadius = MathF.Round(near);
            config.Save();
        }
        ImGui.SameLine();
        Widgets.Muted("Stand in a room and narrow this to work on that room.", false);
    }

    private void DrawPieceRow(uint item, PieceState state, List<PlanEntry> entries)
    {
        ImGui.PushID($"{item}-{state}");
        var start = ImGui.GetCursorScreenPos();
        var color = Overlay.ColorOf(state);
        Widgets.ItemIcon(data.ItemIcon(item), 34, state == PieceState.InPlace ? 0.6f : 1f);
        ImGui.SameLine(0, S(10));
        ImGui.BeginGroup();
        ImGui.TextColored(state == PieceState.InPlace ? Palette.Ash : Palette.Ivory, $"{(entries.Count > 1 ? $"{entries.Count} × " : string.Empty)}{data.ItemName(item)}");

        // What the pieces need: their dyes, where they come from, which floors.
        var details = new List<string>();
        var floors = entries.Select(e => e.Target.Floor).Distinct().Select(Floors.Name);
        details.Add(string.Join(", ", floors));
        var source = entries.Select(e => e.Stock).FirstOrDefault(s => s != null);
        if (state == PieceState.FromInventory && source != null) details.Add($"in {InventoryReader.Describe(source)}");
        if (state == PieceState.FromStoreroom) details.Add("in the storeroom");
        using (theme.Fonts.Small.Push())
            ImGui.TextColored(Palette.Dim, string.Join(" · ", details));
        ImGui.EndGroup();

        // Dyes on the right of the name: wanted, and a note when what you have differs.
        var dyes = entries.Where(e => e.Target.Stain != 0).GroupBy(e => e.Target.Stain).ToList();
        var mismatched = entries.Count(e => e.DyeDiffers);
        ImGui.SameLine();
        var column = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - S(250);
        ImGui.SetCursorScreenPos(new Vector2(Math.Max(ImGui.GetCursorScreenPos().X + S(8), column), start.Y + S(2)));
        ImGui.BeginGroup();
        Widgets.Chip(Overlay.StateWord(state), color, Palette.Mix(Palette.Panel, color, 0.14f));
        foreach (var d in dyes.Take(2))
        {
            ImGui.SameLine();
            Widgets.Swatch(data.DyeRgb(d.Key));
            ImGui.SameLine(0, S(4));
            using (theme.Fonts.Small.Push())
                ImGui.TextColored(Palette.Ash, d.Count() > 1 ? $"{data.DyeName(d.Key)} ×{d.Count()}" : data.DyeName(d.Key));
        }
        if (dyes.Count > 2)
        {
            ImGui.SameLine();
            using (theme.Fonts.Small.Push())
                ImGui.TextColored(Palette.Dim, $"+{dyes.Count - 2}");
        }
        if (mismatched > 0 && state != PieceState.Missing)
        {
            using (theme.Fonts.Small.Push())
                ImGui.TextColored(Palette.Danger, state == PieceState.FromInventory ? $"Dye after placing ({mismatched})" : $"Dye differs on {mismatched}");
        }
        ImGui.EndGroup();

        ImGui.SetCursorScreenPos(new Vector2(start.X, Math.Max(ImGui.GetItemRectMax().Y, start.Y + S(34)) + S(6)));
        Widgets.Rule();
        Widgets.Gap(4);
        ImGui.PopID();
    }

    private string Checklist(Plan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{session.Title}: checklist ({(session.PlanSide == Side.Garden ? "garden" : "inside")})").AppendLine();
        foreach (var group in plan.Entries.GroupBy(e => e.State).OrderBy(g => g.Key))
        {
            sb.AppendLine($"{Overlay.StateWord(group.Key)} ({group.Count()})");
            foreach (var item in group.GroupBy(e => e.Target.ItemId).OrderBy(g => data.ItemName(g.Key)))
            {
                var dyes = item.Where(e => e.Target.Stain != 0).GroupBy(e => e.Target.Stain).Select(d => $"{data.DyeName(d.Key)} ×{d.Count()}").ToList();
                sb.Append("  ").Append(item.Count()).Append(" × ").Append(data.ItemName(item.Key));
                if (dyes.Count > 0) sb.Append(" (").Append(string.Join(", ", dyes)).Append(')');
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
