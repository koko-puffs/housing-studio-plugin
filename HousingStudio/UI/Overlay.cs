using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HousingStudio.Core;
using HousingStudio.Game;
using HousingStudio.Session;
using static HousingStudio.UI.Theme;

namespace HousingStudio.UI;

/// <summary>
/// Markers in the world where the design's pieces go (drawn behind ImGui windows, over the game):
/// a dot at the spot with a tick showing which way it faces, coloured by what's to be done, a line
/// from a placed piece to where it's going, and the piece being worked on ringed in brass.
/// </summary>
public sealed class Overlay
{
    private readonly Configuration config;
    private readonly LayoutSession session;
    private readonly PlacementRunner runner;
    private readonly GameData data;

    public Overlay(Configuration config, LayoutSession session, PlacementRunner runner, GameData data)
    {
        this.config = config;
        this.session = session;
        this.runner = runner;
        this.data = data;
    }

    public static Vector4 ColorOf(PieceState s) => s switch
    {
        PieceState.InPlace => Palette.Success,
        PieceState.Move => Palette.Brass,
        PieceState.FromStoreroom => Palette.BrassSoft,
        PieceState.FromInventory => Palette.Blueprint,
        _ => Palette.Danger,
    };

    private List<(Target Target, string Label)> highlight = new();
    private DateTime highlightUntil;

    /// <summary>"Show where": lights these pieces' spots for a while (even with markers off) and points at them when they're off screen.</summary>
    public void Highlight(IEnumerable<(Target Target, string Label)> spots, double seconds = 12)
    {
        highlight = spots.ToList();
        highlightUntil = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
    }

