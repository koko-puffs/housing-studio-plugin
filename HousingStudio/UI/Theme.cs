using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace HousingStudio.UI;

/// <summary>
/// Pushes Housing Studio's look onto ImGui for one window (and pops it after), the way Lightless
/// and other plugins with their own look do: colours and rounding through ImGui's style stack,
/// custom fonts through Dalamud's font atlas, and the pieces ImGui has no widget for (pills, chips,
/// cards, progress) drawn with draw lists in <see cref="Widgets"/>.
/// </summary>
public sealed class Theme
{
    private int colors;
    private int vars;

    public Fonts Fonts { get; }

    public Theme(Fonts fonts) => Fonts = fonts;

    public static float S(float px) => px * ImGuiHelpers.GlobalScale;

    public void Push()
    {
        Color(ImGuiCol.WindowBg, Palette.Ink);
        Color(ImGuiCol.ChildBg, Palette.WithAlpha(Palette.Ink, 0));
        Color(ImGuiCol.PopupBg, Palette.Float);
        Color(ImGuiCol.Border, Palette.Line);
        Color(ImGuiCol.BorderShadow, Vector4.Zero);
        Color(ImGuiCol.Text, Palette.Text);
        Color(ImGuiCol.TextDisabled, Palette.Dim);
        Color(ImGuiCol.TitleBg, Palette.Ink);
        Color(ImGuiCol.TitleBgActive, Palette.Walnut);
        Color(ImGuiCol.TitleBgCollapsed, Palette.Ink);
        Color(ImGuiCol.FrameBg, Palette.Field);
        Color(ImGuiCol.FrameBgHovered, Palette.Mix(Palette.Field, Palette.Ivory, 0.05f));
        Color(ImGuiCol.FrameBgActive, Palette.Mix(Palette.Field, Palette.Brass, 0.10f));
        Color(ImGuiCol.Button, Palette.WithAlpha(Palette.Panel, 0));
        Color(ImGuiCol.ButtonHovered, Palette.LineFaint);
        Color(ImGuiCol.ButtonActive, Palette.Line);
        Color(ImGuiCol.Header, Palette.BrassWash);
        Color(ImGuiCol.HeaderHovered, Palette.LineFaint);
        Color(ImGuiCol.HeaderActive, Palette.BrassPicked);
        Color(ImGuiCol.CheckMark, Palette.Brass);
        Color(ImGuiCol.SliderGrab, Palette.Brass);
        Color(ImGuiCol.SliderGrabActive, Palette.BrassHover);
        Color(ImGuiCol.Separator, Palette.Line);
        Color(ImGuiCol.SeparatorHovered, Palette.LineHover);
        Color(ImGuiCol.SeparatorActive, Palette.Brass);
        Color(ImGuiCol.ScrollbarBg, Vector4.Zero);
        Color(ImGuiCol.ScrollbarGrab, Palette.WithAlpha(Palette.Hex(0xa89e8e), 0.32f));
        Color(ImGuiCol.ScrollbarGrabHovered, Palette.WithAlpha(Palette.Hex(0xa89e8e), 0.5f));
        Color(ImGuiCol.ScrollbarGrabActive, Palette.Brass);
        Color(ImGuiCol.ResizeGrip, Vector4.Zero);
        Color(ImGuiCol.ResizeGripHovered, Palette.LineHover);
        Color(ImGuiCol.ResizeGripActive, Palette.Brass);
        Color(ImGuiCol.Tab, Palette.Ink);
        Color(ImGuiCol.TabHovered, Palette.LineFaint);
        Color(ImGuiCol.TableHeaderBg, Palette.Walnut);
        Color(ImGuiCol.TableBorderLight, Palette.LineFaint);
        Color(ImGuiCol.TableBorderStrong, Palette.Line);
        Color(ImGuiCol.TableRowBg, Vector4.Zero);
        Color(ImGuiCol.TableRowBgAlt, Palette.WithAlpha(Palette.Ivory, 0.02f));
        Color(ImGuiCol.TextSelectedBg, Palette.WithAlpha(Palette.Brass, 0.3f));
        Color(ImGuiCol.NavHighlight, Palette.BrassSoft);
        Color(ImGuiCol.PlotHistogram, Palette.Brass);

        Var(ImGuiStyleVar.WindowRounding, S(Palette.RadiusXl));
        Var(ImGuiStyleVar.ChildRounding, S(Palette.RadiusLg));
        Var(ImGuiStyleVar.PopupRounding, S(Palette.RadiusLg));
        Var(ImGuiStyleVar.FrameRounding, S(Palette.RadiusMd));
        Var(ImGuiStyleVar.GrabRounding, S(Palette.RadiusPill));
        Var(ImGuiStyleVar.ScrollbarRounding, S(Palette.RadiusPill));
        Var(ImGuiStyleVar.TabRounding, S(Palette.RadiusMd));
        Var(ImGuiStyleVar.WindowPadding, new Vector2(S(Palette.S18), S(Palette.S16)));
        Var(ImGuiStyleVar.FramePadding, new Vector2(S(Palette.S12), S(Palette.S8)));
        Var(ImGuiStyleVar.ItemSpacing, new Vector2(S(Palette.S8), S(Palette.S8)));
        Var(ImGuiStyleVar.ItemInnerSpacing, new Vector2(S(Palette.S8), S(Palette.S6)));
        Var(ImGuiStyleVar.CellPadding, new Vector2(S(Palette.S8), S(Palette.S6)));
        Var(ImGuiStyleVar.ScrollbarSize, S(10));
        Var(ImGuiStyleVar.WindowBorderSize, 1f);
        Var(ImGuiStyleVar.ChildBorderSize, 1f);
        Var(ImGuiStyleVar.FrameBorderSize, 0f);
        Var(ImGuiStyleVar.PopupBorderSize, 1f);
        Var(ImGuiStyleVar.WindowTitleAlign, new Vector2(0f, 0.5f));
    }

    public void Pop()
    {
        if (vars > 0) ImGui.PopStyleVar(vars);
        if (colors > 0) ImGui.PopStyleColor(colors);
        vars = 0;
        colors = 0;
    }

    private void Color(ImGuiCol col, Vector4 value)
    {
        ImGui.PushStyleColor(col, value);
        colors++;
    }

    private void Var(ImGuiStyleVar v, float value)
    {
        ImGui.PushStyleVar(v, value);
        vars++;
    }

    private void Var(ImGuiStyleVar v, Vector2 value)
    {
        ImGui.PushStyleVar(v, value);
        vars++;
    }

    /// <summary>A brass pulse, 0 to 1, for "this is the one" (still when reduced motion is on).</summary>
    public static float Pulse()
    {
        if (Services.PluginInterface.UiBuilder.ShouldUseReducedMotion) return 0.6f;
        return 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 3.2f);
    }
}
