using Ralsei.Core.Data;

namespace Swag.Wasm.Interop.Data;

public interface IDynamicSizeKeyframeInteropCodec<T> where T : IKeyframe
{
    int GetSize(T keyframe);
    int Write(T keyframe, IntPtr bufferPtr);
    T Read(IntPtr bufferPtr, out int bytesRead);
}