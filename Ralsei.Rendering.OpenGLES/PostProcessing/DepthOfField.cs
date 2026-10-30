using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.OpenGLES.PostProcessing;

/// <summary>Blurs the image over a disc.</summary>
internal class DepthOfField : IDisposable
{
    // A blur of less than this is not seen
    private const float MinRadiusInPixels = 0.25f;

    private readonly int program;
    private readonly int outputSizeUniformLocation;
    private readonly int radiusUniformLocation;
    private readonly int textureSampler;
    private readonly int framebuffer;
    private readonly int vaoHandle;

    public DepthOfField(ResourceLoader loader, int vertexShader)
    {
        program = LoaderUtil.LoadPostProcessingProgram(loader, "DepthOfField", vertexShader);
        outputSizeUniformLocation = GL.GetUniformLocation(program, "uOutputSize");
        radiusUniformLocation = GL.GetUniformLocation(program, "uRadius");

        textureSampler = GL.GenSampler();
        GL.SamplerParameteri(textureSampler, SamplerParameterI.TextureMinFilter, (int) TextureMinFilter.Linear);
        GL.SamplerParameteri(textureSampler, SamplerParameterI.TextureMagFilter, (int) TextureMagFilter.Linear);
        GL.SamplerParameteri(textureSampler, SamplerParameterI.TextureWrapS, (int) TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(textureSampler, SamplerParameterI.TextureWrapT, (int) TextureWrapMode.ClampToEdge);

        framebuffer = GL.GenFramebuffer();
        vaoHandle = GL.GenVertexArray();
    }

    /// <param name="radius">The radius of the blur, as a fraction of the height of the image.</param>
    public bool Process(Vector2i size, float radius, int inputTexture, int outputTexture)
    {
        var radiusInPixels = radius * size.Y;
        if (radiusInPixels < MinRadiusInPixels)
            return false;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, outputTexture, 0);
        GL.Viewport(0, 0, size.X, size.Y);

        GL.UseProgram(program);
        GL.Uniform2i(outputSizeUniformLocation, size.X, size.Y);
        GL.Uniform1f(radiusUniformLocation, radiusInPixels);

        // The blur reads between the pixels. The passes after it do not, so the sampler is taken off again
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, inputTexture);
        GL.BindSampler(0, textureSampler);

        GL.BindVertexArray(vaoHandle);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.BindSampler(0, 0);
        return true;
    }

    public void Dispose()
    {
        GL.DeleteProgram(program);
        GL.DeleteSampler(textureSampler);
        GL.DeleteFramebuffer(framebuffer);
        GL.DeleteVertexArray(vaoHandle);
    }
}
