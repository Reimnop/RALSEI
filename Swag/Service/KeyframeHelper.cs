using System.Numerics;
using Pamx.Keyframes;
using Ralsei.Core.Data;
using Ralsei.Mathematics;
using Ralsei.Util;

namespace Swag.Service;

public static class KeyframeHelper
{
    public static IEnumerable<BakedKeyframe<Vector2>> ResolveObjectVector2Keyframes(
        IEnumerable<RandomizableKeyframe<Vector2>> keyframes, ulong seed)
    {
        var prng = NumberUtil.CreatePseudoRng(seed);

        var currentValue = Vector2.Zero;
        foreach (var kf in keyframes)
        {
            var resolvedValue = ParseRandomVector2(
                kf.RandomMode,
                kf.Value,
                kf.RandomValue,
                kf.RandomInterval,
                prng);
            currentValue = kf.IsRelative ? currentValue + resolvedValue : resolvedValue;
            yield return new BakedKeyframe<Vector2>(kf.Time, kf.Ease, resolvedValue);
        }
    }
    
    public static IEnumerable<BakedKeyframe<float>> ResolveObjectRotationKeyframes(
        IEnumerable<RandomizableKeyframe<float>> keyframes, ulong seed)
    {
        var prng = NumberUtil.CreatePseudoRng(seed);
        
        var currentValue = 0f;
        foreach (var kf in keyframes)
        {
            var resolvedValue = ParseRandomFloat(
                kf.RandomMode,
                kf.Value,
                kf.RandomValue,
                kf.RandomInterval,
                prng);
            currentValue = kf.IsRelative ? currentValue + resolvedValue : resolvedValue;
            yield return new BakedKeyframe<float>(kf.Time, kf.Ease, MathUtil.DegreesToRadians(currentValue));
        }
    }
    
    /// <summary>Bakes the per-particle scale track. This track has no randomization.</summary>
    public static IEnumerable<BakedKeyframe<Vector2>> ResolveParticleScaleKeyframes(
        IEnumerable<RandomizableKeyframe<Vector2>> keyframes)
    {
        foreach (var kf in keyframes)
            yield return new BakedKeyframe<Vector2>(kf.Time, kf.Ease, kf.ParticleValue);
    }

    /// <summary>Bakes the per-particle position track, which is where a particle is relative to where it spawned.</summary>
    public static IEnumerable<BakedKeyframe<Vector2>> ResolveParticlePositionKeyframes(
        IEnumerable<RandomizableKeyframe<Vector2>> keyframes)
    {
        foreach (var kf in keyframes)
            yield return new BakedKeyframe<Vector2>(kf.Time, kf.Ease, kf.ParticleValue);
    }

    /// <summary>
    /// Bakes the per-particle rotation track. This track has no randomization.
    /// <para>
    /// The values are absolute angles, the keyframe's relative flag is for the object's own rotation and it is ignored here.
    /// </para>
    /// </summary>
    public static IEnumerable<BakedKeyframe<float>> ResolveParticleRotationKeyframes(
        IEnumerable<RandomizableKeyframe<float>> keyframes)
    {
        foreach (var kf in keyframes)
            yield return new BakedKeyframe<float>(kf.Time, kf.Ease, MathUtil.DegreesToRadians(kf.ParticleValue));
    }

    private static float ParseRandomFloat(RandomMode randomMode, float value, float randomValue, float randomInterval, PseudoRng randomNext)
        => randomMode switch
        {
            RandomMode.None => value,
            RandomMode.Range => RoundToNearest(RandomRange(value, randomValue, randomNext), randomInterval),
            RandomMode.Snap => MathF.Round(RandomRange(value, value + randomInterval, randomNext)),
            RandomMode.Select => RandomFloat(randomNext) < 0.5f ? value : randomValue,
            _ => value
        };
    
    private static Vector2 ParseRandomVector2(RandomMode randomMode, Vector2 value, Vector2 randomValue, float randomInterval, PseudoRng randomNext)
        => randomMode switch
        {
            RandomMode.None => value,
            RandomMode.Range => new Vector2(
                RoundToNearest(RandomRange(value.X, randomValue.X, randomNext), randomInterval),
                RoundToNearest(RandomRange(value.Y, randomValue.Y, randomNext), randomInterval)),
            RandomMode.Snap => new Vector2(
                MathF.Round(RandomRange(value.X, value.X + randomInterval, randomNext)),
                MathF.Round(RandomRange(value.Y, value.Y + randomInterval, randomNext))),
            RandomMode.Select => RandomFloat(randomNext) < 0.5f ? value : randomValue,
            RandomMode.Scale => value * RandomRange(randomValue.X, randomValue.Y, randomNext),
            _ => value
        };
    
    private static float RoundToNearest(float value, float nearest)
    {
        if (nearest == 0.0f)
            return value;
        
        return MathF.Round(value / nearest) * nearest;
    }

    private static float RandomRange(float min, float max, PseudoRng randomNext)
        => float.Lerp(min, max, RandomFloat(randomNext));

    private static float RandomFloat(PseudoRng randomNext)
        => NumberUtil.UlongToFloat01(randomNext());
}