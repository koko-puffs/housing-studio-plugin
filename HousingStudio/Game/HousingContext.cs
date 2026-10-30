using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using HousingStudio.Core;

namespace HousingStudio.Game;

public enum PlaceKind
{
    /// <summary>Not at a home (or not logged in).</summary>
    Elsewhere,

    /// <summary>Inside a house, apartment or private chambers.</summary>
    Interior,

    /// <summary>Out in a residential ward (your garden is on a plot there).</summary>
    Ward,
}

/// <summary>Where you are and what the game lets you do there, read once a frame.</summary>
public sealed record HousingContext(
    PlaceKind Kind,
    uint TerritoryId,
    string TerritoryName,
    ulong HouseId,
    bool HasPermission,
    string? HouseSize,
    string? District,
    int? Plot,
    EditorMode Mode,
    bool EditorOpen,
    bool Busy)
{
    public static readonly HousingContext None = new(PlaceKind.Elsewhere, 0, string.Empty, 0, false, null, null, null, EditorMode.None, false, false);

    public Side? Side => Kind switch
    {
        PlaceKind.Interior => Core.Side.Interior,
        PlaceKind.Ward => Core.Side.Garden,
        _ => null,
    };

    /// <summary>Can pieces be moved right now (editor in Rotate mode, allowed, not busy)?</summary>
    public bool CanEdit => Kind != PlaceKind.Elsewhere && HasPermission && EditorOpen && Mode == EditorMode.Rotate && !Busy;

    public static unsafe HousingContext Read(PlotTable plots)
    {
        if (!Services.ClientState.IsLoggedIn) return None;
        var territory = Services.ClientState.TerritoryType;
        var hm = HousingManager.Instance();
        if (hm == null) return None;

        var busy = Services.Condition[ConditionFlag.InCombat] || Services.Condition[ConditionFlag.BetweenAreas]
                   || Services.Condition[ConditionFlag.BetweenAreas51] || Services.Condition[ConditionFlag.WatchingCutscene]
                   || Services.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Services.Condition[ConditionFlag.LoggingOut];
        var name = GameData.TerritoryName(territory);
        var mode = HousingEditor.Mode;
        var open = HousingEditor.IsOpen;

        if (hm->IndoorTerritory != null)
        {
            var district = name.Length >= 2
                ? name[..2] switch
                {
                    "s1" => "mist",
                    "f1" => "lavender-beds",
                    "w1" => "goblet",
                    "e1" => "shirogane",
                    "r1" => "empyreum",
                    _ => null,
                }
                : null;
            return new HousingContext(PlaceKind.Interior, territory, name, hm->IndoorTerritory->HouseId.Id, hm->HasHousePermissions(),
                Districts.HouseSizeOfInterior(name), district, null, mode, open, busy);
        }

        if (hm->OutdoorTerritory != null)
        {
            var ot = hm->OutdoorTerritory;
            var district = plots.DistrictOfWard(name);
            // The plot whose furnishings you're editing, else the one you're standing on.
            int? plot = ot->EditingFurnishingsOfPlot >= 0 ? ot->EditingFurnishingsOfPlot + 1
                : ot->HouseUnit.IsApartment ? null : ot->HouseUnit.PlotIndex + 1;
            string? size = null;
            if (district != null && plot != null && plots.TryGet(district, plot.Value, out var spot)) size = Districts.HouseSizeOfPlot(spot.Size);
            return new HousingContext(PlaceKind.Ward, territory, name, ot->HouseId.Id, hm->HasHousePermissions(), size, district, plot, mode, open, busy);
        }

        return None with { TerritoryId = territory, TerritoryName = name, Busy = busy };
    }
}
