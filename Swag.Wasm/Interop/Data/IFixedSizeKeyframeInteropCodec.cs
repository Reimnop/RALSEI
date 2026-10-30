using Ralsei.Core.Data;

namespace Swag.Wasm.Interop.Data;

public interface IFixedSizeKeyframeInteropCodec<T> where T : IKeyframe
{
    int Size { get; }
    void Write(T keyframe, IntPtr bufferPtr);
    T Read(IntPtr bufferPtr);
}