namespace Ralsei.Core.Data;

/// <summary>
/// The depth of field at a time: whether the background is blurred, and how far in front of the first layer of the
/// background it is focused.
/// </summary>
public struct DepthOfFieldEffectState
{
    public bool Active { get; set; }
    public float Focus { get; set; }

    /// <summary>
    /// What the interpolation between two keyframes works on: whether it is active is a number there, which is active from a half.
    /// </summary>
    internal readonly record struct Blend(float Active, float Focus)
    {
        public static Blend Lerp(Blend from, Blend to, float t)
            => new(float.Lerp(from.Active, to.Active, t), float.Lerp(from.Focus, to.Focus, t));

        public DepthOfFieldEffectState Resolve()
            => new() { Active = Active > 0.5f, Focus = Focus };
    }
}
