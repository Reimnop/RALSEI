using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Pamx.Objects;
using Ralsei.Core.Data;
using Swag.Model;
using Ralsei.Core.Text;
using Ralsei.Mathematics;
using Ralsei.Rendering.Data;
using Ralsei.Util;
using BeatmapObject = Swag.Model.BeatmapObject;

namespace Swag.Service;

/// <summary>
/// Common logic for sources that mirror <see cref="BeatmapObject"/>s into <see cref="PlaybackObject"/>s.
/// Derived classes decide how objects are identified, timed and parented.
/// </summary>
public abstract class ObjectSourceBase : IDisposable
{
    private const ulong PositionKey = 0;
    private const ulong ScaleKey = 1;
    private const ulong RotationKey = 2;
    private const ulong ParticleKey = 3;

    protected readonly TextShaper TextShaper;
    protected readonly PlaybackObjectContainer PlaybackObjects;
    protected readonly RandomSeedService SeedService;

    /// <summary>Added to a beatmap object's start time to get the start time of its playback object.</summary>
    protected virtual float StartTimeOffset => 0f;

    protected ObjectSourceBase(TextShaper textShaper, PlaybackObjectContainer playbackObjects, RandomSeedService seedService)
    {
        TextShaper = textShaper;
        PlaybackObjects = playbackObjects;
        SeedService = seedService;

        SeedService.SeedChanged += OnSeedChanged;
    }

    public virtual void Dispose()
    {
        SeedService.SeedChanged -= OnSeedChanged;
    }

    /// <summary>The beatmap objects currently mirrored by this source.</summary>
    protected abstract IEnumerable<BeatmapObject> GetBeatmapObjects();

    /// <summary>Maps a beatmap object's ID to the ID of its playback object.</summary>
    protected abstract Identifier GetPlaybackObjectId(string beatmapObjectId);

    /// <summary>Called when <see cref="BeatmapObject.ParentId"/> changes on a mirrored object.</summary>
    protected abstract void OnParentIdChanged(BeatmapObject beatmapObject, int playbackObjectIndex);

    protected PlaybackObject CreatePlaybackObject(BeatmapObject beatmapObject)
    {
        var playbackObject = new PlaybackObject(GetPlaybackObjectId(beatmapObject.Id))
        {
            StartTime = beatmapObject.StartTime + StartTimeOffset,
            EndTime = CalculateEndTime(beatmapObject),
            Type = GetPlaybackObjectType(beatmapObject.Type),
            ParentType = beatmapObject.ParentType,
            ParentOffset = beatmapObject.ParentOffset,
            RenderMode = (RenderMode)beatmapObject.RenderType,
            GradientRotation = -MathUtil.DegreesToRadians(beatmapObject.GradientRotation),
            GradientScale = beatmapObject.GradientScale,
            Origin = beatmapObject.Origin,
            RenderDepth = beatmapObject.RenderDepth,
            RenderLayer = beatmapObject.RenderLayer,
            Shape = beatmapObject.Shape,
            Text = ShapeText(beatmapObject),
            CustomShapeInfo = beatmapObject.CustomShapeInfo,
            ParticleParams = beatmapObject.ParticleParams,
            ParticleLifetime = beatmapObject.Duration
        };

        LoadSeededSequences(beatmapObject, playbackObject, SeedService.Seed);
        playbackObject.ColorSequence.LoadKeyframes(PlaybackObjectHelper.ResolveColorKeyframes(beatmapObject.ColorKeyframes));

        playbackObject.ParticlePositionSequence.LoadKeyframes(
            KeyframeHelper.ResolveParticlePositionKeyframes(beatmapObject.PositionKeyframes));
        playbackObject.ParticleScaleSequence.LoadKeyframes(
            KeyframeHelper.ResolveParticleScaleKeyframes(beatmapObject.ScaleKeyframes));
        playbackObject.ParticleRotationSequence.LoadKeyframes(
            KeyframeHelper.ResolveParticleRotationKeyframes(beatmapObject.RotationKeyframes));

        return playbackObject;
    }

    protected void AttachBeatmapObjectEvents(BeatmapObject beatmapObject)
    {
        beatmapObject.PropertyChanged += OnBeatmapObjectPropertyChanged;
        beatmapObject.PositionKeyframesChanged += OnBeatmapObjectPositionKeyframesChanged;
        beatmapObject.ScaleKeyframesChanged += OnBeatmapObjectScaleKeyframesChanged;
        beatmapObject.RotationKeyframesChanged += OnBeatmapObjectRotationKeyframesChanged;
        beatmapObject.ColorKeyframesChanged += OnBeatmapObjectColorKeyframesChanged;
    }

