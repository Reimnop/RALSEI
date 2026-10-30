using System.Numerics;
using Ralsei.Platform.Input;

namespace Ralsei.Core.Input;

public enum InputEventKind
{
    KeyDown,
    KeyUp,

    /// <summary>A held key came round again (the keyboard's auto-repeat).</summary>
    KeyRepeat,
    MouseMove,
    MouseDown,
    MouseUp,
    Scroll,
    Text,

    /// <summary>Files were dropped on the window.</summary>
    FilesDropped,
}

/// <summary>The modifier keys that were held when something happened.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Super = 8,
}

/// <summary>One thing the user did, raised by an <see cref="InputReceiver"/> as it happens so what reads them in order (the UI) can tell a click from a drag.</summary>
/// <param name="Position">Where the pointer was (in pixels of the window's drawing area) for mouse events.</param>
/// <param name="Delta">How far the pointer moved since the move before, for <see cref="InputEventKind.MouseMove"/> (zero for the first).</param>
/// <param name="Scroll">How far the wheel turned, for <see cref="InputEventKind.Scroll"/>.</param>
/// <param name="Paths">The files, for <see cref="InputEventKind.FilesDropped"/>.</param>
public readonly record struct InputEvent(
    InputEventKind Kind,
    KeyModifiers Modifiers = KeyModifiers.None,
    Keycode Key = Keycode.Unknown,
    MouseButton Button = MouseButton.Left,
    Vector2 Position = default,
    Vector2 Delta = default,
    Vector2 Scroll = default,
    string? Text = null,
    string[]? Paths = null)
{
    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;
    public bool Control => (Modifiers & KeyModifiers.Control) != 0;
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
}
