using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HousingStudio.Core;

/// <summary>What a rehearsal would have done at a commit point (and didn't).</summary>
public enum RehearsalAct
{
    /// <summary>Place a piece (from the storeroom or your bags).</summary>
    Place,

    /// <summary>Move a placed piece to its spot.</summary>
    Move,

    /// <summary>Put a piece away in the storeroom (clear out).</summary>
    Store,

    /// <summary>Dye a piece.</summary>
    Dye,

    /// <summary>Buy something (a vendor, the market board).</summary>
    Buy,

    /// <summary>Take something from a retainer.</summary>
    Take,

    /// <summary>Set a fixture.</summary>
    Fixture,
}

/// <summary>Why a step of a rehearsal goes in its "what didn't work".</summary>
public enum RehearsalTrouble
{
    /// <summary>A step didn't work (the run's own message says why).</summary>
    Failed,

    /// <summary>Something waited for didn't come in time.</summary>
    TimedOut,

    /// <summary>The game's question or menu didn't read as the run expects (a real run would say no, or stop).</summary>
    NoMatch,

    /// <summary>A real run would stop here (gil, bag slots); the rehearsal went on.</summary>
    WouldStop,

    /// <summary>The rehearsal's guard refused to send something that commits (a step that shouldn't have tried).</summary>
    Refused,
}

/// <summary>One thing that didn't work in a rehearsal: where (a piece, a trip's step), what, and what kind.</summary>
public sealed record RehearsalIssue(string Where, string What, RehearsalTrouble Kind);

/// <summary>
/// A rehearsal's report: how many of each thing it would have done (placed, moved, stored, dyed,
/// bought and for how much, taken from retainers, fixtures set), how the game's purchase questions
/// read against the prices, and what didn't work, each with its message. One report covers one
/// rehearsal from Ready, its errands, its placing and its fixtures alike. Pure, so it's tested.
/// </summary>
public sealed class RehearsalReport
{
    private readonly Dictionary<RehearsalAct, int> counts = new();
    private readonly List<RehearsalIssue> issues = new();

    /// <summary>The design rehearsed.</summary>
    public string Title { get; private set; } = string.Empty;

    public DateTime StartedAt { get; private set; }

    /// <summary>Changes whenever anything is added (the window caches what it draws by it).</summary>
    public int Version { get; private set; }

    /// <summary>The gil the purchases would have spent.</summary>
    public long BuyGil { get; private set; }

    /// <summary>Of the dyes, those only counted: their piece isn't placed (a rehearsal places nothing), so they weren't tried.</summary>
    public int DyesOncePlaced { get; private set; }

    /// <summary>Purchase questions that named the price planned, and those that didn't.</summary>
    public int QuestionsMatched { get; private set; }

    public int QuestionsNotMatched { get; private set; }

    /// <summary>Teleports made (their fees are the only gil a rehearsal spends).</summary>
    public int Teleports { get; private set; }

    public IReadOnlyList<RehearsalIssue> Issues => issues;

    private readonly List<string> notes = new();

    /// <summary>What the rehearsal left out, and why ("Getting wasn't rehearsed: …").</summary>
    public IReadOnlyList<string> Notes => notes;

    /// <summary>Notes something the rehearsal left out (said under the counts).</summary>
    public void Note(string line)
    {
        if (!string.IsNullOrWhiteSpace(line) && !notes.Contains(line)) notes.Add(line);
        Version++;
    }

    /// <summary>A new rehearsal: everything counted so far is forgotten.</summary>
    public void Begin(string title, DateTime at)
    {
        counts.Clear();
        issues.Clear();
        notes.Clear();
        BuyGil = 0;
        DyesOncePlaced = 0;
        QuestionsMatched = QuestionsNotMatched = 0;
        Teleports = 0;
        Title = title;
        StartedAt = at;
        Version++;
    }

    public int Count(RehearsalAct act) => counts.TryGetValue(act, out var n) ? n : 0;

    /// <summary>It would have done this (<paramref name="n"/> of them; <paramref name="gil"/> for a purchase).</summary>
    public void Would(RehearsalAct act, int n = 1, long gil = 0)
    {
        if (n <= 0) return;
        counts[act] = Count(act) + n;
        BuyGil += Math.Max(0, gil);
        Version++;
    }

    /// <summary>A dye counted for a piece the rehearsal didn't place: it would be dyed once placed (not tried).</summary>
    public void WouldDyeOncePlaced(int n = 1)
    {
        if (n <= 0) return;
        Would(RehearsalAct.Dye, n);
        DyesOncePlaced += n;
    }