    protected void DetachBeatmapObjectEvents(BeatmapObject beatmapObject)
    {
        beatmapObject.PropertyChanged -= OnBeatmapObjectPropertyChanged;
        beatmapObject.PositionKeyframesChanged -= OnBeatmapObjectPositionKeyframesChanged;
        beatmapObject.ScaleKeyframesChanged -= OnBeatmapObjectScaleKeyframesChanged;
        beatmapObject.RotationKeyframesChanged -= OnBeatmapObjectRotationKeyframesChanged;
        beatmapObject.ColorKeyframesChanged -= OnBeatmapObjectColorKeyframesChanged;
    }

    protected void UpdateStartTime(BeatmapObject beatmapObject, PlaybackObject playbackObject)
    {
        playbackObject.StartTime = beatmapObject.StartTime + StartTimeOffset;
        playbackObject.EndTime = CalculateEndTime(beatmapObject);
    }

    protected bool TryGetPlaybackObject(BeatmapObject beatmapObject, [MaybeNullWhen(false)] out PlaybackObject playbackObject, out int index)
    {
        index = PlaybackObjects.GetIndexForId(GetPlaybackObjectId(beatmapObject.Id));
        return PlaybackObjects.TryGetItem(index, out playbackObject);
    }

    private static PlaybackObjectType GetPlaybackObjectType(BeatmapObjectType type)
        => type switch
        {
            BeatmapObjectType.Empty => PlaybackObjectType.Invisible,
            BeatmapObjectType.Particles => PlaybackObjectType.Particles,
            _ => PlaybackObjectType.Visible
        };

    private float CalculateEndTime(BeatmapObject beatmapObject)
        => PlaybackObjectHelper.CalculateEndTime(
            beatmapObject.StartTime + StartTimeOffset,
            beatmapObject.Duration,
            beatmapObject.AutoKillType,
            beatmapObject.AutoKillOffset);

    /// <summary>Recalculates everything that depends on the object's duration, i.e. the time of its last keyframe.</summary>
    private void UpdateDurationDependentValues(BeatmapObject beatmapObject, PlaybackObject playbackObject)
    {
        playbackObject.EndTime = CalculateEndTime(beatmapObject);
        playbackObject.ParticleLifetime = beatmapObject.Duration;
    }

    private ShapedRichText? ShapeText(BeatmapObject beatmapObject)
        => beatmapObject.Text is not null && beatmapObject.Shape == ObjectShape.Text
            ? TextShaper.ShapeText(beatmapObject.Text, beatmapObject.Origin)
            : null;

    private static void LoadSeededSequences(BeatmapObject beatmapObject, PlaybackObject playbackObject, ulong globalSeed)
    {
        var seed = NumberUtil.Mix(globalSeed, playbackObject.Id);
        playbackObject.PositionSequence.LoadKeyframes(KeyframeHelper.ResolveObjectVector2Keyframes(
            beatmapObject.PositionKeyframes, NumberUtil.Mix(seed, PositionKey)));

        playbackObject.ScaleSequence.LoadKeyframes(KeyframeHelper.ResolveObjectVector2Keyframes(
            beatmapObject.ScaleKeyframes, NumberUtil.Mix(seed, ScaleKey)));

        playbackObject.RotationSequence.LoadKeyframes(KeyframeHelper.ResolveObjectRotationKeyframes(
            beatmapObject.RotationKeyframes, NumberUtil.Mix(seed, RotationKey)));

        playbackObject.ParticleSeed = NumberUtil.Mix(seed, ParticleKey);
    }

    private void OnSeedChanged(object? sender, ulong e)
    {
        foreach (var beatmapObject in GetBeatmapObjects())
        {
            if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out _))
                continue;

