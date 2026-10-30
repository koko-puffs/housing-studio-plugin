using System;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using HousingStudio.Core;

namespace HousingStudio.Game;

/// <summary>
/// The game's housing editor: which mode is on, which piece is picked, and the game's own "pick a
/// piece" and "set it down" functions, which are what clicking a piece and letting go of it in
/// Rotate mode do. Placing through them goes through the game's own checks and messages.
///
/// Where these come from (none of it is in FFXIVClientStructs yet): the editor's state is a pointer
/// 0x40 into the LayoutWorld singleton, with the mode at +0x0 and the picked piece (its layout
/// instance) at +0x18; the two functions are found by byte patterns. These were found by the
/// MakePlace plugin (jawslouis/MakePlacePlugin) and are kept current by its maintained forks for
/// Dalamud API 15 (RemakePlace/plugin, Drakansoul/DisPlace; Wyse2011/Refurnish uses the same).
/// They may need updating after a game patch: if a pattern isn't found, nothing is called and the
/// plugin says so.
/// </summary>
public sealed unsafe class HousingEditor
{
    private const string SelectPattern = "48 85 D2 0F 84 ?? ?? ?? ?? 53 41 56 48 83 EC ?? 48 89 6C 24";
    private const string PlacePattern = "48 89 5C 24 10 48 89 74 24 18 57 48 83 EC 20 4C 8B 41 18 33 FF 0F B6 F2";
    private const int EditorStateOffset = 0x40;
    private const int ModeOffset = 0x0;
    private const int ActiveItemOffset = 0x18;
    private static readonly int MaxMode = Enum.GetValues<EditorMode>().Max(m => (int)m);

    private delegate* unmanaged<nint, nint, void> select;
    private delegate* unmanaged<nint, nint, void> place;

    public string? Error { get; private set; }
    public bool Ready { get; private set; }

    public HousingEditor()
    {
        try
        {
            if (!Services.SigScanner.TryScanText(SelectPattern, out var s) || s == 0)
            {
                Error = "Couldn't find the game's \"pick a piece\" function. The game may have been patched; the plugin needs an update.";
                return;
            }
            if (!Services.SigScanner.TryScanText(PlacePattern, out var p) || p == 0)
            {
                Error = "Couldn't find the game's \"set a piece down\" function. The game may have been patched; the plugin needs an update.";
                return;
            }
            select = (delegate* unmanaged<nint, nint, void>)s;
            place = (delegate* unmanaged<nint, nint, void>)p;
            Ready = true;
        }
        catch (Exception ex)
        {
            Error = "Couldn't look for the game's housing functions.";
            Services.Log.Error(ex, "HousingEditor setup failed");
        }
    }

    /// <summary>The editor's state, or 0 when the furnishing editor isn't open.</summary>
    private static nint State
    {
        get
        {
            var world = LayoutWorld.Instance();
            return world == null ? 0 : *(nint*)((byte*)world + EditorStateOffset);
        }
    }

    /// <summary>
    /// Is the furnishing editor open? The state can stay allocated after the editor closes, so
    /// (as the MakePlace plugin does) a mode of None counts as closed.
    /// </summary>
    public static bool IsOpen => Mode != EditorMode.None;

    public static EditorMode Mode
    {
        get
        {
            var state = State;
            if (state == 0) return EditorMode.None;
            var raw = *(int*)(state + ModeOffset);
            return raw >= (int)EditorMode.None && raw <= MaxMode ? (EditorMode)raw : EditorMode.None;
        }
    }

    /// <summary>The piece the editor has picked up (its layout instance), or 0.</summary>
    public static nint ActiveItem
    {
        get
        {
            var state = State;
            return state == 0 ? 0 : *(nint*)(state + ActiveItemOffset);
        }
    }

    /// <summary>Picks a placed piece, as clicking it in Rotate mode does. Framework thread only.</summary>
    public bool Select(nint layoutInstance)
    {
        var state = State;
        if (!Ready || state == 0 || layoutInstance == 0) return false;
        select(state, layoutInstance);
        return true;
    }

    /// <summary>
    /// Sets the picked piece down where its transform now says. Framework thread only. The game
    /// takes the piece from the editor's state itself (<c>mov r8, [rcx+18h]</c>) and reads only the
    /// low byte of the second argument, as a flag (<c>movzx esi, dl</c>). Until 0.4.0 the piece's
    /// pointer went there, so the flag was its low byte: set for 15 pieces in 16 (that's the way every
    /// log has shown working), clear for the rest. It's now always set. MakePlace passes 0.
    /// </summary>
    public bool Place(nint layoutInstance)
    {
        var state = State;
        if (!Ready || state == 0 || layoutInstance == 0) return false;
        place(state, SetDownFlag);
        return true;
    }

    private const nint SetDownFlag = 1;
}