    /// <summary>A purchase's question was read and checked against the price planned.</summary>
    public void Question(bool matched)
    {
        if (matched) QuestionsMatched++;
        else QuestionsNotMatched++;
        Version++;
    }

    public void Teleported()
    {
        Teleports++;
        Version++;
    }

    public void Issue(string where, string what, RehearsalTrouble kind = RehearsalTrouble.Failed)
    {
        issues.Add(new RehearsalIssue(where, what, kind));
        Version++;
    }

    /// <summary>Nothing counted and nothing went wrong yet.</summary>
    public bool IsEmpty => counts.Values.All(n => n == 0) && issues.Count == 0;

    /// <summary>
    /// "Would place 12, move 3, store 2, dye 4 (1 once placed), buy 6 for 12,400 gil, take 2 from
    /// retainers and set 3 fixtures." Only what it would have done; "Nothing to do" when nothing.
    /// </summary>
    public string CountsLine()
    {
        var parts = new List<string>();
        void Add(RehearsalAct act, string words)
        {
            if (Count(act) > 0) parts.Add(words);
        }
        Add(RehearsalAct.Place, $"place {Count(RehearsalAct.Place)}");
        Add(RehearsalAct.Move, $"move {Count(RehearsalAct.Move)}");
        Add(RehearsalAct.Store, $"store {Count(RehearsalAct.Store)}");
        Add(RehearsalAct.Dye, DyesOncePlaced > 0 ? $"dye {Count(RehearsalAct.Dye)} ({DyesOncePlaced} once placed)" : $"dye {Count(RehearsalAct.Dye)}");
        Add(RehearsalAct.Buy, BuyGil > 0 ? $"buy {Count(RehearsalAct.Buy)} for {ShoppingList.Gil(BuyGil)}" : $"buy {Count(RehearsalAct.Buy)}");
        Add(RehearsalAct.Take, $"take {Count(RehearsalAct.Take)} from retainers");
        Add(RehearsalAct.Fixture, Count(RehearsalAct.Fixture) == 1 ? "set 1 fixture" : $"set {Count(RehearsalAct.Fixture)} fixtures");
        if (parts.Count == 0) return "Nothing it would have done.";
        var list = parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        return $"Would {list}.";
    }

    /// <summary>"3 purchase questions named the price planned; 1 didn't.", or null when none was asked.</summary>
    public string? QuestionsLine()
    {
        var all = QuestionsMatched + QuestionsNotMatched;
        if (all == 0) return null;
        var head = all == 1 ? "1 purchase question" : $"{all} purchase questions";
        if (QuestionsNotMatched == 0) return all == 1 ? $"{head} named the price planned." : $"{head}, each naming the price planned.";
        if (QuestionsMatched == 0) return all == 1 ? $"{head} didn't name the price planned." : $"{head}, none naming the price planned.";
        return $"{head}: {QuestionsMatched} named the price planned, {QuestionsNotMatched} didn't.";
    }

    /// <summary>The kind of trouble in a word or two, for the report.</summary>
    public static string KindWord(RehearsalTrouble kind) => kind switch
    {
        RehearsalTrouble.TimedOut => "timed out",
        RehearsalTrouble.NoMatch => "didn't match",
        RehearsalTrouble.WouldStop => "a real run would stop",
        RehearsalTrouble.Refused => "refused: it would commit",
        _ => "failed",
    };

