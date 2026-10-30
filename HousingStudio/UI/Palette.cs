using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HousingStudio.UI;

/// <summary>
/// Housing Studio's colours, from the site's tokens (src/features/housing-studio/styles/studio.css,
/// <c>--hs-*</c>): a lamplit desk, dark and warm, with brass for the one thing to do. Translucent
/// tokens are flattened over the ink they sit on, since ImGui windows are drawn opaque here.
/// </summary>
public static class Palette
{
    // Surfaces: the room, a raised panel, a field to type in.
    public static readonly Vector4 Ink = Hex(0x0e0c0b);             // --hs-ink
    public static readonly Vector4 Walnut = Hex(0x17130f);          // --hs-walnut
    public static readonly Vector4 Panel = Hex(0x171411);           // --hs-glass (rgb 30 26 22 / .55) over ink
    public static readonly Vector4 PanelSolid = Hex(0x1c1814);      // --hs-glass-solid (rgb 30 26 22 / .86)
    public static readonly Vector4 Float = Hex(0x17130f, 0.97f);    // --hs-float, menus and tooltips
    public static readonly Vector4 Field = Hex(0x0b0908);           // --hs-field (black / .22) over a panel
    public static readonly Vector4 Photo = Hex(0x2a2119);           // --hs-photo, icon wells

    // Words.
    public static readonly Vector4 Ivory = Hex(0xf2e8d6);           // headings, names
    public static readonly Vector4 Text = Hex(0xd2c6b2);            // what you read
    public static readonly Vector4 Ash = Hex(0xa99c88);             // quieter
    public static readonly Vector4 Dim = Hex(0x8f8472);             // quietest

    // Brass: the one thing to do.
    public static readonly Vector4 Brass = Hex(0xd6b06c);
    public static readonly Vector4 BrassHover = Hex(0xe2c07f);
    public static readonly Vector4 BrassSoft = Hex(0xecd3a2);
    public static readonly Vector4 OnBrass = Hex(0x1d150b);
    public static readonly Vector4 BrassWash = Mix(Panel, Brass, 0.10f);   // --hs-brass-wash
    public static readonly Vector4 BrassPicked = Mix(Panel, Brass, 0.16f); // --hs-brass-picked

    // States.
    public static readonly Vector4 Danger = Hex(0xe59a8a);
    public static readonly Vector4 DangerSolid = Hex(0xc9705e);
    public static readonly Vector4 OnDanger = Hex(0x1a0f0b);
    public static readonly Vector4 DangerWash = Mix(Panel, Hex(0xe59a8a), 0.08f);
    public static readonly Vector4 Success = Hex(0x9fd18f);
    public static readonly Vector4 SuccessWash = Mix(Panel, Hex(0x6ea06e), 0.16f);
    public static readonly Vector4 Blueprint = Hex(0xa9c4e2);       // --hs-blueprint-soft, for "from your bags"

    // Lines (ivory at a few strengths, flattened over a panel).
    public static readonly Vector4 LineFaint = Mix(Panel, Hex(0xf2e8d6), 0.06f);
    public static readonly Vector4 Line = Mix(Panel, Hex(0xefe6d6), 0.12f);
    public static readonly Vector4 LineStrong = Mix(Panel, Hex(0xefe6d6), 0.20f);
    public static readonly Vector4 LineHover = Mix(Panel, Hex(0xf2e8d6), 0.34f);

    // Radii (px, before Dalamud's global scale), by material, as --radius-*.
    public const float RadiusSm = 8;
    public const float RadiusMd = 10;
    public const float RadiusLg = 14;
    public const float RadiusXl = 16;
    public const float Radius2Xl = 22;
    public const float RadiusPill = 999;

    // Spacing steps, as --space-*.
    public const float S4 = 4, S6 = 6, S8 = 8, S10 = 10, S12 = 12, S14 = 14, S16 = 16, S18 = 18, S24 = 24, S32 = 32;

    // Control heights: --control-sm / -md / -lg.
    public const float ControlSm = 34;
    public const float ControlMd = 40;
    public const float ControlLg = 46;

    public static Vector4 Hex(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 Mix(Vector4 under, Vector4 over, float amount) =>
        new(under.X + (over.X - under.X) * amount, under.Y + (over.Y - under.Y) * amount, under.Z + (over.Z - under.Z) * amount, 1f);

    public static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    public static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
}
