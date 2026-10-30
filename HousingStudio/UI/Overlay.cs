using System;
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

    public void Draw()
    {
        if (!config.ShowMarkers || Services.GameGui.GameUiHidden) return;
        var plan = session.Plan;
        var me = Services.Objects.LocalPlayer;
        if (plan == null || me == null || session.Context.Side == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        var mouse = ImGui.GetMousePos();
        var current = runner.Current?.Entry;
        var range = config.MarkerRange <= 0 ? float.MaxValue : config.MarkerRange;
        string? hoverLabel = null;
        Vector2 hoverAt = default;
        var hoverDist = S(14);

        foreach (var e in plan.Entries)
        {
            var isCurrent = current != null && current.Target.Key == e.Target.Key && current.Target.ItemId == e.Target.ItemId;
            if (!isCurrent)
            {
                if (e.State == PieceState.InPlace && !config.ShowPlacedMarkers) continue;
                if (!config.FloorShown(e.Target.Floor)) continue;
                if (config.NearMeRadius > 0 && Vector3.Distance(me.Position, e.Target.Position) > config.NearMeRadius) continue;
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

    private void Label(ImDrawListPtr dl, Vector2 at, string text, bool brass)
    {
        using var font = Widgets.Fonts.Small.Push();
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
