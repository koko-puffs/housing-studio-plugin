using System;
using System.Collections.Generic;

namespace HousingStudio.Core;

/// <summary>What a step says after it's run once.</summary>
public enum StepStatus
{
    /// <summary>Finished: go to the next step.</summary>
    Done,

    /// <summary>Not yet (waiting for a window, the game…): run me again next frame, until my timeout.</summary>
    Waiting,

    /// <summary>This piece can't be done: run the clean-up, mark the piece, and go on to the next piece.</summary>
    SkipJob,

    /// <summary>Something's wrong: run the clean-up and stop the whole run.</summary>
    Stop,
}

public readonly record struct StepOutcome(StepStatus Status, string? Message = null)
{
    public static StepOutcome Done => new(StepStatus.Done);
    public static StepOutcome Waiting => new(StepStatus.Waiting);
    public static StepOutcome Skip(string why) => new(StepStatus.SkipJob, why);
    public static StepOutcome Halt(string why) => new(StepStatus.Stop, why);
}

/// <summary>
/// One thing to do or wait for. <see cref="IsAction"/> steps change something in the game (a click,
/// a callback, setting a piece down); in step-by-step mode the queue stops before each of them until
/// you press Continue. Waits time out after <see cref="Timeout"/> with <see cref="OnTimeout"/>.
/// </summary>
public sealed class Step
{
    public required string Label { get; init; }
    public required Func<StepOutcome> Run { get; init; }
    public bool IsAction { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>What a timeout means: by default the whole run stops ("an expected window didn't appear").</summary>
    public StepStatus OnTimeout { get; init; } = StepStatus.Stop;

    /// <summary>What to say when it times out (default: "Timed out: label").</summary>
    public string? TimeoutMessage { get; init; }

    /// <summary>A short wait before the step first runs (the game needs a moment after a click).</summary>
    public TimeSpan Delay { get; init; }

    public static Step Wait(string label, Func<bool> until, TimeSpan timeout, StepStatus onTimeout = StepStatus.Stop) =>
        new() { Label = label, Run = () => until() ? StepOutcome.Done : StepOutcome.Waiting, Timeout = timeout, OnTimeout = onTimeout };

    public static Step Pause(string label, TimeSpan delay) =>
        new() { Label = label, Run = () => StepOutcome.Done, Delay = delay, Timeout = delay + TimeSpan.FromSeconds(1) };

    public static Step Act(string label, Func<StepOutcome> run, TimeSpan? delay = null) =>
        new() { Label = label, Run = run, IsAction = true, Delay = delay ?? TimeSpan.Zero };
}

public enum QueueState
{
    Idle,
    Running,

    /// <summary>Step-by-step mode: holding before an action until <see cref="StepQueue.Continue"/>.</summary>
    AwaitingContinue,
    Finished,
    Skipped,
    Stopped,
}

/// <summary>
/// Runs one piece's steps, one per frame at most, the way automation plugins drive game windows
/// (ECommons' TaskManager works the same way): each step either finishes, waits (until its
/// timeout), skips the piece or stops everything. When a piece ends badly its clean-up steps run
/// (close the menus it opened). Pure: the clock is passed in, so it's tested without the game.
/// </summary>
public sealed class StepQueue
{
    private readonly Func<DateTime> now;
    private readonly List<Step> steps = new();
    private readonly List<Step> cleanup = new();
    private int index;
    private DateTime stepStarted;
    private bool started;
    private bool approved;
    private bool finishEarly;
    private DateTime lastAction = DateTime.MinValue;

    public StepQueue(Func<DateTime> now) => this.now = now;

    public QueueState State { get; private set; } = QueueState.Idle;

    /// <summary>The least time between two actions (clicks), however quickly the waits between them finish.</summary>
    public TimeSpan ActionGap { get; set; }

    /// <summary>Pause before each action until <see cref="Continue"/> is pressed.</summary>
    public bool StepByStep { get; set; }

