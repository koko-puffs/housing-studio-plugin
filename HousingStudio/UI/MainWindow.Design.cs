using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

public sealed partial class MainWindow
{
    private void DrawDesign()
    {
        DrawDropZone();
        Widgets.Gap(10);

        var layout = session.Layout;
        if (layout == null)
        {
            DrawRecent();
            return;
        }

        // The loaded design.
        Widgets.BeginPanel();
        Widgets.Eyebrow(layout.BuildName != null ? "Housing Studio build" : "MakePlace layout");
        Widgets.Heading(session.Title);
        if (session.FilePath != null) Widgets.Muted(session.FilePath);
        Widgets.Gap(4);
        var size = layout.HouseSize switch
        {
            null => "Any home",
            "Apartment" => "Apartment",
            var s => $"{s} house",
        };
        Widgets.Chip(size, Palette.Text, Palette.Field, Palette.Line);
        ImGui.SameLine();
        Widgets.Chip(layout.District != null ? Districts.Display(layout.District) : layout.DistrictName ?? "Any district", Palette.Text, Palette.Field, Palette.Line);
        ImGui.SameLine();
        Widgets.Chip($"{layout.Interior.Count} inside", Palette.Text, Palette.Field, Palette.Line);
        if (layout.Garden.Count > 0)
        {
            ImGui.SameLine();
            Widgets.Chip($"{layout.Garden.Count} in the garden", Palette.Text, Palette.Field, Palette.Line);
        }
        Widgets.Gap(6);
        if (Widgets.Button("Open another…", ButtonStyle.Ghost, small: true)) OpenDialog();
        ImGui.SameLine();
        if (Widgets.Button("Reload", ButtonStyle.Ghost, small: true, disabled: runner.Active) && session.FilePath != null) Open(session.FilePath);
        ImGui.SameLine();
        // Closing (like opening) happens after this frame is drawn: the rest of the page still
        // reads the layout it started drawing with.
        if (Widgets.Button("Close", ButtonStyle.Quiet, small: true, disabled: runner.Active)) Later(session.Unload);
        Widgets.EndPanel();
        Widgets.Gap(10);

        DrawStats();
        DrawWarnings();
        DrawSideNote(layout);
        DrawFixtures(layout);
    }

    private void DrawDropZone()
    {
        var w = ImGui.GetContentRegionAvail().X;
        var h = S(session.Layout == null ? 132 : 56);
        var pos = ImGui.GetCursorScreenPos();
        var dragging = Services.DragDrop.IsDragging && Services.DragDrop.Files.Any(IsLayoutFile);
        ImGui.InvisibleButton("##drop", new Vector2(w, h));
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked()) OpenDialog();
        DropTarget();

        var dl = ImGui.GetWindowDrawList();
        var fill = dragging ? Palette.BrassWash : hovered ? Palette.Mix(Palette.Ink, Palette.Ivory, 0.03f) : Palette.Ink;
        dl.AddRectFilled(pos, pos + new Vector2(w, h), Palette.U32(fill), S(Palette.RadiusLg));
        DashedRect(dl, pos, pos + new Vector2(w, h), dragging ? Palette.Brass : hovered ? Palette.LineHover : Palette.LineStrong, S(Palette.RadiusLg));

