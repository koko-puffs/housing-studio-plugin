using System;
using System.Collections.Generic;
using Dalamud.Game;
using HousingStudio.Core;
using Lumina.Excel.Sheets;

namespace HousingStudio.Game;

/// <summary>
/// The menu words the run chooses entries by ("Place", "Store", "Paint"), read from the game's own
/// text (the Addon sheet, <see cref="MenuWords"/>) in the client's language, once each; the built-in
/// words when the rows can't be read or aren't what they should be.
/// </summary>
public static class GameWords
{
    private static readonly Dictionary<(string, ClientLanguage), (IReadOnlyList<string> Words, bool FromGame)> Read = new();
    private static readonly object Gate = new();

    public static IReadOnlyList<string> Place(ClientLanguage language, IReadOnlyList<string> builtIn) => Words("place", MenuWords.Place, language, builtIn);

    public static IReadOnlyList<string> Store(ClientLanguage language, IReadOnlyList<string> builtIn) => Words("store", MenuWords.Store, language, builtIn);

    public static IReadOnlyList<string> Paint(ClientLanguage language, IReadOnlyList<string> builtIn) => Words("paint", MenuWords.Paint, language, builtIn);

    /// <summary>Whether the "Place" words were read from the game (else they're the built-in ones), for Settings; false before they're read.</summary>
    public static bool PlaceFromGame(ClientLanguage language)
    {
        lock (Gate) return Read.TryGetValue(("place", language), out var r) && r.FromGame;
    }

    private static IReadOnlyList<string> Words(string what, IEnumerable<MenuWord> rows, ClientLanguage language, IReadOnlyList<string> builtIn)
    {
        lock (Gate)
        {
            if (Read.TryGetValue((what, language), out var known)) return known.Words;
            IReadOnlyList<string> words;
            try
            {
                var english = Services.Data.GetExcelSheet<Addon>(ClientLanguage.English);
                var client = Services.Data.GetExcelSheet<Addon>(language);
                words = MenuWords.Resolve(rows,
                    row => english.TryGetRow(row, out var r) ? r.Text.ExtractText() : null,
                    row => client.TryGetRow(row, out var r) ? r.Text.ExtractText() : null,
                    builtIn, language == ClientLanguage.English);
            }
            catch (Exception ex)
            {
                // A sheet that can't be read (a language's data not installed): the built-in words.
                Services.Log.Warning(ex, $"Couldn't read the game's words for \"{what}\"; using the built-in ones");
                words = builtIn;
            }
            if (ReferenceEquals(words, builtIn) && language != ClientLanguage.English)
                Services.Log.Warning($"The game's words for \"{what}\" couldn't be checked; using the built-in ones ({string.Join(", ", builtIn)}).");
            Read[(what, language)] = (words, !ReferenceEquals(words, builtIn));
            return words;
        }
    }
}
