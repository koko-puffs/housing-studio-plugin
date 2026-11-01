using System.Collections.Generic;
using System.Numerics;
using HousingStudio.Core;
using HousingStudio.UI.Model;
using Xunit;

namespace HousingStudio.Tests;

/// <summary>0.9.0's placing screen, Done and the rehearsal's result: one to-do number, and how far a stopped rehearsal got.</summary>
public class PlacingScreenTests
{
    private static RunJob Job(JobKind kind, JobOutcome outcome = JobOutcome.Pending, int key = 0) => new()
    {
        Kind = kind,
        Target = new Target(key, 10, Vector3.Zero, 0, 0, Floor.Ground, false),
        Outcome = outcome,
    };

    private static List<RunJob> Jobs(int moves, int movesDone, int places, int placesDone, int dyes = 0, int dyesDone = 0, int stores = 0, int storesDone = 0)
    {
        var jobs = new List<RunJob>();
        void Add(JobKind kind, int n, int done)
        {
            for (var i = 0; i < n; i++) jobs.Add(Job(kind, i < done ? JobOutcome.Done : JobOutcome.Pending, jobs.Count));
        }
        Add(JobKind.Store, stores, storesDone);
        Add(JobKind.Move, moves, movesDone);
        Add(JobKind.PlaceFromStoreroom, places, placesDone);
        Add(JobKind.Dye, dyes, dyesDone);
        return jobs;
    }

    [Fact]
    public void TheFigureCountsTheMovesAndPlacementsOfThisRun()
    {
        // 46 to do (6 moves, 40 placements), 18 done; 20 dyes expected once placing is done.
        var f = RunFigure.From(Jobs(6, 6, 40, 12), false, 46, 20);
        Assert.Equal(new RunFigure(18, 46, 0, 20, 0, 0), f);
        Assert.Equal(18, f.Figure);
        Assert.Equal(46, f.Total);
        Assert.Equal(18f / 66f, f.Bar, 4);
    }

    [Fact]
    public void ADyeOnlyPieceIsntDoneBeforeItsDyed()
    {
        // Every piece in place, three only need their dye: nothing is done until a dye is.
        var f = RunFigure.From(Jobs(0, 0, 0, 0, 3, 0), false, 0, 0);
        Assert.Equal(0, f.Figure);
        Assert.Equal(3, f.Total);
        Assert.Equal(0f, f.Bar);
        var dyed = RunFigure.From(Jobs(0, 0, 0, 0, 3, 3), false, 0, 0);
        Assert.Equal(3, dyed.Figure);
        Assert.Equal(1f, dyed.Bar);
    }

    [Fact]
    public void PutAwaysArentTheDesignsPieces()
    {
        var f = RunFigure.From(Jobs(2, 2, 3, 1, stores: 4, storesDone: 4), false, 5, 0);
        Assert.Equal(3, f.Figure);
        Assert.Equal(5, f.Total);
        Assert.Equal(4, f.PutAwayDone);
        Assert.Equal(4, f.PutAway);
        // Putting back and nothing else: the put-aways are the figure.
        var only = RunFigure.From(Jobs(0, 0, 0, 0, stores: 5, storesDone: 2), false, 0, 0);
        Assert.Equal(2, only.Figure);
        Assert.Equal(5, only.Total);
    }

    [Fact]
    public void WhileAClearOutGoesFirstTheToDoIsTheOneItStartedWith()
    {
        // The design's pieces aren't planned yet: the run has only its put-aways.
        var f = RunFigure.From(Jobs(0, 0, 0, 0, stores: 30, storesDone: 3), true, 46, 20);
        Assert.Equal(0, f.Figure);
        Assert.Equal(46, f.Total);
        Assert.Equal(20, f.Dyes);
        // Planned again after it: the run's own pieces.
        Assert.Equal(44, RunFigure.From(Jobs(4, 0, 40, 0, stores: 30, storesDone: 30), false, 46, 20).Total);
    }

