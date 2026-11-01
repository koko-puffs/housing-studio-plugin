using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game;
using Dalamud.Interface.ManagedFontAtlas;

namespace HousingStudio.UI;

/// <summary>The design system's three faces, by weight.</summary>
public enum Face
{
    /// <summary>Geist 400: what you read.</summary>
    Sans,

    /// <summary>Geist 500: buttons, names in lists.</summary>
    SansMedium,

    /// <summary>Geist 600: the brass button's words.</summary>
    SansSemibold,

    /// <summary>Geist Mono 400: eyebrows, counts, figures, times.</summary>
    Mono,

    /// <summary>Source Serif 4 400: names and headings (the site's serif, 0.9.1).</summary>
    Serif,

    /// <summary>Source Serif 4 400 italic: a title's one italic word.</summary>
    SerifItalic,
}

/// <summary>
/// The faces at the sizes the mockups use, loaded through Dalamud's font atlas (so they follow
/// Dalamud's global scale and rebuild with it): Geist 15/14/13/12 (400), 15/14/13 (500), 15/14
/// (600); Geist Mono 13/12/11/10 (10: the Beta tag); Source Serif 4 56/32/28/26/22 and 32 italic (400, its 32 px optical size,
/// as the site has it since it left the mockups' Cormorant Garamond). All
/// under the SIL Open Font License 1.1, unmodified, with their licences (Resources/Fonts).
///
/// Sizes are CSS sizes (the em). ImGui sizes a font by its line (ascent to descent), so each face
/// is loaded at its em times its line ratio, and 14px Geist here is as tall as 14px in the browser.
///
/// On a Japanese client the sans sizes are Dalamud's own font (it has the kana and kanji that item
/// names need); the serif and mono keep the design's faces.
/// </summary>
public sealed class Fonts : IDisposable
{
    /// <summary>(hhea ascent − descent) / em, read from the font files.</summary>
    private const float GeistLine = 1.3f;
    private const float SerifLine = 1.371f;

    /// <summary>Latin, punctuation, currency, letterlike symbols and arrows: what names and headings need.</summary>
    private static readonly ushort[] SerifGlyphs = { 0x0020, 0x024F, 0x2000, 0x206F, 0x20A0, 0x20CF, 0x2100, 0x214F, 0x2190, 0x21FF, 0 };

    /// <summary>The big figure only ever shows digits.</summary>
    private static readonly ushort[] FigureGlyphs = { 0x0020, 0x007E, 0x00A0, 0x00FF, 0x2000, 0x206F, 0 };

    private static readonly (Face Face, int Px)[] Wanted =
    {
        (Face.Sans, 15), (Face.Sans, 14), (Face.Sans, 13), (Face.Sans, 12),
        (Face.SansMedium, 15), (Face.SansMedium, 14), (Face.SansMedium, 13),
        (Face.SansSemibold, 15), (Face.SansSemibold, 14), (Face.SansSemibold, 13),
        (Face.Mono, 13), (Face.Mono, 12), (Face.Mono, 11), (Face.Mono, 10),
        (Face.Serif, 56), (Face.Serif, 32), (Face.Serif, 28), (Face.Serif, 26), (Face.Serif, 22),
        (Face.SerifItalic, 32), (Face.SerifItalic, 26),
    };

    private readonly Dictionary<(Face, int), IFontHandle> handles = new();

    public Fonts()
    {
        var atlas = Services.PluginInterface.UiBuilder.FontAtlas;
        var japanese = Services.ClientState.ClientLanguage == ClientLanguage.Japanese;
        foreach (var (face, px) in Wanted)
        {
            var file = face switch
            {
                Face.Sans => "Geist-Regular.ttf",
                Face.SansMedium => "Geist-Medium.ttf",
                Face.SansSemibold => "Geist-SemiBold.ttf",
                Face.Mono => "GeistMono-Regular.ttf",
                Face.Serif => "SourceSerif4-Regular.ttf",
                _ => "SourceSerif4-Italic.ttf",
            };
            var serif = face is Face.Serif or Face.SerifItalic;
            var line = serif ? SerifLine : GeistLine;
            var glyphs = serif ? px >= 56 ? FigureGlyphs : SerifGlyphs : null;
            var dalamudFont = japanese && face is Face.Sans or Face.SansMedium or Face.SansSemibold;
            handles[(face, px)] = Make(atlas, file, px * line, glyphs, dalamudFont);
        }
    }

    /// <summary>Pushes a face at a CSS size (the nearest loaded size when that one isn't).</summary>
    public IDisposable Push(Face face, int px) => Handle(face, px).Push();

    public IFontHandle Handle(Face face, int px)
    {
        if (handles.TryGetValue((face, px), out var h)) return h;
        var best = handles.Keys.Where(k => k.Item1 == face).OrderBy(k => Math.Abs(k.Item2 - px)).FirstOrDefault();
        return handles.TryGetValue(best, out h) ? h : handles[(Face.Sans, 14)];
    }

    private static IFontHandle Make(IFontAtlas atlas, string file, float sizePx, ushort[]? glyphs, bool dalamudFont) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var font = dalamudFont
                ? tk.AddDalamudDefaultFont(sizePx, null)
                : tk.AddFontFromMemory(Read(file), new SafeFontConfig { SizePx = sizePx, GlyphRanges = glyphs }, file);
            // The game's own symbols (the HQ mark, arrows) and whatever Dalamud's language needs.
            tk.AddGameSymbol(new SafeFontConfig { SizePx = sizePx, MergeFont = font });
            tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = sizePx, MergeFont = font });
        }));

    private static readonly Dictionary<string, byte[]> Files = new();

    private static byte[] Read(string file)
    {
        lock (Files)
        {
            if (Files.TryGetValue(file, out var cached)) return cached;
            var asm = typeof(Fonts).Assembly;
            var name = asm.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.Ordinal));
            using var s = asm.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return Files[file] = ms.ToArray();
        }
    }

    public void Dispose()
    {
        foreach (var h in handles.Values) h.Dispose();
        handles.Clear();
    }
}
