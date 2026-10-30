namespace HousingStudio.Core;

public enum Floor
{
    Basement,
    Ground,
    Upper,
    Garden,
}

/// <summary>
/// Which floor a height is on inside a house: below 0 the basement, from 0 to 7 yalms the ground
/// floor, above that the upper floor (houses with one; small houses keep everything on the ground
/// floor). The MakePlace plugin splits floors at the same heights. Apartments are one floor.
/// </summary>
public static class Floors
{
    public const float GroundFrom = -0.001f;
    public const float UpperFrom = 6.999f;

    public static Floor Of(float y, string? houseSize)
    {
        if (houseSize == "Apartment") return Floor.Ground;
        if (y < GroundFrom) return Floor.Basement;
        if (y < UpperFrom) return Floor.Ground;
        return houseSize is "Medium" or "Large" ? Floor.Upper : Floor.Ground;
    }

    public static string Name(Floor f) => f switch
    {
        Floor.Basement => "Basement",
        Floor.Ground => "Ground floor",
        Floor.Upper => "Upper floor",
        _ => "Garden",
    };
}
