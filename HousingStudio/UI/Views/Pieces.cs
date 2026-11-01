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

    /// <summary>Where the missing pieces come from, for this frame's rows (a piece on a retainer says so).</summary>
    private ShoppingPlan shopForRows = ShoppingPlan.Empty;
    private string pieceSearch = string.Empty;
    private readonly Dictionary<Floor, bool> floorOpen = new();
    private (Layout? Layout, Side Side, PlotSpot? Plot) targetsFor;
    private List<PlanEntry> designPieces = new();

    /// <summary>The pieces to list: the plan's (matched, with states), or before you're home, the design's own.</summary>
    private (List<PlanEntry> Entries, bool Known) PieceEntries()
    {
        if (session.Plan is { } plan) return (FilteredList(), true);
        // Worked out again when the design, the side or the plot changes (a garden's pieces move with its plot).
        var key = (session.Layout, ReadySide, session.Plot);
        if (!ReferenceEquals(targetsFor.Layout, key.Layout) || targetsFor.Side != key.ReadySide || !Equals(targetsFor.Plot, key.Plot))
        {
            targetsFor = key;
            designPieces = session.Targets(ReadySide, toList: true).Select(t => new PlanEntry { Target = t, State = PieceState.Missing }).ToList();
        }
        return (designPieces, false);
    }

    private (List<PlanEntry>? Entries, bool Known, FilterCounts Counts) piecesCountsFor;
    private (List<PlanEntry>? Entries, bool Known, PieceFilter Filter, string? Search, List<FloorGroup>? Groups) piecesGroupsFor;

    private void DrawPieces()
    {
        var (entries, known) = PieceEntries();
        // 1.0.0: counted and grouped again only when the pieces, the filter or the search change (every frame before).
        if (!ReferenceEquals(piecesCountsFor.Entries, entries) || piecesCountsFor.Known != known)
            piecesCountsFor = (entries, known, PieceRows.Counts(entries, known));
        var counts = piecesCountsFor.Counts;
        // A chip with nothing in it can't stay picked: fall back to All.
        var filter = known && counts.Of(pieceFilter) > 0 ? pieceFilter : PieceFilter.All;

        Frame.BeginHeader(20);
        var w = Kit.Width;
        if (ScreenHeader($"Back to {session.Title}", $"{session.ShortTitle} · pieces", Words.Count(entries.Count, "piece"))) sub = Sub.None;
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
        DrawPiecesClashes();
        if (!ReferenceEquals(piecesGroupsFor.Entries, entries) || piecesGroupsFor.Known != known || piecesGroupsFor.Filter != filter
            || piecesGroupsFor.Search != pieceSearch || piecesGroupsFor.Groups == null)
            piecesGroupsFor = (entries, known, filter, pieceSearch, PieceRows.Build(entries, filter, data.ItemName, pieceSearch, known));
        var groups = piecesGroupsFor.Groups;
        if (groups.Count == 0)
        {
            Kit.BeginPanel("##pieces-none", 18, 20, 18);
            Txt.Note(pieceSearch.Trim().Length > 0 ? $"No piece here is called “{pieceSearch.Trim()}”." : "Nothing here with these filters.");
            Kit.EndPanel();
        }
        var myFloor = MyFloor();
        shopForRows = known ? ShopPlan() : ShoppingPlan.Empty;
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
        var onRetainer = row.State == PieceState.Missing ? RetainerNote(row.ItemId, row.Count, shopForRows) : null;
        var retainerOnly = onRetainer != null && onRetainer.StartsWith("On ", StringComparison.Ordinal);
        var stateW = retainerOnly ? RetainerPillWidth : row.State is { } st ? Kit.StatePillWidth(st) : 0;
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
                if (row.State == PieceState.Missing)
                {
                    var why = onRetainer ?? "Not in your storeroom or bags";
                    note.Add(Run.Note(row.Stain != 0 ? why + " · " : why));
                }
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

        if (retainerOnly) RetainerPill(new Vector2(stateX, mid - Kit.StatePillHeight / 2));
        else if (row.State is { } state) Kit.StatePill(new Vector2(stateX, mid - Kit.StatePillHeight / 2), state);
        var away = session.Context.Side == null || (session.PlanSide != null && session.Context.Side != session.PlanSide);
        var tip = away ? "Go to your home to see where it goes" : row.Count > 1 ? "Show where they go" : row.State == PieceState.InPlace ? "Show where it is" : "Show where it goes";
        if (Kit.IconButton($"##where{row.ItemId}-{row.Stain}-{row.State}-{row.Floor}", new Vector2(pinX, mid - S(16)), Icon.MapPin, tip, 32, Btn.Glass, away))
            ShowWhere(row.Entries.Select(e => e.Target));
        Kit.Reserve(at, w, h);
    }

    /// <summary>The footer's count line for a shopping plan (made again only when the plan changes).</summary>
    private (ShoppingPlan? For, string Line) piecesFoot = (null, string.Empty);

    /// <summary>
    /// The footer (shared rule 2, no brass: this screen is for looking things up): Shopping list and
    /// Copy checklist on the left, and under them what's to get, counted as Ready counts it
    /// ("99 pieces and 3 fixtures to get · no dyes to buy").
    /// </summary>
    private void DrawPiecesFooter(bool known)
    {
        Frame.BeginFooter("pieces-foot", 16, 20);
        var at = Kit.At;
        var w = Kit.Width;
        const string shop = "Shopping list";
        const string check = "Copy checklist";
        var sw = Kit.ButtonWidth(shop, BtnSize.Lg, Icon.Package);
        var cw = Kit.ButtonWidth(check, BtnSize.Lg, Icon.Clipboard, Btn.Quiet);
        // Side by side when both fit; else Copy checklist goes under it.
        var beside = sw + S(10) + cw <= w;
        var plan = session.Plan;
        if (Kit.Button("##shop", at, shop, Btn.Glass, BtnSize.Lg, Icon.Package, 0, plan == null,
                plan == null ? "Go to your home to see what you have" : "Where to get what's missing: your retainers, vendors, the market board"))
            OpenShopping(Sub.Pieces);
        var checkAt = beside ? new Vector2(at.X + sw + S(10), at.Y) : new Vector2(at.X, at.Y + S(46) + S(10));
        if (Kit.Button("##checklist", checkAt, check, Btn.Quiet, BtnSize.Lg, Icon.Clipboard, 0, plan == null))
        {
            ImGui.SetClipboardText(Checklist(plan!));
            Notify("Checklist copied");
        }
        Kit.Reserve(at, w, beside ? S(46) : S(46) * 2 + S(10));
        Kit.Gap(10);
        if (known)
        {
            // From the shopping plan, as Ready's "102 to get": the pieces and fixtures to get, the dyes apart.
            if (!ReferenceEquals(piecesFoot.For, shopForRows))
            {
                var (pieces, fixtures, dyes) = ShoppingScreen.Counts(shopForRows);
                piecesFoot = (shopForRows, ShoppingScreen.PiecesFoot(pieces, fixtures, dyes));
            }
            Txt.Note(piecesFoot.Line);
        }
        else Txt.Note("Go to your home to see what's missing.");
        Frame.EndFooter();
    }

    private void CopyShoppingList()
    {
        if (session.Plan is not { } plan) return;
        var shop = ShopPlan();
        ImGui.SetClipboardText(ShoppingList.WithSources(plan, shop, data.AnyItemName, data.DyeName,
            t => t is { } at ? When.Relative(at.ToLocalTime(), DateTime.Now, Clock) : null, session.Title));
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
