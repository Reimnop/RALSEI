using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ralsei.Rendering;

public class Buffer<T>(int capacity = 1024): IReadOnlyBuffer<T> where T : unmanaged
{    
    public ReadOnlySpan<T> Data => data.AsSpan(0, length);
    public ReadOnlySpan<byte> DataAsBytes => MemoryMarshal.AsBytes(Data);

    /// <summary>A writable span of the populated region, for in-place patches after an <see cref="Append(ReadOnlySpan{T})"/>.</summary>
    public Span<T> AsWritableSpan(int start, int length)
        => data.AsSpan(start, length);
    
    public int Length => length;
    public int LengthInBytes => length * Unsafe.SizeOf<T>();

    private T[] data = new T[capacity];

    private int length;
    
    public void EnsureSize(int size)
    {
        if (size < data.Length)
            return;
        
        var newSize = data.Length * 2;
        newSize = Math.Max(newSize, size);
        Array.Resize(ref data, newSize);
    }

    public void Append(T data)
    {
        var start = length;

        EnsureSize(start + 1);
        
        this.data[start] = data;

        length++;
    }
    
    public void Append(ReadOnlySpan<T> data)
    {
        var start = length;

        EnsureSize(start + data.Length);
        
        data.CopyTo(this.data.AsSpan(start));
        
        length += data.Length;
    }
    
    public void Clear()
    {
        length = 0;
    }
}