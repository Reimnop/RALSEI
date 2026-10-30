using Pamx.Keyframes;

namespace Swag.Data;

public class Keyframe<T>(float time, Ease ease, T value) : IKeyframe
{
    public float Time => time;
    public Ease Ease => ease;
    public T Value => value;
}