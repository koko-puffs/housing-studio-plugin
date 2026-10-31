using System.Globalization;

namespace HousingStudio.UI.Model;

/// <summary>Done's one line of what happened: "445 of 447 pieces · 12 got on 2 trips for 58,920 gil". Pure, so it's tested.</summary>
public static class DoneWords
{
    /// <summary>"445 of 447 pieces"; "All 447 pieces" when every one is where the design has it.</summary>
    public static string Pieces(int figure, int total) =>
        total > 1 && figure >= total ? $"All {total} pieces" : $"{figure} of {Words.Count(total, "piece")}";

    /// <summary>What the errands did before placing: "12 got on 2 trips for 58,920 gil"; no gil, no "for".</summary>
    public static string Errands(int got, int trips, long gil) =>
        $"{got} got on {Words.Count(trips, "trip")}{(gil > 0 ? $" for {gil.ToString("N0", CultureInfo.InvariantCulture)} gil" : string.Empty)}";

    /// <summary>The pieces, then the errands when the run came from them.</summary>
    public static string Line(int figure, int total, (int Got, int Trips, long Gil)? errands) =>
        errands is { } e ? $"{Pieces(figure, total)} · {Errands(e.Got, e.Trips, e.Gil)}" : Pieces(figure, total);
}