    /// <summary>The report as text, for Copy report: the counts, the questions, what didn't work, then the log (if given).</summary>
    public string Text(string? log = null)
    {
        var sb = new StringBuilder();
        sb.Append("Housing Studio rehearsal");
        if (Title.Length > 0) sb.Append(" of ").Append(Title);
        if (StartedAt != default) sb.Append(", ").Append(StartedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        sb.AppendLine();
        sb.AppendLine("Nothing was committed: nothing placed, moved, stored, dyed, bought or set.");
        sb.AppendLine(CountsLine());
        if (QuestionsLine() is { } q) sb.AppendLine(q);
        if (Teleports > 0) sb.AppendLine(Teleports == 1 ? "1 teleport (its fee is the only gil spent)." : $"{Teleports} teleports (their fees are the only gil spent).");
        foreach (var n in notes) sb.AppendLine(n);
        sb.AppendLine();
        if (issues.Count == 0) sb.AppendLine("What didn't work: nothing.");
        else
        {
            sb.Append("What didn't work (").Append(issues.Count).AppendLine("):");
            foreach (var i in issues) sb.Append("- ").Append(i.Where).Append(": ").Append(i.What).Append(" (").Append(KindWord(i.Kind)).AppendLine(")");
        }
        if (!string.IsNullOrEmpty(log))
        {
            sb.AppendLine();
            sb.AppendLine("Log:");
            sb.Append(log.TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}

/// <summary>
/// The window callbacks that commit something, by their window and values: Yes to the game's
/// question (a purchase, a placement, "Proceed with fixture changes?"), the dye window's Dye and its
/// confirmation, the fixtures window's apply. A rehearsal never sends these (its own steps don't, and
/// <see cref="Rehearsals"/> refuses them as a second guard). Pure, so it's tested.
/// </summary>
public static class RehearsalRules
{
    public const string YesNo = "SelectYesno";
    public const string DyeWindow = "ColorantColoring";
    public const string DyeConfirm = "MiragePrismMiragePlateConfirm";
    public const string InteriorFixtures = "HousingEditInterior";
    public const string ExteriorFixtures = "HousingEditExterior";

    /// <summary>What this callback commits, in words, or null when it commits nothing.</summary>
    public static string? Commits(string addon, IReadOnlyList<(ArgKind Kind, long Value)> values)
    {
        var first = values.Count > 0 && values[0].Kind is ArgKind.Int or ArgKind.UInt ? values[0].Value : (long?)null;
        return addon switch
        {
            YesNo when first == 0 => "Yes to the game's question",
            DyeWindow when first == 0 && values.Count == 1 => "the dye window's Dye button",
            DyeConfirm when first == 0 => "Yes to dyeing the piece",
            InteriorFixtures or ExteriorFixtures when first == 0 => "the fixtures window's apply",
            _ => null,
        };
    }

    public static string? Commits(UiCall call, IReadOnlyDictionary<string, long>? blanks = null) => Commits(call.Addon, call.Resolve(blanks));
}

/// <summary>
/// The rehearsal going now, for every part of the plugin: its report, and the guard. Each runner
/// says whether it's rehearsing (<see cref="Register"/>); while one is, the clicks that commit
/// (<see cref="RehearsalRules"/>) and the editor's set-down are refused and noted in the report, so a
/// rehearsal can't commit even through a step that forgot to check. Nothing changes for a real run:
/// the guard is off unless a runner says it's rehearsing. Framework thread.
/// </summary>
public static class Rehearsals
{
    private static readonly List<Func<bool>> Sources = new();
    private static int allowing;

    /// <summary>The report of the rehearsal going now, or the last one.</summary>
    public static RehearsalReport Report { get; } = new();

    /// <summary>Adds a runner's "am I rehearsing now" (asked each time the guard is consulted).</summary>
    public static void Register(Func<bool> rehearsing)
    {
        lock (Sources) Sources.Add(rehearsing);
    }

    public static void Unregister(Func<bool> rehearsing)
    {
        lock (Sources) Sources.Remove(rehearsing);
    }

    /// <summary>A rehearsal is going now (any runner's).</summary>
    public static bool Active
    {
        get
        {
            lock (Sources)
            {
                foreach (var s in Sources)
                    if (s()) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Lets one click through the guard that commits nothing in a rehearsal even though it reads like
    /// one that does (Yes to "Enter the estate hall?": going in is part of the trip). Dispose to end it.
    /// </summary>
    public static IDisposable Allow() => new Allowance();

    /// <summary>
    /// True (and noted in the report) when a rehearsal is going and this callback would commit: the
    /// caller doesn't send it.
    /// </summary>
    public static bool Blocks(string addon, IReadOnlyList<(ArgKind Kind, long Value)> values)
    {
        if (allowing > 0 || !Active) return false;
        if (RehearsalRules.Commits(addon, values) is not { } what) return false;
        Report.Issue("The rehearsal's guard", $"Refused to send {addon} ← [{string.Join(", ", values.Select(v => v.Kind == ArgKind.Empty ? "empty" : v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))}] ({what}): it would commit.", RehearsalTrouble.Refused);
        return true;
    }

    /// <summary>True (and noted) when a rehearsal is going: something that commits by itself (the editor's set-down) isn't done.</summary>
    public static bool Blocks(string what)
    {
        if (allowing > 0 || !Active) return false;
        Report.Issue("The rehearsal's guard", $"Refused {what}: it would commit.", RehearsalTrouble.Refused);
        return true;
    }

    private sealed class Allowance : IDisposable
    {
        private bool done;

        public Allowance() => allowing++;

        public void Dispose()
        {
            if (done) return;
            done = true;
            allowing--;
        }
    }
}
