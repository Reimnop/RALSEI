using System.Runtime.InteropServices;
using Ralsei.Core;
using Ralsei.Mathematics;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

/// <summary>Lays one image over another, which is how the passes of a frame are layered.</summary>
internal sealed class Blend : IDisposable
{
    // Spelled out in Blend.comp as well
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public int RegionX;
        public int RegionY;
        public int RegionWidth;
        public int RegionHeight;
        public float Opacity;
    }

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
    /// <param name="opacity">How much of the source shows: 1 for all of it.</param>
    /// <param name="region">The part of the images to do (see <see cref="PassRegion"/>); <c>default</c> for all of them.</param>
    public void Process(FrameResources frame, Vector2i size, GpuImage source, GpuImage target, float opacity = 1f, ClipRect region = default)
    {
        var (x, y, width, height) = PassRegion.ToImageRect(region, size);
        var push = new PushConstants { RegionX = x, RegionY = y, RegionWidth = width, RegionHeight = height, Opacity = opacity };
        kernel.Run(frame, new Vector2i(width, height), push, target, source, shared.NearestClamp);
    }

    public void Dispose()
    {
        kernel.Dispose();
    }
}
