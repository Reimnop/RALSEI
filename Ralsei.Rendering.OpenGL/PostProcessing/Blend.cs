using OpenTK.Graphics.OpenGL;
using Ralsei.Core;
using Ralsei.Mathematics;
using Ralsei.Rendering.Common;
using Ralsei.Rendering.Data;

namespace Ralsei.Rendering.OpenGL.PostProcessing;

/// <summary>Lays one image over another, which is how the passes of a frame are layered.</summary>
internal class Blend : IDisposable
{
    private readonly int program;
    private readonly int opacityUniformLocation;
    private readonly int regionUniformLocation;
    private readonly int sampler;

    public Blend(ResourceLoader loader)
    {
        program = LoaderUtil.LoadComputeProgram(loader, "PostProcessing/Blend");
        opacityUniformLocation = GL.GetUniformLocation(program, "uOpacity");
        regionUniformLocation = GL.GetUniformLocation(program, "uRegion");

        sampler = GL.CreateSampler();
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureWrapT, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureMagFilter, (int) TextureMagFilter.Linear);
    }

    /// <summary>Draws <paramref name="sourceTexture"/> over <paramref name="targetTexture"/>, both premultiplied.</summary>
    /// <param name="opacity">How much of the source shows: 1 for all of it.</param>
    /// <param name="region">The part of the images to do (see <see cref="PassRegion"/>); <c>default</c> for all of them.</param>
    public void Process(Vector2i size, int sourceTexture, int targetTexture, float opacity = 1f, ClipRect region = default)
    {
        var (x, y, width, height) = PassRegion.ToImageRect(region, size);

        GL.UseProgram(program);
        GL.Uniform1f(opacityUniformLocation, opacity);
        GL.Uniform4i(regionUniformLocation, x, y, width, height);

        GL.BindImageTexture(0, targetTexture, 0, false, 0, BufferAccess.ReadWrite, InternalFormat.Rgba16f);
        GL.BindTextureUnit(0, sourceTexture);
        GL.BindSampler(0, sampler);

        GL.DispatchCompute(
            (uint) MathUtil.DivideCeil(width, 8),
            (uint) MathUtil.DivideCeil(height, 8),
            1);
        GL.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);
    }

    public void Dispose()
    {
        GL.DeleteProgram(program);
        GL.DeleteSampler(sampler);
    }
}
