using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HousingStudio.UI;

/// <summary>
/// Housing Studio's colours, exactly as the design system's tokens.json (<c>hs-*</c>). Translucent
/// tokens keep their alpha (ImGui blends them over what's beneath, as the browser does), except the
/// glass of panels, which ImGui can't blur: it's drawn as its colour over ink, solid.
/// </summary>
public static class Palette
{
    // The room.
    public static readonly Vector4 Ink = Hex(0x0e0c0b);                    // hs-ink
    public static readonly Vector4 Walnut = Hex(0x17130f);                 // hs-walnut: footers, the title bar
    public static readonly Vector4 Glass = Over(Rgba(30, 26, 22, 0.55f), Ink);      // hs-glass, over ink (#171411)
    public static readonly Vector4 GlassLive = Rgba(30, 26, 22, 0.55f);    // hs-glass as is, for glass buttons over anything
    public static readonly Vector4 GlassSolid = Over(Rgba(30, 26, 22, 0.86f), Ink); // hs-glass-solid: dialogs
    public static readonly Vector4 Float = Rgba(23, 19, 15, 0.94f);        // hs-float: menus, tooltips, toasts
    public static readonly Vector4 Well = Rgba(255, 255, 255, 0.03f);      // hs-well: inside fields and switches
    public static readonly Vector4 Photo = Hex(0x2a2119);                  // hs-photo: thumbnails, file badges

    // Words, brightest to quietest.
    public static readonly Vector4 Ivory = Hex(0xefe6d6);                  // hs-ivory
    public static readonly Vector4 Text = Hex(0xc9bfae);                   // hs-text
    public static readonly Vector4 Ash = Hex(0xa89e8e);                    // hs-ash
    public static readonly Vector4 Dim = Hex(0x8f8576);                    // hs-dim

    // Brass, the one accent.
    public static readonly Vector4 Brass = Hex(0xc9a96e);                  // hs-brass
    public static readonly Vector4 BrassHover = Hex(0xd8bb85);             // hs-brass-hover
    public static readonly Vector4 BrassSoft = Hex(0xe9d3a6);              // hs-brass-soft
    public static readonly Vector4 OnBrass = Hex(0x1a140c);                // hs-on-brass
    public static readonly Vector4 BrassWash = Rgba(201, 169, 110, 0.12f); // hs-brass-wash
    public static readonly Vector4 BrassPicked = Rgba(201, 169, 110, 0.18f); // hs-brass-picked
    public static readonly Vector4 BrassNow = Rgba(201, 169, 110, 0.08f);  // the phase card in hand (mockup: .phase.is-now)
    public static readonly Vector4 BrassEdge = Rgba(201, 169, 110, 0.6f);  // a picked chip's and the phase in hand's edge

    // States.
    public static readonly Vector4 Danger = Hex(0xe59a8a);                 // hs-danger
    public static readonly Vector4 DangerSolid = Hex(0xc9705e);            // hs-danger-solid
    public static readonly Vector4 OnDanger = Hex(0x1a0f0b);               // hs-on-danger
    public static readonly Vector4 DangerWash = Rgba(229, 154, 138, 0.08f); // hs-danger-wash
    public static readonly Vector4 DangerEdge = Rgba(229, 154, 138, 0.4f); // a danger button's resting edge (mockup: .btn-danger)
    public static readonly Vector4 Success = Hex(0x9fd18f);                // hs-success
    public static readonly Vector4 SuccessWash = Rgba(110, 160, 110, 0.16f); // hs-success-wash

    /// <summary>The garden's blueprint blue, kept for "from your bags" markers in the world.</summary>
    public static readonly Vector4 Blueprint = Hex(0xa9c4e2);

    // Lines: ivory at fixed strengths.
    public static readonly Vector4 LineFaint = Rgba(239, 230, 214, 0.06f); // hs-line-faint: dividers, hover fills
    public static readonly Vector4 LinePicked = Rgba(239, 230, 214, 0.08f); // hs-line-picked
    public static readonly Vector4 Line = Rgba(239, 230, 214, 0.12f);      // hs-line: panels, cards, chips
    public static readonly Vector4 LineStrong = Rgba(239, 230, 214, 0.2f); // hs-line-strong: things you press or type in
    public static readonly Vector4 LineHover = Rgba(239, 230, 214, 0.34f); // hs-line-hover: any edge pointed at
    public static readonly Vector4 LineDashed = Rgba(239, 230, 214, 0.28f); // hs-line-dashed: things you can add
    public static readonly Vector4 RowHover = Rgba(239, 230, 214, 0.04f);  // a piece row pointed at (mockup: .piece:hover)
    public static readonly Vector4 GlassEdge = Rgba(255, 255, 255, 0.05f); // shadow-glass-edge: glass's lit top edge
    public static readonly Vector4 Track = Rgba(239, 230, 214, 0.08f);     // the progress bar's track

    // Radii (CSS px, before Dalamud's global scale).
    public const float RadiusSm = 8;    // radius-sm: thumbnails, file badges, menu rows
    public const float RadiusMd = 10;   // radius-md: fields, rows you can press
    public const float RadiusLg = 14;   // radius-lg: menus, notices
    public const float RadiusXl = 16;   // radius-xl: panels
    public const float Radius2Xl = 20;  // radius-2xl: dialogs, toasts
    public const float RadiusCard = 12; // the phase and tally cards (mockup: .phase, .cell)
    public const float RadiusPill = 999; // radius-pill: buttons, chips, bars

    // Control heights.
    public const float ControlSm = 34;
    public const float ControlMd = 40;
    public const float ControlLg = 46;

    /// <summary>Everything drawn is multiplied by this (0.5 for a disabled button, 0.4 for a small control).</summary>
    public static float Opacity { get; set; } = 1f;

    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 Rgba(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    /// <summary>A translucent colour flattened over an opaque one.</summary>
    public static Vector4 Over(Vector4 top, Vector4 under) =>
        new(under.X + (top.X - under.X) * top.W, under.Y + (top.Y - under.Y) * top.W, under.Z + (top.Z - under.Z) * top.W, 1f);

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(new Vector4(c.X, c.Y, c.Z, c.W * Opacity));
}