    /// <summary>Why the piece was skipped or the run stopped.</summary>
    public string? Message { get; private set; }

    public Step? Current => index < steps.Count ? steps[index] : null;
    public int Index => index;
    public int Count => steps.Count;
    public IReadOnlyList<Step> Steps => steps;

    /// <summary>Called for every step as it starts and as it ends: (step, "start" | "done" | outcome message).</summary>
    public event Action<Step, string>? Trace;

    /// <summary>Starts a new piece's steps; <paramref name="cleanupSteps"/> run (best effort, no waiting) if it ends badly.</summary>
    public void Begin(IEnumerable<Step> work, IEnumerable<Step>? cleanupSteps = null)
    {
        steps.Clear();
        steps.AddRange(work);
        cleanup.Clear();
        if (cleanupSteps != null) cleanup.AddRange(cleanupSteps);
        index = 0;
        started = false;
        approved = false;
        finishEarly = false;
        Message = null;
        State = steps.Count == 0 ? QueueState.Finished : QueueState.Running;
    }

    /// <summary>Adds steps right after the current one (a step that finds out more to do).</summary>
    public void InsertNext(IEnumerable<Step> more) => steps.InsertRange(Math.Min(index + 1, steps.Count), more);

    /// <summary>Ends the piece successfully once the current step is done (it found there's nothing left to do).</summary>
    public void FinishAfterThis() => finishEarly = true;

    public void Continue()
    {
        if (State != QueueState.AwaitingContinue) return;
        approved = true;
        State = QueueState.Running;
    }

    /// <summary>Ends the piece now (Stop pressed), running the clean-up.</summary>
    public void Abort(string why) => End(QueueState.Stopped, why);

    /// <summary>Run once per frame.</summary>
    public void Tick()
    {
        if (State != QueueState.Running) return;
        if (index >= steps.Count)
        {
            State = QueueState.Finished;
            return;
        }
        var step = steps[index];
        var t = now();
        if (!started)
        {
            if (step.IsAction && t - lastAction < ActionGap) return;
            if (step.IsAction && StepByStep && !approved)
            {
                State = QueueState.AwaitingContinue;
                return;
            }
            started = true;
            stepStarted = t;
            Trace?.Invoke(step, "start");
        }
        if (t - stepStarted < step.Delay) return;

        StepOutcome outcome;
        try
        {
            outcome = step.Run();
        }
        catch (Exception ex)
        {
            outcome = StepOutcome.Halt($"{step.Label}: {ex.Message}");
        }

        switch (outcome.Status)
        {
            case StepStatus.Done:
                if (step.IsAction) lastAction = t;
                Trace?.Invoke(step, "done");
                index = finishEarly ? steps.Count : index + 1;
                started = false;
                approved = false;
                if (index >= steps.Count) State = QueueState.Finished;
                break;
            case StepStatus.Waiting:
                if (t - stepStarted - step.Delay >= step.Timeout)
                {
                    var why = step.TimeoutMessage ?? $"Timed out: {step.Label} (waited {step.Timeout.TotalSeconds:0.#} s).";
                    End(step.OnTimeout == StepStatus.SkipJob ? QueueState.Skipped : QueueState.Stopped, why);
                }
                break;
            case StepStatus.SkipJob:
                End(QueueState.Skipped, outcome.Message ?? step.Label);
                break;
            default:
                End(QueueState.Stopped, outcome.Message ?? step.Label);
                break;
        }
    }

    private void End(QueueState state, string why)
    {
        if (State is QueueState.Stopped or QueueState.Skipped) return;
        Message = why;
        Trace?.Invoke(steps.Count > 0 && index < steps.Count ? steps[index] : new Step { Label = "end", Run = () => StepOutcome.Done }, why);
        foreach (var c in cleanup)
        {
            try
            {
                c.Run();
            }
            catch
            {
                // Clean-up is best effort.
            }
        }
        State = state;
    }
}
