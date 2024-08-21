using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.Vulkan;
using Ralsei.Core;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

internal sealed class UniversalBloom : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PrefilterPushConstants
    {
        public float Threshold;
        public float Knee;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurPushConstants
    {
        public int IsVertical;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UpsamplePushConstants
    {
        public float Scatter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CombinePushConstants
    {
        public ColorRgb Tint;
    }

    private sealed record Mip(GpuImage Down, GpuImage Up, Vector2i Size);

    private readonly ComputeShared shared;
    private readonly VulkanContext context;
    private readonly ComputeKernel prefilter, blur, upsample, combine;

    private readonly List<Mip> mipChain = [];
    private Vector2i currentSize = -Vector2i.One;

    public UniversalBloom(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.context = context;
        this.shared = shared;

        prefilter = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Prefilter");
        blur = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Universal/Blur");
        upsample = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Universal/Upsample");
        combine = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Combine");
    }

    public void Dispose()
    {
        prefilter.Dispose();
        blur.Dispose();
        upsample.Dispose();
        combine.Dispose();

        foreach (var mip in mipChain)
        {
            mip.Down.Dispose();
            mip.Up.Dispose();
        }
    }

    public bool Process(
        FrameResources frame, Vector2i size,
        float intensity, float diffusion, ColorRgb color,
        GpuImage input, GpuImage output)
    {
        if (intensity == 0.0f)
            return false;

        // Update mip chain if size has changed
        if (size != currentSize)
        {
            currentSize = size;

            var s = MathF.Max(size.X, size.Y);
            var iterations = (int) MathF.Log2(s);
            UpdateMipChain(frame, size, iterations);
        }

        if (mipChain.Count < 2)
            return false;

        // Get mip 0
        var mip0 = mipChain[0];

        // Prefilter to mip 0
        var threshold = ColorUtil.SrgbToLinear(0.9f); // TODO: expose as parameter
        var knee = 0.5f;
        prefilter.Run(
            frame, mip0.Size,
            new PrefilterPushConstants { Threshold = threshold, Knee = threshold * knee },
            mip0.Down, input, shared.LinearClamp);

        // Downsample and blur down the chain
        for (var i = 1; i < mipChain.Count; i++)
        {
            var sourceMip = mipChain[i - 1];
            var targetMip = mipChain[i];

            // Downsample pass (mip[i - 1] -> mip[i])

            // Horizontal blur
            // Use up mip as our target to save memory
            blur.Run(
                frame, targetMip.Size,
                new BlurPushConstants { IsVertical = 0 },
                targetMip.Up, sourceMip.Down, shared.LinearClamp);

            // Vertical blur
            blur.Run(
                frame, targetMip.Size,
                new BlurPushConstants { IsVertical = 1 },
                targetMip.Down, targetMip.Up, shared.LinearClamp);
        }

        // Upsample back up the chain
        var scatter = MathUtil.MapRange(diffusion, 0.0f, 1.0f, 0.05f, 0.95f);
        for (var i = mipChain.Count - 2; i >= 0; i--)
        {
            var lowMip = i == mipChain.Count - 2 ? mipChain[i + 1].Down : mipChain[i + 1].Up;
            var highMip = mipChain[i].Down;
            var targetMip = mipChain[i]; // Up direction

            // Upsample pass (mip[i + 1] -> mip[i])
            upsample.Run(
                frame, targetMip.Size,
                new UpsamplePushConstants { Scatter = scatter },
                targetMip.Up, lowMip, shared.LinearClamp, highMip, shared.LinearClamp);
        }

        // Combine result with input
        var colorLinear = ColorUtil.SrgbToLinear(color);
        var colorLuminance = 0.2126f * colorLinear.R + 0.7152f * colorLinear.G + 0.0722f * colorLinear.B;
        colorLinear = colorLuminance > 0f ? colorLinear * (1f / colorLuminance) : new ColorRgb(1f, 1f, 1f);
        var tint = colorLinear * intensity;

        combine.Run(
            frame, size,
            new CombinePushConstants { Tint = tint },
            output, input, shared.LinearClamp, mip0.Up, shared.LinearClamp);

        return true;
    }

    private void UpdateMipChain(FrameResources frame, Vector2i size, int levels)
    {
        // The GPU might still be using the old mips
        foreach (var mip in mipChain)
        {
            frame.Defer(mip.Down);
            frame.Defer(mip.Up);
        }

        mipChain.Clear();

        // Create new mip chain
        for (var i = 0; i < levels; i++)
        {
            var mipSize = new Vector2i(
                Math.Max(size.X >> i, 1),
                Math.Max(size.Y >> i, 1));

            var down = GpuImage.CreateGeneral(context, mipSize, VkFormat.FormatR16g16b16a16Sfloat, GpuImage.ComputeUsage);
            var up = GpuImage.CreateGeneral(context, mipSize, VkFormat.FormatR16g16b16a16Sfloat, GpuImage.ComputeUsage);
            mipChain.Add(new Mip(down, up, mipSize));

            if (mipSize is { X: 1, Y: 1 })
                break; // Stop if we've reached 1x1
        }
    }
}
