using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Ralsei.Avalonia.Services;

/// <summary>
/// What view models need from the window they are shown in (file pickers and the like), without knowing about it.
/// </summary>
public sealed class WindowServices
{
    /// <summary>
    /// Set by the window when it is there.
    /// </summary>
    public TopLevel? TopLevel { get; set; }

    public IStorageProvider? StorageProvider => TopLevel?.StorageProvider;
    public IClipboard? Clipboard => TopLevel?.Clipboard;
    public ILauncher? Launcher => TopLevel?.Launcher;
}
