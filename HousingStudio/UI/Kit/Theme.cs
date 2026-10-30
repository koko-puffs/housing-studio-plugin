using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace HousingStudio.UI;

/// <summary>
/// Pushes the window's look onto ImGui. Before ImGui.Begin only what can't touch the title bar's
/// buttons: colours (the walnut bar, the ink window, the 0.2 ivory edge), the 10px window radius
/// and no window padding. The title bar keeps Dalamud's own font and frame padding, so its ☰, ▼ and
/// × sit and hit exactly as in every other Dalamud window (0.3.0 pushed a 13px font and a 12px frame
/// padding there, which made each button's hit box about 41px wide while they stand 17px apart).
/// Inside the window: no item spacing (every gap is the mockup's own, placed by the views), and
/// ImGui's own controls (text fields, scrollbars, popups) in the tokens' colours.
/// </summary>
public static class Theme
{
    /// <summary>CSS pixels to screen pixels (Dalamud's global scale).</summary>
    public static float S(float px) => px * ImGuiHelpers.GlobalScale;

    public static bool ReducedMotion => Services.PluginInterface.UiBuilder.ShouldUseReducedMotion;

    private static readonly Stack<(int Colors, int Vars, IDisposable? Font)> Pushed = new();

    /// <summary>Before ImGui.Begin: the frame's colours, radius, edge and padding; nothing that sizes the title bar.</summary>
    public static void PushFrame()
    {
        var colors = 0;
        var vars = 0;
        void C(ImGuiCol col, Vector4 v)
        {
            ImGui.PushStyleColor(col, v);
            colors++;
        }
        void V(ImGuiStyleVar v, Vector2 value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }
        void F(ImGuiStyleVar v, float value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }

        C(ImGuiCol.WindowBg, Palette.Ink);
        C(ImGuiCol.Border, Palette.LineStrong);
        C(ImGuiCol.BorderShadow, Vector4.Zero);
        C(ImGuiCol.TitleBg, Palette.Walnut);
        C(ImGuiCol.TitleBgActive, Palette.Walnut);
        C(ImGuiCol.TitleBgCollapsed, Palette.Walnut);
        C(ImGuiCol.Text, Palette.Ivory);

        F(ImGuiStyleVar.WindowRounding, S(10));
        F(ImGuiStyleVar.WindowBorderSize, 1f);
        V(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        Pushed.Push((colors, vars, null));
    }

    /// <summary>Inside the window: no spacing of ImGui's own, and its controls in the tokens' colours.</summary>
    public static void PushInside()
    {
        var colors = 0;
        var vars = 0;
        void C(ImGuiCol col, Vector4 v)
        {
            ImGui.PushStyleColor(col, v);
            colors++;
        }
        void V(ImGuiStyleVar v, Vector2 value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }
        void F(ImGuiStyleVar v, float value)
        {
            ImGui.PushStyleVar(v, value);
            vars++;
        }

        C(ImGuiCol.Text, Palette.Text);
        C(ImGuiCol.TextDisabled, Palette.Dim);
        C(ImGuiCol.ChildBg, Vector4.Zero);
        C(ImGuiCol.PopupBg, Palette.Over(Palette.Float, Palette.Ink));
        C(ImGuiCol.Border, Palette.Line);
        C(ImGuiCol.FrameBg, Vector4.Zero);
        C(ImGuiCol.FrameBgHovered, Vector4.Zero);
        C(ImGuiCol.FrameBgActive, Vector4.Zero);
        C(ImGuiCol.TextSelectedBg, Palette.WithAlpha(Palette.Brass, 0.3f));
        C(ImGuiCol.NavHighlight, Palette.BrassSoft);
        C(ImGuiCol.Header, Palette.LineFaint);
        C(ImGuiCol.HeaderHovered, Palette.LineFaint);
        C(ImGuiCol.HeaderActive, Palette.LinePicked);
        C(ImGuiCol.ModalWindowDimBg, Palette.WithAlpha(Palette.Ink, 0.6f));
        C(ImGuiCol.ScrollbarBg, Vector4.Zero);
        C(ImGuiCol.ScrollbarGrab, Palette.WithAlpha(Palette.Ash, 0.32f));
        C(ImGuiCol.ScrollbarGrabHovered, Palette.WithAlpha(Palette.Ash, 0.5f));
        C(ImGuiCol.ScrollbarGrabActive, Palette.Brass);

        V(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        V(ImGuiStyleVar.ItemInnerSpacing, Vector2.Zero);
        V(ImGuiStyleVar.FramePadding, Vector2.Zero);
        F(ImGuiStyleVar.FrameBorderSize, 0f);
        F(ImGuiStyleVar.ChildBorderSize, 0f);
        F(ImGuiStyleVar.PopupRounding, S(Palette.RadiusLg));
        F(ImGuiStyleVar.PopupBorderSize, 1f);
        V(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        F(ImGuiStyleVar.ScrollbarSize, S(8));
        F(ImGuiStyleVar.ScrollbarRounding, S(999));
        Pushed.Push((colors, vars, null));
    }

    public static void Pop()
    {
        if (Pushed.Count == 0) return;
        var (colors, vars, font) = Pushed.Pop();
        if (vars > 0) ImGui.PopStyleVar(vars);
        if (colors > 0) ImGui.PopStyleColor(colors);
        font?.Dispose();
    }

    /// <summary>A slow brass pulse, 0 to 1, for "this is live" (still under reduced motion).</summary>
    public static float Pulse()
    {
        if (ReducedMotion) return 0.6f;
        return 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 3.2f);
    }
}