    [Fact]
    public void TheFiguresWords()
    {
        var f = new RunFigure(18, 46, 0, 20, 0, 0);
        Assert.Equal("of 46 to do · then 20 dyes", PlacingWords.Figure(f, RunKind.Placing, false));
        Assert.Equal("of 46 to do · 3 of 20 dyed", PlacingWords.Figure(f with { Done = 46, Dyed = 3 }, RunKind.Placing, true));
        Assert.Equal("of 46 to do · 0 of 20 dyed", PlacingWords.Figure(f with { Done = 46 }, RunKind.Placing, true));
        Assert.Equal("of 46 to do · then 1 dye", PlacingWords.Figure(f with { Dyes = 1 }, RunKind.Placing, false));
        Assert.Equal("of 46 to do", PlacingWords.Figure(f with { Dyes = 0 }, RunKind.Placing, false));
        Assert.Equal("of 46 marked · then 20 dyes", PlacingWords.Figure(f, RunKind.Preview, false));
        Assert.Equal("of 46 rehearsed · then 20 dyes", PlacingWords.Figure(f, RunKind.Rehearsal, false));
        Assert.Equal("of 20 to dye", PlacingWords.Figure(new RunFigure(0, 0, 4, 20, 0, 0), RunKind.Placing, true));
        Assert.Equal("of 20 dyes rehearsed", PlacingWords.Figure(new RunFigure(0, 0, 4, 20, 0, 0), RunKind.Rehearsal, true));
        Assert.Equal("of 5 to put away", PlacingWords.Figure(new RunFigure(0, 0, 0, 0, 2, 5), RunKind.Placing, false));
    }

    [Fact]
    public void TheEyebrowIsShort()
    {
        Assert.Equal("Placing · Solution9Loft", PlacingWords.Eyebrow(RunKind.Placing, "Solution9Loft"));
        Assert.Equal("Previewing · Solution9Loft", PlacingWords.Eyebrow(RunKind.Preview, "Solution9Loft"));
        Assert.Equal("Rehearsal · Solution9Loft", PlacingWords.Eyebrow(RunKind.Rehearsal, "Solution9Loft"));
        Assert.Equal("Placing", PlacingWords.Eyebrow(RunKind.Placing, ""));
        Assert.Equal("1 skipped so far · listed at the end", PlacingWords.Skipped(1));
    }

    [Fact]
    public void DonesLineUsesTheSameNumbers()
    {
        Assert.Equal("44 of 46 placed · 20 dyed · 2 need you", DoneWords.Line(new RunFigure(44, 46, 20, 21, 0, 0), 2));
        Assert.Equal("All 46 placed · 20 dyed", DoneWords.Line(new RunFigure(46, 46, 20, 20, 0, 0), 0));
        Assert.Equal("All 46 placed", DoneWords.Line(new RunFigure(46, 46, 0, 0, 0, 0), 0));
        Assert.Equal("1 of 1 placed · 1 needs you", DoneWords.Line(new RunFigure(1, 1, 0, 0, 0, 0), 1));
        Assert.Equal("18 of 20 dyed", DoneWords.Line(new RunFigure(0, 0, 18, 20, 0, 0), 0));
        Assert.Equal("All 20 dyed", DoneWords.Line(new RunFigure(0, 0, 20, 20, 0, 0), 0));
        Assert.Equal("5 of 5 put away", DoneWords.Line(new RunFigure(0, 0, 0, 0, 5, 5), 0));
        Assert.Equal("3 need you", DoneWords.Line(new RunFigure(), 3));
        Assert.Equal("Nothing was left to do", DoneWords.Line(new RunFigure(), 0));
    }

    [Theory]
    [InlineData(true, true, "Stopped by you.", "at the first stop", 0, "Stopped by you at the first stop. Nothing was bought or taken.")]
    [InlineData(true, false, "You left the home.", "at piece 3 of 46", 1, "Stopped at piece 3 of 46: You left the home. Nothing was committed.")]
    [InlineData(true, false, null, null, 0, "Stopped. Nothing was committed.")]
    [InlineData(false, false, null, null, 0, "Every step worked. Nothing was committed.")]
    [InlineData(false, false, null, null, 2, "2 steps didn't work. Nothing was committed.")]
    public void OneSentenceForTheOutcome(bool stopped, bool byYou, string? why, string? at, int problems, string expected)
    {
        var errands = expected.Contains("bought");
        Assert.Equal(expected, RehearsalWords.Outcome(stopped, byYou, why, at, problems, RehearsalWords.Nothing(errands, false)));
    }

