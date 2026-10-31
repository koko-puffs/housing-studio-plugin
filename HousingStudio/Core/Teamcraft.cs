using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HousingStudio.Core;

/// <summary>A recipe that makes an item (from the game's RecipeLookup).</summary>
/// <param name="Job">The crafter's job: "Carpenter".</param>
/// <param name="Level">The job level the recipe asks, 0 when not known.</param>
public sealed record CraftOption(uint ItemId, uint RecipeId, string Job, int Level);

/// <summary>
/// FFXIV Teamcraft's list import: <c>https://ffxivteamcraft.com/import/&lt;base64&gt;</c>, the base64 of
/// <c>itemId,recipeId,amount</c> lines joined by <c>;</c> (recipeId <c>null</c> lets Teamcraft choose),
/// as Allagan Tools and Artisan export. Opening it asks Teamcraft to make a list of those items, with
/// everything they're crafted from.
/// </summary>
public static class Teamcraft
{
    public const string ImportBase = "https://ffxivteamcraft.com/import/";

    /// <summary>The import text: "5333,null,2;5334,null,1" (each item once, its counts added, in id order).</summary>
    public static string ImportText(IEnumerable<(uint ItemId, int Count)> items) =>
        string.Join(";", items.Where(i => i.ItemId != 0 && i.Count > 0)
            .GroupBy(i => i.ItemId)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key},null,{g.Sum(i => i.Count)}"));

    /// <summary>The link that opens the import in Teamcraft, or null when there's nothing to craft.</summary>
    public static string? ImportUrl(IEnumerable<(uint ItemId, int Count)> items)
    {
        var text = ImportText(items);
        return text.Length == 0 ? null : ImportBase + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }
}
