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
    private readonly HashSet<uint> dyeable = new();
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
                if (item.DyeCount > 0) dyeable.Add(id);
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

    /// <summary>The game lets this furnishing be dyed (its Item row has a dye channel).</summary>
    public bool Dyeable(uint itemId) => dyeable.Contains(itemId);

    public uint ItemIcon(uint itemId) => items.TryGetValue(itemId, out var i) ? i.Icon : 0;

    /// <summary>Is this an item the game lets you place (inside or in a garden)?</summary>
    public bool IsFurnishing(uint itemId) => items.ContainsKey(itemId);

    /// <summary>Tabletop and wall-mounted pieces stand on (or hang from) something else.</summary>
    public bool GoesOnSomethingElse(uint itemId) =>
        items.TryGetValue(itemId, out var i) && i.UiCategory is TabletopCategory or WallMountedCategory;

    /// <summary>
    /// Whether the item goes on this side: an indoor furnishing (a HousingFurniture row) inside, an
    /// outdoor one (HousingYardObject) in the garden. An item the game doesn't list either way counts as yes.
    /// </summary>
    public bool PlaceableOn(uint itemId, Side side)
    {
        sides ??= (indoorFurnitureToItem.Values.ToHashSet(), yardObjectToItem.Values.ToHashSet());
        var (inside, outside) = sides.Value;
        if (!inside.Contains(itemId) && !outside.Contains(itemId)) return true;
        return side == Side.Interior ? inside.Contains(itemId) : outside.Contains(itemId);
    }

    private (HashSet<uint> Inside, HashSet<uint> Outside)? sides;

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

    /// <summary>
    /// The fixture tables: read in the background once the plugin starts (the whole Item sheet is
    /// gone through), so saving a home doesn't hitch the game; a save before that waits for them. A
    /// sheet that can't be read leaves its table empty (logged once), rather than being read again.
    /// </summary>
    private readonly System.Lazy<Fixtures> fixtures = new(() => new Fixtures(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private Fixtures FixtureTables => fixtures.Value;

    /// <summary>Whether the fixture tables are read (the ward's outsides are only kept once they are).</summary>
    public bool FixturesReady => fixtures.IsValueCreated;

    /// <summary>Starts reading the fixture tables off the framework thread.</summary>
    public void WarmUp() => System.Threading.Tasks.Task.Run(() =>
    {
        try { _ = fixtures.Value; }
        catch (System.Exception ex) { Services.Log.Warning(ex, "Couldn't read the fixture tables yet"); }
    });

    /// <summary>The fixtures' item UI categories (65 Exterior Fixtures … 75).</summary>
    public const uint FixtureCategoryFirst = 65;

    public const uint FixtureCategoryLast = 75;

    /// <summary>Whether an item UI category is a fixture's.</summary>
    public static bool IsFixtureCategory(uint category) => category is >= FixtureCategoryFirst and <= FixtureCategoryLast;

    /// <summary>The item a fixture key is (a HousingExterior or HousingInterior row, as the game keeps them), or 0.</summary>
    public uint FixtureItem(uint key) => key != 0 && FixtureTables.ItemOfKey.TryGetValue(key, out var item) ? item : 0;

    /// <summary>The all-in-one exterior (a themed house, "Chocobo Cottage Walls") a part's key belongs to, or 0.</summary>
    public uint UnitedOf(uint partKey) => partKey != 0 && FixtureTables.UnitedOfPart.TryGetValue(partKey, out var item) ? item : 0;

    /// <summary>Whether an item is an all-in-one exterior (a themed house).</summary>
    public bool IsUnited(uint item) => item != 0 && FixtureTables.PartsOfUnited.ContainsKey(item);

    /// <summary>Whether a part's key is one of an all-in-one exterior's own parts.</summary>
    public bool IsPartOf(uint unitedItem, uint partKey) =>
        FixtureTables.PartsOfUnited.TryGetValue(unitedItem, out var parts) && parts.Contains(partKey);

    /// <summary>
    /// The item a piece's material is (a partition's wallpaper, a picture frame's painting, an angler's
    /// canvas's fish print), from its material number (the VaseFlower sheet's), or 0.
    /// </summary>
    public uint MaterialItem(uint itemId, ushort material)
    {
        if (material == 0) return 0;
        var t = FixtureTables;
        var map = itemId switch
        {
            28931 => t.Fishprints[0],
            28932 => t.Fishprints[1],
            28933 => t.Fishprints[2],
            28934 => t.Fishprints[3],
            >= 16935 and <= 16937 => t.Paintings,
            _ => t.Wallpapers,
        };
        return map.TryGetValue(material, out var item) ? item : 0;
    }

    /// <summary>Any item's name (fixtures, wallpapers and the like as well as furnishings).</summary>
    public string AnyItemName(uint itemId)
    {
        if (items.TryGetValue(itemId, out var i) && !string.IsNullOrEmpty(i.Name)) return i.Name;
        if (otherNames.TryGetValue(itemId, out var name)) return name;
        name = Services.Data.GetExcelSheet<Item>().TryGetRow(itemId, out var row) ? row.Name.ExtractText() : string.Empty;
        if (name.Length == 0) name = $"Item {itemId}";
        otherNames[itemId] = name;
        return name;
    }

    private readonly Dictionary<uint, string> otherNames = new();

    private sealed class Fixtures
    {
        /// <summary>ItemUICategory rows for fixtures: 65 Roof … 72 Fence outside, 73 Interior Wall, 74 Flooring, 75 Ceiling Light.</summary>
        private const uint FirstFixtureCategory = FixtureCategoryFirst;
        private const uint LastFixtureCategory = FixtureCategoryLast;

        public readonly Dictionary<uint, uint> ItemOfKey = new();
        public readonly Dictionary<uint, uint> UnitedOfPart = new();
        public readonly Dictionary<uint, HashSet<uint>> PartsOfUnited = new();
        public readonly Dictionary<ushort, uint> Wallpapers = new();
        public readonly Dictionary<ushort, uint> Paintings = new();
        public readonly Dictionary<ushort, uint>[] Fishprints = { new(), new(), new(), new() };

        public Fixtures()
        {
            Read("the fixture items", ReadItems);
            Read("the themed houses", ReadUnited);
            Read("the wallpapers and paintings", ReadMaterials);
        }

        private static void Read(string what, System.Action read)
        {
            try
            {
                read();
            }
            catch (System.Exception ex)
            {
                Services.Log.Warning(ex, $"Couldn't read {what}: saved homes leave them out");
            }
        }

        private void ReadItems()
        {
            // A fixture item's AdditionalData is its HousingExterior, HousingInterior or (themed houses)
            // HousingUnitedExterior row: the key the game keeps for what's put up.
            foreach (var item in Services.Data.GetExcelSheet<Item>())
            {
                var ui = item.ItemUICategory.RowId;
                if (ui < FirstFixtureCategory || ui > LastFixtureCategory) continue;
                var key = item.AdditionalData.RowId;
                if (key != 0) ItemOfKey.TryAdd(key, item.RowId);
            }
        }

        private void ReadUnited()
        {
            // A themed house's parts each have their own key; the house is put up as all of them.
            foreach (var row in Services.Data.GetExcelSheet<HousingUnitedExterior>())
            {
                if (!ItemOfKey.TryGetValue(row.RowId, out var united)) continue;
                var parts = new HashSet<uint>();
                foreach (var part in new[] { row.Roof, row.Walls, row.Windows, row.Door, row.OptionalRoof, row.OptionalWall, row.OptionalSignboard, row.Fence })
                    if (part.RowId != 0) parts.Add(part.RowId);
                PartsOfUnited[united] = parts;
                // Only the roof and walls tell a themed house: its door, window or fence may be anyone's.
                if (row.Roof.RowId != 0) UnitedOfPart.TryAdd(row.Roof.RowId, united);
                if (row.Walls.RowId != 0) UnitedOfPart.TryAdd(row.Walls.RowId, united);
            }
        }

        private void ReadMaterials()
        {
            // VaseFlower: 1001-1999 paintings, 2001-2999 wallpapers, then the four sizes of fish print.
            foreach (var row in Services.Data.GetExcelSheet<VaseFlower>())
            {
                var map = row.RowId switch
                {
                    > 1000 and < 2000 => Paintings,
                    > 2000 and < 3000 => Wallpapers,
                    > 3000 and < 4000 => Fishprints[0],
                    > 4000 and < 5000 => Fishprints[1],
                    > 5000 and < 6000 => Fishprints[2],
                    > 6000 and < 7000 => Fishprints[3],
                    _ => null,
                };
                if (map != null && row.Item.RowId != 0) map.TryAdd(row.Unknown0, row.Item.RowId);
            }
        }
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
