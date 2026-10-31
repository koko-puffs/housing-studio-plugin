namespace HousingStudio.UI.Model;

/// <summary>
/// A rehearsal's words (Ready's Preview menu, Running, Done, the errands): a run that does every step
/// a real start does and commits nothing. Pure, so it's tested.
/// </summary>
public static class RehearsalWords
{
    public const string Mark = "Mark the spots";
    public const string MarkNote = "Today's preview: lights each spot, moves nothing";
    public const string Rehearse = "Rehearse the run";
    public const string RehearseNote = "Does every step, commits nothing";

    /// <summary>The eyebrow while it runs.</summary>
    public const string Eyebrow = "Rehearsal · nothing is committed";

    /// <summary>The teleports are real: the only gil a rehearsal spends.</summary>
    public const string Teleports = "Teleports are real: their fees are the only gil a rehearsal spends.";

    /// <summary>The errands' brass button in a rehearsal.</summary>
    public const string ErrandsButton = "Rehearse these errands";

    /// <summary>The pill: "Rehearsing · 4 minutes left", "Rehearsing" when the time isn't known.</summary>
    public static string Pill(string? left) => string.IsNullOrEmpty(left) ? "Rehearsing" : $"Rehearsing · {left} left";

    /// <summary>The menu row's tip for Rehearse, saying what it follows (Ready's choices, as the brass button would).</summary>
    public static string RehearseTip(string goLabel, bool getting) =>
        getting
            ? $"Does what \"{goLabel}\" would, step by step: the trips (asking for each purchase and cancelling), then placing. Nothing is bought or placed; teleports are real."
            : $"Does what \"{goLabel}\" would, step by step: each piece held at its spot and let go, each dye picked and closed. Nothing is placed, moved, stored or dyed.";

    /// <summary>Done's title: ("Rehearsal", "done") or ("Rehearsal", "stopped").</summary>
    public static (string Plain, string Italic) Title(bool stopped) => ("Rehearsal", stopped ? "stopped" : "done");

    /// <summary>Done's line under the title: what didn't work, in a few words.</summary>
    public static string Line(int problems, bool stopped, string? why)
    {
        var head = problems switch
        {
            0 => "Every step worked",
            1 => "1 step didn't work",
            _ => $"{problems} steps didn't work",
        };
        if (stopped && !string.IsNullOrEmpty(why)) return $"{head}. It stopped: {why}";
        return $"{head}, and nothing was committed.";
    }

    /// <summary>The figure's words while it runs: "of 120 pieces rehearsed".</summary>
    public static string Figure(int pieces) => $"of {pieces} pieces rehearsed";
}
