using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using HousingStudio.Automation;
using HousingStudio.Core;

namespace HousingStudio.Game;

/// <summary>
/// The game-side actions automation needs beyond moving a piece (<see cref="HousingEditor"/>):
/// opening an item's menu in your bags, opening the dye window on the picked piece, and aiming the
/// preview of a piece being placed. Framework thread only.
/// </summary>
public sealed unsafe class HousingActions
{
    // Opening the dye window on the picked piece, as RemakePlace/plugin (API 15) does: the game's
    // "picked housing item" getter and the housing agent's "interact with item" function.
    private const string SelectedItemPattern = "E8 ?? ?? ?? ?? 48 85 C0 75 ?? E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 84 C0 75 ?? E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 8B 8B";
    private const string InteractPattern = "48 85 D2 0F 84 ?? ?? ?? ?? 56 57 48 83 EC ?? 0F B6 81";

    private readonly delegate* unmanaged<nint, nint> selectedItem;
    private readonly delegate* unmanaged<nint, nint, void> interact;

    public string? DyeError { get; }

    public HousingActions()
    {
        try
        {
            if (Services.SigScanner.TryScanText(SelectedItemPattern, out var s) && s != 0
                && Services.SigScanner.TryScanText(InteractPattern, out var i) && i != 0)
            {
                selectedItem = (delegate* unmanaged<nint, nint>)s;
                interact = (delegate* unmanaged<nint, nint, void>)i;
            }
            else
            {
                DyeError = "Couldn't find the game's \"open the dye window\" functions (the game may have been patched), so dyeing is off.";
            }
        }
        catch (Exception ex)
        {
            DyeError = "Couldn't look for the game's dye functions.";
            Services.Log.Error(ex, "HousingActions setup failed");
        }
    }

    public bool CanDye => DyeError == null;

    /// <summary>
    /// Opens the item menu on a bag slot, as clicking the item does (AgentInventoryContext, which item
    /// automation plugins use), owned by the open bag window (or the inventory agent's).
    /// </summary>
    public static bool OpenBagItemMenu(int container, int slot)
    {
        var agent = AgentInventoryContext.Instance();
        if (agent == null) return false;
        var owner = GameUi.BagWindowId();
        if (owner == 0)
        {
            var inventory = AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory);
            owner = inventory == null ? 0 : inventory->GetAddonId();
        }
        agent->OpenForItemSlot((InventoryType)container, slot, 0, owner);
        return true;
    }

    /// <summary>Opens the dye window on the piece the editor has picked (dye mode).</summary>
    public bool OpenDyeWindow()
    {
        if (!CanDye) return false;
        var hm = HousingManager.Instance();
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Housing);
        if (hm == null || agent == null) return false;
        var item = selectedItem((nint)hm);
        if (item == 0) return false;
        interact((nint)agent, item);
        return true;
    }

    /// <summary>
    /// Puts the preview of a piece being placed at a spot, and (only while the editor is placing,
    /// so a piece already in the room is never touched) the piece it's holding.
    /// </summary>
    public static bool AimHeld(Vector3 position, float yaw)
    {
        var done = false;
        var preview = FurnitureReader.Preview();
        if (preview != null)
        {
            preview->SetPosition(position.X, position.Y, position.Z);
            preview->SetRotation(yaw);
            done = true;
        }
        var held = HousingEditor.Mode == EditorMode.Place ? HousingEditor.ActiveItem : 0;
        if (held != 0)
        {
            var sg = (SharedGroupLayoutInstance*)held;
            var q = Transforms.YawQuaternion(yaw);
            sg->Transform.Translation = position;
            sg->Transform.Rotation = q;
            done = true;
        }
        return done;
    }

    /// <summary>Where the preview is now, or null.</summary>
    public static (Vector3 Position, float Yaw)? PreviewAt()
    {
        var preview = FurnitureReader.Preview();
        if (preview == null) return null;
        return (new Vector3(preview->Position.X, preview->Position.Y, preview->Position.Z), preview->Rotation);
    }

    /// <summary>How many of a dye's items you have in your bags (any of the Stain's items: the dye, its general-purpose version).</summary>
    public static int DyesOwned(GameData data, byte stain)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;
        var total = 0;
        foreach (var item in data.DyeItems(stain)) total += mgr->GetInventoryItemCount(item, false, false, false, 0);
        return total;
    }
}
