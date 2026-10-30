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
        SavedPiece Saved(LivePiece p, System.Numerics.Vector3 at, float yaw)
        {
            var material = data.MaterialItem(p.ItemId, FurnitureReader.Material(p.Instance));
            return new SavedPiece(p.ItemId, data.ItemName(p.ItemId), at, yaw, Hex(p.Stain),
                material == 0 ? null : new SavedItem(material, data.AnyItemName(material)));
        }
        var fixtures = session.Fixtures;

        if (ctx.Side == Side.Interior)
        {
            var size = ctx.HouseSize;
            var home = new SavedHome(size, Districts.StyleOfInterior(ctx.TerritoryName) ?? StyleOf(ctx.District),
                fixtures.ReadInterior(size), size == "Apartment" ? [] : fixtures.ExteriorOfHome(ctx), FixtureReader.PlotOfInterior());
            var inside = pieces.Select(p => Saved(p, p.Position, p.Yaw)).ToList();
            return Write(path, LayoutWriter.Write(home, inside, [], title), out why);
        }

        if ((readPlot ?? session.Plot) is not { } plot)
        {
            why = "Couldn't tell which plot you're on.";
            return false;
        }
        var garden = pieces.Select(p =>
        {
            var (local, yaw) = Transforms.WardToPlot(p.Position, p.Yaw, plot);
            return Saved(p, local, yaw);
        }).ToList();
        var outside = new SavedHome(ctx.HouseSize, StyleOf(ctx.District), [], fixtures.ExteriorOfHome(ctx), plot.Number);
        return Write(path, LayoutWriter.Write(outside, [], garden, title), out why);
    }

    private static bool Write(string path, string json, out string why)
    {
        why = string.Empty;
        try
        {
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            why = $"Couldn't write the file: {ex.Message}";
            return false;
        }
    }

    private static string? StyleOf(string? district) => district == null ? null : Districts.Display(district);
}
