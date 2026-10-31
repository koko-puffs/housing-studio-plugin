namespace HousingStudio.UI.Model;

/// <summary>
/// Done's one line of what happened: "445 of 447 pieces". (What the errands got is said on their own
/// summary, which comes in Done's place after them.) Pure, so it's tested.
/// </summary>
public static class DoneWords
{
    /// <summary>"445 of 447 pieces"; "All 447 pieces" when every one is where the design has it.</summary>
    public static string Pieces(int figure, int total) =>
        total > 1 && figure >= total ? $"All {total} pieces" : $"{figure} of {Words.Count(total, "piece")}";

    public static string Line(int figure, int total) => Pieces(figure, total);
}
