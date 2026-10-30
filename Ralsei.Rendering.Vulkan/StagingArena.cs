using OpenTK.Graphics.Vulkan;

namespace Ralsei.Rendering.Vulkan;

/// <summary>Bump allocator over host-visible buffers holding data on its way to device-local resources. Allocations are valid until the next <see cref="Reset"/>, once the GPU is done with the frame that owns the arena.</summary>
internal sealed unsafe class StagingArena(VulkanContext context) : IDisposable
{
    private const ulong MinChunkSize = 4 * 1024 * 1024;
    // Keep copies 16-byte aligned: this satisfies the offset alignment of every copy we do (buffer-to-image included).
    private const ulong Alignment = 16;
    // Chunks grown for a one-off burst (e.g. loading a big beatmap) are given back instead of being kept around.
    private const int MaxRetainedChunks = 2;

    private readonly List<GpuBuffer> chunks = [];
    private int currentChunk;
    private ulong currentOffset;

    public readonly struct Allocation(VkBuffer buffer, ulong offset, byte* pointer)
    {
        public VkBuffer Buffer { get; } = buffer;
        public ulong Offset { get; } = offset;
        public byte* Pointer { get; } = pointer;
    }

    public Allocation Allocate(ulong size)
    {
        size = Math.Max(size, 1);

        while (currentChunk < chunks.Count)
        {
            var chunk = chunks[currentChunk];
            var offset = AlignUp(currentOffset, Alignment);
            if (offset + size <= chunk.Size)
            {
                currentOffset = offset + size;
                return new Allocation(chunk.Buffer, offset, chunk.Mapped + offset);
            }

            currentChunk++;
            currentOffset = 0;
        }

        var newChunk = new GpuBuffer(
            context,
            Math.Max(MinChunkSize, AlignUp(size, Alignment)),
            VkBufferUsageFlagBits.BufferUsageTransferSrcBit,
            MemoryUsage.Staging);
        chunks.Add(newChunk);

        currentChunk = chunks.Count - 1;
        currentOffset = size;
        return new Allocation(newChunk.Buffer, 0, newChunk.Mapped);
    }

    public void Reset()
    {
        for (var i = chunks.Count - 1; i >= MaxRetainedChunks; i--)
        {
            chunks[i].Dispose();
            chunks.RemoveAt(i);
        }

        currentChunk = 0;
        currentOffset = 0;
    }

    public void Dispose()
    {
        foreach (var chunk in chunks)
            chunk.Dispose();
        chunks.Clear();
    }

    private static ulong AlignUp(ulong value, ulong alignment)
        => (value + alignment - 1) / alignment * alignment;
}
