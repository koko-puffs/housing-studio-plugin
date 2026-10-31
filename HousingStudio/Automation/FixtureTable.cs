using System.Collections.Generic;
using Dalamud.Game;

namespace HousingStudio.Automation;

/// <summary>
/// The game's fixtures windows, as "Set them for me" (Settings → Experimental) uses them: every name,
/// container and word in one place, with where each came from. None of it has been seen working from
/// the plugin: a recording of setting one fixture by hand (Settings → Something not working? →
/// Start recording, then Housing → Interior Fixtures, put a wall in its slot, apply it) shows what
/// to put right here.
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
/// </list>
/// Pressing the window's apply button is left to you until a recording shows its click.
/// </summary>
public static class FixtureTable
{
    /// <summary>Housing → Interior Fixtures. [ClientStructs]</summary>
    public const string InteriorWindow = "HousingEditInterior";

    /// <summary>Housing → Exterior Fixtures (on your plot). [ClientStructs]</summary>
    public const string ExteriorWindow = "HousingEditExterior";

    /// <summary>What the interior window shows and edits (InventoryType.HousingInteriorAppearanceEdit). [ClientStructs]</summary>
    public const int InteriorEdit = 2012;

    /// <summary>What the exterior window shows and edits (InventoryType.HousingExteriorAppearanceEdit). [ClientStructs]</summary>
    public const int ExteriorEdit = 2011;

    /// <summary>
    /// The window's apply button: not known, so you press it (the run waits, and checks the game's own
    /// tables for the fixtures). A recording of applying a fixture gives its values; set them here.
    /// </summary>
    public static readonly Core.UiCall? InteriorApply = null;

    /// <inheritdoc cref="InteriorApply"/>
    public static readonly Core.UiCall? ExteriorApply = null;

    /// <summary>
    /// Whether every step above has been seen working. While not, a run that sets fixtures goes step
    /// by step whatever Settings says: it holds before every action and shows it, until you press Continue.
    /// </summary>
    public static readonly bool Verified = false;

    /// <summary>The Housing menu's entry for the inside's fixtures, by client language. [Guess: the game's English words; others unconfirmed]</summary>
    public static IReadOnlyList<string> InteriorMenuWords(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "内装", "内装変更" },
        ClientLanguage.German => new[] { "Innenausstattung", "Innen" },
        ClientLanguage.French => new[] { "intérieur", "Aménagement intérieur" },
        _ => new[] { "Interior Fixtures", "Fixtures" },
    };

    /// <summary>The Housing menu's entry for the outside's fixtures, by client language. [Guess]</summary>
    public static IReadOnlyList<string> ExteriorMenuWords(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => new[] { "外装", "外装変更" },
        ClientLanguage.German => new[] { "Außenausstattung", "Außen" },
        ClientLanguage.French => new[] { "extérieur", "Aménagement extérieur" },
        _ => new[] { "Exterior Fixtures", "Fixtures" },
    };

    /// <summary>
    /// Choose the Housing menu's n-th entry: Indoor Furnishings is [i:0] and Furnishing Color [i:1]
    /// [Recording 1], so the fixtures entries are taken to be their place in the menu. [Guess]
    /// </summary>
    public static readonly Core.UiCall MenuEntry = new(UiTable.HousingMenu, "i:{entry}", true, "Recording 1's [i:0] and [i:1], by place", Verified: false);
}
