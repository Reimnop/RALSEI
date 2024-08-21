using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Pamx.Objects;
using Ralsei.Core.Data;
using Ralsei.Core.Shape;
using Ralsei.Core.Text;
using Ralsei.Rendering.Data;

namespace Ralsei.Core.Service;

public class PlaybackObject(Identifier id) : IIdentifiable, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public Identifier Id => id;
    
    public float StartTime
    {
        get;
        set => SetField(ref field, value);
    }
    
    public float EndTime
    {
        get;
        set => SetField(ref field, value);
    }

    public PlaybackObjectType Type
    {
        get;
        set => SetField(ref field, value);
    }
    
    public ParentType ParentType 
    {
        get;
        set => SetField(ref field, value);
    }
    
    public ParentOffset ParentOffset
    {
        get;
        set => SetField(ref field, value);
    }

    public RenderMode RenderMode
    {
        get;
        set => SetField(ref field, value);
    }
    
    public float GradientRotation
    {
        get;
        set => SetField(ref field, value);
    }
    
    public float GradientScale
    {
        get;
        set => SetField(ref field, value);
    }

    public Vector2 Origin
    {
        get;
        set => SetField(ref field, value);
    }

    public float RenderDepth
    {
        get;
        set => SetField(ref field, value);
    }

    public RenderLayerType RenderLayer
    {
        get;
        set => SetField(ref field, value);
    }
    
    public ObjectShape Shape 
    {
        get;
        set => SetField(ref field, value);
    }
    
    public ShapedRichText? Text 
    {
        get;
        set => SetField(ref field, value);
    }
    
    public ShapeParameters? CustomShapeInfo 
    {
        get;
        set => SetField(ref field, value);
    }

    public ParticleEmitterParams? ParticleParams
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// How long a single particle lives, in seconds. This is the time of the emitter's last keyframe,
    /// and is independent of the emitter's own lifetime.
    /// </summary>
    public float ParticleLifetime
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// The seed which all per-particle random values are derived from.
    /// </summary>
    public ulong ParticleSeed
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// The time at which this object stops being processed.
    /// For particle emitters whose particles outlive them, this is later than <see cref="EndTime"/>.
    /// </summary>
    public float KillTime
        => Type == PlaybackObjectType.Particles && ParticleParams is { DespawnOnEnd: false }
            ? EndTime + ParticleLifetime
            : EndTime;

    public Sequence<Vector2> PositionSequence { get; } = new(Vector2.Lerp, () => Vector2.Zero);
    public Sequence<Vector2> ScaleSequence { get; } = new(Vector2.Lerp , () => Vector2.One);
    public Sequence<float> RotationSequence { get; } = new(float.Lerp, () => 0f);

    // Per-particle tracks. These are sampled at the age of a particle instead of the time since the emitter started.
    public Sequence<Vector2> ParticlePositionSequence { get; } = new(Vector2.Lerp, () => Vector2.Zero);
    public Sequence<Vector2> ParticleScaleSequence { get; } = new(Vector2.Lerp, () => Vector2.One);
    public Sequence<float> ParticleRotationSequence { get; } = new(float.Lerp, () => 0f);

    /// <summary>
    /// Cache of the distance the emitter has travelled over time. Only used for emitters that emit per unit moved.
    /// </summary>
    public ParticleDistanceTable? ParticleDistanceTable { get; set; }

    public IndirectSequence<BeatmapObjectIndexedColor, BeatmapObjectColor, ThemeColorState> ColorSequence { get; }
        = new(BeatmapObjectColor.Resolve, BeatmapObjectColor.Lerp, 
            _ => new BeatmapObjectColor(new ColorRgb(), new ColorRgb(), 1f));

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}