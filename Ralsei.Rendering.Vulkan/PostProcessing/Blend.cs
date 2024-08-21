using Ralsei.Core;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

/// <summary>
/// Lays one image over another, which is how the passes of a frame are layered.
/// </summary>
internal sealed class Blend : IDisposable
{
    private readonly ComputeShared shared;
    private readonly ComputeKernel kernel;

    public Blend(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.shared = shared;
        kernel = new ComputeKernel(context, shared, loader, "PostProcessing/Blend");
    }

    /// <summary>
    /// Draws <paramref name="source"/> over what <paramref name="target"/> has already, which are both premultiplied.
    /// </summary>
    public void Process(FrameResources frame, Vector2i size, GpuImage source, GpuImage target)
    {
        kernel.Run(frame, size, target, source, shared.NearestClamp);
    }

    public void Dispose()
    {
        kernel.Dispose();
    }
}
