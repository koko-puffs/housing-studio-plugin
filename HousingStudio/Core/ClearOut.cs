using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>
/// Clearing out: the pieces placed in a home that a design doesn't use (<see cref="Plan.Extras"/>),
/// put away in the storeroom before the design is placed. A run's filters count for them as for the
/// design's pieces: the floors switched off and what "Near me" leaves out stay where they are. Pure,
/// so it's tested.
/// </summary>
public static class ClearOut
{
    /// <summary>
    /// The extras a run puts away: inside, those on the floors shown (by their height, as the
    /// design's pieces are); within <paramref name="reach"/> yalms of <paramref name="me"/> when
    /// Near me is on (a null <paramref name="me"/> or a reach of 0 leaves nothing out, as for the
    /// design's pieces). In the garden every floor is the garden.
    /// </summary>
    public static List<PlacedPiece> Pick(IEnumerable<PlacedPiece> extras, Side side, string? houseSize, Func<Floor, bool> floorShown, Vector3? me, float reach)
    {
        var list = new List<PlacedPiece>();
        foreach (var p in extras)
        {
            var floor = side == Side.Garden ? Floor.Garden : Floors.Of(p.Position.Y, houseSize);
            if (!floorShown(floor)) continue;
            if (me is { } at && reach > 0 && Vector3.Distance(at, p.Position) > reach) continue;
            list.Add(p);
        }
        return list;
    }
}

/// <summary>
/// The names a swap keeps what was in a home under ("Medium house in Mist before Winter Lodge"), so
/// its backup is found again in Recent to swap back. Pure, so it's tested.
/// </summary>
public static class SwapNames
{
    /// <summary>
    /// The home in words: "Medium house in Mist, plot 12", "Apartment in the Goblet", "Garden in the
    /// Lavender Beds, plot 3". Unknown parts are left out.
    /// </summary>
    public static string Home(Side side, string? houseSize, string? districtName, int? plot)
    {
        var what = side == Side.Garden ? "Garden"
            : houseSize switch
            {
                "Apartment" => "Apartment",
                null or "" => "House",
                _ => $"{houseSize} house",
            };
        var where = string.IsNullOrWhiteSpace(districtName) ? string.Empty : $" in {districtName}";
        var number = plot is { } n && houseSize != "Apartment" ? $", plot {n}" : string.Empty;
        return what + where + number;
    }

    /// <summary>"Medium house in Mist before Winter Lodge".</summary>
    public static string KeepAs(string home, string design) => $"{home} before {(string.IsNullOrWhiteSpace(design) ? "a swap" : design.Trim())}";

    /// <summary>
    /// A file name for it (no extension): the characters a file name can't hold become spaces, runs
    /// of spaces one, and it's kept to 80 characters. Never one of the backups' own names, which are
    /// pruned (they start "interior-" or "garden-" and a date).
    /// </summary>
    public static string FileStem(string name)
    {
        var bad = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
        var chars = name.Select(c => bad.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray();
        var stem = string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (stem.Length > 80) stem = stem[..80].TrimEnd(' ', '.');
        return stem.Length == 0 ? "Home before a swap" : stem;
    }
}
