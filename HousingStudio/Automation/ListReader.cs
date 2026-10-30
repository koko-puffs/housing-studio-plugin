using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HousingStudio.Automation;

/// <summary>A list found in a window, and the words of its rows in order.</summary>
/// <param name="Source">Where the rows were read from ("tree list items", "list labels", "list item renderers").</param>
public sealed record ListRows(uint NodeId, string Source, int Length, List<string> Rows);

/// <summary>
/// Reads the rows of the lists in a window (the furnishing list, the dye mode's list) without
/// clicking anything, from the list components FFXIVClientStructs describes: a tree list's items
/// (their texts), or a plain list's labels. Which one the furnishing list uses, and whether its rows'
/// texts are the pieces' names, isn't confirmed: the run checks every guess by picking the row and
/// reading which storeroom slot the game picked, and logs what it found here.
/// </summary>
public static unsafe class ListReader
{
    public static List<ListRows> Read(string addonName)
    {
        var result = new List<ListRows>();
        var addon = GameUi.Ready(addonName);
        if (addon == null) return result;
        var uld = &addon->UldManager;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || (ushort)node->Type < 1000) continue;
            var compNode = (AtkComponentNode*)node;
            var comp = compNode->Component;
            if (comp == null) continue;
            ComponentType type;
            try
            {
                type = comp->GetComponentType();
            }
            catch
            {
                continue;
            }
            if (type == ComponentType.TreeList) result.Add(ReadTree((AtkComponentTreeList*)comp, node->NodeId));
            else if (type == ComponentType.List) result.Add(ReadList((AtkComponentList*)comp, node->NodeId));
        }
        return result;
    }

    private static ListRows ReadTree(AtkComponentTreeList* tree, uint nodeId)
    {
        var rows = new List<string>();
        foreach (var p in tree->Items.AsSpan())
        {
            var item = p.Value;
            if (item == null) continue;
            // Group headers aren't pickable rows.
            if (item->Type is TreeListItemType.Group or TreeListItemType.SectionHeader) continue;
            rows.Add(FirstText(item->StringValues.AsSpan()));
        }
        return new ListRows(nodeId, "tree list items", rows.Count, rows);
    }

    private static ListRows ReadList(AtkComponentList* list, uint nodeId)
    {
        var rows = new List<string>();
        var length = Math.Max(0, list->ListLength);
        if (list->ItemLabels != null)
        {
            for (var i = 0; i < length; i++) rows.Add(Text(list->ItemLabels[i]));
            if (rows.Any(r => r.Length > 0)) return new ListRows(nodeId, "list labels", length, rows);
            rows.Clear();
        }
        if (list->ItemRendererList != null)
        {
            var n = Math.Min(length, list->AllocatedItemRendererListLength);
            for (var i = 0; i < n; i++) rows.Add(Text(list->ItemRendererList[i].Label));
        }
        return new ListRows(nodeId, "list item labels", length, rows);
    }

    private static string FirstText(Span<InteropGenerator.Runtime.CStringPointer> strings)
    {
        foreach (var s in strings)
        {
            var t = Text(s);
            if (t.Length > 0) return t;
        }
        return string.Empty;
    }

    private static string Text(InteropGenerator.Runtime.CStringPointer s)
    {
        try
        {
            return s.HasValue ? GameUi.Text(s.Value) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The list most likely to be the one with <paramref name="expected"/> rows (the storeroom's pieces), or the longest.</summary>
    public static ListRows? Best(List<ListRows> lists, int expected) =>
        lists.Where(l => l.Rows.Count(r => r.Length > 0) > 0)
            .OrderBy(l => l.Rows.Count == expected ? 0 : 1)
            .ThenByDescending(l => l.Rows.Count)
            .FirstOrDefault();

    public static string Describe(List<ListRows> lists) =>
        lists.Count == 0
            ? "no lists found"
            : string.Join("; ", lists.Select(l => $"node {l.NodeId} ({l.Source}, {l.Length} rows): {string.Join(" | ", l.Rows.Take(8))}{(l.Rows.Count > 8 ? " …" : string.Empty)}"));
}
