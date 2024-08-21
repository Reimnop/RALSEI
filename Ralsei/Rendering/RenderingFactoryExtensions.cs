using Ralsei.Rendering.Handle;

namespace Ralsei.Rendering;

public static class RenderingFactoryExtensions
{
    /// <summary>
    /// Requests a buffer for a frame of a single pass.
    /// </summary>
    public static FrameBuffer RequestFrameBuffer(this IRenderingFactory factory, int drawCommandCount, int meshDrawItemCount, int textDrawItemCount)
        => factory.RequestFrameBuffer([new PassSize(drawCommandCount, meshDrawItemCount, textDrawItemCount)]);

    /// <summary>
    /// Gets the handle if the render thread has created the resource already.
    /// </summary>
    public static bool TryGetHandle<THandle>(this Task<THandle> task, out THandle handle)
        where THandle : struct, IRenderingResourceHandle
    {
        if (task.IsCompletedSuccessfully)
        {
            handle = task.Result;
            return true;
        }

        handle = default;
        return false;
    }

    /// <summary>
    /// Destroys a mesh that might still be waiting to be created. Does nothing if the creation failed.
    /// </summary>
    public static void DestroyMesh(this IRenderingFactory factory, Task<MeshHandle> handle)
    {
        if (handle.TryGetHandle(out var meshHandle))
            factory.DestroyMesh(meshHandle);
        else
            handle.ContinueWith(
                static (task, state) => ((IRenderingFactory) state!).DestroyMesh(task.Result),
                factory,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    /// <summary>
    /// Destroys a text that might still be waiting to be created. Does nothing if the creation failed.
    /// </summary>
    public static void DestroyText(this IRenderingFactory factory, Task<TextHandle> handle)
    {
        if (handle.TryGetHandle(out var textHandle))
            factory.DestroyText(textHandle);
        else
            handle.ContinueWith(
                static (task, state) => ((IRenderingFactory) state!).DestroyText(task.Result),
                factory,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    /// <summary>
    /// Destroys a texture that might still be waiting to be created. Does nothing if the creation failed.
    /// </summary>
    public static void DestroyTexture(this IRenderingFactory factory, Task<TextureHandle> handle)
    {
        if (handle.TryGetHandle(out var textureHandle))
            factory.DestroyTexture(textureHandle);
        else
            handle.ContinueWith(
                static (task, state) => ((IRenderingFactory) state!).DestroyTexture(task.Result),
                factory,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }
}
