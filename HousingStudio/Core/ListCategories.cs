using System;

namespace HousingStudio.Core;

/// <summary>
/// The furnishing list's category filter (its words in the window: Display All, Indoor Furnishings,
/// Tables, Tabletop, Wall-mounted, Rugs), and which one a piece is in, from its item's UI category
/// (Lumina's ItemUICategory: "Furnishing", "Table", "Tabletop", "Wall-mounted", "Rug"). Filtering the
/// list to a piece's category would make it short enough to read every name. The click that picks a
/// filter isn't known yet (UiTable.ListCategory): a recording of choosing one gives it. Pure, tested.
/// </summary>
public static class ListCategories
{
    public static readonly string[] Filters = { "Display All", "Indoor Furnishings", "Tables", "Tabletop", "Wall-mounted", "Rugs" };

    /// <summary>The filter's position in the list's category menu for an item's UI category name.</summary>
    public static int FilterFor(string uiCategoryName)
    {
        var c = (uiCategoryName ?? string.Empty).Trim();
        if (c.Equals("Tabletop", StringComparison.OrdinalIgnoreCase)) return 3;
        if (c.StartsWith("Wall", StringComparison.OrdinalIgnoreCase)) return 4;
        if (c.StartsWith("Rug", StringComparison.OrdinalIgnoreCase)) return 5;
        if (c.StartsWith("Table", StringComparison.OrdinalIgnoreCase)) return 2;
        return 1;
    }
}
