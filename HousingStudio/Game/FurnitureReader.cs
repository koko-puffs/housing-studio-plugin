using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace HousingStudio.Game;

/// <summary>A piece placed in the house or the garden, as the game has it right now.</summary>
/// <param name="Key">Its furniture slot (index into the territory's furniture list): stable while you stay.</param>
/// <param name="Instance">Its layout instance (what the editor picks and sets down), or 0 when it isn't loaded.</param>
public readonly record struct LivePiece(int Key, uint ItemId, uint BaseId, Vector3 Position, float Yaw, byte Stain, nint Instance);

/// <summary>
/// Reads placed furniture: inside, every furnishing of the house; in a ward, only your plot's
/// garden (the game's list holds the whole ward's, so each of your plot's placed items is looked
/// up by its slot with the game's own function, as the MakePlace plugin does).
/// </summary>
public sealed unsafe class FurnitureReader
{
    // Finds which entry of the ward's furniture list holds a plot's garden slot. The pattern is the
    // call site the maintained MakePlace forks use for API 15 (resolved to its target).
    private const string YardIndexPattern = "E8 ?? ?? ?? ?? 44 0F B7 D8";

    private readonly GameData data;
    private readonly delegate* unmanaged<byte, ushort, ushort> yardIndex;

    public string? GardenError { get; }

    public FurnitureReader(GameData data)
    {
        this.data = data;
        try
        {
            if (Services.SigScanner.TryScanText(YardIndexPattern, out var addr) && addr != 0)
                yardIndex = (delegate* unmanaged<byte, ushort, ushort>)addr;
            else
                GardenError = "Couldn't find the game's garden lookup (the game may have been patched), so gardens can't be read.";
        }
        catch (Exception ex)
        {
            GardenError = "Couldn't look for the game's garden lookup.";
            Services.Log.Error(ex, "FurnitureReader setup failed");
        }
    }

    /// <summary>Every furnishing placed inside, or null when you aren't inside a home.</summary>
    public List<LivePiece>? ReadInterior()
    {
        var hm = HousingManager.Instance();
        if (hm == null || hm->IndoorTerritory == null) return null;
        var fm = &hm->IndoorTerritory->FurnitureManager;
        var memory = fm->FurnitureMemory;
        var array = &fm->ObjectManager.ObjectArray;
        var list = new List<LivePiece>();
        foreach (var p in array->Objects)
        {
            var o = p.Value;
            if (o == null || o == array->TempObject) continue;
            var ho = (HousingObject*)o;
            if (ho->HousingObjectId.Type != HousingObjectType.Furniture) continue;
            var key = (int)ho->HousingFurnitureIndex;
            var item = data.ItemOfPlaced(o->BaseId, yard: false);
            if (item == 0) continue;
            byte stain = 0;
            if (key >= 0 && key < memory.Length) stain = memory[key].Stain;
            list.Add(new LivePiece(key, item, o->BaseId, Pos(o), o->Rotation, stain, (nint)ho->SharedGroupLayoutInstance));
        }
        return list;
    }

    /// <summary>The garden of the plot you're editing, or null when it can't be read here.</summary>
    public List<LivePiece>? ReadGarden(out string? why)
    {
        why = null;
        var hm = HousingManager.Instance();
        if (hm == null || hm->OutdoorTerritory == null) { why = "You're not in a residential ward."; return null; }
        if (yardIndex == null) { why = GardenError; return null; }
        var ot = hm->OutdoorTerritory;
        if (ot->HouseUnit.IsApartment) { why = "Apartments have no garden."; return null; }
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.HousingExteriorPlacedItems);
        if (container == null || !container->IsLoaded)
        {
            why = "Your garden's list isn't loaded. Stand on your plot and open Housing → Outdoor Furnishings.";
            return null;
        }

        var fm = &ot->FurnitureManager;
        var memory = fm->FurnitureMemory;
        var array = &fm->ObjectManager.ObjectArray;
        // The ward's objects by furniture slot, to find each garden piece's layout instance.
        var objects = new Dictionary<int, nint>();
        foreach (var p in array->Objects)
        {
            var o = p.Value;
            if (o == null || o == array->TempObject) continue;
            var ho = (HousingObject*)o;
            if (ho->HousingObjectId.Type != HousingObjectType.YardObject) continue;
            objects[ho->HousingFurnitureIndex] = (nint)ho;
        }

        // The plot whose furnishings are being edited, else the one you're standing on (0-based).
        var plotIndex = ot->EditingFurnishingsOfPlot >= 0 ? (byte)ot->EditingFurnishingsOfPlot : ot->HouseUnit.Value;
        var list = new List<LivePiece>();
        for (var slot = 0; slot < container->Size; slot++)
        {
            var inv = container->GetInventorySlot(slot);
            if (inv == null || inv->IsEmpty()) continue;
            int key = yardIndex(plotIndex, (ushort)slot);
            if (key < 0 || key >= memory.Length) continue;
            ref var f = ref memory[key];
            var itemId = inv->GetBaseItemId();
            nint instance = 0;
            var position = new Vector3(f.Position.X, f.Position.Y, f.Position.Z);
            var yaw = f.Rotation;
            if (objects.TryGetValue(key, out var objAddr))
            {
                var ho = (HousingObject*)objAddr;
                instance = (nint)ho->SharedGroupLayoutInstance;
                position = Pos(&ho->GameObject);
                yaw = ho->GameObject.Rotation;
            }
            list.Add(new LivePiece(key, itemId, f.Id, position, yaw, inv->GetStain(0), instance));
        }
        return list;
    }

    /// <summary>Where the game has a placed piece now, by its furniture slot (for checking a move landed).</summary>
    public static (Vector3 Position, float Yaw)? Stored(bool garden, int key)
    {
        var hm = HousingManager.Instance();
        if (hm == null) return null;
        HousingFurnitureManager* fm = garden
            ? (hm->OutdoorTerritory == null ? null : &hm->OutdoorTerritory->FurnitureManager)
            : (hm->IndoorTerritory == null ? null : &hm->IndoorTerritory->FurnitureManager);
        if (fm == null) return null;
        var memory = fm->FurnitureMemory;
        if (key < 0 || key >= memory.Length) return null;
        ref var f = ref memory[key];
        return (new Vector3(f.Position.X, f.Position.Y, f.Position.Z), f.Rotation);
    }

    /// <summary>The dye a placed piece has now, by its furniture slot (0 undyed), or null.</summary>
    public static byte? StoredStain(bool garden, int key)
    {
        var hm = HousingManager.Instance();
        if (hm == null) return null;
        HousingFurnitureManager* fm = garden
            ? (hm->OutdoorTerritory == null ? null : &hm->OutdoorTerritory->FurnitureManager)
            : (hm->IndoorTerritory == null ? null : &hm->IndoorTerritory->FurnitureManager);
        if (fm == null) return null;
        var memory = fm->FurnitureMemory;
        if (key < 0 || key >= memory.Length) return null;
        return memory[key].Stain;
    }

    /// <summary>The preview the game shows while a piece is being picked from the storeroom or bags, or null.</summary>
    public static GameObject* Preview()
    {
        var hm = HousingManager.Instance();
        if (hm == null) return null;
        if (hm->IndoorTerritory != null) return hm->IndoorTerritory->FurnitureManager.ObjectManager.ObjectArray.TempObject;
        if (hm->OutdoorTerritory != null) return hm->OutdoorTerritory->FurnitureManager.ObjectManager.ObjectArray.TempObject;
        return null;
    }

    private static Vector3 Pos(GameObject* o) => new(o->Position.X, o->Position.Y, o->Position.Z);
}
