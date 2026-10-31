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
            if (!SaveTo(path, session, data, $"Backup {why}", out var failed))
            {
                log.Error($"Couldn't save a backup: {(failed.Length > 0 ? failed : "what's placed couldn't be read")}");
                return null;
            }
            log.Done($"Saved a backup of what's placed ({why}) to {path}.");
            Prune(log);
            return path;
        }
        catch (Exception ex)
        {
            // Never into the run's start: it carries on (and says so) without a backup.
            Services.Log.Error(ex, "Backup failed");
            log.Error($"Couldn't save a backup: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A swap's backup: what's placed now, kept in the backups folder under a name you'll know again
    /// ("Medium house in Mist before Winter Lodge.build", the build named so too), which isn't one
    /// of the backups' own names, so it's never pruned. Returns its path, or null.
    /// </summary>
    public static string? SaveKept(LayoutSession session, GameData data, ActivityLog log, string name)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var stem = SwapNames.FileStem(name);
            var path = Path.Combine(Folder, stem + ".build");
            for (var n = 2; File.Exists(path); n++) path = Path.Combine(Folder, $"{stem} {n}.build");
            if (!SaveTo(path, session, data, name, out var failed))
            {
                log.Error($"Couldn't keep what's here: {(failed.Length > 0 ? failed : "what's placed couldn't be read")}");
                return null;
            }
            log.Done($"Kept what's placed as “{name}” ({path}).");
            return path;
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Keeping a swap's backup failed");
            log.Error($"Couldn't keep what's here: {ex.Message}");
            return null;
        }
    }

    /// <summary>Deletes the plugin's own backups past the newest <see cref="Kept"/> (best effort).</summary>
    private static void Prune(ActivityLog log)
    {
        try
        {
            // Only names a backup gets ("interior-20261030-091500.build", "-2" for a second in the same
            // second): a garden saved there as "garden-2026-10-30" is yours, and stays.
            var old = new DirectoryInfo(Folder).GetFiles("*.build")
                .Where(f => SwapNames.IsBackupName(f.Name))
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
        if (!Layout(session, data, title, out var json, out _, out why)) return false;
        return Write(path, () => json, out why);
    }

    /// <summary>
    /// What's placed where you are (inside, or your garden) as a .build's JSON, read as it is now,
    /// and the site's code for the home (<see cref="SiteLink.HouseCode"/>). False, with why, when it
    /// can't be read. Saving writes it to a file; Send to my builds sends it to the site.
    /// </summary>
    public static bool Layout(LayoutSession session, GameData data, string title, out string json, out string house, out string why)
    {
        json = string.Empty;
        var ctx = session.Context;
        house = SiteLink.HouseCode(ctx.HouseSize, Districts.StyleOfInterior(ctx.TerritoryName) ?? StyleOf(ctx.District));
        if (!session.ReadPlacedNow(out var pieces, out var readPlot, out why)) return false;
        string? Hex(byte stain) => stain == 0 ? null : data.DyeRgb(stain) is { } rgb ? Dyes.HexOfRgb(rgb) : null;
        SavedPiece Saved(LivePiece p, System.Numerics.Vector3 at, float yaw)
        {
            var material = Try(() => data.MaterialItem(p.ItemId, FurnitureReader.Material(p.Instance)), 0u, "a piece's wallpaper");
            return new SavedPiece(p.ItemId, data.ItemName(p.ItemId), at, yaw, Hex(p.Stain),
                material == 0 ? null : new SavedItem(material, data.AnyItemName(material)));
        }
        var fixtures = session.Fixtures;

        if (ctx.Side == Side.Interior)
        {
            var size = ctx.HouseSize;
            var home = new SavedHome(size, Districts.StyleOfInterior(ctx.TerritoryName) ?? StyleOf(ctx.District),
                Try(() => fixtures.ReadInterior(size), [], "the fixtures"),
                size == "Apartment" ? [] : Try(() => fixtures.ExteriorOfHome(ctx), [], "the outside"),
                FixtureReader.PlotOfInterior(),
                FixtureReader.LightOfInterior());
            var inside = pieces.Select(p => Saved(p, p.Position, p.Yaw)).ToList();
            return Json(() => LayoutWriter.Write(home, inside, [], title), out json, out why);
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
        var outside = new SavedHome(ctx.HouseSize, StyleOf(ctx.District), [], Try(() => fixtures.ExteriorOfHome(ctx), [], "the outside"), plot.Number);
        return Json(() => LayoutWriter.Write(outside, [], garden, title), out json, out why);
    }

    private static bool Json(Func<string> write, out string json, out string why)
    {
        why = string.Empty;
        try
        {
            json = write();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            json = string.Empty;
            why = $"Couldn't write it down: {ex.Message}";
            return false;
        }
    }

    /// <summary>The home's fixtures and wallpapers are extras: when one can't be read, the furniture is still saved.</summary>
    private static T Try<T>(Func<T> read, T otherwise, string what)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, $"Couldn't read {what} to save");
            return otherwise;
        }
    }

    private static bool Write(string path, Func<string> json, out string why)
    {
        why = string.Empty;
        // Written beside it first, then moved over it: a write that fails partway (a full disk)
        // leaves the file that was there whole, rather than cut short.
        var temp = path + ".saving";
        try
        {
            File.WriteAllText(temp, json());
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            why = $"Couldn't write the file: {ex.Message}";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Left for the next save to replace.
            }
            return false;
        }
    }

    private static string? StyleOf(string? district) => district == null ? null : Districts.Display(district);
}
