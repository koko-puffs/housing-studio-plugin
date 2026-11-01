using System;
using System.Collections.Generic;
using System.Linq;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>Settings' words that are worked out (0.9.0): the folded Currencies row. Pure, so it's tested.</summary>
public static class SettingsWords
{
    /// <summary>"20 of 20 allowed · only what you have is spent"; none allowed: the gil vendors and the board only.</summary>
    public static string Currencies(int allowed, int all) =>
        allowed <= 0 ? $"None of {all} allowed · gil vendors and the board only" : $"{Math.Min(allowed, all)} of {all} allowed · only what you have is spent";
}

/// <summary>What happened to a piece since the last run, as This house's changes group them.</summary>
public enum ChangeKind
{
    Moved,
    Gone,
    Redyed,
    Added,
}

/// <summary>
/// One group of This house's changes: its head ("Moved · 2"), the items in it (for their names), and
/// where they were (for Show where: the moved and taken away ones, as before 0.9.0; empty for the others).
/// </summary>
public sealed record ChangeGroup(ChangeKind Kind, string Head, IReadOnlyList<uint> Items, IReadOnlyList<Target> Where);

/// <summary>This house's changes grouped by what happened (0.9.0). Pure, so it's tested.</summary>
public static class ChangeGroups
{
    public static string Label(ChangeKind kind) => kind switch
    {
        ChangeKind.Moved => "Moved",
        ChangeKind.Gone => "Taken away",
        ChangeKind.Redyed => "Dyed again",
        _ => "Added",
    };

    /// <summary>Moved, taken away, dyed again, added: each with its count, only those with something in them.</summary>
    public static List<ChangeGroup> Of(DriftReport r)
    {
        var groups = new List<ChangeGroup>();
        if (r.Moved.Count > 0)
            groups.Add(Make(ChangeKind.Moved, r.Moved.Select(m => m.Was.ItemId).ToList(), r.Moved.Select(m => m.Was).ToList()));
        if (r.Gone.Count > 0)
            groups.Add(Make(ChangeKind.Gone, r.Gone.Select(t => t.ItemId).ToList(), r.Gone.ToList()));
        if (r.Redyed.Count > 0)
            groups.Add(Make(ChangeKind.Redyed, r.Redyed.Select(m => m.Was.ItemId).ToList(), Array.Empty<Target>()));
        if (r.Added.Count > 0)
            groups.Add(Make(ChangeKind.Added, r.Added.Select(p => p.ItemId).ToList(), Array.Empty<Target>()));
        return groups;
    }

    private static ChangeGroup Make(ChangeKind kind, List<uint> items, IReadOnlyList<Target> where) =>
        new(kind, $"{Label(kind)} · {items.Count}", items, where);
}

/// <summary>
/// Which fixture of how many the fixtures' run is on ("1 of 3"), for its panel: the fixtures it set
/// out to set (their item names and slots, in its order) are taken when it's first seen running, and
/// each step's words are matched against them (a step names the fixture it's for: "Put Oasis Wall in
/// the ground floor's walls slot", "Is Oasis Wall in its slot?"). It only ever goes forward. Pure, so
/// it's tested.
/// </summary>
public sealed class FixtureProgress
{
    private readonly List<(string Name, string Slot)> fixtures = new();
    private string? lastLabel;

    /// <summary>The fixture it's on, from 1 (0 until one is named).</summary>
    public int At { get; private set; }

    public int Of => Math.Max(fixtures.Count, At);

    /// <summary>A new run: the fixtures it sets, in order (each one's item name and its slot's words, "ground floor's walls").</summary>
    public void Begin(IEnumerable<(string Name, string Slot)> toSet)
    {
        fixtures.Clear();
        fixtures.AddRange(toSet);
        At = 0;
        lastLabel = null;
    }

    /// <summary>The step the run is on, in words: moves on to the fixture it names, if a later one.</summary>
    public void See(string? label)
    {
        if (label == null || label == lastLabel) return;
        lastLabel = label;
        var from = Math.Max(0, At - 1);
        // Its slot named too picks out one of two of the same item; else the first named from the one it's on.
        for (var i = from; i < fixtures.Count; i++)
        {
            var (name, slot) = fixtures[i];
            if (name.Length > 0 && label.Contains($"{name} in the {slot} slot", StringComparison.Ordinal))
            {
                At = i + 1;
                return;
            }
        }
        for (var i = from; i < fixtures.Count; i++)
        {
            var name = fixtures[i].Name;
            if (name.Length > 0 && label.Contains(name, StringComparison.Ordinal))
            {
                At = Math.Max(At, i + 1);
                return;
            }
        }
    }

    /// <summary>"1 of 3" (the first until one is named); null when it has none.</summary>
    public string? Text => Of == 0 ? null : $"{Math.Max(1, At)} of {Of}";
}
