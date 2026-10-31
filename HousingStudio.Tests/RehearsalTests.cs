using System;
using System.Collections.Generic;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

public class RehearsalReportTests
{
    [Fact]
    public void EmptyReportSaysNothing()
    {
        var r = new RehearsalReport();
        Assert.True(r.IsEmpty);
        Assert.Equal("Nothing it would have done.", r.CountsLine());
        Assert.Null(r.QuestionsLine());
    }

    [Fact]
    public void CountsLineListsWhatItWouldDoInOrder()
    {
        var r = new RehearsalReport();
        r.Would(RehearsalAct.Move, 3);
        r.Would(RehearsalAct.Place, 12);
        r.Would(RehearsalAct.Store, 2);
        r.Would(RehearsalAct.Dye, 3);
        r.WouldDyeOncePlaced();
        r.Would(RehearsalAct.Buy, 4, 10_000);
        r.Would(RehearsalAct.Buy, 2, 2_400);
        r.Would(RehearsalAct.Take, 2);
        r.Would(RehearsalAct.Fixture, 3);
        Assert.Equal("Would place 12, move 3, store 2, dye 4 (1 once placed), buy 6 for 12,400 gil, take 2 from retainers and set 3 fixtures.", r.CountsLine());
        Assert.Equal(12_400, r.BuyGil);
        Assert.Equal(4, r.Count(RehearsalAct.Dye));
    }

    [Fact]
    public void OneThingHasNoAnd()
    {
        var r = new RehearsalReport();
        r.Would(RehearsalAct.Fixture);
        Assert.Equal("Would set 1 fixture.", r.CountsLine());
        r.Would(RehearsalAct.Move, 0);
        Assert.Equal("Would set 1 fixture.", r.CountsLine());
    }

    [Fact]
    public void QuestionsLineSaysHowTheyRead()
    {
        var r = new RehearsalReport();
        r.Question(true);
        Assert.Equal("1 purchase question named the price planned.", r.QuestionsLine());
        r.Question(true);
        Assert.Equal("2 purchase questions, each naming the price planned.", r.QuestionsLine());
        r.Question(false);
        Assert.Equal("3 purchase questions: 2 named the price planned, 1 didn't.", r.QuestionsLine());
        var none = new RehearsalReport();
        none.Question(false);
        Assert.Equal("1 purchase question didn't name the price planned.", none.QuestionsLine());
    }

    [Fact]
    public void BeginForgetsTheLastRehearsal()
    {
        var r = new RehearsalReport();
        r.Would(RehearsalAct.Place, 2);
        r.Issue("Riviera Table", "Didn't show.");
        r.Teleported();
        var v = r.Version;
        r.Begin("Winter Lodge", new DateTime(2026, 10, 31, 12, 0, 0));
        Assert.True(r.IsEmpty);
        Assert.Equal(0, r.Teleports);
        Assert.Equal("Winter Lodge", r.Title);
        Assert.True(r.Version > v);
    }

    [Fact]
    public void TextHasCountsIssuesAndLog()
    {
        var r = new RehearsalReport();
        r.Begin("Winter Lodge", new DateTime(2026, 10, 31, 12, 5, 0));
        r.Would(RehearsalAct.Place, 2);
        r.Teleported();
        r.Issue("Riviera Table", "The game didn't show the piece's preview.");
        r.Issue("Buying 2 × Lumber", "Not enough gil. A real run would stop here.", RehearsalTrouble.WouldStop);
        var text = r.Text("12:05 Rehearsal started");
        Assert.Contains("Housing Studio rehearsal of Winter Lodge, 2026-10-31 12:05", text);
        Assert.Contains("Nothing was committed", text);
        Assert.Contains("Would place 2.", text);
        Assert.Contains("1 teleport (its fee is the only gil spent).", text);
        Assert.Contains("What didn't work (2):", text);
        Assert.Contains("- Riviera Table: The game didn't show the piece's preview. (failed)", text);
        Assert.Contains("(a real run would stop)", text);
        Assert.Contains("Log:", text);
        Assert.Contains("12:05 Rehearsal started", text);
    }

    [Fact]
    public void TextWithoutIssuesSaysSo()
    {
        var r = new RehearsalReport();
        Assert.Contains("What didn't work: nothing.", r.Text());
        Assert.DoesNotContain("Log:", r.Text());
    }
}

