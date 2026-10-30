namespace Ralsei.Platform.Window;

/// <summary>What a plugin may ask of the window it runs in.</summary>
public interface IWindowControl
{
    /// <summary>The title in the window's title bar.</summary>
    string Title { get; set; }

    /// <summary>How many pixels the display wants for each unit of an interface that is sized for a plain screen (1.25 at 125%).</summary>
    float ContentScale => 1f;

    /// <summary>Asks the window to close, as its close button does. A <see cref="CloseGuard"/> may say no.</summary>
    void RequestClose();

    /// <summary>Closes the window whatever the <see cref="CloseGuard"/> says (it has had its say).</summary>
    void ForceClose() => RequestClose();

    /// <summary>Asked when the window is about to close; returns true to keep it open (e.g. to ask about unsaved work). A scene that sets it clears it when it goes.</summary>
    Func<bool>? CloseGuard
    {
        get => null;
        set { }
    }

    /// <summary>Changes the picture of the pointer while it is over the window. A host without a pointer ignores it.</summary>
    void SetCursor(CursorShape shape)
    {
    }

    /// <summary>Asks for the window to be this size (in screen units). A host that can not do it ignores it.</summary>
    void Resize(int width, int height)
    {
    }

    /// <summary>Where the picture of the window is on the screen: its top-left corner, in screen units. A host without a screen says 0, 0.</summary>
    (int X, int Y) Position
    {
        get => (0, 0);
        set { }
    }

    /// <summary>Where the pointer is on the screen, in screen units, as of now (the pointer positions of events are as of when they happened, and a window that moves under the pointer makes them useless for moving it).</summary>
    (float X, float Y) CursorScreenPosition => (0f, 0f);

    /// <summary>The size of the picture of the window, in screen units.</summary>
    (int Width, int Height) ClientSize => (0, 0);

    /// <summary>How many pixels the picture has for each screen unit (2 on a display that draws everything twice as fine).</summary>
    float PixelRatio => 1f;

    /// <summary>Whether the window is on the screen at all (a window that is not is kept, ready to be shown again at once).</summary>
    bool Visible
    {
        get => true;
        set { }
    }

    /// <summary>Whether the window is the one the keyboard is for.</summary>
    bool Focused => true;

    /// <summary>Asks for the keyboard to be for this window (when it is shown, if it is not yet).</summary>
    void Focus()
    {
    }

    /// <summary>The part of the screen that windows can be put in (not under a taskbar) at a point of the screen, or nothing if there is no screen.</summary>
    (int X, int Y, int Width, int Height)? WorkAreaAt(int x, int y) => null;
}

/// <summary>For hosts with no window of their own: a title that goes nowhere, and a close that is remembered.</summary>
public sealed class NullWindowControl : IWindowControl
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Whether <see cref="RequestClose"/> was called.</summary>
    public bool CloseRequested { get; private set; }

    public void RequestClose()
    {
        if (CloseGuard?.Invoke() != true)
            CloseRequested = true;
    }

    public void ForceClose() => CloseRequested = true;

    public Func<bool>? CloseGuard { get; set; }

    /// <summary>The cursor that was asked for last.</summary>
    public CursorShape Cursor { get; private set; }

    public void SetCursor(CursorShape shape) => Cursor = shape;

    /// <summary>The size that <see cref="Resize"/> asked for last, if it did.</summary>
    public (int Width, int Height)? RequestedSize { get; private set; }

    public void Resize(int width, int height) => RequestedSize = (width, height);

    public (int X, int Y) Position { get; set; }

    public bool Visible { get; set; } = true;
}
