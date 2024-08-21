using Pamx.Keyframes;

namespace Ralsei.Core.Data;

public class RandomizableKeyframe<T>(
    float time, Ease ease, T value,
    RandomMode randomMode, T randomValue, float randomInterval,
    bool isRelative, T particleValue) : IKeyframe
{
    public float Time => time;
    public Ease Ease => ease;
    public T Value => value;
    public RandomMode RandomMode => randomMode;
    public T RandomValue => randomValue;
    public float RandomInterval => randomInterval;
    public bool IsRelative => isRelative;

    /// <summary>
    /// The value of the per-particle track. Only used by particle emitters.
    /// </summary>
    public T ParticleValue => particleValue;
}
