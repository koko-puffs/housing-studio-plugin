using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.UI.Model;

/// <summary>
/// The fixtures you've ticked as set, per design, kept in the configuration
/// (<c>FixturesDone</c>: design → fixture keys). Pure, so it's tested.
/// </summary>
public static class FixtureTicks
{
    /// <summary>A fixture's key within its design: its floor, kind and item.</summary>
    public static string KeyOf(string level, string type, uint itemId) => $"{level}|{type}|{itemId}";

    /// <summary>A design's key: its file (else its name), the same however the path was typed.</summary>
    public static string DesignKey(string? path, string title) => (path ?? title).Trim().Replace('\\', '/').ToLowerInvariant();

    public static bool IsDone(IReadOnlyDictionary<string, List<string>> store, string design, string fixture) =>
        store.TryGetValue(design, out var done) && done.Contains(fixture);

    /// <summary>Ticks or unticks a fixture; a design with nothing ticked is dropped from the store.</summary>
    public static void Set(Dictionary<string, List<string>> store, string design, string fixture, bool done)
    {
        if (!store.TryGetValue(design, out var list))
        {
            if (!done) return;
            list = new List<string>();
            store[design] = list;
        }
        if (done)
        {
            if (!list.Contains(fixture)) list.Add(fixture);
        }
        else
        {
            list.Remove(fixture);
            if (list.Count == 0) store.Remove(design);
        }
    }

    /// <summary>How many of these fixtures are ticked (ticks for fixtures the design no longer has don't count).</summary>
    public static int CountDone(IReadOnlyDictionary<string, List<string>> store, string design, IEnumerable<string> fixtures) =>
        store.TryGetValue(design, out var done) ? fixtures.Distinct().Count(done.Contains) : 0;
}
