namespace HousingStudio.Core;

/// <summary>A design against the furnishings a home holds (inside, or its garden).</summary>
/// <param name="Limit">How many pieces the home holds on this side.</param>
/// <param name="Design">The design's pieces on this side.</param>
/// <param name="Others">Pieces placed there now that aren't the design's: they stay, and count.</param>
/// <param name="After">What would be placed once Start has placed every piece it can (what's placed now stays).</param>
public readonly record struct LimitCheck(int Limit, int Design, int Others, int After)
{
    /// <summary>The design alone is more than the home holds.</summary>
    public bool DesignOver => Design > Limit;

    /// <summary>How many pieces past the limit Start would end up, 0 when it fits.</summary>
    public int Over => After > Limit ? After - Limit : 0;
}

/// <summary>
/// How many furnishings the game lets a home hold, by size, the same numbers Housing Studio's site
/// uses (sketch/model.ts <c>ITEM_LIMITS</c>, makeplace.ts <c>OUTDOOR_LIMIT</c>): inside, an
/// apartment 150, a cottage 300, a house 450, a mansion 600; in the garden 40, 60 and 80 (an
/// apartment has none). Pure, so it's tested.
/// </summary>
public static class ItemLimits
{
    /// <summary>Pieces inside, or null when the size isn't known.</summary>
    public static int? Inside(string? houseSize) => houseSize switch
    {
        "Apartment" => 150,
        "Small" => 300,
        "Medium" => 450,
        "Large" => 600,
        _ => null,
    };

    /// <summary>Pieces in the garden, or null (an apartment, or a size not known).</summary>
    public static int? Garden(string? houseSize) => houseSize switch
    {
        "Small" => 40,
        "Medium" => 60,
        "Large" => 80,
        _ => null,
    };

    public static int? Of(Side side, string? houseSize) => side == Side.Interior ? Inside(houseSize) : Garden(houseSize);

    /// <summary>
    /// The design against the home's limit: <paramref name="toPlace"/> pieces Start places (from the
    /// storeroom and bags) on top of the <paramref name="placedNow"/> there (moves don't add any), of
    /// which <paramref name="others"/> aren't the design's. Null when the size isn't known.
    /// </summary>
    public static LimitCheck? Check(Side side, string? houseSize, int design, int placedNow, int others, int toPlace)
    {
        if (Of(side, houseSize) is not { } limit) return null;
        return new LimitCheck(limit, design, System.Math.Max(0, others), System.Math.Max(0, placedNow) + System.Math.Max(0, toPlace));
    }
}
