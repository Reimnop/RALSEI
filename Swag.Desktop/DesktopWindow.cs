using Ico.Reader;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Ralsei.Mathematics;
using Ralsei.Rendering;
using ReFuel.Stb;

namespace Swag.Desktop;

/// <summary>A GLFW window; what is drawn to it is up to the backend, see <see cref="DesktopSurface"/> and <see cref="VulkanDesktopSurface"/>.</summary>
public abstract unsafe class DesktopWindow : IDisposable
{
    private const string Title = "Swarm Graphics";
    private const float LockedAspectRatio = 16f / 9f;

    public Vector2i RenderSize
    {
        get
        {
            var size = FramebufferSize;
            RenderUtil.GetRenderSize(size, SurfaceSettings.LockAspectRatio ? LockedAspectRatio : null, out var renderSize);
            return renderSize;
        }
    }

    public Vector2i FramebufferSize
    {
        get
        {
            GLFW.GetFramebufferSize(window, out var width, out var height);
            return new Vector2i(width, height);
        }
    }

    public bool ShouldClose => GLFW.WindowShouldClose(window);

    public Window* WindowPtr => window;

    protected DesktopSurfaceSettings SurfaceSettings { get; }

    private readonly GlfwService glfw;
    private readonly Window* window;
    private bool disposed;

    protected DesktopWindow(DesktopSurfaceSettings surfaceSettings, GlfwService glfw)
    {
        SurfaceSettings = surfaceSettings;
        this.glfw = glfw;

        window = this.glfw.CreateWindowHandle(surfaceSettings.Size.X, surfaceSettings.Size.Y, Title, surfaceSettings.UseEgl);

        using var iconStream = typeof(DesktopWindow).Assembly.GetManifestResourceStream("Swag.Desktop.icon.ico");
        if (iconStream is not null)
            LoadIcon(iconStream);
    }

    public virtual void Dispose()
    {
        // The window is registered under several service types (the surface interface, the window itself, ...),
        // and the container disposes the instance once for each of them
        if (disposed)
            return;

        disposed = true;
        GLFW.DestroyWindow(window);
    }

    public void PollEvents()
    {
        glfw.PollEvents();
    }

    private void LoadIcon(Stream iconStream)
    {
        var icoReader = new IcoReader();
        var iconData = icoReader.Read(iconStream);
        if (iconData is null)
            return;
        var group = iconData.Groups[0];
        var images = new List<StbImage>();
        try
        {
            for (var i = 0; i < group.DirectoryEntries.Length; i++)
            {
                var data = iconData.GetImage(group.Name, i);
                var image = StbImage.Load(data);
                images.Add(image);
            }
            var glfwImages = images
                .Select(image => new Image
                {
                    Width = image.Width,
                    Height = image.Height,
                    Pixels = (byte*) image.ImagePointer,
                });
            GLFW.SetWindowIcon(window, glfwImages.ToArray());
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
        }
    }
}
