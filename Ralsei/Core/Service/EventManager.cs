using System.Numerics;
using Pamx.Events;
using Ralsei.Core.Data;
using Ralsei.Core.Model;

namespace Ralsei.Core.Service;

public class EventManager
{
    private readonly Sequence<Vector2> cameraPositionSequence = new(Vector2.Lerp, () => Vector2.Zero);
    private readonly Sequence<float> cameraRotationSequence = new(float.Lerp, () => 0f);
    private readonly Sequence<float> cameraScaleSequence = new(float.Lerp, () => 20f);
    private readonly Sequence<float> cameraShakeSequence = new(float.Lerp, () => 0f);
    private readonly Sequence<float> chromaSequence = new(float.Lerp, () => 0f);
    private readonly IndirectSequence<BloomValue, BloomEffectState, ThemeColorState> bloomSequence = new(
        EventHelper.ResolveBloomData,
        BloomEffectState.Lerp,
        _ => default);
    private readonly IndirectSequence<VignetteValue, VignetteEffectState, ThemeColorState> vignetteSequence = new(
        EventHelper.ResolveVignetteData,
        VignetteEffectState.Lerp,
        _ => default);
    private readonly Sequence<LensDistortionValue> lensDistortionSequence = new(EventHelper.LerpLensDistortionData, () => default);
    private readonly Sequence<GrainValue> grainSequence = new(EventHelper.LerpGrainData, () => default);
    private readonly IndirectSequence<GradientValue, GradientEffectState, ThemeColorState> gradientSequence = new(
        EventHelper.ResolveGradientData,
        GradientEffectState.Lerp,
        _ => default);
    private readonly Sequence<GlitchValue> glitchSequence = new(EventHelper.LerpGlitchData, () => default);
    private readonly Sequence<float> hueSequence = new(float.Lerp, () => 0f);

    // What the player events say of the depth of field. Without any, it is what the beatmap's parallax settings say
    private readonly Sequence<DepthOfFieldEffectState.Blend> depthOfFieldSequence;

    private readonly EventState state = new();

    public EventManager()
    {
        depthOfFieldSequence = new Sequence<DepthOfFieldEffectState.Blend>(DepthOfFieldEffectState.Blend.Lerp, GetLevelDepthOfField);
    }
    
    private BeatmapData? attachedBeatmapData;
    
