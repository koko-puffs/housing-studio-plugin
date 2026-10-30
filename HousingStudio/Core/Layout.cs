using System.Collections.Generic;
using System.Numerics;

namespace HousingStudio.Core;

/// <summary>Where a piece goes: inside the house (or apartment), or out in the garden.</summary>
public enum Side
{
    Interior,
    Garden,
}

/// <summary>
/// A piece of furniture from a layout file, in the game's own axes (x east, y up, z south, in
/// yalms) and turned about +y the way the game turns things.
/// </summary>
/// <param name="Index">Its place in the file's list (attachments follow their parent).</param>
/// <param name="ItemId">The Item sheet row (what you buy or craft).</param>
/// <param name="Name">The name the file gives it (may be empty; the game's name is looked up).</param>
/// <param name="Position">
/// Inside: the house's own coordinates. Garden: the plot's own coordinates, unless the file holds
/// the garden in the ward's (see <see cref="Layout.GardenInWardCoordinates"/>).
/// </param>
/// <param name="Yaw">Radians, between -pi and pi.</param>
/// <param name="DyeHex">"4f2d1f", or null when undyed.</param>
/// <param name="WallItemId">A partition's wall: the interior wall fixture it's painted with, or null.</param>
/// <param name="ParentIndex">For something placed on another piece (a vase on a table): that piece's index.</param>
public sealed record LayoutPiece(
    int Index,
    uint ItemId,
    string Name,
    Side Side,
    Vector3 Position,
    float Yaw,
    string? DyeHex,
    uint? WallItemId,
    int? ParentIndex);

/// <summary>A wall, floor, light, roof, door… the file picks. The plugin lists these; it doesn't set them.</summary>
public sealed record LayoutFixture(string Level, string Type, uint ItemId, string Name, string? DyeHex);

/// <summary>A MakePlace layout (.json) or a Housing Studio build (.build), which is the same JSON.</summary>
public sealed class Layout
{
    /// <summary>"Small", "Medium", "Large", "Apartment", or null when the file doesn't say.</summary>
    public string? HouseSize { get; init; }

    /// <summary>The district (or "Minimalist" style) the file's District line names, or null.</summary>
    public string? DistrictName { get; init; }

    /// <summary>Housing Studio's own district key (mist, lavender-beds…), when it can tell.</summary>
    public string? District { get; init; }

    /// <summary>A Housing Studio build's name (properties.build), or null for a MakePlace file.</summary>
    public string? BuildName { get; init; }

    /// <summary>The plot a build's garden was laid out on (properties.plot, 1–60), or null.</summary>
    public int? Plot { get; init; }

    public float InteriorScale { get; init; } = 1;
    public float ExteriorScale { get; init; } = 1;

    public List<LayoutPiece> Interior { get; init; } = new();
    public List<LayoutPiece> Garden { get; init; } = new();
    public List<LayoutFixture> InteriorFixtures { get; init; } = new();
    public List<LayoutFixture> ExteriorFixtures { get; init; } = new();

    /// <summary>
    /// Some files hold the garden in the ward's own coordinates rather than the plot's (the pieces
    /// sit far from the origin). Housing Studio's 3D view tells the two apart the same way.
    /// </summary>
    public bool GardenInWardCoordinates { get; init; }

    public bool IsApartment => HouseSize is "Apartment";

    public IEnumerable<LayoutPiece> Pieces(Side side) => side == Side.Interior ? Interior : Garden;
}
