namespace Ralsei.Core.Service;

/// <summary>
/// Which of the layers of a frame an object is drawn in, from the back to the front.
/// </summary>
public enum RenderLayer : byte
{
    /// <summary>
    /// The background, which is far behind the rest and is drawn with a camera of its own (see <see cref="AppDirector"/>).
    /// </summary>
    Background = 0,

    /// <summary>
    /// What is drawn in the normal layer.
    /// </summary>
    Foreground = 1,

    /// <summary>
    /// What is drawn above the normal layer: the objects that ask for it, and those that follow the camera.
    /// </summary>
    AbovePlayer = 2
}