    public void AttachBeatmapData(BeatmapData beatmapData)
    {
        if (attachedBeatmapData is not null)
            throw new InvalidOperationException($"A {nameof(BeatmapData)} is already attached");
        
        attachedBeatmapData = beatmapData;
        
        // load events
        cameraPositionSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.CameraPosition));
        cameraRotationSequence.LoadKeyframes(EventHelper.ResolveRotationKeyframes(beatmapData.Events.CameraRotation));
        cameraScaleSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.CameraScale));
        cameraShakeSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.CameraShake));
        chromaSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.Chroma));
        bloomSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.Bloom));
        vignetteSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.Vignette));
        lensDistortionSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.LensDistortion));
        grainSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.Grain));
        gradientSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.Gradient));
        glitchSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(beatmapData.Events.Glitch));
        hueSequence.LoadKeyframes(EventHelper.ResolveRotationKeyframes(beatmapData.Events.Hue));
        LoadDepthOfFieldKeyframes(beatmapData.Events.Player);

        // attach events
        beatmapData.Events.CameraPositionKeyframesChanged += OnEventsCameraPositionKeyframesChanged;
        beatmapData.Events.CameraRotationKeyframesChanged += OnEventsCameraRotationKeyframesChanged;
        beatmapData.Events.CameraScaleKeyframesChanged += OnEventsCameraScaleKeyframesChanged;
        beatmapData.Events.CameraShakeKeyframesChanged += OnEventsCameraShakeKeyframesChanged;
        beatmapData.Events.ChromaKeyframesChanged += OnEventsChromaKeyframesChanged;
        beatmapData.Events.BloomKeyframesChanged += OnBloomKeyframesChanged;
        beatmapData.Events.VignetteKeyframesChanged += OnVignetteKeyframesChanged;
        beatmapData.Events.LensDistortionKeyframesChanged += OnLensDistortionKeyframesChanged;
        beatmapData.Events.GrainKeyframesChanged += OnGrainKeyframesChanged;
        beatmapData.Events.GradientKeyframesChanged += OnGradientKeyframesChanged;
        beatmapData.Events.GlitchKeyframesChanged += OnGlitchKeyframesChanged;
        beatmapData.Events.HueKeyframesChanged += OnHueKeyframesChanged;
        beatmapData.Events.PlayerKeyframesChanged += OnPlayerKeyframesChanged;
        beatmapData.Parallax.DepthOfFieldChanged += OnParallaxDepthOfFieldChanged;
    }

    public void DetachBeatmapData()
    {
        if (attachedBeatmapData is null)
            return;
        
        // unload events
        cameraPositionSequence.LoadKeyframes([]);
        cameraRotationSequence.LoadKeyframes([]);
        cameraScaleSequence.LoadKeyframes([]);
        cameraShakeSequence.LoadKeyframes([]);
        chromaSequence.LoadKeyframes([]);
        bloomSequence.LoadKeyframes([]);
        vignetteSequence.LoadKeyframes([]);
        lensDistortionSequence.LoadKeyframes([]);
        grainSequence.LoadKeyframes([]);
        gradientSequence.LoadKeyframes([]);
        glitchSequence.LoadKeyframes([]);
        hueSequence.LoadKeyframes([]);
        depthOfFieldSequence.LoadKeyframes([]);
        
        // detach events
        attachedBeatmapData.Events.CameraPositionKeyframesChanged -= OnEventsCameraPositionKeyframesChanged;
        attachedBeatmapData.Events.CameraRotationKeyframesChanged -= OnEventsCameraRotationKeyframesChanged;
        attachedBeatmapData.Events.CameraScaleKeyframesChanged -= OnEventsCameraScaleKeyframesChanged;
        attachedBeatmapData.Events.CameraShakeKeyframesChanged -= OnEventsCameraShakeKeyframesChanged;
        attachedBeatmapData.Events.ChromaKeyframesChanged -= OnEventsChromaKeyframesChanged;
        attachedBeatmapData.Events.BloomKeyframesChanged -= OnBloomKeyframesChanged;
        attachedBeatmapData.Events.VignetteKeyframesChanged -= OnVignetteKeyframesChanged;
        attachedBeatmapData.Events.LensDistortionKeyframesChanged -= OnLensDistortionKeyframesChanged;
        attachedBeatmapData.Events.GrainKeyframesChanged -= OnGrainKeyframesChanged;
        attachedBeatmapData.Events.GradientKeyframesChanged -= OnGradientKeyframesChanged;
        attachedBeatmapData.Events.GlitchKeyframesChanged -= OnGlitchKeyframesChanged;
        attachedBeatmapData.Events.HueKeyframesChanged -= OnHueKeyframesChanged;
        attachedBeatmapData.Events.PlayerKeyframesChanged -= OnPlayerKeyframesChanged;
        attachedBeatmapData.Parallax.DepthOfFieldChanged -= OnParallaxDepthOfFieldChanged;
        
        attachedBeatmapData = null;
    }

    public EventState ComputeEventAt(float time, ThemeColorState tcs)
    {
        state.CameraPosition = cameraPositionSequence.ComputeValueAt(time);
        state.CameraRotation = cameraRotationSequence.ComputeValueAt(time);
        state.CameraScale = cameraScaleSequence.ComputeValueAt(time);
        state.CameraShake = cameraShakeSequence.ComputeValueAt(time);
        state.Chroma = chromaSequence.ComputeValueAt(time);
        state.Bloom = bloomSequence.ComputeValueAt(time, tcs);
        state.Vignette = vignetteSequence.ComputeValueAt(time, tcs);
        state.LensDistortion = lensDistortionSequence.ComputeValueAt(time);
        state.Grain = grainSequence.ComputeValueAt(time);
        state.Gradient = gradientSequence.ComputeValueAt(time, tcs);
        state.Glitch = glitchSequence.ComputeValueAt(time);
        state.Hue = hueSequence.ComputeValueAt(time);
        state.DepthOfField = depthOfFieldSequence.ComputeValueAt(time).Resolve();
        return state;
    }

    private DepthOfFieldEffectState.Blend GetLevelDepthOfField()
    {
        var parallax = attachedBeatmapData?.Parallax;
        return parallax is null
            ? default
            : new DepthOfFieldEffectState.Blend(parallax.IsDepthOfFieldEnabled ? 1f : 0f, parallax.DepthOfField);
    }

    /// <summary>
    /// The player events can turn the depth of field on and off and move its focus. Each keyframe says what it does, in the
    /// context of those before it: one that does not say (level) is what the beatmap's parallax settings say, one that turns it off
    /// leaves the focus where it was. A beatmap whose player events do not touch it has the parallax settings' depth of field, always.
    /// </summary>
    private void LoadDepthOfFieldKeyframes(KeyframeList<Keyframe<PlayerValue>> playerKeyframes)
    {
        var level = GetLevelDepthOfField();
        var baked = new List<BakedKeyframe<DepthOfFieldEffectState.Blend>>();
        var driven = false;

        var current = level;
        foreach (var keyframe in playerKeyframes)
        {
            switch (keyframe.Value.DepthOfFieldMode)
            {
                case PlayerDepthOfFieldMode.Level:
                    current = level;
                    break;
                case PlayerDepthOfFieldMode.On:
                    current = new DepthOfFieldEffectState.Blend(1f, float.IsNaN(keyframe.Value.DepthOfFieldFocus) ? level.Focus : keyframe.Value.DepthOfFieldFocus);
                    driven = true;
                    break;
                case PlayerDepthOfFieldMode.Off:
                    current = current with { Active = 0f };
                    driven = true;
                    break;
            }

            baked.Add(new BakedKeyframe<DepthOfFieldEffectState.Blend>(keyframe.Time, keyframe.Ease, current));
        }

        depthOfFieldSequence.LoadKeyframes(driven ? baked : []);
    }

    private void OnParallaxDepthOfFieldChanged(object? sender, EventArgs e)
    {
        if (attachedBeatmapData is not null)
            LoadDepthOfFieldKeyframes(attachedBeatmapData.Events.Player);
    }

    private void OnPlayerKeyframesChanged(object? sender, KeyframeList<Keyframe<PlayerValue>> e)
    {
        LoadDepthOfFieldKeyframes(e);
    }

    private void OnEventsCameraPositionKeyframesChanged(object? sender, KeyframeList<Keyframe<Vector2>> e)
    {
        cameraPositionSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }

    private void OnEventsCameraRotationKeyframesChanged(object? sender, KeyframeList<Keyframe<float>> e)
    {
        cameraRotationSequence.LoadKeyframes(EventHelper.ResolveRotationKeyframes(e));
    }

    private void OnEventsCameraScaleKeyframesChanged(object? sender, KeyframeList<Keyframe<float>> e)
    {
        cameraScaleSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnEventsCameraShakeKeyframesChanged(object? sender, KeyframeList<Keyframe<float>> e)
    {
        cameraShakeSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnEventsChromaKeyframesChanged(object? sender, KeyframeList<Keyframe<float>> e)
    {
        chromaSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnBloomKeyframesChanged(object? sender, KeyframeList<Keyframe<BloomValue>> e)
    {
        bloomSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnVignetteKeyframesChanged(object? sender, KeyframeList<Keyframe<VignetteValue>> e)
    {
        vignetteSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnLensDistortionKeyframesChanged(object? sender, KeyframeList<Keyframe<LensDistortionValue>> e)
    {
        lensDistortionSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnGrainKeyframesChanged(object? sender, KeyframeList<Keyframe<GrainValue>> e)
    {
        grainSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnGradientKeyframesChanged(object? sender, KeyframeList<Keyframe<GradientValue>> e)
    {
        gradientSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnGlitchKeyframesChanged(object? sender, KeyframeList<Keyframe<GlitchValue>> e)
    {
        glitchSequence.LoadKeyframes(EventHelper.ResolveGenericKeyframes(e));
    }
    
    private void OnHueKeyframesChanged(object? sender, KeyframeList<Keyframe<float>> e)
    {
        hueSequence.LoadKeyframes(EventHelper.ResolveRotationKeyframes(e));
    }
}