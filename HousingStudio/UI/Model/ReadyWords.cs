using System.Collections.Generic;
using HousingStudio.Core;

namespace HousingStudio.UI.Model;

/// <summary>What Ready's Place stage does with the pieces placed here that aren't in the design.</summary>
public enum ReadyMode
{
    /// <summary>They stay where they are.</summary>
    Keep,

    /// <summary>They go to the storeroom first.</summary>
    ClearOut,

    /// <summary>What's here is kept under a name to swap back to, then it clears out and places.</summary>
    Swap,
}

/// <summary>
/// Ready's words (the 0.6 flow): the brass button saying what it does, the note under Keep / Clear
/// out / Swap, the estimate under the button, the check chips' words. Pure, so it's tested.
/// </summary>
public static class ReadyWords
{
    /// <summary>
    /// Clear out's ask, when the storeroom hasn't room for every piece: " The storeroom has room for
    /// 3: those go first, and the other 26 once the design is placed (…)". Empty when it has room, or
    /// isn't known.
    /// </summary>
    public static string StoreroomRoomNote(int clearOut, int? room)
    {
        if (room is not { } r || r >= clearOut || clearOut <= 0) return string.Empty;
        if (r <= 0)
            return $" The storeroom is full: the {clearOut} go once the design is placed (each piece placed from the storeroom makes room for one), and any it still has no room for are left for you.";
        return $" The storeroom has room for {r}: those go first, and the other {clearOut - r} once the design is placed (each piece placed from the storeroom makes room for one); any it still has no room for are left for you.";
    }

    /// <summary>
    /// The brass button: "Get 12, then place", "Get 12, then clear out 14", "Get 12, then swap";
    /// without getting first "Clear out 14, then place", "Swap and place", "Start placing"; and
    /// " · fixtures" when they're set after placing.
    /// </summary>
    /// <param name="toGet">Things missing (pieces and dyes); counts only with <paramref name="getFirst"/>.</param>
    /// <param name="clearOut">The pieces Clear out puts away (Clear out with none is Keep).</param>
    public static string Go(ReadyMode mode, int toGet, int clearOut, bool getFirst, bool fixturesAfter = false)
    {
        if (mode == ReadyMode.ClearOut && clearOut <= 0) mode = ReadyMode.Keep;
        string words;
        if (getFirst && toGet > 0)
            words = mode switch
            {
                ReadyMode.ClearOut => $"Get {toGet}, then clear out {clearOut}",
                ReadyMode.Swap => $"Get {toGet}, then swap",
                _ => $"Get {toGet}, then place",
            };
        else
            words = mode switch
            {
                ReadyMode.ClearOut => $"Clear out {clearOut}, then place",
                ReadyMode.Swap => "Swap and place",
                _ => "Start placing",
            };
        return fixturesAfter ? words + " · fixtures" : words;
    }

    /// <summary>The segment's words: "Clear out 14", or "Clear out" when there's nothing to clear.</summary>
    public static string ClearOutLabel(int pieces) => pieces > 0 ? $"Clear out {pieces}" : "Clear out";

    /// <summary>
    /// The line under Keep / Clear out / Swap.
    /// </summary>
    /// <param name="extras">Pieces placed here that aren't in the design (all of them).</param>
    /// <param name="pieces">Of those, the ones on the floors placed and near you: what Clear out takes.</param>
    /// <param name="keepAs">A swap's name for what's here.</param>
    public static string ModeNote(ReadyMode mode, int extras, int pieces, string? keepAs)
    {
        var one = pieces == 1;
        switch (mode)
        {
            case ReadyMode.ClearOut when pieces > 0:
                return $"First, the {Words.Count(pieces, "piece")} not in the design {(one ? "goes" : "go")} to your storeroom.";
            case ReadyMode.Swap when pieces > 0:
                return $"What's here is kept as “{keepAs}” so you can swap back. Then it clears out and places.";
        }
        if (pieces > 0)
            return one ? "The 1 piece not in the design stays where it is." : $"The {pieces} pieces not in the design stay where they are.";
        if (extras > 0)
            return $"{Words.Capital(Words.Count(extras, "piece"))} here {(extras == 1 ? "isn't" : "aren't")} in the design, none on the floors placed or near you, so nothing is put away.";
        return "Everything placed here is in the design.";
    }

