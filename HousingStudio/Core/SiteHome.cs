using System;
using System.Text.Json.Nodes;

namespace HousingStudio.Core;

/// <summary>
/// A home as the site keeps an address (<c>hs_designs</c>: world, district, ward, plot, room,
/// subdivision, home_kind), and as the plugin sends it (the contract's <c>p_home</c>):
/// <c>{"world": "Lich", "district": "mist", "ward": 5, "kind": "house", "plot": 23}</c>, an
/// apartment with its <c>room</c> and <c>subdivision</c> (its building), a free company's private
/// chamber (<c>kind: "room"</c>) with its <c>plot</c> and <c>room</c>.
/// </summary>
/// <param name="Kind">"house", "apartment" or "room" (a private chamber).</param>
public sealed record SiteHome(string World, string District, int Ward, string Kind, int? Plot = null, int? Room = null, bool? Subdivision = null)
{
    public const string House = "house";
    public const string Apartment = "apartment";
    public const string Chamber = "room";

    /// <summary>The address as the site's functions take it: only the parts its kind has, numbers as JSON integers.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["world"] = World,
            ["district"] = District,
            ["ward"] = Ward,
            ["kind"] = Kind,
        };
        if (Kind is House or Chamber && Plot is { } plot) o["plot"] = plot;
        if (Kind is Apartment or Chamber && Room is { } room) o["room"] = room;
        if (Kind == Apartment) o["subdivision"] = Subdivision ?? false;
        return o;
    }

    /// <summary>One key per home (the same home, the same key), for caching what the site said of it.</summary>
    public string Key => $"{World.ToLowerInvariant()}|{District}|{Ward}|{Kind}|{Plot}|{Room}|{Subdivision}";

    /// <summary>In words: "Lich, Mist, ward 5, plot 23", "Lich, Mist, ward 5 (subdivision), apartment 41", "…, plot 23, private chambers 3".</summary>
    public string Describe()
    {
        var where = $"{World}, {Districts.Display(District).Replace("The ", string.Empty, StringComparison.Ordinal)}, ward {Ward}";
        return Kind switch
        {
            Apartment => $"{where}{(Subdivision == true ? " (subdivision)" : string.Empty)}, apartment {Room}",
            Chamber => $"{where}, plot {Plot}, private chambers {Room}",
            _ => $"{where}, plot {Plot}",
        };
    }
}

/// <summary>
/// What the game says of the home you're in, as plain numbers (read in Game/HomeReader.cs from
/// <c>HousingManager</c>'s <c>HouseId</c>), for <see cref="HomeAddress.From"/> to make the site's
/// address of. Nothing here is the game's own type, so it's tested without the game.
/// </summary>
/// <param name="World">The world you're on, in English (the World sheet's name); the house id's own may be missing.</param>
/// <param name="District">The site's district key ("mist"), or null when it couldn't be told.</param>
/// <param name="WardIndex">The house id's ward, 0-based (ward 1 is 0).</param>
/// <param name="PlotIndex">The house id's plot, 0-based across both divisions: 0-29 the main ward, 30-59 its subdivision.</param>
/// <param name="IsApartment">The house id's apartment flag (its plot byte's 0x80).</param>
/// <param name="ApartmentDivision">An apartment's building: 0 the main ward's, 1 the subdivision's.</param>
/// <param name="RoomNumber">The house id's room: 0 the house itself, an apartment's or a chamber's number as the game shows it, 0x3FF a workshop.</param>
public readonly record struct HouseFacts(
    string? World,
    string? District,
    int WardIndex,
    int PlotIndex,
    bool IsApartment,
    int ApartmentDivision,
    int RoomNumber,
    bool IsWorkshop);

/// <summary>The site's address of the home you're in, from the game's numbers.</summary>
public static class HomeAddress
{
    /// <summary>The 10-bit room field's "a company workshop" (the house id's 0x3FF).</summary>
    public const int Workshop = 0x3FF;

    /// <summary>
    /// The address of an interior: an apartment (its number and building), a private chamber (its
    /// plot and number) or a house (its plot, 31-60 in the subdivision). Null, with why in words,
    /// when it can't be told, and for a company workshop (not a home on the site).
    /// </summary>
    public static SiteHome? From(HouseFacts f, out string why)
    {
        why = string.Empty;
        if (f.IsWorkshop || f.RoomNumber == Workshop)
        {
            why = "A workshop isn't a home on Housing Studio.";
            return null;
        }
        if (Common(f.World, f.District, f.WardIndex, out why) is not { } common) return null;
        var (world, district, ward) = common;

        if (f.IsApartment)
        {
            if (f.ApartmentDivision is not (0 or 1))
            {
                why = "Couldn't tell which apartment building this is.";
                return null;
            }
            if (f.RoomNumber is < 1 or > 9999)
            {
                why = "Couldn't tell this apartment's number.";
                return null;
            }
            return new SiteHome(world, district, ward, SiteHome.Apartment, Room: f.RoomNumber, Subdivision: f.ApartmentDivision == 1);
        }

        if (f.PlotIndex is < 0 or > 59)
        {
            why = "Couldn't tell which plot this is.";
            return null;
        }
        if (f.RoomNumber is < 0 or > 9999)
        {
            why = "Couldn't tell which room this is.";
            return null;
        }
        return f.RoomNumber > 0
            ? new SiteHome(world, district, ward, SiteHome.Chamber, Plot: f.PlotIndex + 1, Room: f.RoomNumber)
            : new SiteHome(world, district, ward, SiteHome.House, Plot: f.PlotIndex + 1);
    }

    /// <summary>A garden: the house on its plot (1-60, as the plugin numbers plots), in the ward you're in.</summary>
    /// <param name="wardIndex">The ward, 0-based (the game's <c>GetCurrentWard</c>).</param>
    public static SiteHome? Garden(string? world, string? district, int wardIndex, int? plot, out string why)
    {
        if (Common(world, district, wardIndex, out why) is not { } common) return null;
        var (w, d, ward) = common;
        if (plot is not (>= 1 and <= 60))
        {
            why = "Couldn't tell which plot this is.";
            return null;
        }
        return new SiteHome(w, d, ward, SiteHome.House, Plot: plot);
    }

    private static (string World, string District, int Ward)? Common(string? world, string? district, int wardIndex, out string why)
    {
        why = string.Empty;
        var w = (world ?? string.Empty).Trim();
        if (w.Length is 0 or > 40)
        {
            why = "Couldn't tell which world you're on.";
            return null;
        }
        if (district == null || Array.IndexOf(Districts.Keys, district) < 0)
        {
            why = "Couldn't tell which district this is.";
            return null;
        }
        if (wardIndex is < 0 or > 29)
        {
            why = "Couldn't tell which ward this is.";
            return null;
        }
        return (w, district, wardIndex + 1);
    }
}
