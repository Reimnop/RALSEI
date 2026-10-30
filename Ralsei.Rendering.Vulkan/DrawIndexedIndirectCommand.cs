using System.Runtime.InteropServices;

namespace Ralsei.Rendering.Vulkan;

// Same layout as VkDrawIndexedIndirectCommand
[StructLayout(LayoutKind.Sequential, Size = 20)]
internal struct DrawIndexedIndirectCommand
{
    public required int IndexCount;
    public required int InstanceCount;
    public required int FirstIndex;
    public required int VertexOffset;
    public required int FirstInstance;
}
