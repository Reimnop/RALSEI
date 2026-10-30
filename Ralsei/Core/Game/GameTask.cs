using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Ralsei.Core.Game;

/// <summary>
/// What an <c>async GameTask</c> method returns: like <see cref="Task"/> but for a single-threaded game that advances one tick (<see cref="CoroutineRunner.Update"/>) at a time. No threads or synchronization context: a method awaiting <see cref="Wait.Ticks"/> or <see cref="Wait.Until"/> resumes in its <see cref="CoroutineRunner"/> on the tick the wait ends, and one awaiting another <see cref="GameTask"/> resumes in the tick it finishes.
/// <para>
/// Started by <see cref="CoroutineRunner.Start"/>, which runs it on its next tick. Inside a task, calling another <c>async GameTask</c> method starts it at once and <c>await</c>ing it waits: <c>await Say(...); await Wait.Ticks(30);</c>.
/// </para>
/// </summary>
[AsyncMethodBuilder(typeof(GameTaskMethodBuilder))]
public class GameTask
{
    [ThreadStatic]
    private static GameTask? running;

    private Action? continuation;
    private List<Action>? moreContinuations;
    private ExceptionDispatchInfo? error;

    internal GameTask()
    {
        // A task belongs to the flow that started it: the one that is running now, or the entry the runner is running.
        Root = running?.Root ?? CoroutineRunner.CurrentRoot ?? this;
    }

    /// <summary>A task that has already finished, for a method that has nothing to wait for.</summary>
    public static GameTask CompletedTask { get; } = new() { IsCompleted = true };

    public bool IsCompleted { get; private set; }

    public bool IsCanceled { get; private set; }

    public bool IsFaulted => error is not null;

    /// <summary>The task that was started by a runner and that this one is a part of (itself, if it is one).</summary>
    internal GameTask Root { get; set; }

    /// <summary>The runner that started this task, if it is one that was started by a runner.</summary>
    internal CoroutineRunner? Owner { get; set; }

    /// <summary>Whether anybody has awaited this task, and so will hear if it fails.</summary>
    internal bool IsObserved { get; private set; }

    internal StateMachineBox? Box { get; set; }

    /// <summary>The method that is being run right now, for the tasks it starts to know whose they are.</summary>
    internal static GameTask? Running => running;

    public GameTaskAwaiter GetAwaiter()
    {
        IsObserved = true;
        return new GameTaskAwaiter(this);
    }

    /// <summary>Stops a task a runner started: what it waited for is dropped and it never carries on (<c>finally</c> blocks don't run). Awaiters get an <see cref="OperationCanceledException"/>. No effect once finished.</summary>
    public void Cancel()
    {
        if (IsCompleted)
            return;

        IsCanceled = true;
        Finish();
    }

    internal void Complete()
    {
        if (!IsCompleted)
            Finish();
    }

    internal void Fail(Exception exception)
    {
        if (IsCompleted)
            return;

        error = ExceptionDispatchInfo.Capture(exception);
        Finish();

        // A task nobody is awaiting would swallow its error; one a runner started reports it from the runner's next update.
        if (!IsObserved)
            Owner?.Report(exception);
    }

    internal void AddContinuation(Action action)
    {
        IsObserved = true;
        if (IsCompleted)
        {
            action();
            return;
        }

        if (continuation is null)
        {
            continuation = action;
            return;
        }

        (moreContinuations ??= []).Add(action);
    }

    internal void ThrowIfNotSucceeded()
    {
        if (IsCanceled)
            throw new OperationCanceledException();

        error?.Throw();
    }

    private void Finish()
    {
        IsCompleted = true;
        var first = continuation;
        var rest = moreContinuations;
        continuation = null;
        moreContinuations = null;

        first?.Invoke();
        if (rest is not null)
        {
            foreach (var action in rest)
                action();
        }
    }

    /// <summary>Runs a step of a method as <paramref name="task"/>, so tasks it starts know whose they are.</summary>
    internal static void Step(GameTask task, Action step)
    {
        var previous = running;
        running = task;
        try
        {
            step();
        }
        finally
        {
            running = previous;
        }
    }

    internal abstract class StateMachineBox
    {
        public abstract void MoveNext();
    }
}

/// <summary>What <c>await</c> uses on a <see cref="GameTask"/>.</summary>
public readonly struct GameTaskAwaiter(GameTask task) : ICriticalNotifyCompletion
{
    public bool IsCompleted => task.IsCompleted;

    public void OnCompleted(Action continuation) => task.AddContinuation(continuation);

    public void UnsafeOnCompleted(Action continuation) => task.AddContinuation(continuation);

    public void GetResult() => task.ThrowIfNotSucceeded();
}

/// <summary>Builds the <see cref="GameTask"/> of an <c>async GameTask</c> method. The compiler uses it; nothing else should.</summary>
public struct GameTaskMethodBuilder
{
    private GameTask task;

    public static GameTaskMethodBuilder Create() => new() { task = new GameTask() };

    public GameTask Task => task;

    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
    {
        // Run to the first await at once, on the caller's stack: a method that is called starts running.
        var machine = stateMachine;
        GameTask.Step(task, () => machine.MoveNext());
        stateMachine = machine;
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
    }

    public void SetResult() => task.Complete();

    public void SetException(Exception exception) => task.Fail(exception);

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
        => awaiter.OnCompleted(BoxFor(ref stateMachine));

    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
        => awaiter.UnsafeOnCompleted(BoxFor(ref stateMachine));

    /// <summary>Moves the state machine off the stack the first time it waits, once, and hands back what resumes it.</summary>
    private Action BoxFor<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
    {
        if (task.Box is not Box<TStateMachine> box)
        {
            box = new Box<TStateMachine>(task) { Machine = stateMachine };
            task.Box = box;
        }

        return box.Resume;
    }

    private sealed class Box<TStateMachine> : GameTask.StateMachineBox where TStateMachine : IAsyncStateMachine
    {
        public TStateMachine Machine = default!;

        public Box(GameTask task) => Resume = () => GameTask.Step(task, MoveNext);

        /// <summary>What a wait runs when it is over: the next piece of the method, as part of its task.</summary>
        public Action Resume { get; }

        public override void MoveNext() => Machine.MoveNext();
    }
}
