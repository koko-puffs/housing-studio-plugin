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

    /// <summary>How many of its own backups the folder keeps (the oldest go as new ones come; files you saved there yourself stay).</summary>
    public const int Kept = 50;

    /// <summary>Writes a backup to the plugin's backups folder; returns its path, or null.</summary>
    public static string? Save(LayoutSession session, GameData data, ActivityLog log, string why)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var ctx = session.Context;
            var stem = $"{(ctx.Side == Side.Garden ? "garden" : "interior")}-{DateTime.Now:yyyyMMdd-HHmmss}";
            // Two in the same second (a run stopped and started again at once) mustn't overwrite each other.
            var path = Path.Combine(Folder, stem + ".build");
            for (var n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"{stem}-{n}.build");
            if (!SaveTo(path, session, data, $"Backup {why}")) return null;
            log.Done($"Saved a backup of what's placed ({why}) to {path}.");
            Prune(log);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Couldn't save a backup: {ex.Message}");
            return null;
        }
    }

    /// <summary>Deletes the plugin's own backups past the newest <see cref="Kept"/> (best effort).</summary>
    private static void Prune(ActivityLog log)
    {
        try
        {
            var old = new DirectoryInfo(Folder).GetFiles("*.build")
                .Where(f => f.Name.StartsWith("interior-", StringComparison.Ordinal) || f.Name.StartsWith("garden-", StringComparison.Ordinal))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Kept)
                .ToList();
            foreach (var f in old) f.Delete();
            if (old.Count > 0) log.Info($"Removed {old.Count} old backup{(old.Count == 1 ? "" : "s")} (the newest {Kept} are kept).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn($"Couldn't tidy the old backups: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes what's placed where you are (inside, or your garden) to a file, read as it is now (no
    /// design needed, nor the editor). False, with why, when it can't be read.
    /// </summary>
    public static bool SaveTo(string path, LayoutSession session, GameData data, string title) => SaveTo(path, session, data, title, out _);

    public static bool SaveTo(string path, LayoutSession session, GameData data, string title, out string why)
    {
        var ctx = session.Context;
        if (!session.ReadPlacedNow(out var pieces, out var readPlot, out why)) return false;
        string? Hex(byte stain) => stain == 0 ? null : data.DyeRgb(stain) is { } rgb ? Dyes.HexOfRgb(rgb) : null;

        if (ctx.Side == Side.Interior)
        {
            var inside = pieces.Select(p => new SavedPiece(p.ItemId, data.ItemName(p.ItemId), p.Position, p.Yaw, Hex(p.Stain))).ToList();
            File.WriteAllText(path, LayoutWriter.Write(ctx.HouseSize, Districts.Display(ctx.District), inside, Array.Empty<SavedPiece>(), title));
            return true;
        }

        if ((readPlot ?? session.Plot) is not { } plot)
        {
            why = "Couldn't tell which plot you're on.";
            return false;
        }
        var garden = pieces.Select(p =>
        {
            var (local, yaw) = Transforms.WardToPlot(p.Position, p.Yaw, plot);
            return new SavedPiece(p.ItemId, data.ItemName(p.ItemId), local, yaw, Hex(p.Stain));
        }).ToList();
        File.WriteAllText(path, LayoutWriter.Write(ctx.HouseSize, Districts.Display(ctx.District), Array.Empty<SavedPiece>(), garden, title, plot.Number));
        return true;
    }
}
