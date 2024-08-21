using Pamx.Keyframes;

namespace Ralsei.Core.Data;

public interface IKeyframe
{
    public float Time { get; }
    public Ease Ease { get; }
}