using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace HousingStudio.UI;

/// <summary>
/// Pushes the window's look onto ImGui: the frame (Dalamud's title bar in walnut, 32px, with 13px
/// Geist in ivory; a 10px window radius and the 0.2 ivory edge, as the mockups' window) before
/// ImGui.Begin, and inside it no padding or item spacing (every gap is the mockup's own, placed by
/// the views) and ImGui's own controls (the text fields, scrollbars, popups) in the tokens' colours.
/// </summary>
public static class Theme
{
    /// <summary>CSS pixels to screen pixels (Dalamud's global scale).</summary>
    public static float S(float px) => px * ImGuiHelpers.GlobalScale;

    public static bool ReducedMotion => Services.PluginInterface.UiBuilder.ShouldUseReducedMotion;

    private static readonly Stack<(int Colors, int Vars, IDisposable? Font)> Pushed = new();

    /// <summary>Before ImGui.Begin: what the window frame and title bar use.</summary>
    public static void PushFrame(Fonts fonts)
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
        C(ImGuiCol.ResizeGrip, Vector4.Zero);
        C(ImGuiCol.ResizeGripHovered, Palette.LineHover);
        C(ImGuiCol.ResizeGripActive, Palette.Brass);
        C(ImGuiCol.Button, Vector4.Zero);
        C(ImGuiCol.ButtonHovered, Palette.LineFaint);
        C(ImGuiCol.ButtonActive, Palette.LinePicked);
        C(ImGuiCol.ScrollbarBg, Vector4.Zero);
        C(ImGuiCol.ScrollbarGrab, Palette.WithAlpha(Palette.Ash, 0.32f));
        C(ImGuiCol.ScrollbarGrabHovered, Palette.WithAlpha(Palette.Ash, 0.5f));
        C(ImGuiCol.ScrollbarGrabActive, Palette.Brass);

        F(ImGuiStyleVar.WindowRounding, S(10));
        F(ImGuiStyleVar.WindowBorderSize, 1f);
        V(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        V(ImGuiStyleVar.WindowTitleAlign, new Vector2(0f, 0.5f));
        // The title bar is the font's line plus twice this: 13px Geist (16.9px) + 2 × 7.55 = 32px.
        var font = fonts.Push(Face.Sans, 13);
        V(ImGuiStyleVar.FramePadding, new Vector2(S(12), MathF.Max(0, (S(32) - ImGui.GetFontSize()) / 2)));
        F(ImGuiStyleVar.ScrollbarSize, S(8));
        F(ImGuiStyleVar.ScrollbarRounding, S(999));
        Pushed.Push((colors, vars, font));
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

        V(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        V(ImGuiStyleVar.ItemInnerSpacing, Vector2.Zero);
        V(ImGuiStyleVar.FramePadding, Vector2.Zero);
        F(ImGuiStyleVar.FrameBorderSize, 0f);
        F(ImGuiStyleVar.ChildBorderSize, 0f);
        F(ImGuiStyleVar.PopupRounding, S(Palette.RadiusLg));
        F(ImGuiStyleVar.PopupBorderSize, 1f);
        V(ImGuiStyleVar.WindowPadding, Vector2.Zero);
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