        if (session.Layout == null)
        {
            using (theme.Fonts.Heading.Push())
                Center(dl, pos, w, S(30), dragging ? "Let go to load it" : "Drop a .build here", Palette.Ivory);
            using (theme.Fonts.Small.Push())
                Center(dl, pos, w, S(64), "or click to choose a Housing Studio build or a MakePlace .json", Palette.Ash);
            ImGui.SetCursorScreenPos(pos + new Vector2((w - S(150)) / 2, S(84)));
            if (Widgets.Button("Open a file…", ButtonStyle.Brass, S(150), small: true)) OpenDialog();
            ImGui.SetCursorScreenPos(pos + new Vector2(0, h));
            ImGui.Dummy(new Vector2(w, 0));
        }
        else
        {
            using (theme.Fonts.Small.Push())
                Center(dl, pos, w, (h - ImGui.GetTextLineHeight()) / 2, dragging ? "Let go to load it" : "Drop another .build or .json here, or click to choose one", dragging ? Palette.BrassSoft : Palette.Ash);
        }
    }

    private static void Center(ImDrawListPtr dl, Vector2 pos, float width, float y, string text, Vector4 color)
    {
        var ts = ImGui.CalcTextSize(text);
        dl.AddText(pos + new Vector2((width - ts.X) / 2, y), Palette.U32(color), text);
    }

    private static void DashedRect(ImDrawListPtr dl, Vector2 min, Vector2 max, Vector4 color, float radius)
    {
        // Straight dashes along the sides, plain arcs at the corners.
        var c = Palette.U32(color);
        var dash = S(7);
        var gap = S(5);
        void Line(Vector2 a, Vector2 b)
        {
            var len = Vector2.Distance(a, b);
            var dir = (b - a) / Math.Max(len, 1);
            for (var t = 0f; t < len; t += dash + gap)
                dl.AddLine(a + dir * t, a + dir * Math.Min(len, t + dash), c, 1f);
        }
        Line(new Vector2(min.X + radius, min.Y), new Vector2(max.X - radius, min.Y));
        Line(new Vector2(min.X + radius, max.Y), new Vector2(max.X - radius, max.Y));
        Line(new Vector2(min.X, min.Y + radius), new Vector2(min.X, max.Y - radius));
        Line(new Vector2(max.X, min.Y + radius), new Vector2(max.X, max.Y - radius));
        dl.PathArcTo(new Vector2(min.X + radius, min.Y + radius), radius, MathF.PI, MathF.PI * 1.5f, 8);
        dl.PathStroke(c, ImDrawFlags.None, 1f);
        dl.PathArcTo(new Vector2(max.X - radius, min.Y + radius), radius, MathF.PI * 1.5f, MathF.PI * 2, 8);
        dl.PathStroke(c, ImDrawFlags.None, 1f);
        dl.PathArcTo(new Vector2(max.X - radius, max.Y - radius), radius, 0, MathF.PI * 0.5f, 8);
        dl.PathStroke(c, ImDrawFlags.None, 1f);
        dl.PathArcTo(new Vector2(min.X + radius, max.Y - radius), radius, MathF.PI * 0.5f, MathF.PI, 8);
        dl.PathStroke(c, ImDrawFlags.None, 1f);
    }

    private void DrawRecent()
    {
        if (config.RecentFiles.Count == 0)
        {
            Widgets.Muted("Download a build from Housing Studio (a design's page → Build → Download .build), or save a layout from MakePlace, then open it here. Nothing in your house changes until you press Start on the Place page.");
            return;
        }
        Widgets.Eyebrow("Recent");
        Widgets.Gap(2);
        string? remove = null;
        // A copy: opening a file moves it to the top of the list (opening also waits until after
        // drawing, but the list must never change while it's being walked).
        foreach (var path in config.RecentFiles.ToArray())
        {
            var exists = File.Exists(path);
            ImGui.PushID(path);
            var start = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = S(48);
            if (ImGui.InvisibleButton("##row", new Vector2(w - S(40), h)) && exists) Open(path);
            var hovered = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            if (hovered) dl.AddRectFilled(start, start + new Vector2(w, h), Palette.U32(Palette.LineFaint), S(Palette.RadiusMd));
            dl.AddText(start + new Vector2(S(12), S(6)), Palette.U32(exists ? Palette.Ivory : Palette.Dim), Path.GetFileName(path));
            using (theme.Fonts.Small.Push())
                dl.AddText(start + new Vector2(S(12), S(26)), Palette.U32(Palette.Dim), exists ? Path.GetDirectoryName(path) ?? string.Empty : "Not found");
            ImGui.SetCursorScreenPos(start + new Vector2(w - S(36), (h - S(Palette.ControlSm)) / 2));
            if (Widgets.Button("×##remove", ButtonStyle.Quiet, S(32), small: true, tooltip: "Remove from this list")) remove = path;
            ImGui.SetCursorScreenPos(start + new Vector2(0, h));
            ImGui.Dummy(new Vector2(w, S(2)));
            ImGui.PopID();
        }
        if (remove != null)
        {
            config.RecentFiles.Remove(remove);
            config.Save();
        }
    }

    private void DrawStats()
    {
        var plan = session.Plan;
        if (plan == null)
        {
            if (session.Context.Side == null)
            {
                Widgets.BeginPanel();
                Widgets.Eyebrow("Where you are");
                Widgets.Wrapped("Go inside your house or apartment (or out to your plot for the garden) and this shows what you have, what's where, and what's missing.", Palette.Text);
                Widgets.EndPanel();
                Widgets.Gap(10);
            }
            return;
        }

        var side = session.PlanSide == Side.Garden ? "Garden" : "Inside";
        Widgets.Eyebrow($"{side} · matched against what you have now");
        Widgets.Gap(2);
        var gap = S(8);
        var w = (ImGui.GetContentRegionAvail().X - gap * 4) / 5;
        Widgets.Stat(plan.Count(PieceState.InPlace).ToString(), "in place", Palette.Success, w);
        ImGui.SameLine(0, gap);
        Widgets.Stat(plan.Count(PieceState.Move).ToString(), "to move", Palette.Brass, w);
        ImGui.SameLine(0, gap);
        Widgets.Stat(plan.Count(PieceState.FromStoreroom).ToString(), "storeroom", Palette.BrassSoft, w);
        ImGui.SameLine(0, gap);
        Widgets.Stat(plan.Count(PieceState.FromInventory).ToString(), "in bags", Palette.Blueprint, w);
        ImGui.SameLine(0, gap);
        Widgets.Stat(plan.Count(PieceState.Missing).ToString(), "missing", Palette.Danger, w);
        Widgets.Gap(6);
        if (!session.StoreroomLoaded)
            Widgets.Muted("The storeroom isn't loaded yet: open Housing → Furnishings and its Storeroom tab once, and pieces there are counted too.");
        if (plan.Extras.Count > 0)
            Widgets.Muted($"{plan.Extras.Count} placed piece{(plan.Extras.Count == 1 ? " isn't" : "s aren't")} in this design; they're left where they are.");
        Widgets.Gap(10);
    }

    private void DrawWarnings()
    {
        if (session.Warnings.Count == 0) return;
        Widgets.BeginPanel();
        Widgets.Eyebrow("Check this", Palette.Danger);
        foreach (var w in session.Warnings.ToArray()) Widgets.Wrapped(w, Palette.Danger);
        Widgets.EndPanel(Palette.DangerWash, Palette.Mix(Palette.Panel, Palette.Danger, 0.35f));
        Widgets.Gap(10);
    }

    private void DrawSideNote(Layout layout)
    {
        var ctx = session.Context;
        if (ctx.Side == Side.Interior && layout.Garden.Count > 0)
            Widgets.Muted($"This design also has {layout.Garden.Count} garden piece{(layout.Garden.Count == 1 ? "" : "s")}: go out to your plot and open Housing → Outdoor Furnishings to place those.");
        else if (ctx.Side == Side.Garden && layout.Interior.Count > 0)
            Widgets.Muted($"The {layout.Interior.Count} pieces inside are placed from inside the house (Housing → Indoor Furnishings).");
        Widgets.Gap(6);
    }

    private void DrawFixtures(Layout layout)
    {
        if (layout.InteriorFixtures.Count == 0 && layout.ExteriorFixtures.Count == 0) return;
        Widgets.BeginPanel();
        Widgets.Eyebrow("Fixtures · set these yourself");
        Widgets.Muted("Walls, floors, lights and the house's outside are changed through the game's own fixture menus (Housing → Interior Fixtures / Exterior Fixtures); the plugin lists them but doesn't apply them.");
        Widgets.Gap(4);
        foreach (var group in layout.InteriorFixtures.Concat(layout.ExteriorFixtures).GroupBy(f => f.Level))
        {
            ImGui.TextColored(Palette.Ash, group.Key == "Main" ? "The whole home" : group.Key);
            foreach (var f in group)
            {
                ImGui.Bullet();
                ImGui.SameLine();
                ImGui.TextColored(Palette.Text, $"{f.Type}: {(f.Name.Length > 0 ? f.Name : data.ItemName(f.ItemId))}");
                if (f.DyeHex != null)
                {
                    ImGui.SameLine();
                    var stain = data.StainOf(f.DyeHex);
                    Widgets.Swatch(Dyes.RgbOf(f.DyeHex));
                    ImGui.SameLine();
                    ImGui.TextColored(Palette.Dim, data.DyeName(stain));
                }
            }
        }
        Widgets.EndPanel();
    }
}
