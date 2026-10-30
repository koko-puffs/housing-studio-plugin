using System;
using System.IO;
using System.Linq;
using Dalamud.Game;
using Dalamud.Interface.ManagedFontAtlas;

namespace HousingStudio.UI;

/// <summary>
/// The site's faces, loaded through Dalamud's font atlas (so they follow Dalamud's global scale and
/// rebuild with it): Geist for everything you read or press, Source Serif 4 (its Subhead cut) for
/// names and headings, and Geist Mono for figures and eyebrows. Both are under the SIL Open Font
/// License 1.1 and ship inside the plugin unmodified (Resources/Fonts, with their licences).
///
/// Game text (item names) in Japanese doesn't exist in Geist, so on a Japanese client the body
/// falls back to Dalamud's own font, which has those glyphs.
/// </summary>
public sealed class Fonts : IDisposable
{
    public IFontHandle Body { get; }
    public IFontHandle BodyMedium { get; }
    public IFontHandle Small { get; }
    public IFontHandle Mono { get; }
    public IFontHandle Heading { get; }
    public IFontHandle Title { get; }

    private readonly bool ownBody;

    public Fonts()
    {
        var atlas = Services.PluginInterface.UiBuilder.FontAtlas;
        ownBody = Services.ClientState.ClientLanguage != ClientLanguage.Japanese;
        Body = ownBody ? Make(atlas, "Geist-Regular.ttf", 15) : Services.PluginInterface.UiBuilder.DefaultFontHandle;
        BodyMedium = ownBody ? Make(atlas, "Geist-Medium.ttf", 14) : Services.PluginInterface.UiBuilder.DefaultFontHandle;
        Small = ownBody ? Make(atlas, "Geist-Regular.ttf", 13) : Services.PluginInterface.UiBuilder.DefaultFontHandle;
        Mono = Make(atlas, "GeistMono-Regular.ttf", 12);
        Heading = Make(atlas, "SourceSerif4Subhead-Semibold.ttf", 21);
        Title = Make(atlas, "SourceSerif4Subhead-It.ttf", 27);
    }

    private static IFontHandle Make(IFontAtlas atlas, string file, float sizePx) =>
        atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var bytes = Read(file);
            var config = new SafeFontConfig { SizePx = sizePx };
            var font = tk.AddFontFromMemory(bytes, config, file);
            // The game's own symbols (the HQ mark, arrows) and whatever Dalamud's language needs.
            tk.AddGameSymbol(new SafeFontConfig { SizePx = sizePx, MergeFont = font });
            tk.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = sizePx, MergeFont = font });
        }));

    private static byte[] Read(string file)
    {
        var asm = typeof(Fonts).Assembly;
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (ownBody)
        {
            Body.Dispose();
            BodyMedium.Dispose();
            Small.Dispose();
        }
        Mono.Dispose();
        Heading.Dispose();
        Title.Dispose();
    }
}
