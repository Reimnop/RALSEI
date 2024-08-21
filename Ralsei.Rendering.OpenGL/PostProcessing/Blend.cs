using OpenTK.Graphics.OpenGL;
using Ralsei.Core;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.OpenGL.PostProcessing;

/// <summary>
/// Lays one image over another, which is how the passes of a frame are layered.
/// </summary>
public class Blend : IDisposable
{
    private readonly int program;
    private readonly int sampler;

    public Blend(ResourceLoader loader)
    {
        program = LoaderUtil.LoadComputeProgram(loader, "PostProcessing/Blend");

        sampler = GL.CreateSampler();
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureWrapT, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureMagFilter, (int) TextureMagFilter.Linear);
    }

    /// <summary>
    /// Draws <paramref name="sourceTexture"/> over what <paramref name="targetTexture"/> has already, which are both premultiplied.
    /// </summary>
    public void Process(Vector2i size, int sourceTexture, int targetTexture)
    {
        GL.UseProgram(program);

        GL.BindImageTexture(0, targetTexture, 0, false, 0, BufferAccess.ReadWrite, InternalFormat.Rgba16f);
        GL.BindTextureUnit(0, sourceTexture);
        GL.BindSampler(0, sampler);

        GL.DispatchCompute(
            (uint) MathUtil.DivideCeil(size.X, 8),
            (uint) MathUtil.DivideCeil(size.Y, 8),
            1);
        GL.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    public void Dispose()
    {
        GL.DeleteProgram(program);
        GL.DeleteSampler(sampler);
    }
}
