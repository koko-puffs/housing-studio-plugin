using System;
using System.Collections.Generic;
using System.Linq;

namespace HousingStudio.Core;

/// <summary>A row of the game's Addon sheet (the words its windows and menus show), with its English words as a check.</summary>
public sealed record MenuWord(uint Row, string English);

/// <summary>
/// The menu entries the run chooses by their words, as rows of the game's own text (the Addon sheet),
/// so the words come in the client's language: German, French and Japanese clients need nothing typed.
/// Each row's English words are checked first: if a patch moved the rows, the built-in words are used
/// instead (and the setting still overrides both). Rows from the game's 2026.10 data:
/// <list type="bullet">
/// <item>"Place": 1751, an item's menu in your bags (beside Use, Hand Over, Sell/Donate), and 6229, the
/// housing windows' (Platzieren, Placer, 配置する).</item>
/// <item>"Store": 6840, beside Storeroom and Placed (Ins Lagerhaus legen, Ranger dans l'entrepôt, 倉庫に入れる).</item>
/// <item>"Paint": 6272, Furnishing Color's list menu (Färben, Teindre, 染色する).</item>
/// </list>
/// Pure, so it's tested.
/// </summary>
public static class MenuWords
{
    public static readonly MenuWord[] Place = { new(1751, "Place"), new(6229, "Place") };
    public static readonly MenuWord[] Store = { new(6840, "Store") };
    public static readonly MenuWord[] Paint = { new(6272, "Paint") };

    /// <summary>
    /// The words for an entry: each row's in the client's language (<paramref name="client"/>), from the
    /// rows whose English words (<paramref name="english"/>) are what they should be; none of those,
    /// the <paramref name="builtIn"/> words. An English client also keeps the built-in words (the same).
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<MenuWord> rows, Func<uint, string?> english, Func<uint, string?> client,
        IReadOnlyList<string> builtIn, bool clientIsEnglish)
    {
        var found = new List<string>();
        foreach (var row in rows)
        {
            string? en, here;
            try
            {
                en = english(row.Row);
                here = client(row.Row);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
            {
                continue;
            }
            if (en == null || here == null) continue;
            if (!string.Equals(Menus.Clean(en), row.English, StringComparison.OrdinalIgnoreCase)) continue;
            var word = Menus.Clean(here);
            if (word.Length > 0 && !found.Contains(word, StringComparer.OrdinalIgnoreCase)) found.Add(word);
        }
        if (found.Count == 0) return builtIn;
        if (clientIsEnglish)
            foreach (var w in builtIn)
                if (!found.Contains(w, StringComparer.OrdinalIgnoreCase)) found.Add(w);
        return found;
    }
}
