using System.Collections.Generic;

namespace HousingStudio.Core;

/// <summary>The furnishing editor's modes, as the game numbers them (the editor's state, +0x0).</summary>
public enum EditorMode
{
    None = 0,
    Move = 1,
    Rotate = 2,
    /// <summary>The bar's "Place selected object in your inventory" (HousingLayout [i:2]): a piece picked goes to your bags.</summary>
    ToBags = 3,
    Place = 4,
    Dye = 5,
    /// <summary>The bar's "Place selected item into your storeroom" (HousingLayout [i:3]): a piece picked goes to the storeroom.</summary>
    Store = 6,
}

/// <summary>One click on the way to another editor mode.</summary>
public enum ModeClick
{
    /// <summary>The editor bar's Rotate button.</summary>
    Rotate,

    /// <summary>The editor bar's Move button.</summary>
    Move,

    /// <summary>Close the editor (layout modes): back to the Housing menu.</summary>
    CloseEditor,

    /// <summary>Close the dye mode's list: back to the Housing menu.</summary>
    CloseDyeList,

    /// <summary>Housing menu → Indoor Furnishings: opens the editor in a layout mode, Rotate or Move (0.3.0 saw Move).</summary>
    OpenFurnishings,

    /// <summary>Housing menu → Furnishing Color (the dye mode).</summary>
    OpenDye,

    /// <summary>Open the Housing menu itself (when nothing housing is open).</summary>
    OpenHousingMenu,
}

/// <summary>
/// How to get the editor from the mode it's in to another, with the clicks seen in a recording of
/// someone doing it by hand: Move and Rotate are buttons on the editor's bar; the dye mode
/// (Furnishing Color) is its own entry in the Housing menu, so going between it and the others means
/// closing one (which brings the Housing menu back) and choosing the other. Pure, so it's tested.
/// </summary>
public static class ModeRoute
{
    /// <returns>The clicks, in order (empty when it's already there), or null when there's no known way
    /// (a piece is being placed, or the mode isn't one of the editor's).</returns>
    public static List<ModeClick>? Plan(EditorMode current, EditorMode want, bool housingMenuOpen)
    {
        if (current == want) return new List<ModeClick>();
        if (current == EditorMode.Place) return null; // mid-placing: wait for it to finish, never interrupt
        if (current == EditorMode.None && !housingMenuOpen)
        {
            // Nothing housing is open: open the Housing menu first, then go on from there.
            var rest = Plan(EditorMode.None, want, true);
            if (rest == null) return null;
            rest.Insert(0, ModeClick.OpenHousingMenu);
            return rest;
        }
        var layout = IsLayout(current);

        if (want is EditorMode.Rotate or EditorMode.Move)
        {
            var bar = want == EditorMode.Rotate ? ModeClick.Rotate : ModeClick.Move;
            if (layout) return new List<ModeClick> { bar };
            var route = new List<ModeClick>();
            if (current == EditorMode.Dye) route.Add(ModeClick.CloseDyeList);
            else if (!housingMenuOpen) return null;
            route.Add(ModeClick.OpenFurnishings);
            // Indoor Furnishings opens in Rotate or in Move (a user's run, 0.3.0: Move), so the bar's button
            // follows; it's pressed only if the editor didn't open in the mode wanted (Needed).
            route.Add(bar);
            return route;
        }

        if (want == EditorMode.Dye)
        {
            if (layout) return new List<ModeClick> { ModeClick.CloseEditor, ModeClick.OpenDye };
            if (current == EditorMode.None && housingMenuOpen) return new List<ModeClick> { ModeClick.OpenDye };
            return null;
        }
        return null;
    }

    /// <summary>
    /// The mode the editor is in once a click has gone through (None for the clicks that bring the
    /// Housing menu up). Indoor Furnishings has no one mode: see <see cref="Reached"/>.
    /// </summary>
    public static EditorMode After(ModeClick click) => click switch
    {
        ModeClick.Rotate => EditorMode.Rotate,
        ModeClick.Move => EditorMode.Move,
        ModeClick.OpenDye => EditorMode.Dye,
        _ => EditorMode.None,
    };

    /// <summary>A layout mode: one of the modes Indoor Furnishings' editor bar has.</summary>
    public static bool IsLayout(EditorMode mode) => mode is EditorMode.Move or EditorMode.Rotate or EditorMode.Store or EditorMode.ToBags;

    /// <summary>
    /// Has the click gone through, by the mode the editor is in now? Indoor Furnishings has once the
    /// editor is open in any layout mode (it opened in Move in 0.3.0, and the run waited for Rotate).
    /// </summary>
    public static bool Reached(ModeClick click, EditorMode now) => click switch
    {
        ModeClick.OpenFurnishings => IsLayout(now),
        _ when LeadsToHousingMenu(click) => now == EditorMode.None,
        _ => now == After(click),
    };

    /// <summary>Is the click still needed, in the mode the editor is in now? The bar's buttons aren't when it's already in their mode.</summary>
    public static bool Needed(ModeClick click, EditorMode now) => click is not (ModeClick.Rotate or ModeClick.Move) || now != After(click);

    /// <summary>Does this click bring the Housing menu up (rather than an editor mode)?</summary>
    public static bool LeadsToHousingMenu(ModeClick click) => click is ModeClick.CloseEditor or ModeClick.CloseDyeList or ModeClick.OpenHousingMenu;
}
