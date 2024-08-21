using System.Numerics;
using System.Runtime.InteropServices;
using Pamx.Events;
using Ralsei.Core;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.Vulkan.PostProcessing;

internal sealed class UberPost : IDisposable
{
    // The offsets are spelled out in UberPost.comp as well. 112 bytes in total.
    [StructLayout(LayoutKind.Explicit, Size = 112)]
    private struct PushConstants
    {
        [FieldOffset(0)] public float HueShiftAngle;

        [FieldOffset(4)] public float LensDistortionIntensity;
        [FieldOffset(8)] public Vector2 LensDistortionCenter;

        [FieldOffset(16)] public float ChromaticAberrationIntensity;

        [FieldOffset(20)] public float VignetteIntensity;
        [FieldOffset(24)] public Vector2 VignetteCenter;
        [FieldOffset(32)] public float VignetteRoundness;
        [FieldOffset(36)] public float VignetteSmoothness;
        [FieldOffset(40)] public int VignetteMode;

        [FieldOffset(44)] public int GradientMode;
        [FieldOffset(48)] public Vector3 VignetteColor;
        [FieldOffset(60)] public float GradientIntensity;
        [FieldOffset(64)] public Vector3 GradientColor1;
        [FieldOffset(80)] public Vector3 GradientColor2;

        // mat2, column-major
        [FieldOffset(96)] public Vector2 GradientRotationColumn0;
        [FieldOffset(104)] public Vector2 GradientRotationColumn1;
    }

    private readonly ComputeShared shared;
    private readonly ComputeKernel kernel;

    public UberPost(VulkanContext context, ComputeShared shared, ResourceLoader loader)
    {
        this.shared = shared;
        kernel = new ComputeKernel(context, shared, loader, "PostProcessing/UberPost");
    }

    public void Dispose()
    {
        kernel.Dispose();
    }

    public bool Process(
        FrameResources frame, Vector2i size,
        float hueShiftAngle,
        float lensDistortionIntensity, Vector2 lensDistortionCenter,
        float chromaticAberrationIntensity,
        Vector2 vignetteCenter, float vignetteIntensity, bool vignetteRounded, float vignetteRoundness, float vignetteSmoothness, ColorRgb vignetteColor, VignetteMode vignetteMode,
        ColorRgb gradientColor1, ColorRgb gradientColor2, float gradientIntensity, float gradientRotation, GradientOverlayMode gradientMode,
        GpuImage input, GpuImage output)
    {
        vignetteColor = ColorUtil.SrgbToLinear(vignetteColor);
        // gradient is done in gamma, so don't convert
        
        var push = new PushConstants
        {
            HueShiftAngle = hueShiftAngle,
            LensDistortionIntensity = lensDistortionIntensity,
            LensDistortionCenter = lensDistortionCenter,
            ChromaticAberrationIntensity = chromaticAberrationIntensity,
            VignetteCenter = vignetteCenter,
            VignetteIntensity = vignetteIntensity * 3,
            VignetteSmoothness = vignetteSmoothness * 5f,
            VignetteColor = new Vector3(vignetteColor.R, vignetteColor.G, vignetteColor.B),
            GradientColor1 = new Vector3(gradientColor1.R, gradientColor1.G, gradientColor1.B),
            GradientColor2 = new Vector3(gradientColor2.R, gradientColor2.G, gradientColor2.B),
            GradientIntensity = gradientIntensity,
            GradientMode = (int) gradientMode,
            GradientRotationColumn0 = new Vector2(MathF.Cos(gradientRotation), -MathF.Sin(gradientRotation)),
            GradientRotationColumn1 = new Vector2(MathF.Sin(gradientRotation), MathF.Cos(gradientRotation))
        };

        if (vignetteMode == VignetteMode.UseRounded)
        {
            push.VignetteRoundness = vignetteRounded ? 1f : 0f;
            push.VignetteMode = 0;
        }
        else
        {
            push.VignetteRoundness = (1f - vignetteRoundness) * 6f + vignetteRoundness;
            push.VignetteMode = 1;
        }

        kernel.Run(frame, size, push, output, input, shared.LinearClamp);
        return true;
    }
}