    public void Draw()
    {
        if (Services.GameGui.GameUiHidden) return;
        DrawHighlight();
        if (!config.ShowMarkers) return;
        var plan = session.Plan;
        var me = Services.Objects.LocalPlayer;
        if (plan == null || me == null || session.Context.Side == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        var mouse = ImGui.GetMousePos();
        var current = runner.Current?.Target;
        var range = config.MarkerRange <= 0 ? float.MaxValue : config.MarkerRange;
        string? hoverLabel = null;
        Vector2 hoverAt = default;
        var hoverDist = S(14);

        foreach (var e in plan.Entries)
        {
            var isCurrent = current != null && current.Key == e.Target.Key && current.ItemId == e.Target.ItemId;
            if (!isCurrent)
            {
                if (e.State == PieceState.InPlace && !config.ShowPlacedMarkers) continue;
                if (!config.FloorShown(e.Target.Floor)) continue;
                if (config.NearMe && config.NearMeRadius > 0 && Vector3.Distance(me.Position, e.Target.Position) > config.NearMeRadius) continue;
            }
            if (Vector3.Distance(me.Position, e.Target.Position) > range && !isCurrent) continue;
            if (!Services.GameGui.WorldToScreen(e.Target.Position, out var at, out var inView) || !inView) continue;

            var color = ColorOf(e.State);
            var r = S(isCurrent ? 6 : 4.5f);

            // A placed piece on its way: a faint line from where it is.
            if (e.State == PieceState.Move && e.Placed is { } placed
                && Services.GameGui.WorldToScreen(placed.Position, out var from, out var fromInView) && fromInView)
            {
                dl.AddLine(from, at, Palette.U32(Palette.WithAlpha(color, isCurrent ? 0.9f : 0.35f)), S(isCurrent ? 2 : 1));
                dl.AddCircle(from, S(3), Palette.U32(Palette.WithAlpha(color, 0.5f)));
            }

            // Which way it faces.
            var ahead = e.Target.Position + new Vector3(MathF.Sin(e.Target.Yaw), 0, MathF.Cos(e.Target.Yaw)) * 0.6f;
            if (Services.GameGui.WorldToScreen(ahead, out var tip, out _))
                dl.AddLine(at, tip, Palette.U32(Palette.WithAlpha(color, 0.85f)), S(1.5f));

            dl.AddCircleFilled(at, r + S(1.5f), Palette.U32(Palette.WithAlpha(Palette.Ink, 0.75f)));
            dl.AddCircleFilled(at, r, Palette.U32(color));
            if (isCurrent)
            {
                var pulse = Pulse();
                dl.AddCircle(at, r + S(6 + 6 * pulse), Palette.U32(Palette.WithAlpha(Palette.Brass, 0.9f - 0.5f * pulse)), 0, S(2));
                Label(dl, at + new Vector2(S(12), -S(10)), data.ItemName(e.Target.ItemId), true);
            }
            else if (config.ShowLabels)
            {
                var d = Vector2.Distance(mouse, at);
                if (d < hoverDist)
                {
                    hoverDist = d;
                    hoverLabel = $"{data.ItemName(e.Target.ItemId)} · {StateWord(e.State)}";
                    hoverAt = at;
                }
            }
        }

        if (hoverLabel != null) Label(dl, hoverAt + new Vector2(S(12), -S(10)), hoverLabel, false);
    }

    private void DrawHighlight()
    {
        if (highlight.Count == 0) return;
        if (DateTime.UtcNow > highlightUntil)
        {
            highlight.Clear();
            return;
        }
        var me = Services.Objects.LocalPlayer;
        if (me == null) return;
        var dl = ImGui.GetBackgroundDrawList();
        var pulse = Pulse();
        var anyInView = false;
        foreach (var (target, label) in highlight)
        {
            if (!Services.GameGui.WorldToScreen(target.Position, out var at, out var inView) || !inView) continue;
            anyInView = true;
            dl.AddCircleFilled(at, S(7), Palette.U32(Palette.Brass));
            dl.AddCircle(at, S(7), Palette.U32(Palette.Ink), 0, S(1.5f));
            dl.AddCircle(at, S(14 + 10 * pulse), Palette.U32(Palette.WithAlpha(Palette.Brass, 0.95f - 0.6f * pulse)), 0, S(2.5f));
            dl.AddCircle(at, S(26 + 10 * pulse), Palette.U32(Palette.WithAlpha(Palette.Brass, 0.4f - 0.3f * pulse)), 0, S(1.5f));
            Label(dl, at + new Vector2(S(18), -S(10)), label, true);
        }
        if (anyInView) return;

        // None in view: an arrow at the edge of the screen, towards the nearest.
        var (nearest, name) = highlight.OrderBy(h => Vector3.Distance(me.Position, h.Target.Position)).First();
        var front = Services.GameGui.WorldToScreen(nearest.Position, out var p, out _);
        var view = ImGui.GetMainViewport();
        var centre = view.Pos + view.Size / 2;
        var dir = p - centre;
        if (!front) dir = -dir;
        if (dir.LengthSquared() < 1) dir = new Vector2(0, -1);
        dir = Vector2.Normalize(dir);
        var inset = S(70);
        var half = view.Size / 2 - new Vector2(inset);
        var t = MathF.Min(MathF.Abs(dir.X) > 0.0001f ? half.X / MathF.Abs(dir.X) : float.MaxValue, MathF.Abs(dir.Y) > 0.0001f ? half.Y / MathF.Abs(dir.Y) : float.MaxValue);
        var spot = centre + dir * t;
        var side = new Vector2(-dir.Y, dir.X);
        var r = S(20);
        dl.AddCircleFilled(spot, r + S(2), Palette.U32(Palette.WithAlpha(Palette.Ink, 0.85f)));
        dl.AddCircle(spot, r + S(6 + 6 * pulse), Palette.U32(Palette.WithAlpha(Palette.Brass, 0.8f - 0.5f * pulse)), 0, S(2));
        dl.AddTriangleFilled(spot + dir * S(12), spot - dir * S(6) + side * S(9), spot - dir * S(6) - side * S(9), Palette.U32(Palette.Brass));
        var yalms = Vector3.Distance(me.Position, nearest.Position);
        var text = $"{name} · {yalms:0} yalms";
        using (Txt.Fonts.Push(Face.Sans, 13))
        {
            var size = ImGui.CalcTextSize(text);
            var labelAt = spot - dir * (r + S(16)) - size / 2;
            Label(dl, labelAt, text, true);
        }
    }

    private void Label(ImDrawListPtr dl, Vector2 at, string text, bool brass)
    {
        using var font = Txt.Fonts.Push(Face.Sans, 13);
        var size = ImGui.CalcTextSize(text);
        var pad = new Vector2(S(8), S(4));
        dl.AddRectFilled(at - pad, at + size + pad, Palette.U32(Palette.Float), S(Palette.RadiusPill));
        dl.AddRect(at - pad, at + size + pad, Palette.U32(brass ? Palette.Brass : Palette.Line), S(Palette.RadiusPill));
        dl.AddText(at, Palette.U32(brass ? Palette.BrassSoft : Palette.Ivory), text);
    }

    public static string StateWord(PieceState s) => s switch
    {
        PieceState.InPlace => "in place",
        PieceState.Move => "to move",
        PieceState.FromStoreroom => "in the storeroom",
        PieceState.FromInventory => "in your bags",
        _ => "missing",
    };
}
