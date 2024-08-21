using System.Runtime.InteropServices;
using Ralsei.Core;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

/// <summary>
/// Blurs the image over a disc, like a lens that is out of focus.
/// </summary>
internal sealed class DepthOfField : IDisposable
{
    // Spelled out in DepthOfField.comp as well
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Radius;
    }

    // A blur of less than this is not seen
    private const float MinRadiusInPixels = 0.25f;

    private readonly ComputeShared shared;
    private readonly ComputeKernel kernel;

    public DepthOfField(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.shared = shared;
        kernel = new ComputeKernel(context, shared, loader, "PostProcessing/DepthOfField");
    }

    /// <param name="radius">The radius of the blur, as a fraction of the height of the image.</param>
    public bool Process(FrameResources frame, Vector2i size, float radius, GpuImage input, GpuImage output)
    {
        var radiusInPixels = radius * size.Y;
        if (radiusInPixels < MinRadiusInPixels)
            return false;

        kernel.Run(frame, size, new PushConstants { Radius = radiusInPixels }, output, input, shared.LinearClamp);
        return true;
    }

    public void Dispose()
    {
        kernel.Dispose();
    }
}
