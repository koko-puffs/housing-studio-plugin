using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;
using Lumina.Excel.Sheets;

namespace HousingStudio.Game;

/// <summary>What the game's own data says about furniture, dyes and places (read once, kept).</summary>
public sealed class GameData
{
    /// <summary>ItemUICategory rows for "Tabletop" and "Wall-mounted" furnishings: they go on something else, so last.</summary>
    private const uint TabletopCategory = 78;
    private const uint WallMountedCategory = 79;

    private readonly Dictionary<uint, (string Name, uint Icon, uint UiCategory, ListSortKey Sort)> items = new();
    private readonly Dictionary<uint, uint> indoorFurnitureToItem = new();
    private readonly Dictionary<uint, uint> yardObjectToItem = new();
    private readonly Dictionary<byte, DyeColor> dyesById = new();
    private readonly Dictionary<byte, (byte Shade, byte SubOrder, uint[] Items)> dyeUi = new();

    public IReadOnlyList<DyeColor> HousingDyes { get; }

    public GameData()
    {
        foreach (var row in Services.Data.GetExcelSheet<HousingFurniture>())
            if (row.Item.RowId != 0) indoorFurnitureToItem[row.RowId] = row.Item.RowId;
        foreach (var row in Services.Data.GetExcelSheet<HousingYardObject>())
            if (row.Item.RowId != 0) yardObjectToItem[row.RowId] = row.Item.RowId;

        var wanted = new HashSet<uint>(indoorFurnitureToItem.Values.Concat(yardObjectToItem.Values));
        var sheet = Services.Data.GetExcelSheet<Item>();
        foreach (var id in wanted)
            if (sheet.TryGetRow(id, out var item))
            {
                var ui = item.ItemUICategory.ValueNullable;
                // Unknown4 is the Item sheet's SubcategorySort (between LevelItem and ItemAction; Lumina's
                // Experimental sheet names it): Furnishing 8230 … 42000, Table 2000 … 4230 in a user's list.
                var sort = new ListSortKey(ui?.OrderMajor ?? 0, ui?.OrderMinor ?? 0, item.Unknown4, id);
                items[id] = (item.Name.ExtractText(), item.Icon, item.ItemUICategory.RowId, sort);
            }

        var dyes = new List<DyeColor>();
        foreach (var stain in Services.Data.GetExcelSheet<Stain>())
        {
            if (stain.RowId == 0 || stain.RowId > byte.MaxValue) continue;
            var dye = new DyeColor((byte)stain.RowId, stain.Color & 0xFFFFFF, stain.Name.ExtractText());
            dyesById[dye.StainId] = dye;
            var dyeItems = new List<uint>();
            foreach (var item in stain.Item)
                if (item.RowId != 0) dyeItems.Add(item.RowId);
            dyeUi[dye.StainId] = (stain.Shade, stain.SubOrder, dyeItems.ToArray());
            if (stain.IsHousingApplicable && !string.IsNullOrEmpty(dye.Name)) dyes.Add(dye);
        }
        HousingDyes = dyes;
    }

    public string ItemName(uint itemId) =>
        items.TryGetValue(itemId, out var i) && !string.IsNullOrEmpty(i.Name) ? i.Name : $"Item {itemId}";

    /// <summary>Where the item sorts in the furnishing list's Storeroom tab (<see cref="ListSortKey"/>).</summary>
    public ListSortKey SortKey(uint itemId) => items.TryGetValue(itemId, out var i) ? i.Sort : new ListSortKey(int.MaxValue, 0, 0, itemId);

    public uint ItemIcon(uint itemId) => items.TryGetValue(itemId, out var i) ? i.Icon : 0;

    /// <summary>Is this an item the game lets you place (inside or in a garden)?</summary>
    public bool IsFurnishing(uint itemId) => items.ContainsKey(itemId);

    /// <summary>Tabletop and wall-mounted pieces stand on (or hang from) something else.</summary>
    public bool GoesOnSomethingElse(uint itemId) =>
        items.TryGetValue(itemId, out var i) && i.UiCategory is TabletopCategory or WallMountedCategory;

    /// <summary>The Item behind a placed piece (its HousingFurniture or HousingYardObject row).</summary>
    public uint ItemOfPlaced(uint baseId, bool yard) =>
        (yard ? yardObjectToItem : indoorFurnitureToItem).TryGetValue(baseId, out var item) ? item : 0;

    public string DyeName(byte stainId) =>
        stainId == 0 ? "Undyed" : dyesById.TryGetValue(stainId, out var d) && d.Name.Length > 0 ? d.Name : $"Dye {stainId}";

    /// <summary>The items that apply a dye (from the Stain sheet).</summary>
    public IReadOnlyList<uint> DyeItems(byte stainId) => dyeUi.TryGetValue(stainId, out var d) ? d.Items : System.Array.Empty<uint>();

    /// <summary>Where a dye sits in the dye window: its shade (the row of circles) and its place in it.</summary>
    public (byte Shade, byte SubOrder)? DyeSlot(byte stainId) => dyeUi.TryGetValue(stainId, out var d) ? (d.Shade, d.SubOrder) : null;

    public uint? DyeRgb(byte stainId) => dyesById.TryGetValue(stainId, out var d) ? d.Rgb : null;

    /// <summary>The game dye for a file's colour, 0 when undyed.</summary>
    public byte StainOf(string? hex)
    {
        if (hex == null) return 0;
        return Dyes.Closest(hex, HousingDyes)?.StainId ?? 0;
    }

    private static readonly Dictionary<uint, string> TerritoryNames = new();

    /// <summary>A TerritoryType's short name ("s1i2", "r1h1"), or an empty string.</summary>
    public static string TerritoryName(uint territoryId)
    {
        if (TerritoryNames.TryGetValue(territoryId, out var name)) return name;
        name = Services.Data.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var row) ? row.Name.ExtractText() : string.Empty;
        TerritoryNames[territoryId] = name;
        return name;
    }
}
