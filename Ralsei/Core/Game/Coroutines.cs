using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Ralsei.Core.Game;

/// <summary>What a script waits for, <c>await</c>ed inside an <c>async GameTask</c> method run by a <see cref="CoroutineRunner"/>: <c>await Wait.Ticks(30);</c>. A tick is one <see cref="CoroutineRunner.Update"/> of that runner. A wait always lets at least one tick pass, even if its condition is already true.</summary>
public readonly struct Wait
{
    /// <summary>Resume on the next tick.</summary>
    public static Wait Tick => new(1, null);

    internal int TickCount { get; }
    internal Func<bool>? Condition { get; }

    private Wait(int tickCount, Func<bool>? condition)
    {
        TickCount = tickCount;
        Condition = condition;
    }

    /// <summary>Resume after <paramref name="count"/> ticks have elapsed.</summary>
    public static Wait Ticks(int count) => new(count, null);

    /// <summary>Resume on the first tick where <paramref name="condition"/> is true.</summary>
    public static Wait Until(Func<bool> condition) => new(0, condition);

    public WaitAwaiter GetAwaiter() => new(this);
}

/// <summary>What <c>await</c> uses on a <see cref="Wait"/>.</summary>
public readonly struct WaitAwaiter(Wait wait) : ICriticalNotifyCompletion
{
    // A wait is never over before it is begun: the script always gives up the rest of the tick.
    public bool IsCompleted => false;

    public void OnCompleted(Action continuation) => CoroutineRunner.Register(wait, continuation);

    public void UnsafeOnCompleted(Action continuation) => CoroutineRunner.Register(wait, continuation);

    public void GetResult()
    {
    }
}

/// <summary>Runs scripts (<c>async GameTask</c> methods) one <see cref="Update"/> at a time, so "slide in, wait 10 ticks, fire" reads top to bottom instead of as a state machine. A started script begins at the next <see cref="Update"/>; each <c>await Wait.…</c> hands control back until the wait is over. Scripts run in start order, and a waiting script keeps its place.</summary>
public sealed class CoroutineRunner
{
    [ThreadStatic]
    private static CoroutineRunner? current;

    [ThreadStatic]
    private static GameTask? currentRoot;

    private sealed class Entry(GameTask root)
    {
        public GameTask Root = root;
        public Func<GameTask>? Begin;
        public Func<bool>? Condition;
        public int Remaining;
        public Action? Continuation;
        public long Armed;
        public bool Rearmed;
        public bool Dead;
    }

    private readonly List<Entry> entries = [];
    private Entry? active;
    private long tick;
    private ExceptionDispatchInfo? fault;

    /// <summary>The script a runner is running right now, as the task that was handed out for it.</summary>
    internal static GameTask? CurrentRoot => currentRoot;

    /// <summary>Starts a script, run by the next <see cref="Update"/> (or this one, if started by a running script). The returned task finishes with the script and can be cancelled.</summary>
    public GameTask Start(Func<GameTask> script)
    {
        var handle = new GameTask { Owner = this };
        handle.Root = handle;
        entries.Add(new Entry(handle) { Begin = script, Armed = tick });
        return handle;
    }

    /// <summary>Lets every script that is waiting on this tick carry on.</summary>
    public void Update()
    {
        var previousRunner = current;
        var previousRoot = currentRoot;
        current = this;
        tick++;

        try
        {
            // Scripts may start scripts while this runs, so go by index: one started now begins this tick.
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Dead)
                    continue;

                if (entry.Root.IsCompleted)
                {
                    // Cancelled while it waited.
                    entry.Dead = true;
                    continue;
                }

                if (entry.Begin is null && !IsOver(entry))
                    continue;

                Run(entry);
            }
        }
        finally
        {
            entries.RemoveAll(e => e.Dead);
            current = previousRunner;
            currentRoot = previousRoot;
        }

        if (fault is not null)
        {
            var thrown = fault;
            fault = null;
            thrown.Throw();
        }
    }

    /// <summary>Cancels every script that is running or waiting.</summary>
    public void CancelAll()
    {
        foreach (var entry in entries.ToList())
            entry.Root.Cancel();

        entries.Clear();
    }

    private bool IsOver(Entry entry)
    {
        // A wait that was only set up on this tick has not had a tick to wait through yet.
        if (entry.Armed >= tick)
            return false;

        if (entry.Condition is { } condition)
            return condition();

        return --entry.Remaining <= 0;
    }

    private void Run(Entry entry)
    {
        var previous = active;
        active = entry;
        currentRoot = entry.Root;
        entry.Rearmed = false;

        var begin = entry.Begin;
        var resume = entry.Continuation;
        entry.Begin = null;
        entry.Continuation = null;

        try
        {
            if (begin is not null)
            {
                // Run the script as part of the task that was handed out for it.
                var handle = entry.Root;
                var script = begin();
                script.AddContinuation(() =>
                {
                    if (script.IsFaulted)
                        handle.Fail(ScriptError(script));
                    else
                        handle.Complete();
                });
            }
            else
            {
                resume!();
            }
        }
        finally
        {
            // Not waiting for anything any more (finished, or waiting for another task to finish): its place is free.
            if (!entry.Rearmed)
                entry.Dead = true;

            active = previous;
        }
    }

    private static Exception ScriptError(GameTask task)
    {
        try
        {
            task.ThrowIfNotSucceeded();
        }
        catch (Exception exception)
        {
            return exception;
        }

        return new InvalidOperationException();
    }

    internal void Report(Exception exception) => fault ??= ExceptionDispatchInfo.Capture(exception);

    /// <summary>Called by the awaiter of a <see cref="Wait"/>: the script that is running carries on when the wait is over.</summary>
    internal static void Register(Wait wait, Action continuation)
    {
        var runner = current ?? throw new InvalidOperationException(
            "A Wait can only be awaited by a script that a CoroutineRunner is running. Start the script with CoroutineRunner.Start.");

        runner.Arm(wait, continuation);
    }

    private void Arm(Wait wait, Action continuation)
    {
        var root = GameTask.Running?.Root ?? currentRoot ?? throw new InvalidOperationException("There is no script to wait.");

        // The script keeps its place in the order if it is the one being run; otherwise (a task that another script was waiting
        // on has finished and woken it) it takes a new one.
        var entry = active is { Rearmed: false } slot && slot.Root == root ? slot : AddEntry(root);
        entry.Rearmed = true;
        entry.Dead = false;
        entry.Begin = null;
        entry.Continuation = continuation;
        entry.Condition = wait.Condition;
        entry.Remaining = Math.Max(wait.TickCount, 1);
        entry.Armed = tick;
    }

    private Entry AddEntry(GameTask root)
    {
        var entry = new Entry(root);
        entries.Add(entry);
        return entry;
    }
}
