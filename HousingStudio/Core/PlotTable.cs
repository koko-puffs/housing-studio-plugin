using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HousingStudio.Core;

/// <summary>A plot's centre in its ward and the way it faces (radians about +y, the game's way).</summary>
/// <param name="Size">"s", "m" or "l".</param>
public readonly record struct PlotSpot(int Number, string Size, float X, float Y, float Z, float Turn);

/// <summary>
/// Every district's plots (Data/plots.json, made by tools/make-plots.mjs from Housing Studio's own
/// extraction of the game's ward layouts), to turn a garden's plot-local coordinates into the
/// ward's and back.
/// </summary>
public sealed class PlotTable
{
    private readonly Dictionary<string, (string Ward, Dictionary<int, PlotSpot> Plots)> districts;

    private PlotTable(Dictionary<string, (string, Dictionary<int, PlotSpot>)> districts) => this.districts = districts;

    public static PlotTable Load(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, (string, Dictionary<int, PlotSpot>)>(StringComparer.Ordinal);
        foreach (var d in doc.RootElement.GetProperty("districts").EnumerateObject())
        {
            var ward = d.Value.GetProperty("ward").GetString() ?? string.Empty;
            var plots = new Dictionary<int, PlotSpot>();
            foreach (var p in d.Value.GetProperty("plots").EnumerateArray())
            {
                var n = p.GetProperty("n").GetInt32();
                plots[n] = new PlotSpot(
                    n,
                    p.GetProperty("size").GetString() ?? "s",
                    p.GetProperty("x").GetSingle(),
                    p.GetProperty("y").GetSingle(),
                    p.GetProperty("z").GetSingle(),
                    p.GetProperty("turn").GetSingle());
            }
            result[d.Name] = (ward, plots);
        }
        return new PlotTable(result);
    }

    /// <summary>The bundled table (an embedded resource of the plugin's assembly).</summary>
    public static PlotTable LoadEmbedded()
    {
        var asm = typeof(PlotTable).Assembly;
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("plots.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        return Load(s);
    }

    public IEnumerable<string> DistrictKeys => districts.Keys;

    /// <summary>The district whose ward has this TerritoryType name ("s1h1" is Mist), or null.</summary>
    public string? DistrictOfWard(string territoryName)
    {
        foreach (var (key, (ward, _)) in districts)
            if (string.Equals(ward, territoryName, StringComparison.OrdinalIgnoreCase)) return key;
        return null;
    }

    public bool TryGet(string district, int plotNumber, out PlotSpot plot)
    {
        plot = default;
        return districts.TryGetValue(district, out var d) && d.Plots.TryGetValue(plotNumber, out plot);
    }
}
