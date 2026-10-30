using Ralsei.Core.Game;

namespace Ralsei.Platform.Window;

/// <summary>A window that is to be opened next to the one that is there: its title, its size, and what it shows.</summary>
/// <param name="CreateScene">Makes the scene the window begins with, given the new window's services (it has an engine of its own: pictures, fonts, input, scenes). Runs once the window exists. What windows share is what scene makers pass them.</param>
public sealed record WindowRequest(string Title, int Width, int Height, Func<IServiceProvider, IScene> CreateScene)
{
    /// <summary>Whether the window has the frame of the system (a title bar, a border and buttons).</summary>
    public bool Decorated { get; init; } = true;

    /// <summary>Whether the window stays over the others (for menus and tool windows).</summary>
    public bool Floating { get; init; }

    /// <summary>Whether what the window doesn't draw over lets the screen through (rounded menus): the picture has alpha and the scene clears to a colour that does. Only for undecorated windows; hosts that can't ignore it.</summary>
    public bool Transparent { get; init; }

    /// <summary>Whether the window is on the screen when it is made. One that is not is made all the same, ready to be shown (<see cref="IWindowControl.Visible"/>).</summary>
    public bool Visible { get; init; } = true;

    public bool Resizable { get; init; } = true;

    /// <summary>Whether the keyboard goes to the window when it is shown.</summary>
    public bool TakeFocus { get; init; } = true;

    /// <summary>Where the picture of the window is on the screen (screen units), or nothing for where the system puts it.</summary>
    public (int X, int Y)? Position { get; init; }
}

/// <summary>A window that was opened.</summary>
public interface IWindowHandle
{
    bool IsOpen { get; }

    /// <summary>The window was closed, by the user or by <see cref="Close"/>.</summary>
    event Action? Closed;

    string Title { get; set; }

    /// <summary>What can be asked of the window: where it is, whether it shows, which of the windows has the keyboard.</summary>
    IWindowControl Control { get; }

    /// <summary>Closes the window, whatever its <see cref="IWindowControl.CloseGuard"/> says.</summary>
    void Close();
}

/// <summary>Opens more windows. A host with only one window says so, and opens none.</summary>
public interface IWindowManager
{
    bool IsSupported { get; }

    /// <summary>Opens a window, or returns null if the host can not.</summary>
    IWindowHandle? Open(WindowRequest request);
}

public sealed class NullWindowManager : IWindowManager
{
    public bool IsSupported => false;

    public IWindowHandle? Open(WindowRequest request) => null;
}
