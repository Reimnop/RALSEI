namespace Ralsei.Core.Data;

/// <summary>The depth of field at a time: whether the background is blurred, and how far in front of its first layer it is focused.</summary>
public struct DepthOfFieldEffectState
{
    public bool Active { get; set; }
    public float Focus { get; set; }

    /// <summary>What interpolation between keyframes works on: whether it is active is a number there, active from a half.</summary>
    public readonly record struct Blend(float Active, float Focus)
    {
        public static Blend Lerp(Blend from, Blend to, float t)
            => new(float.Lerp(from.Active, to.Active, t), float.Lerp(from.Focus, to.Focus, t));

        public DepthOfFieldEffectState Resolve()
            => new() { Active = Active > 0.5f, Focus = Focus };
    }
}
