using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.UI.Model;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Pieces (mockup 5), from the Ready header: the filter chips, Find a piece and Near me, the pieces by
/// floor (stacked when the same item in the same dye and state), each with its state and Show where;
/// and the shopping list and checklist.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly (PieceFilter Filter, string Label)[] Chips =
    {
        (PieceFilter.ToDo, "To do"), (PieceFilter.InPlace, "In place"), (PieceFilter.Missing, "Missing"), (PieceFilter.All, "All"),
    };

    private PieceFilter pieceFilter = PieceFilter.ToDo;
    private string pieceSearch = string.Empty;
    private readonly Dictionary<Floor, bool> floorOpen = new();
    private Layout? targetsFor;
    private List<PlanEntry> designPieces = new();

    /// <summary>The pieces to list: the plan's (matched, with states), or before you're home, the design's own.</summary>
    private (List<PlanEntry> Entries, bool Known) PieceEntries()
    {
        if (session.Plan is { } plan) return (Filtered().ToList(), true);
        if (!ReferenceEquals(targetsFor, session.Layout))
        {
            targetsFor = session.Layout;
            designPieces = session.Targets(ReadySide).Select(t => new PlanEntry { Target = t, State = PieceState.Missing }).ToList();
        }
        return (designPieces, false);
    }

    private void DrawPieces()
    {
        var (entries, known) = PieceEntries();
        var counts = PieceRows.Counts(entries, known);
        // A chip with nothing in it can't stay picked: fall back to All.
        var filter = known && counts.Of(pieceFilter) > 0 ? pieceFilter : PieceFilter.All;

        Frame.BeginHeader(20);
        var at = Kit.At;
        var w = Kit.Width;
        var eyebrowH = Txt.Natural(Face.Mono, 12);
        var titleH = S(26 * 1.1f);
        var headH = MathF.Max(S(40), eyebrowH + S(2) + titleH);
        if (BackButton(new Vector2(at.X, at.Y + (headH - S(40)) / 2), $"Back to {session.Title}")) sub = Sub.None;
        var tx = at.X + S(40) + S(10);
        var top = at.Y + (headH - (eyebrowH + S(2) + titleH)) / 2;
        Kit.Eyebrow(new Vector2(tx, top), session.Title, null, at.X + w - tx);
        Title(new Vector2(tx, top + eyebrowH + S(2)), Words.Count(entries.Count, "piece"), null, at.X + w - tx, 26);
        Kit.Reserve(at, w, headH);
        Kit.Gap(16);

        var row = Kit.At;
        var x = row.X;
        foreach (var (f, label) in Chips)
        {
            var n = counts.Of(f);
            if (Kit.FilterChip($"##chip{f}", new Vector2(x, row.Y), label, n, filter == f)) pieceFilter = f;
            x += Kit.FilterChipWidth(label, n) + S(8);
        }
        Kit.Reserve(row, w, S(34));
        Kit.Gap(16);

        row = Kit.At;
        const string near = "Near me";
        var nw = Kit.ButtonWidth(near, BtnSize.Md, Icon.MapPin);
        Kit.Field("##find", row, w - nw - S(10), ref pieceSearch, "Find a piece", Icon.Search, 64);
        var nearTip = config.NearMe
            ? $"On: only pieces within {config.NearMeRadius:0} yalms of you are listed, and placed (Settings → More options sets how far)."
            : $"Only pieces within {config.NearMeRadius:0} yalms of you, to work on the room you're in. Start places only those too.";
        if (Kit.Button("##nearme", new Vector2(row.X + w - nw, row.Y), near, config.NearMe ? Btn.Picked : Btn.Glass, BtnSize.Md, Icon.MapPin, 0, false, nearTip))
        {
            config.NearMe = !config.NearMe;
            config.Save();
        }
        Kit.Reserve(row, w, S(40));
        Frame.EndHeader();

        Frame.BeginBody("##pieces", "pieces-foot", 16, 22);
        if (!known)
        {
            AlertNote("Go to your home to see where each piece stands: what's in place, what moves, and what comes from your storeroom or bags.", Palette.Dim, Icon.Info);
            Kit.Gap(14);
        }
        var groups = PieceRows.Build(entries, filter, data.ItemName, pieceSearch, known);
        if (groups.Count == 0)
        {
            Kit.BeginPanel("##pieces-none", 18, 20, 18);
            Txt.Note(pieceSearch.Trim().Length > 0 ? $"No piece here is called “{pieceSearch.Trim()}”." : "Nothing here with these filters.");
            Kit.EndPanel();
        }
        var myFloor = MyFloor();
        var missingStains = known ? vm.MissingStains : new HashSet<byte>();
        for (var g = 0; g < groups.Count; g++)
        {
            if (g > 0) Kit.Gap(14);
            var group = groups[g];
            if (!floorOpen.TryGetValue(group.Floor, out var open)) open = myFloor != null ? group.Floor == myFloor : g == 0;
            Kit.BeginPanel($"##floor{group.Floor}", 0, 0, 0);
            if (FloorHeader(group, ref open)) floorOpen[group.Floor] = open;
            if (open)
                foreach (var r in group.Rows) PieceRow(r, missingStains);
            Kit.EndPanel();
        }
        Frame.EndBody();

        DrawPiecesFooter(known);
    }

    /// <summary>The floor you're standing on, to open its group first.</summary>
    private Floor? MyFloor()
    {
        if (session.Context.Side == null) return null;
        if (session.Context.Side == Side.Garden) return Floor.Garden;
        var me = Services.Objects.LocalPlayer?.Position;
        return me == null ? null : Floors.Of(me.Value.Y, session.Context.HouseSize);
    }

    /// <summary>A floor's header row: the chevron, its name, and its count in mono.</summary>
    private static bool FloorHeader(FloorGroup group, ref bool open)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var nameH = Txt.Natural(Face.SansMedium, 14);
        var h = S(12) * 2 + MathF.Max(S(16), nameH);
        var (clicked, hovered, _) = Kit.Hit($"##floorhead{group.Floor}", at, new Vector2(w, h));
        if (clicked) open = !open;
        if (hovered) Kit.RowFill(at, at + new Vector2(w, h), Palette.RowHover, true, !open);
        Kit.FocusRing(at + new Vector2(S(4)), at + new Vector2(w, h) - new Vector2(S(4)), S(12));
        var dl = Kit.Dl;
        var mid = at.Y + h / 2;
        Icons.Draw(dl, open ? Icon.ChevronDown : Icon.ChevronRight, new Vector2(at.X + S(18), mid - S(8)), S(16), Palette.Dim);
        Txt.Line(dl, new Vector2(at.X + S(18 + 16 + 10), mid - nameH / 2), Floors.Name(group.Floor), Face.SansMedium, 14, Palette.Ivory);
        var count = group.Count.ToString();
        var cw = Txt.Measure(count, Face.Mono, 12).X;
        Txt.Line(dl, new Vector2(at.X + w - S(18) - cw, mid - Txt.Natural(Face.Mono, 12) / 2), count, Face.Mono, 12, Palette.Dim);
        Kit.Reserve(at, w, h);
        return clicked;
    }

    /// <summary>A piece row (10px 18px inside, the faint line above): thumbnail, name and note, state, Show where.</summary>
    private void PieceRow(PieceRow row, HashSet<byte> missingStains)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var nameH = Txt.Natural(Face.Sans, 14);
        var h = S(10) * 2 + MathF.Max(S(34), nameH + S(2) + S(19.5f));
        // Rows out of sight only take their room.
        var clipTop = ImGui.GetWindowPos().Y;
        var clipBottom = clipTop + ImGui.GetWindowHeight();
        if (at.Y > clipBottom || at.Y + h < clipTop)
        {
            Kit.Reserve(at, w, h);
            return;
        }

        var dl = Kit.Dl;
        if (!Kit.Blocked && ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(at, at + new Vector2(w, h)))
            dl.AddRectFilled(new Vector2(Kit.Panel.Start.X + 1, at.Y), new Vector2(Kit.Panel.Start.X + Kit.Panel.Width - 1, at.Y + h), Palette.U32(Palette.RowHover));
        Kit.Divider(Kit.Panel.Start.X, Kit.Panel.Start.X + Kit.Panel.Width, at.Y - 0.5f);

        var mid = at.Y + h / 2;
        var x = at.X + S(18);
        Kit.Thumb(new Vector2(x, mid - S(17)), S(34), S(Palette.RadiusSm), data.ItemIcon(row.ItemId));
        x += S(34) + S(12);

        var pinX = at.X + w - S(18) - S(32);
        var stateW = row.State is { } st ? Kit.StatePillWidth(st) : 0;
        var stateX = pinX - S(12) - stateW;
        var textW = (row.State != null ? stateX : pinX) - S(12) - x;

        var name = data.ItemName(row.ItemId) + (row.Count > 1 ? $" ×{row.Count}" : string.Empty);
        var note = new List<Run>();
        switch (row.State)
        {
            case PieceState.Move:
                note.Add(Run.Note("In the wrong spot"));
                break;
            case PieceState.InPlace:
                note.Add(Run.Note("Where the design has it"));
                break;
            default:
                if (row.State == PieceState.Missing) note.Add(Run.Note(row.Stain != 0 ? "Not in your storeroom or bags · " : "Not in your storeroom or bags"));
                if (row.Stain != 0)
                {
                    note.Add(Run.Dot(data.DyeRgb(row.Stain)));
                    note.Add(Run.Note(data.DyeName(row.Stain)));
                    if (missingStains.Contains(row.Stain))
                    {
                        note.Add(Run.Note(" · "));
                        note.Add(Run.Note("dye missing", Palette.Danger));
                    }
                }
                else if (row.State != PieceState.Missing)
                {
                    note.Add(Run.Note("Undyed"));
                }
                break;
        }
        var top = mid - (nameH + S(2) + S(19.5f)) / 2;
        Txt.Line(dl, new Vector2(x, top), Txt.Fit(name, Face.Sans, 14, textW), Face.Sans, 14, Palette.Ivory);
        // One line: a long note is cut rather than wrapped, so rows keep one height.
        var noteText = string.Concat(note.Select(r => r.Text));
        if (Txt.Measure(noteText, Face.Sans, 13).X + (row.Stain != 0 ? S(16) : 0) > textW)
            note = new List<Run> { Run.Note(Txt.Fit(noteText, Face.Sans, 13, textW)) };
        Txt.Flow(new Vector2(x, top + nameH + S(2)), textW + S(40), note, 19.5f, out _);

        if (row.State is { } state) Kit.StatePill(new Vector2(stateX, mid - Kit.StatePillHeight / 2), state);
        var away = session.Context.Side == null || (session.PlanSide != null && session.Context.Side != session.PlanSide);
        var tip = away ? "Go to your home to see where it goes" : row.Count > 1 ? "Show where they go" : row.State == PieceState.InPlace ? "Show where it is" : "Show where it goes";
        if (Kit.IconButton($"##where{row.ItemId}-{row.Stain}-{row.State}-{row.Floor}", new Vector2(pinX, mid - S(16)), Icon.MapPin, tip, 32, Btn.Glass, away))
            ShowWhere(row.Entries.Select(e => e.Target));
        Kit.Reserve(at, w, h);
    }

    private void DrawPiecesFooter(bool known)
    {
        Frame.BeginFooter("pieces-foot", 16, 20);
        var at = Kit.At;
        var w = Kit.Width;
        const string shop = "Copy shopping list";
        const string check = "Copy checklist";
        var sw = Kit.ButtonWidth(shop, BtnSize.Md, Icon.Clipboard);
        var cw = Kit.ButtonWidth(check, BtnSize.Md, null, Btn.Quiet);
        var plan = session.Plan;
        if (Kit.Button("##shop", at, shop, Btn.Glass, BtnSize.Md, Icon.Clipboard, 0, plan == null, plan == null ? "Go to your home to see what you have" : null))
            CopyShoppingList();
        if (known)
        {
            var summary = Words.ToBuy(plan!.Entries.Count(e => e.State == PieceState.Missing), vm.MissingDyes.Count);
            var noteX = at.X + sw + S(10);
            Txt.Line(Kit.Dl, new Vector2(noteX, at.Y), Txt.Fit(summary, Face.Sans, 13, w - sw - cw - S(20)), Face.Sans, 13, Palette.Ash, 40);
        }
        if (Kit.Button("##checklist", new Vector2(at.X + w - cw, at.Y), check, Btn.Quiet, BtnSize.Md, null, 0, plan == null))
        {
            ImGui.SetClipboardText(Checklist(plan!));
            Notify("Checklist copied");
        }
        Kit.Reserve(at, w, S(40));
        Frame.EndFooter();
    }

    private void CopyShoppingList()
    {
        if (session.Plan is not { } plan) return;
        ImGui.SetClipboardText(ShoppingList.Build(plan, data.ItemName, data.DyeName, session.Title, s => HousingActions.DyesOwned(data, s)));
        Notify("Shopping list copied");
    }

    private string Checklist(Plan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{session.Title}: checklist ({(session.PlanSide == Side.Garden ? "garden" : "inside")})").AppendLine();
        foreach (var group in plan.Entries.GroupBy(e => e.State).OrderBy(g => PieceRows.StateOrder(g.Key)))
        {
            sb.AppendLine($"{Words.Capital(Overlay.StateWord(group.Key))} ({group.Count()})");
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
