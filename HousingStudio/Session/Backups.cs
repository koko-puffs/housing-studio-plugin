using System;
using System.IO;
using System.Linq;
using HousingStudio.Core;
using HousingStudio.Game;

namespace HousingStudio.Session;

/// <summary>
/// Saves what's placed where you are as a .build (the same JSON as MakePlace's files), so you can
/// load it here again to put everything back, open it in MakePlace, or upload it to Housing Studio.
/// </summary>
public static class Backups
{
    public static string Folder => Path.Combine(Services.PluginInterface.ConfigDirectory.FullName, "backups");

    /// <summary>Writes a backup to the plugin's backups folder; returns its path, or null.</summary>
    public static string? Save(LayoutSession session, GameData data, ActivityLog log, string why)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var ctx = session.Context;
            var name = $"{(ctx.Side == Side.Garden ? "garden" : "interior")}-{DateTime.Now:yyyyMMdd-HHmmss}.build";
            var path = Path.Combine(Folder, name);
            if (!SaveTo(path, session, data, $"Backup {why}")) return null;
            log.Done($"Saved a backup of what's placed ({why}) to {path}.");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Couldn't save a backup: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes what's placed where you are (inside, or your garden) to a file.</summary>
    public static bool SaveTo(string path, LayoutSession session, GameData data, string title)
    {
        var ctx = session.Context;
        if (ctx.Side == null) return false;
        string? Hex(byte stain) => stain == 0 ? null : data.DyeRgb(stain) is { } rgb ? Dyes.HexOfRgb(rgb) : null;

        var pieces = session.Live.Values.OrderBy(p => p.Key).ToList();
        if (ctx.Side == Side.Interior)
        {
            var inside = pieces.Select(p => new SavedPiece(p.ItemId, data.ItemName(p.ItemId), p.Position, p.Yaw, Hex(p.Stain))).ToList();
            File.WriteAllText(path, LayoutWriter.Write(ctx.HouseSize, Districts.Display(ctx.District), inside, Array.Empty<SavedPiece>(), title));
            return true;
        }

        if (session.Plot is not { } plot) return false;
        var garden = pieces.Select(p =>
        {
            var (local, yaw) = Transforms.WardToPlot(p.Position, p.Yaw, plot);
            return new SavedPiece(p.ItemId, data.ItemName(p.ItemId), local, yaw, Hex(p.Stain));
        }).ToList();
        File.WriteAllText(path, LayoutWriter.Write(ctx.HouseSize, Districts.Display(ctx.District), Array.Empty<SavedPiece>(), garden, title, plot.Number));
        return true;
    }
}
