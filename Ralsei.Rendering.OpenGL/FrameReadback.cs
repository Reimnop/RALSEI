using OpenTK.Graphics.OpenGL;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.OpenGL;

/// <summary>Gets a frame back for an <see cref="Ralsei.Platform.IFrameReceiver"/>: converts the final texture to 8 bits per channel in a target of its own and reads that. The blit turns the frame over, so rows come out in raster order.</summary>
internal sealed unsafe class FrameReadback : IDisposable
{
    private int sourceFramebuffer, targetFramebuffer, targetTexture;
    private Vector2i size;
    private byte[] pixels = [];

    /// <summary>The first <paramref name="frameSize"/> pixels of <paramref name="texture"/>, valid until the next call.</summary>
    public ReadOnlySpan<byte> Read(int texture, Vector2i frameSize)
    {
        if (targetTexture == 0 || size != frameSize)
        {
            if (sourceFramebuffer == 0)
            {
                sourceFramebuffer = GL.GenFramebuffer();
                targetFramebuffer = GL.GenFramebuffer();
            }

            if (targetTexture != 0)
                GL.DeleteTexture(targetTexture);

            targetTexture = GL.CreateTexture(TextureTarget.Texture2d);
            GL.TextureStorage2D(targetTexture, 1, SizedInternalFormat.Rgba8, frameSize.X, frameSize.Y);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, targetTexture, 0);

            size = frameSize;
            pixels = new byte[frameSize.X * frameSize.Y * 4];
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, sourceFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, texture, 0);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, targetFramebuffer);
        GL.BlitFramebuffer(
            0, 0, size.X, size.Y,
            0, size.Y, size.X, 0,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, targetFramebuffer);
        GL.PixelStorei(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* data = pixels)
            GL.ReadPixels(0, 0, size.X, size.Y, PixelFormat.Rgba, PixelType.UnsignedByte, data);

        return pixels;
    }

    public void Dispose()
    {
        if (targetTexture != 0)
            GL.DeleteTexture(targetTexture);
        if (sourceFramebuffer != 0)
        {
            GL.DeleteFramebuffer(sourceFramebuffer);
            GL.DeleteFramebuffer(targetFramebuffer);
        }

        targetTexture = sourceFramebuffer = targetFramebuffer = 0;
    }
}
