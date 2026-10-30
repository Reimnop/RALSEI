using System.Numerics;
using Ralsei.Core.Data;

namespace Ralsei.Rendering.Data;

public struct HueShiftEffectState
{
    public float Angle;
}

public struct BloomEffectState
{
    public float Intensity;
    public float Diffusion;
    public ColorRgb Color;
}

public struct LensDistortionEffectState
{
    public float Intensity;
    public Vector2 Center;
}

public struct ChromaticAberrationEffectState
{
    public float Intensity;
}

public struct VignetteEffectState
{
    public Vector2 Center;
    public float Intensity;
    public bool Rounded;
    public float Roundness;
    public float Smoothness;
    public ColorRgb Color;
    public VignetteMode Mode;
}

public struct GradientEffectState
{
    public ColorRgb Color1;
    public ColorRgb Color2;
    public float Intensity;
    public float Rotation;
    public GradientOverlayMode Mode;
}

public struct GlitchEffectState
{
    public float Speed;
    public float Intensity;
    public float Amount;
    public float StretchMultiplier;
}

public struct DepthOfFieldEffectState
{
    /// <summary>How far the image is blurred, as a fraction of its height (the radius of the disc each point spreads over). Zero for none.</summary>
    public float Radius;
}

public struct GrainEffectState
{
    public bool Colored;
    public float Intensity;
    public float Size;
    public float LuminanceContribution;
}

public struct PostProcessingState
{
    /// <summary>Whether any of the effects done together at the very end is on (hue shift, lens distortion, chromatic aberration, vignette, gradient). If none, that pass only converts to the displayed colours.</summary>
    public readonly bool HasFinalEffects =>
        HueShift.Angle != 0f ||
        LensDistortion.Intensity != 0f ||
        ChromaticAberration.Intensity != 0f ||
        Vignette.Intensity != 0f ||
        Gradient.Intensity != 0f;

    public float Time;
    public HueShiftEffectState HueShift;
    public BloomEffectState LegacyBloom;
    public BloomEffectState UniversalBloom;
    public LensDistortionEffectState LensDistortion;
    public ChromaticAberrationEffectState ChromaticAberration;
    public VignetteEffectState Vignette;
    public GradientEffectState Gradient;
    public GlitchEffectState Glitch;
    public GrainEffectState Grain;
    public DepthOfFieldEffectState DepthOfField;
}