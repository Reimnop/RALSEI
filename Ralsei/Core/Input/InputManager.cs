using System.Numerics;
using Ralsei.Platform.Input;

namespace Ralsei.Core.Input;

/// <summary>Where a window's input comes in. The host (windowing code, a script, a test) reports what the user did and it goes at once to every <see cref="InputReceiver"/> made from it, on the reporting thread; nothing reads input any other way. A host with no input reports nothing.</summary>
public sealed class InputManager
{
    private const int KeyCount = (int)Keycode.Count;
    private const int ButtonCount = 5;

    private readonly Lock gate = new();
    private readonly bool[] keysDown = new bool[KeyCount];
    private readonly bool[] buttonsDown = new bool[ButtonCount];
    private Vector2 pointer;
    private bool pointerKnown;

    // Replaced, never changed, so that an event can go to the receivers without a lock or a copy
    private InputReceiver[] receivers = [];

    /// <summary>Makes a receiver, which knows what is held and where the pointer is, and hears everything from now on.</summary>
    public InputReceiver CreateReceiver(InputReceiverType type)
    {
        lock (gate)
        {
            var receiver = new InputReceiver(this, type, keysDown, buttonsDown, pointer, CurrentModifiers());
            receivers = [.. receivers, receiver];
            return receiver;
        }
    }

    internal void Remove(InputReceiver receiver)
    {
        lock (gate)
            receivers = receivers.Where(x => x != receiver).ToArray();
    }

    #region What the host reports

    public void KeyDown(Keycode keycode)
    {
        if (!IsKey(keycode))
            return;

        InputEvent e;
        lock (gate)
        {
            keysDown[(int)keycode] = true;
            e = new InputEvent(InputEventKind.KeyDown, CurrentModifiers(), keycode);
        }

        Dispatch(e);
    }

    public void KeyUp(Keycode keycode)
    {
        if (!IsKey(keycode))
            return;

        InputEvent e;
        lock (gate)
        {
            keysDown[(int)keycode] = false;
            e = new InputEvent(InputEventKind.KeyUp, CurrentModifiers(), keycode);
        }

        Dispatch(e);
    }

    /// <summary>A held key came round again (the keyboard's auto-repeat).</summary>
    public void KeyRepeat(Keycode keycode)
    {
        if (!IsKey(keycode))
            return;

        InputEvent e;
        lock (gate)
            e = new InputEvent(InputEventKind.KeyRepeat, CurrentModifiers(), keycode);

        Dispatch(e);
    }

    /// <summary>The pointer moved, to a place in pixels of the window's drawing area. The event says how far it moved from where it was.</summary>
    public void MouseMove(float x, float y)
    {
        InputEvent e;
        lock (gate)
        {
            var position = new Vector2(x, y);

            // The first move is from nowhere in particular
            var delta = pointerKnown ? position - pointer : Vector2.Zero;
            pointer = position;
            pointerKnown = true;
            e = new InputEvent(InputEventKind.MouseMove, CurrentModifiers(), Position: position, Delta: delta);
        }

        Dispatch(e);
    }

    public void MouseDown(MouseButton button) => MouseButtonChanged(button, true);

    public void MouseUp(MouseButton button) => MouseButtonChanged(button, false);

    /// <summary>The wheel (or touchpad) scrolled. One unit is a notch of a wheel; positive y is away from the user.</summary>
    public void Scroll(float x, float y)
    {
        InputEvent e;
        lock (gate)
            e = new InputEvent(InputEventKind.Scroll, CurrentModifiers(), Position: pointer, Scroll: new Vector2(x, y));

        Dispatch(e);
    }

    /// <summary>Text was typed: what the key presses came to, once the layout and the input method had their say.</summary>
    public void TextInput(string text)
    {
        if (text.Length == 0)
            return;

        InputEvent e;
        lock (gate)
            e = new InputEvent(InputEventKind.Text, CurrentModifiers(), Text: text);

        Dispatch(e);
    }

    /// <summary>Files were dropped on the window.</summary>
    public void FilesDropped(string[] paths)
    {
        if (paths.Length == 0)
            return;

        InputEvent e;
        lock (gate)
            e = new InputEvent(InputEventKind.FilesDropped, CurrentModifiers(), Position: pointer, Paths: paths);

        Dispatch(e);
    }

    #endregion

    private void MouseButtonChanged(MouseButton button, bool pressed)
    {
        var index = (int)button;
        if (index < 0 || index >= ButtonCount)
            return;

        InputEvent e;
        lock (gate)
        {
            buttonsDown[index] = pressed;
            e = new InputEvent(pressed ? InputEventKind.MouseDown : InputEventKind.MouseUp, CurrentModifiers(), Button: button, Position: pointer);
        }

        Dispatch(e);
    }

    // Outside the lock: a receiver calls its handlers, which may ask for receivers of their own
    private void Dispatch(in InputEvent e)
    {
        foreach (var receiver in Volatile.Read(ref receivers))
            receiver.Receive(e);
    }

    private static bool IsKey(Keycode keycode) => (int)keycode >= 0 && (int)keycode < KeyCount;

    private KeyModifiers CurrentModifiers()
    {
        var modifiers = KeyModifiers.None;
        if (keysDown[(int)Keycode.LeftShift] || keysDown[(int)Keycode.RightShift])
            modifiers |= KeyModifiers.Shift;
        if (keysDown[(int)Keycode.LeftControl] || keysDown[(int)Keycode.RightControl])
            modifiers |= KeyModifiers.Control;
        if (keysDown[(int)Keycode.LeftAlt] || keysDown[(int)Keycode.RightAlt])
            modifiers |= KeyModifiers.Alt;
        if (keysDown[(int)Keycode.LeftSuper] || keysDown[(int)Keycode.RightSuper])
            modifiers |= KeyModifiers.Super;
        return modifiers;
    }
}
