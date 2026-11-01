using System.Collections.Generic;
using Dalamud.Game;

namespace HousingStudio.Automation;

/// <summary>
/// The game's fixtures windows, as "Set them for me" uses them: every name,
/// container and word in one place, with where each came from. Recording 2 (setting one fixture by
/// hand, inside) showed the menu's entry (Housing → Remodel Interior, [i:2]), the window's apply and
/// its yes/no. How a fixture goes in its slot from the plugin is still a guess: the game's own way
/// was HousingEditInterior [i:2, u:0, empty, empty, u:1] (a slot clicked), then ContextIconMenu
/// [i:0, i:0, u:51218, u:0, empty] (a fixture chosen from the icons), which isn't decoded yet.
///
/// Sources:
/// <list type="bullet">
/// <item>[ClientStructs] aers/FFXIVClientStructs: the windows' names (AtkArrayData's Housing list:
/// "HousingEditInterior, HousingEditExterior") and the containers the windows edit
/// (InventoryType HousingInteriorAppearanceEdit 2012, HousingExteriorAppearanceEdit 2011; what's set
/// is HousingInteriorAppearance 25002 and HousingExteriorAppearance 25000).</item>
/// <item>[Inventory Tools] Critical-Impact's inventory scanner: the appearance containers are loaded
/// while those windows are open.</item>
/// <item>[Guess] Everything else: the Housing menu's entries are chosen by their words, at their place
/// in the menu (Indoor Furnishings is [i:0] and Furnishing Color [i:1], Recording 1, so the place
/// is checked against those two first); a fixture is put in its slot as dragging it there would
/// (the game's own item move, from the bag to the edit container's slot); the slots' order is the
/// game's own order of the parts (<see cref="Core.FixtureJobs"/>), checked against what's set.</item>
/// <item>[Recording 2] Seen sent by the game, by hand.</item>
/// </list>
/// </summary>
public static class FixtureTable
{
    /// <summary>Housing → Remodel Interior. [ClientStructs; Recording 2]</summary>
    public const string InteriorWindow = "HousingEditInterior";

    /// <summary>Housing → Remodel Exterior (on your plot). [ClientStructs]</summary>
    public const string ExteriorWindow = "HousingEditExterior";

    /// <summary>What the interior window shows and edits (InventoryType.HousingInteriorAppearanceEdit). [ClientStructs]</summary>
    public const int InteriorEdit = 2012;

    /// <summary>What the exterior window shows and edits (InventoryType.HousingExteriorAppearanceEdit). [ClientStructs]</summary>
    public const int ExteriorEdit = 2011;

    /// <summary>
    /// The window's apply button; the game then asks "Proceed with fixture changes?" (<see cref="ApplyWords"/>),
    /// and once it's answered, the window closes and the Housing menu comes back. [Recording 2:
    /// HousingEditInterior [i:0, empty, empty, empty, empty]]
    /// </summary>
    public static readonly Core.UiCall? InteriorApply = new(InteriorWindow, "i:0 empty empty empty empty", true, "Recording 2");

    /// <summary>The same for the outside's window. [Guess: as the inside's]</summary>
    public static readonly Core.UiCall? ExteriorApply = new(ExteriorWindow, "i:0 empty empty empty empty", true, "Guess: as the inside's", Verified: false);

    /// <summary>The apply's question, answered yes only when it reads so. [Recording 2: "Proceed with fixture changes?"]</summary>
    public static readonly string[] ApplyWords = { "Proceed with fixture changes" };

    /// <summary>
    /// Whether every step above has been seen working. While not, a run that sets fixtures goes step
    /// by step whatever Settings says: it holds before every action and shows it, until you press Continue.
    /// </summary>
    public static readonly bool Verified = false;

    /// <summary>The Housing menu's entry for the inside's fixtures, by client language. [Recording 2: "Remodel Interior", the third entry; the other languages' are guesses]</summary>
    public static IReadOnlyList<string> InteriorMenuWords(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "内装", "内装変更" },
        ClientLanguage.German => new[] { "Innenausstattung", "Innen" },
        ClientLanguage.French => new[] { "intérieur", "Aménagement intérieur" },
        _ => new[] { "Remodel Interior", "Interior Fixtures", "Fixtures" },
    };

    /// <summary>The Housing menu's entry for the outside's fixtures, by client language. [Guess]</summary>
    public static IReadOnlyList<string> ExteriorMenuWords(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "外装", "外装変更" },
        ClientLanguage.German => new[] { "Außenausstattung", "Außen" },
        ClientLanguage.French => new[] { "extérieur", "Aménagement extérieur" },
        _ => new[] { "Remodel Exterior", "Exterior Fixtures", "Fixtures" },
    };

    /// <summary>
    /// Choose the Housing menu's n-th entry: Indoor Furnishings is [i:0], Furnishing Color [i:1]
    /// [Recording 1] and Remodel Interior [i:2] [Recording 2].
    /// </summary>
    public static readonly Core.UiCall MenuEntry = new(UiTable.HousingMenu, "i:{entry}", true, "Recordings 1 and 2");
}