public class RehearsalRulesTests
{
    [Theory]
    [InlineData("SelectYesno", "i:0", true)]
    [InlineData("SelectYesno", "i:1", false)]
    [InlineData("ColorantColoring", "i:0", true)]
    [InlineData("ColorantColoring", "i:-2", false)]
    [InlineData("ColorantColoring", "i:5 i:6 i:6 i:0", false)]
    [InlineData("ColorantColoring", "i:4 i:2", false)]
    [InlineData("MiragePrismMiragePlateConfirm", "i:0", true)]
    [InlineData("MiragePrismMiragePlateConfirm", "i:-1", false)]
    [InlineData("HousingEditInterior", "i:0 empty empty empty empty", true)]
    [InlineData("HousingEditExterior", "i:0 empty empty empty empty", true)]
    [InlineData("HousingEditInterior", "i:2 u:0 empty empty u:1", false)]
    [InlineData("HousingGoods", "i:1 u:3", false)]
    [InlineData("HousingLayout", "i:0", false)]
    [InlineData("HousingLayout", "i:2", false)]
    [InlineData("ContextMenu", "i:0 i:-1 u:0 empty empty", false)]
    [InlineData("Shop", "i:0 i:14 i:1 empty", false)]
    [InlineData("ItemSearchResult", "i:2 i:0", false)]
    [InlineData("Talk", "", false)]
    public void TheCallbacksThatCommit(string addon, string spec, bool commits)
    {
        var call = new UiCall(addon, spec, true, "test");
        Assert.Equal(commits, RehearsalRules.Commits(call) != null);
    }

    [Fact]
    public void BlanksAreFilledInBeforeJudging()
    {
        var yes = new UiCall("SelectYesno", "i:{answer}", true, "test");
        Assert.NotNull(RehearsalRules.Commits(yes, UiCallValues.Of(("answer", 0))));
        Assert.Null(RehearsalRules.Commits(yes, UiCallValues.Of(("answer", 1))));
    }
}

/// <summary>The guard is shared (static): its tests run one at a time.</summary>
[Collection("Rehearsal guard")]
public class RehearsalGuardTests
{
    private static readonly IReadOnlyList<(ArgKind, long)> Yes = new UiCall("SelectYesno", "i:0", true, "test").Resolve();
    private static readonly IReadOnlyList<(ArgKind, long)> Tab = new UiCall("HousingGoods", "i:11", true, "test").Resolve();

    [Fact]
    public void OffUnlessARunnerRehearses()
    {
        var on = false;
        Func<bool> source = () => on;
        Rehearsals.Register(source);
        try
        {
            Assert.False(Rehearsals.Active);
            Assert.False(Rehearsals.Blocks("SelectYesno", Yes));
            Assert.False(Rehearsals.Blocks("the editor's set-down"));
            on = true;
            Assert.True(Rehearsals.Active);
        }
        finally
        {
            Rehearsals.Unregister(source);
        }
        Assert.False(Rehearsals.Active);
    }

    [Fact]
    public void RefusesWhatCommitsAndNotesIt()
    {
        Func<bool> source = () => true;
        Rehearsals.Register(source);
        try
        {
            Rehearsals.Report.Begin("test", DateTime.Now);
            Assert.True(Rehearsals.Blocks("SelectYesno", Yes));
            Assert.False(Rehearsals.Blocks("HousingGoods", Tab));
            Assert.True(Rehearsals.Blocks("the editor's set-down"));
            Assert.Equal(2, Rehearsals.Report.Issues.Count);
            Assert.All(Rehearsals.Report.Issues, i => Assert.Equal(RehearsalTrouble.Refused, i.Kind));
        }
        finally
        {
            Rehearsals.Unregister(source);
        }
    }

    [Fact]
    public void AllowLetsOneThrough()
    {
        Func<bool> source = () => true;
        Rehearsals.Register(source);
        try
        {
            using (Rehearsals.Allow()) Assert.False(Rehearsals.Blocks("SelectYesno", Yes));
            Assert.True(Rehearsals.Blocks("SelectYesno", Yes));
            var a = Rehearsals.Allow();
            a.Dispose();
            a.Dispose();
            Assert.True(Rehearsals.Blocks("SelectYesno", Yes));
        }
        finally
        {
            Rehearsals.Unregister(source);
        }
    }
}

public class RehearsalWordsTests
{
    [Fact]
    public void Titles()
    {
        Assert.Equal(("Rehearsal", "done"), RehearsalWords.Title(false));
        Assert.Equal(("Rehearsal", "stopped"), RehearsalWords.Title(true));
    }

    [Theory]
    [InlineData(0, false, null, "Every step worked, and nothing was committed.")]
    [InlineData(1, false, null, "1 step didn't work, and nothing was committed.")]
    [InlineData(3, true, "You left the home.", "3 steps didn't work. It stopped: You left the home.")]
    [InlineData(2, true, null, "2 steps didn't work, and nothing was committed.")]
    public void Line(int problems, bool stopped, string? why, string expected) => Assert.Equal(expected, RehearsalWords.Line(problems, stopped, why));

    [Fact]
    public void PillAndFigure()
    {
        Assert.Equal("Rehearsing · about 4 minutes left", RehearsalWords.Pill("about 4 minutes"));
        Assert.Equal("Rehearsing", RehearsalWords.Pill(null));
        Assert.Equal("of 120 pieces rehearsed", RehearsalWords.Figure(120));
    }

    [Fact]
    public void TipSaysWhatItFollows()
    {
        Assert.Contains("\"Clear out 4, then place\"", RehearsalWords.RehearseTip("Clear out 4, then place", false));
        Assert.Contains("teleports are real", RehearsalWords.RehearseTip("Get 12, then place", true));
    }
}
