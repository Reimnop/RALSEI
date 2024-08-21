using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.Vulkan;
using Ralsei.Core;
using Ralsei.Mathematics;
using Ralsei.Util;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

internal sealed class Grain : IDisposable
{
    private const int NoiseTextureWidth = 128;
    private const int NoiseTextureHeight = 128;

    private static readonly Vector2i NoiseTextureSize = new(NoiseTextureWidth, NoiseTextureHeight);

    [StructLayout(LayoutKind.Sequential)]
    private struct NoisePushConstants
    {
        public float Phase;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterPushConstants
    {
        public Vector2 Params; // x = b, y = c
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NoiseAddPushConstants
    {
        public int Channel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositePushConstants
    {
        public Vector2 Scale;
        public Vector2 Offset;
        public int Colored;
        public float LuminanceContribution;
        public float Intensity;
    }

    private readonly ComputeShared shared;
    private readonly ComputeKernel noiseKernel, filterKernel, noiseAddKernel, compositeKernel;
    private readonly GpuImage noiseTextureA, noiseTextureB, noiseTextureCombined;

    public Grain(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.shared = shared;

        noiseKernel = new ComputeKernel(context, shared, loader, "PostProcessing/Grain/Noise");
        filterKernel = new ComputeKernel(context, shared, loader, "PostProcessing/Grain/Filter");
        noiseAddKernel = new ComputeKernel(context, shared, loader, "PostProcessing/Grain/NoiseAdd");
        compositeKernel = new ComputeKernel(context, shared, loader, "PostProcessing/Grain/Composite");

        // Set up noise textures
        noiseTextureA = GpuImage.CreateGeneral(context, NoiseTextureSize, VkFormat.FormatR16Sfloat, GpuImage.ComputeUsage);
        noiseTextureB = GpuImage.CreateGeneral(context, NoiseTextureSize, VkFormat.FormatR16Sfloat, GpuImage.ComputeUsage);
        noiseTextureCombined = GpuImage.CreateGeneral(context, NoiseTextureSize, VkFormat.FormatR16g16b16a16Sfloat, GpuImage.ComputeUsage);
    }

    public void Dispose()
    {
        noiseKernel.Dispose();
        filterKernel.Dispose();
        noiseAddKernel.Dispose();
        compositeKernel.Dispose();

        noiseTextureA.Dispose();
        noiseTextureB.Dispose();
        noiseTextureCombined.Dispose();
    }

    public bool Process(
        FrameResources frame, Vector2i size, float time,
        bool colored, float intensity,
        float grainSize, float luminanceContribution,
        GpuImage input, GpuImage output)
    {
        if (intensity == 0f)
            return false;

        // Get noise texture for this frame
        var noiseTexture = ComputeNoise(frame, time, colored);

        // Randomize noise offset each frame to reduce pattern repetition
        var rndX = NumberUtil.UlongToFloat01(NumberUtil.Mix(NumberUtil.ComputeHash(time), 0));
        var rndY = NumberUtil.UlongToFloat01(NumberUtil.Mix(NumberUtil.ComputeHash(time), 1));

        compositeKernel.Run(
            frame, size,
            new CompositePushConstants
            {
                Scale = new Vector2(
                    size.X / (float) NoiseTextureWidth / grainSize,
                    size.Y / (float) NoiseTextureHeight / grainSize),
                Offset = new Vector2(rndX, rndY),
                Colored = colored ? 1 : 0,
                LuminanceContribution = luminanceContribution,
                Intensity = intensity * 4f
            },
            output,
            input, shared.LinearClamp,
            noiseTexture, shared.LinearRepeat);
        return true;
    }

    private GpuImage ComputeNoise(FrameResources frame, float time, bool colored)
    {
        var phase = time - MathF.Floor(time);
        if (!colored)
            return ComputeNoiseSingleChannel(frame, phase);

        // Generate noise for each channel with different phases
        var redChannel = ComputeNoiseSingleChannel(frame, phase * 0.07f);
        ComputeCombinedNoiseTexture(frame, redChannel, noiseTextureCombined, 0);

        var greenChannel = ComputeNoiseSingleChannel(frame, phase * 0.11f);
        ComputeCombinedNoiseTexture(frame, greenChannel, noiseTextureCombined, 1);

        var blueChannel = ComputeNoiseSingleChannel(frame, phase * 0.13f);
        ComputeCombinedNoiseTexture(frame, blueChannel, noiseTextureCombined, 2);

        return noiseTextureCombined;
    }

    private void ComputeCombinedNoiseTexture(FrameResources frame, GpuImage texture, GpuImage target, int channel)
    {
        noiseAddKernel.Run(
            frame, NoiseTextureSize,
            new NoiseAddPushConstants { Channel = channel },
            target, texture, shared.NearestRepeat);
    }

    private GpuImage ComputeNoiseSingleChannel(FrameResources frame, float phase)
    {
        // Generate noise in noiseTextureA
        noiseKernel.Run(frame, NoiseTextureSize, new NoisePushConstants { Phase = phase }, noiseTextureA);

        // Filter noise into noiseTextureB
        filterKernel.Run(
            frame, NoiseTextureSize,
            new FilterPushConstants { Params = new Vector2(2f, -12f) },
            noiseTextureB, noiseTextureA, shared.NearestRepeat);

        // Filter noise again into noiseTextureA
        filterKernel.Run(
            frame, NoiseTextureSize,
            new FilterPushConstants { Params = new Vector2(2f, 4f) },
            noiseTextureA, noiseTextureB, shared.NearestRepeat);

        return noiseTextureA;
    }
}