    [Fact]
    public void WhatARehearsalDidntDo()
    {
        Assert.Equal("Nothing was committed.", RehearsalWords.Nothing(false, false));
        Assert.Equal("Nothing was bought or taken.", RehearsalWords.Nothing(true, false));
        Assert.Equal("Nothing was bought, taken or placed.", RehearsalWords.Nothing(true, true));
        Assert.Equal("What it did · 13 lines", RehearsalWords.LogTitle(13));
        Assert.Equal("What it did · 1 line", RehearsalWords.LogTitle(1));
    }

    [Fact]
    public void HowFarAPlacingRehearsalGot()
    {
        // Stopped by you part way through the first piece.
        var far = RehearsalWords.Pieces(new List<(string, JobKind, JobOutcome, bool)>
        {
            ("Everkeep Kitchen Counter", JobKind.PlaceFromStoreroom, JobOutcome.Skipped, true),
            ("Everkeep Rug", JobKind.PlaceFromStoreroom, JobOutcome.Pending, false),
        });
        Assert.Equal("0 of 2 pieces", far.Count);
        Assert.Equal("at the first piece", far.At);
        Assert.Equal(new[] { new HowFarRow("Everkeep Kitchen Counter", "Placing it from the storeroom · stopped part way", false) }, far.Rows);

        // Two worked, stopped between pieces: the next one is where it stopped.
        far = RehearsalWords.Pieces(new List<(string, JobKind, JobOutcome, bool)>
        {
            ("Chair", JobKind.Move, JobOutcome.Done, false),
            ("Table", JobKind.Move, JobOutcome.Done, false),
            ("Lamp", JobKind.PlaceFromBags, JobOutcome.Pending, false),
        });
        Assert.Equal("2 of 3 pieces", far.Count);
        Assert.Equal("at piece 3 of 3", far.At);
        Assert.Equal(2, far.Rows.Count);
        Assert.Equal(new HowFarRow("Table", "Moving it to its spot · the last of 2 that worked", true), far.Rows[0]);
        Assert.Equal(new HowFarRow("Lamp", "Placing it from your bags · not started", false), far.Rows[1]);

        Assert.Null(RehearsalWords.Pieces(new List<(string, JobKind, JobOutcome, bool)>()).At);
    }

    [Fact]
    public void HowFarTheErrandsRehearsalGot()
    {
        var stops = new List<(string, int)> { ("Housing Merchant · The Lavender Beds", 8), ("Market board", 90), ("Alys · your retainer", 4) };
        var far = RehearsalWords.Stops(stops, 0, 1);
        Assert.Equal("0 of 3 stops", far.Count);
        Assert.Equal("at the first stop", far.At);
        Assert.Equal(new HowFarRow("Setting out", "1 teleport made", true), far.Rows[0]);
        Assert.Equal(new HowFarRow("Housing Merchant · The Lavender Beds", "Nothing asked for yet · 8 things", false), far.Rows[1]);

        far = RehearsalWords.Stops(stops, 50, 2);
        Assert.Equal("1 of 3 stops", far.Count);
        Assert.Equal("at stop 2 of 3", far.At);
        Assert.Equal(new HowFarRow("Housing Merchant · The Lavender Beds", "8 things", true), far.Rows[0]);
        Assert.Equal(new HowFarRow("Market board", "Part way · 90 things", false), far.Rows[1]);

        far = RehearsalWords.Stops(stops, 102, 3);
        Assert.Equal("after the last stop", far.At);
        Assert.Null(RehearsalWords.Stops(new List<(string, int)>(), 0, 0).At);
    }
}
