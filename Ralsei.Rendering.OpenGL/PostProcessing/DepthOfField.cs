using OpenTK.Graphics.OpenGL;
using Ralsei.Core;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.OpenGL.PostProcessing;

/// <summary>
/// Blurs the image over a disc
/// </summary>
public class DepthOfField : IDisposable
{
    // A blur of less than this is not seen
    private const float MinRadiusInPixels = 0.25f;

    private readonly int program;
    private readonly int radiusUniformLocation;
    private readonly int sampler;

    public DepthOfField(ResourceLoader loader)
    {
        program = LoaderUtil.LoadComputeProgram(loader, "PostProcessing/DepthOfField");
        radiusUniformLocation = GL.GetUniformLocation(program, "uRadius");

        sampler = GL.CreateSampler();
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureWrapT, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.SamplerParameteri(sampler, SamplerParameterI.TextureMagFilter, (int) TextureMagFilter.Linear);
    }

    /// <param name="radius">The radius of the blur, as a fraction of the height of the image.</param>
    public bool Process(Vector2i size, float radius, int inputTexture, int outputTexture)
    {
        var radiusInPixels = radius * size.Y;
        if (radiusInPixels < MinRadiusInPixels)
            return false;

        GL.UseProgram(program);

        GL.BindImageTexture(0, outputTexture, 0, false, 0, BufferAccess.WriteOnly, InternalFormat.Rgba16f);
        GL.BindTextureUnit(0, inputTexture);
        GL.BindSampler(0, sampler);

        GL.Uniform1f(radiusUniformLocation, radiusInPixels);

        GL.DispatchCompute(
            (uint) MathUtil.DivideCeil(size.X, 8),
            (uint) MathUtil.DivideCeil(size.Y, 8),
            1);
        GL.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);

        return true;
    }

    public void Dispose()
    {
        GL.DeleteProgram(program);
        GL.DeleteSampler(sampler);
    }
}
