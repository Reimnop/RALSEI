using System.Numerics;
using Ralsei.Platform.Input;

namespace Ralsei.Core.Input;

/// <summary>Hears the input of one <see cref="InputManager"/>. No polling: everything is raised as <see cref="InputReceived"/> when the host reports it (on its thread, before the report returns), and key and button state can be asked for any time as <see cref="Type"/> says.</summary>
public sealed class InputReceiver : IDisposable
{
    private const int KeyCount = (int)Keycode.Count;
    private const int ButtonCount = 5;

    // A Fifo that is never read does not grow without end: the oldest are let go
    private const int MaxQueuedStates = 64;

    private readonly InputManager manager;
    private readonly Lock gate = new();

    // The newest state of the keys and buttons
    private readonly bool[] keyDown = new bool[KeyCount];
    private readonly bool[] buttonDown = new bool[ButtonCount];

    // Fifo only: the changes that were not read yet, and the state as of the last one that was
    private readonly Queue<bool>?[]? keyQueues;
    private readonly Queue<bool>?[]? buttonQueues;
    private readonly bool[]? keyRead;
    private readonly bool[]? buttonRead;

    private bool disposed;

    internal InputReceiver(InputManager manager, InputReceiverType type, ReadOnlySpan<bool> keysDown, ReadOnlySpan<bool> buttonsDown, Vector2 pointer, KeyModifiers modifiers)
    {
        this.manager = manager;
        Type = type;

        // What is held already when the receiver is made is held for it
        keysDown.CopyTo(keyDown);
        buttonsDown.CopyTo(buttonDown);
        MousePosition = pointer;
        Modifiers = modifiers;

        if (type == InputReceiverType.Fifo)
        {
            keyQueues = new Queue<bool>?[KeyCount];
            buttonQueues = new Queue<bool>?[ButtonCount];
            keyRead = (bool[])keyDown.Clone();
            buttonRead = (bool[])buttonDown.Clone();
        }
    }

    public InputReceiverType Type { get; }

    /// <summary>Raised for everything the user does, in the order it happens. Handlers should be quick, and must not wait for the host.</summary>
    public event Action<InputEvent>? InputReceived;

    /// <summary>Where the pointer is now, in pixels of the window's drawing area.</summary>
    public Vector2 MousePosition { get; private set; }

    /// <summary>The modifier keys held now.</summary>
    public KeyModifiers Modifiers { get; private set; }

    /// <summary>Whether the key is down. <see cref="InputReceiverType.Mailbox"/>: down now. <see cref="InputReceiverType.Fifo"/>: takes the oldest unread change of the key (down or up) and says what it made of the key, so a tap is seen even if over before the read; with none left, whether it is down now.</summary>
    public bool IsDown(Keycode keycode)
    {
        var index = (int)keycode;
        if (index < 0 || index >= KeyCount)
            return false;

        lock (gate)
            return Read(keyDown, keyQueues, keyRead, index);
    }

    /// <summary>Whether the button is down, read the way <see cref="IsDown(Keycode)"/> reads a key.</summary>
    public bool IsDown(MouseButton button)
    {
        var index = (int)button;
        if (index < 0 || index >= ButtonCount)
            return false;

        lock (gate)
            return Read(buttonDown, buttonQueues, buttonRead, index);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        manager.Remove(this);
    }

    // Called by the manager, with what happened
    internal void Receive(in InputEvent e)
    {
        lock (gate)
        {
            Modifiers = e.Modifiers;
            switch (e.Kind)
            {
                case InputEventKind.KeyDown:
                    Set(keyDown, keyQueues, (int)e.Key, true);
                    break;
                case InputEventKind.KeyUp:
                    Set(keyDown, keyQueues, (int)e.Key, false);
                    break;
                case InputEventKind.MouseMove:
                    MousePosition = e.Position;
                    break;
                case InputEventKind.MouseDown:
                    Set(buttonDown, buttonQueues, (int)e.Button, true);
                    break;
                case InputEventKind.MouseUp:
                    Set(buttonDown, buttonQueues, (int)e.Button, false);
                    break;
            }
        }

        // Outside the lock, so that a handler can read the state (or be slow) without holding the host up
        InputReceived?.Invoke(e);
    }

    private static void Set(bool[] states, Queue<bool>?[]? queues, int index, bool down)
    {
        if (index < 0 || index >= states.Length)
            return;

        states[index] = down;
        if (queues is null)
            return;

        var queue = queues[index] ??= new Queue<bool>();
        if (queue.Count == MaxQueuedStates)
            queue.Dequeue();
        queue.Enqueue(down);
    }

    private static bool Read(bool[] states, Queue<bool>?[]? queues, bool[]? read, int index)
    {
        if (queues is null || read is null)
            return states[index];

        if (queues[index] is { Count: > 0 } queue)
            read[index] = queue.Dequeue();

        return read[index];
    }
}
