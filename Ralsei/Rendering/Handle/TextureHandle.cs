namespace Ralsei.Rendering.Handle;

/// <summary>What a mesh draw is painted with: a factory texture, or what an earlier pass of the frame drew (<see cref="FromPass"/>).</summary>
public readonly struct TextureHandle : IRenderingResourceHandle
{
    /// <summary>No texture (flat colour); also what <c>default</c> is.</summary>
    public static TextureHandle None => default;

    /// <summary>What pass <paramref name="passIndex"/> drew, post-processed, as a frame-sized texture with the first row at the top. Only later passes can use it.</summary>
    public static TextureHandle FromPass(int passIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(passIndex);
        return new TextureHandle(-passIndex - 1, true);
    }

    /// <summary>The texture's id. For a pass it is a distinct negative number, which is how renderers tell them apart.</summary>
    public int Id => idPlusOne - 1;

    /// <summary>Whether <paramref name="id"/>, the <see cref="Id"/> of a handle, is of a pass; and if so, which.</summary>
    public static bool TryGetPassIndex(int id, out int passIndex)
    {
        passIndex = -id - 2;
        return id < 0;
    }

    public bool IsNone => idPlusOne == 0;

    /// <summary>Whether this is the output of a pass of the frame (see <see cref="FromPass"/>) and not a texture of the factory.</summary>
    public bool IsPass => idPlusOne < 0;

    /// <summary>The index of the pass whose output this is.</summary>
    public int PassIndex => IsPass ? -idPlusOne - 1 : throw new InvalidOperationException("The handle is not of a pass");

    // Stored off by one so that the zeroed handle is the one without a texture, not the one of texture 0.
    // A pass is stored as the negative of its index off by one.
    private readonly int idPlusOne;

    public TextureHandle(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        idPlusOne = id + 1;
    }

    private TextureHandle(int idPlusOne, bool _)
    {
        this.idPlusOne = idPlusOne;
    }
}
