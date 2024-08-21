using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core;
using Ralsei.Mathematics;
using Ralsei.Rendering.Common;

namespace Ralsei.Rendering.OpenGLES.PostProcessing;

/// <summary>
/// Lays one image over another, which is how the passes of a frame are layered.
/// </summary>
public class Blend : IDisposable
{
    private readonly int program;
    private readonly int outputSizeUniformLocation;
    private readonly int framebuffer;
    private readonly int vaoHandle;

    public Blend(ResourceLoader loader, int vertexShader)
    {
        program = LoaderUtil.LoadPostProcessingProgram(loader, "Blend", vertexShader);
        outputSizeUniformLocation = GL.GetUniformLocation(program, "uOutputSize");

        framebuffer = GL.GenFramebuffer();
        vaoHandle = GL.GenVertexArray();
    }

    /// <summary>
    /// Makes <paramref name="targetTexture"/> empty: transparent.
    /// </summary>
    public void Clear(Vector2i size, int targetTexture)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, targetTexture, 0);
        GL.Viewport(0, 0, size.X, size.Y);

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>
    /// Draws <paramref name="sourceTexture"/> over what <paramref name="targetTexture"/> has already, which are both premultiplied.
    /// </summary>
    public void Process(Vector2i size, int sourceTexture, int targetTexture)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, targetTexture, 0);
        GL.Viewport(0, 0, size.X, size.Y);

        GL.UseProgram(program);
        GL.Uniform2i(outputSizeUniformLocation, size.X, size.Y);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, sourceTexture);

        // The pass can not read the target, so the blending does what it would
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(vaoHandle);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        GL.DeleteProgram(program);
        GL.DeleteFramebuffer(framebuffer);
        GL.DeleteVertexArray(vaoHandle);
    }
}
