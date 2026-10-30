using Pamx.Keyframes;

namespace Swag.Data;

public interface IKeyframe
{
    public float Time { get; }
    public Ease Ease { get; }
}