    /// <summary>
    /// The line under the brass button: "About 25 minutes at a quick pace"; getting first, "2 trips
    /// first, then about 25 minutes at a quick pace".
    /// </summary>
    /// <param name="about">"about 25 minutes" (<see cref="Words.About"/>).</param>
    /// <param name="trips">The errands' trips, 0 when not known.</param>
    /// <param name="notGetting">Get first is on but the errands can't go (why): the button places what you have, and says so first.</param>
    public static string Hint(string about, string pace, bool getFirst, int trips = 0, string? notGetting = null)
    {
        if (!getFirst && notGetting != null)
        {
            var why = notGetting.TrimEnd();
            if (why.Length > 0 && why[^1] is not ('.' or '!' or '?')) why += ".";
            return $"Not getting first: {why} {Words.Capital(about)} at a {pace} pace";
        }
        if (!getFirst) return $"{Words.Capital(about)} at a {pace} pace";
        return trips > 0 ? $"{Words.Count(trips, "trip")} first, then {about} at a {pace} pace" : $"The errands first, then {about} at a {pace} pace";
    }

    /// <summary>The Place stage's big words: "443 to do", "87 to dye" (all in place, the dyes left), "Nothing to do".</summary>
    public static string PlaceBig(int toDo, int dyes) => toDo > 0 ? $"{toDo} to do" : dyes > 0 ? $"{dyes} to dye" : "Nothing to do";

    /// <summary>The floors left out as a chip: "Basement left out", "2 floors left out".</summary>
    public static string FloorsLeftOut(IReadOnlyList<string> off) =>
        off.Count == 1 ? $"{Words.Capital(off[0])} left out" : $"{off.Count} floors left out";

    /// <summary>The dyes short as a chip: "3 dyes short" (each dye counted as many times as it's needed).</summary>
    public static string DyesShort(int count) => $"{Words.Count(count, "dye")} short";

    /// <summary>
    /// Under Get's "102 to get": what that is, so it matches the Pieces screen and the shopping list
    /// without a sum ("99 pieces and 3 fixtures", "99 pieces, 3 fixtures and 2 dyes"); null for nothing.
    /// </summary>
    public static string? GetLine(int pieces, int fixtures, int dyes)
    {
        var parts = new List<string>();
        if (pieces > 0) parts.Add(Words.Count(pieces, "piece"));
        if (fixtures > 0) parts.Add(Words.Count(fixtures, "fixture"));
        if (dyes > 0) parts.Add(Words.Count(dyes, "dye"));
        return parts.Count == 0 ? null : Words.List(parts);
    }

    /// <summary>A Get source chip's words after its count: it counts things, not retainers or vendors ("1 on a retainer", "30 from shops", "71 on the board").</summary>
    public static string SourceLabel(SourceKind source, int count) => source switch
    {
        SourceKind.Retainer => count == 1 ? "on a retainer" : "on retainers",
        SourceKind.Vendor => "from shops",
        SourceKind.Exchange => "for currencies",
        SourceKind.Market => "on the board",
        SourceKind.Craft => "to craft",
        _ => "not known",
    };

    /// <summary>The item limit chip ("46 of 150 item limit").</summary>
    public static string LimitChip(int after, int limit) => $"{after} of {limit} item limit";

    /// <summary>Ready's Pieces chip ("All 145 pieces").</summary>
    public static string PiecesChip(int count) => count == 1 ? "1 piece" : $"All {count} pieces";
}
