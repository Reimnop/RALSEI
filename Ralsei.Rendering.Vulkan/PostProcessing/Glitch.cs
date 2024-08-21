using System.Runtime.InteropServices;
using OpenTK.Graphics.Vulkan;
using Ralsei.Core;
using Ralsei.Mathematics;
using Ralsei.Util;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

// based on limitless glitch 2
internal sealed class Glitch : IDisposable
{
    private const int NoiseTextureWidth = 64;
    private const int NoiseTextureHeight = 62;

    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Intensity;
        public float ColorIntensity;
    }

    private readonly ComputeShared shared;
    private readonly ComputeKernel kernel;
    private readonly GpuImage noiseTexture;

    private ulong currentNoiseSeed;
    private float currentStretchMultiplier;

    // RGBA rather than RGB: 3-channel 8-bit formats are not universally supported for sampling.
    // The shader only reads the rgb channels.
    private readonly byte[] noiseBuffer = new byte[NoiseTextureWidth * NoiseTextureHeight * 4];

    public Glitch(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.shared = shared;
        kernel = new ComputeKernel(context, shared, loader, "PostProcessing/Glitch");

        noiseTexture = GpuImage.CreateGeneral(
            context, new Vector2i(NoiseTextureWidth, NoiseTextureHeight), VkFormat.FormatR8g8b8a8Unorm,
            VkImageUsageFlagBits.ImageUsageSampledBit | VkImageUsageFlagBits.ImageUsageTransferDstBit);
    }

    public void Dispose()
    {
        kernel.Dispose();
        noiseTexture.Dispose();
    }

    public bool Process(
        FrameResources frame, Vector2i size,
        float time,
        float speed, float intensity, float amount, float stretchMultiplier,
        GpuImage input, GpuImage output)
    {
        if (intensity == 0f || amount == 0f)
            return false;

        var updateFrequency = (0.1f + 0.4f * speed) * 60f; // assume 60 FPS as base
        var step = (int) MathF.Floor(time * updateFrequency); // snap time to intervals

        var noiseSeed = NumberUtil.ComputeHash(step);
        if (noiseSeed != currentNoiseSeed || stretchMultiplier != currentStretchMultiplier)
        {
            currentNoiseSeed = noiseSeed;
            currentStretchMultiplier = stretchMultiplier;

            var rng = NumberUtil.CreatePseudoRng(currentNoiseSeed);
            FillNoiseTexture(rng, currentStretchMultiplier);

            // Upload data to GPU
            frame.UploadToImage(noiseTexture, noiseBuffer);
        }

        kernel.Run(
            frame, size,
            new PushConstants { Intensity = amount, ColorIntensity = intensity },
            output,
            input, shared.LinearClamp,
            noiseTexture, shared.NearestClamp);
        return true;
    }

    private void FillNoiseTexture(PseudoRng rng, float stretchMultiplier)
    {
        GetRandomRgb(rng, out var r, out var g, out var b);
        for (var y = 0; y < NoiseTextureHeight; y++)
        {
            for (var x = 0; x < NoiseTextureWidth; x++)
            {
                if (GetRandomFloat(rng) > stretchMultiplier)
                    GetRandomRgb(rng, out r, out g, out b);

                var offset = (y * NoiseTextureWidth + x) * 4;
                noiseBuffer[offset] = r;
                noiseBuffer[offset + 1] = g;
                noiseBuffer[offset + 2] = b;
                noiseBuffer[offset + 3] = 255;
            }
        }
    }

    private static float GetRandomFloat(PseudoRng rng)
        => NumberUtil.UlongToFloat01(rng());

    private static void GetRandomRgb(PseudoRng rng, out byte r, out byte g, out byte b)
    {
        unchecked
        {
            var value = rng();
            r = (byte) (value & 0xFF);
            g = (byte) ((value >> 8) & 0xFF);
            b = (byte) ((value >> 16) & 0xFF);
        }
    }
}
