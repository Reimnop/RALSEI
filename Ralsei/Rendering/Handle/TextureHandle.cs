namespace Ralsei.Rendering.Handle;

public readonly struct TextureHandle : IRenderingResourceHandle
{
    /// <summary>
    /// What a draw that has no texture is given. It is also what <c>default</c> is, so draw items that nobody
    /// has filled in draw flat colour.
    /// </summary>
    public static TextureHandle None => default;

    public int Id => idPlusOne - 1;

    public bool IsNone => idPlusOne == 0;

    // Stored off by one so that the zeroed handle is the one without a texture, not the one of texture 0
    private readonly int idPlusOne;

    public TextureHandle(int id)
    {
        idPlusOne = id + 1;
    }
}