            LoadSeededSequences(beatmapObject, playbackObject, e);
        }
    }

    private void OnBeatmapObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BeatmapObject beatmapObject)
            return;

        if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out var index))
            return;

        switch (e.PropertyName)
        {
            case nameof(BeatmapObject.ParentId):
                OnParentIdChanged(beatmapObject, index);
                break;
            case nameof(BeatmapObject.StartTime):
                UpdateStartTime(beatmapObject, playbackObject);
                break;
            case nameof(BeatmapObject.AutoKillType):
            case nameof(BeatmapObject.AutoKillOffset):
                playbackObject.EndTime = CalculateEndTime(beatmapObject);
                break;
            case nameof(BeatmapObject.Type):
                playbackObject.Type = GetPlaybackObjectType(beatmapObject.Type);
                break;
            case nameof(BeatmapObject.ParentType):
                playbackObject.ParentType = beatmapObject.ParentType;
                break;
            case nameof(BeatmapObject.ParentOffset):
                playbackObject.ParentOffset = beatmapObject.ParentOffset;
                break;
            case nameof(BeatmapObject.RenderType):
                playbackObject.RenderMode = (RenderMode)beatmapObject.RenderType;
                break;
            case nameof(BeatmapObject.GradientRotation):
                playbackObject.GradientRotation = -MathUtil.DegreesToRadians(beatmapObject.GradientRotation);
                break;
            case nameof(BeatmapObject.GradientScale):
                playbackObject.GradientScale = beatmapObject.GradientScale;
                break;
            case nameof(BeatmapObject.Origin):
                playbackObject.Origin = beatmapObject.Origin;
                playbackObject.Text = ShapeText(beatmapObject);
                break;
            case nameof(BeatmapObject.RenderDepth):
                playbackObject.RenderDepth = beatmapObject.RenderDepth;
                break;
            case nameof(BeatmapObject.RenderLayer):
                playbackObject.RenderLayer = beatmapObject.RenderLayer;
                break;
            case nameof(BeatmapObject.Shape):
                playbackObject.Shape = beatmapObject.Shape;
                playbackObject.Text = ShapeText(beatmapObject);
                break;
            case nameof(BeatmapObject.Text):
                playbackObject.Text = ShapeText(beatmapObject);
                break;
            case nameof(BeatmapObject.CustomShapeInfo):
                playbackObject.CustomShapeInfo = beatmapObject.CustomShapeInfo;
                break;
            case nameof(BeatmapObject.ParticleParams):
                playbackObject.ParticleParams = beatmapObject.ParticleParams;
                break;
        }
    }

    private void OnBeatmapObjectPositionKeyframesChanged(object? sender, KeyframeList<RandomizableKeyframe<Vector2>> e)
    {
        if (sender is not BeatmapObject beatmapObject)
            return;

        if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out _))
            return;

        var seed = NumberUtil.Mix(SeedService.Seed, playbackObject.Id);
        playbackObject.PositionSequence.LoadKeyframes(
            KeyframeHelper.ResolveObjectVector2Keyframes(e, NumberUtil.Mix(seed, PositionKey)));
        playbackObject.ParticlePositionSequence.LoadKeyframes(
            KeyframeHelper.ResolveParticlePositionKeyframes(e));

        UpdateDurationDependentValues(beatmapObject, playbackObject);
    }

    private void OnBeatmapObjectScaleKeyframesChanged(object? sender, KeyframeList<RandomizableKeyframe<Vector2>> e)
    {
        if (sender is not BeatmapObject beatmapObject)
            return;

        if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out _))
            return;

        var seed = NumberUtil.Mix(SeedService.Seed, playbackObject.Id);
        playbackObject.ScaleSequence.LoadKeyframes(
            KeyframeHelper.ResolveObjectVector2Keyframes(e, NumberUtil.Mix(seed, ScaleKey)));
        playbackObject.ParticleScaleSequence.LoadKeyframes(
            KeyframeHelper.ResolveParticleScaleKeyframes(e));

        UpdateDurationDependentValues(beatmapObject, playbackObject);
    }

    private void OnBeatmapObjectRotationKeyframesChanged(object? sender, KeyframeList<RandomizableKeyframe<float>> e)
    {
        if (sender is not BeatmapObject beatmapObject)
            return;

        if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out _))
            return;

        var seed = NumberUtil.Mix(SeedService.Seed, playbackObject.Id);
        playbackObject.RotationSequence.LoadKeyframes(
            KeyframeHelper.ResolveObjectRotationKeyframes(e, NumberUtil.Mix(seed, RotationKey)));
        playbackObject.ParticleRotationSequence.LoadKeyframes(
            KeyframeHelper.ResolveParticleRotationKeyframes(e));

        UpdateDurationDependentValues(beatmapObject, playbackObject);
    }

    private void OnBeatmapObjectColorKeyframesChanged(object? sender, KeyframeList<Data.Keyframe<BeatmapObjectIndexedColor>> e)
    {
        if (sender is not BeatmapObject beatmapObject)
            return;

        if (!TryGetPlaybackObject(beatmapObject, out var playbackObject, out _))
            return;

        playbackObject.ColorSequence.LoadKeyframes(PlaybackObjectHelper.ResolveColorKeyframes(e));

        UpdateDurationDependentValues(beatmapObject, playbackObject);
    }
}
