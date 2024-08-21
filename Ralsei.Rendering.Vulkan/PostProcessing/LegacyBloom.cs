using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.Vulkan;
using Ralsei.Core;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

internal sealed class LegacyBloom : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PrefilterPushConstants
    {
        public float Threshold;
        public float Knee;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UpsamplePushConstants
    {
        public float SampleScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CombinePushConstants
    {
        public Vector3 Tint;
    }

    private readonly ComputeShared shared;
    private readonly VulkanContext context;
    private readonly ComputeKernel prefilter, downsample, upsample, combine;

    private readonly List<GpuImage> mipChain = [];
    private Vector2i currentSize = -Vector2i.One;
    private int currentIterations;

    public LegacyBloom(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.context = context;
        this.shared = shared;

        prefilter = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Prefilter");
        downsample = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Legacy/Downsample");
        upsample = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Legacy/Upsample");
        combine = new ComputeKernel(context, shared, loader, "PostProcessing/Bloom/Combine");
    }

    public void Dispose()
    {
        prefilter.Dispose();
        downsample.Dispose();
        upsample.Dispose();
        combine.Dispose();

        foreach (var mip in mipChain)
            mip.Dispose();
    }

    public bool Process(
        FrameResources frame, Vector2i size,
        float intensity, float diffusion, ColorRgb color,
        GpuImage input, GpuImage output)
    {
        if (intensity == 0.0f)
            return false;

        intensity = MathF.Pow(2.0f, intensity / 10.0f) - 1.0f;

        // Determine iteration count
        var s = MathF.Max(size.X, size.Y);
        var logS = MathF.Log2(s) + MathF.Min(diffusion, 10f) - 10f; // Use 10 as base
        var logSFloored = MathF.Floor(logS);
        var iterations = (int) Math.Clamp(logSFloored, 1, 16); // Limit to 16 levels
        var sampleScale = 0.5f + (logS - logSFloored);

        // Update mip chain if size has changed
        if (size != currentSize || iterations != currentIterations)
        {
            currentSize = size;
            currentIterations = iterations;
            UpdateMipChain(frame, size, iterations);
        }

        if (mipChain.Count < 2)
            return false;

        // Get mip 0
        var mip0 = mipChain[0];

        // Prefilter to mip 0
        var threshold = ColorUtil.SrgbToLinear(0.95f); // TODO: expose as parameter
        var knee = 0.5f;
        prefilter.Run(
            frame, mip0.Size,
            new PrefilterPushConstants { Threshold = threshold, Knee = threshold * knee },
            mip0, input, shared.LinearClamp);

        // Downsample
        for (var i = 1; i < mipChain.Count; i++)
        {
            var sourceMip = mipChain[i - 1];
            var targetMip = mipChain[i];

            // Downsample pass (mip[i - 1] -> mip[i])
            downsample.Run(frame, targetMip.Size, targetMip, sourceMip, shared.LinearClamp);
        }

        // Upsample back up the chain
        for (var i = mipChain.Count - 2; i >= 0; i--)
        {
            var sourceMip = mipChain[i + 1];
            var targetMip = mipChain[i];

            // Upsample pass (mip[i + 1] -> mip[i])
            upsample.Run(
                frame, targetMip.Size,
                new UpsamplePushConstants { SampleScale = sampleScale },
                targetMip, sourceMip, shared.LinearClamp);
        }

        // Combine result with input
        var colorLinear = ColorUtil.SrgbToLinear(color);
        var colorLuminance = 0.2126f * colorLinear.R + 0.7152f * colorLinear.G + 0.0722f * colorLinear.B;
        colorLinear = colorLuminance > 0f ? colorLinear * (1f / colorLuminance) : new ColorRgb(1f, 1f, 1f);
        var tint = colorLinear * intensity;

        combine.Run(
            frame, size,
            new CombinePushConstants { Tint = new Vector3(tint.R, tint.G, tint.B) },
            output, input, shared.LinearClamp, mip0, shared.LinearClamp);

        return true;
    }

    private void UpdateMipChain(FrameResources frame, Vector2i size, int levels)
    {
        // The GPU might still be using the old mips
        foreach (var mip in mipChain)
            frame.Defer(mip);

        mipChain.Clear();

        // Create new mip chain
        for (var i = 0; i < levels; i++)
        {
            var mipSize = new Vector2i(
                Math.Max(size.X >> i, 1),
                Math.Max(size.Y >> i, 1));

            mipChain.Add(GpuImage.CreateGeneral(context, mipSize, VkFormat.FormatR16g16b16a16Sfloat, GpuImage.ComputeUsage));

            if (mipSize is { X: 1, Y: 1 })
                break; // Stop if we've reached 1x1
        }
    }
}
