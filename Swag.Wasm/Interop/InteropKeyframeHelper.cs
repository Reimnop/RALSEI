using System.Numerics;
using Ralsei.Core.Data;

namespace Swag.Wasm.Interop;

public static class InteropKeyframeHelper
{
    public static KeyframeType GetKeyframeType(IKeyframe keyframe)
        => keyframe switch
        {
            RandomizableKeyframe<Vector2> => KeyframeType.RandomizableVector2,
            RandomizableKeyframe<float> => KeyframeType.RandomizableFloat,
            Keyframe<BeatmapObjectIndexedColor> => KeyframeType.BeatmapObjectIndexedColor,
            _ => KeyframeType.Unknown
        };
}