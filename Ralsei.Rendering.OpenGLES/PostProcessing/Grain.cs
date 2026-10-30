using OpenTK.Graphics.OpenGLES2;
using Ralsei.Core;
using Ralsei.Mathematics;
using Ralsei.Util;

namespace Ralsei.Rendering.OpenGLES.PostProcessing;

internal class Grain : IDisposable
{
    private const int NoiseTextureWidth = 128;
    private const int NoiseTextureHeight = 128;
    
    private readonly int noiseProgram, filterProgram, noiseAddProgram, compositeProgram;

    private readonly int
        noisePhaseUniformLocation,
        filterParamsUniformLocation,
        noiseAddChannelUniformLocation,
        compositeColoredUniformLocation,
        compositeLuminanceContributionUniformLocation,
        compositeIntensityUniformLocation,
        compositeScaleUniformLocation,
        compositeOffsetUniformLocation;

    private readonly int noiseTextureA, noiseTextureB, noiseTextureCombined;
    private readonly int noiseSampler, linearSampler, noiseSamplerLinearRepeat;

    private readonly int vao, fbo;
    
    public Grain(ResourceLoader loader, int vertexShader)
    {
        noiseProgram = LoaderUtil.LoadPostProcessingProgram(loader, "Grain/Noise", vertexShader);
        filterProgram = LoaderUtil.LoadPostProcessingProgram(loader, "Grain/Filter", vertexShader);
        noiseAddProgram = LoaderUtil.LoadPostProcessingProgram(loader, "Grain/NoiseAdd", vertexShader);
        compositeProgram = LoaderUtil.LoadPostProcessingProgram(loader, "Grain/Composite", vertexShader);
        
        noisePhaseUniformLocation = GL.GetUniformLocation(noiseProgram, "uPhase");
        filterParamsUniformLocation = GL.GetUniformLocation(filterProgram, "uParams");
        noiseAddChannelUniformLocation = GL.GetUniformLocation(noiseAddProgram, "uChannel");
        compositeColoredUniformLocation = GL.GetUniformLocation(compositeProgram, "uColored");
        compositeLuminanceContributionUniformLocation = GL.GetUniformLocation(compositeProgram, "uLuminanceContribution");
        compositeIntensityUniformLocation = GL.GetUniformLocation(compositeProgram, "uIntensity");
        compositeScaleUniformLocation = GL.GetUniformLocation(compositeProgram, "uScale");
        compositeOffsetUniformLocation = GL.GetUniformLocation(compositeProgram, "uOffset");
        
        var filterSourceSamplerUniformLocation = GL.GetUniformLocation(filterProgram, "uSourceSampler");
        GL.UseProgram(filterProgram);
        GL.Uniform1i(filterSourceSamplerUniformLocation, 0);
        
        var compositeSourceSamplerUniformLocation = GL.GetUniformLocation(compositeProgram, "uSourceSampler");
        var compositeNoiseSamplerUniformLocation = GL.GetUniformLocation(compositeProgram, "uNoiseSampler");
        GL.UseProgram(compositeProgram);
        GL.Uniform1i(compositeSourceSamplerUniformLocation, 0);
        GL.Uniform1i(compositeNoiseSamplerUniformLocation, 1);
        
        var noiseAddSourceSamplerUniformLocation = GL.GetUniformLocation(noiseAddProgram, "uSourceSampler");
        GL.UseProgram(noiseAddProgram);
        GL.Uniform1i(noiseAddSourceSamplerUniformLocation, 0);
        
        noiseTextureA = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2d, noiseTextureA);
        GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.R16f, NoiseTextureWidth, NoiseTextureHeight);
        
        noiseTextureB = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2d, noiseTextureB);
        GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.R16f, NoiseTextureWidth, NoiseTextureHeight);
        
        noiseTextureCombined = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2d, noiseTextureCombined);
        GL.TexStorage2D(TextureTarget.Texture2d, 1, SizedInternalFormat.Rgb16f, NoiseTextureWidth, NoiseTextureHeight);
        
        noiseSampler = GL.GenSampler();
        GL.SamplerParameteri(noiseSampler, SamplerParameterI.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.SamplerParameteri(noiseSampler, SamplerParameterI.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.SamplerParameteri(noiseSampler, SamplerParameterI.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.SamplerParameteri(noiseSampler, SamplerParameterI.TextureWrapT, (int)TextureWrapMode.Repeat);
        
        linearSampler = GL.GenSampler();
        GL.SamplerParameteri(linearSampler, SamplerParameterI.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.SamplerParameteri(linearSampler, SamplerParameterI.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.SamplerParameteri(linearSampler, SamplerParameterI.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.SamplerParameteri(linearSampler, SamplerParameterI.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        
        noiseSamplerLinearRepeat = GL.GenSampler();
        GL.SamplerParameteri(noiseSamplerLinearRepeat, SamplerParameterI.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.SamplerParameteri(noiseSamplerLinearRepeat, SamplerParameterI.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.SamplerParameteri(noiseSamplerLinearRepeat, SamplerParameterI.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.SamplerParameteri(noiseSamplerLinearRepeat, SamplerParameterI.TextureWrapT, (int)TextureWrapMode.Repeat);
        
        vao = GL.GenVertexArray();
        fbo = GL.GenFramebuffer();
    }
    
    public void Dispose()
    {
        GL.DeleteProgram(noiseProgram);
        GL.DeleteProgram(filterProgram);
        GL.DeleteProgram(noiseAddProgram);
        GL.DeleteProgram(compositeProgram); 
        
        GL.DeleteTexture(noiseTextureA);
        GL.DeleteTexture(noiseTextureB);
        GL.DeleteTexture(noiseTextureCombined);
        
        GL.DeleteSampler(noiseSampler);
        GL.DeleteSampler(linearSampler);
        GL.DeleteSampler(noiseSamplerLinearRepeat);
        
        GL.DeleteVertexArray(vao);
        GL.DeleteFramebuffer(fbo);
    }

    public bool Process(
        Vector2i size, float time,
        bool colored, float intensity,
        float grainSize, float luminanceContribution,
        int inputTexture, int outputTexture)
    {
        if (intensity == 0f)
            return false;
        
        GL.BindVertexArray(vao);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        
        GL.BindSampler(0, noiseSampler);
        var noiseTexture = ComputeNoise(time, colored);
        
        GL.Viewport(0, 0, size.X, size.Y);
        
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, outputTexture, 0);
        
        GL.BindSampler(0, linearSampler);
        GL.BindSampler(1, noiseSamplerLinearRepeat);
        
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, inputTexture);
        
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2d, noiseTexture);
        
        GL.UseProgram(compositeProgram);
        GL.Uniform1i(compositeColoredUniformLocation, colored ? 1 : 0);
        GL.Uniform1f(compositeLuminanceContributionUniformLocation, luminanceContribution);
        GL.Uniform1f(compositeIntensityUniformLocation, intensity * 4f);
        GL.Uniform2f(compositeScaleUniformLocation, 
            size.X / (float)NoiseTextureWidth / grainSize, 
            size.Y / (float)NoiseTextureHeight / grainSize);
        
        // Randomize noise offset each frame to reduce pattern repetition
        var rndX = NumberUtil.UlongToFloat01(NumberUtil.Mix(NumberUtil.ComputeHash(time), 0));
        var rndY = NumberUtil.UlongToFloat01(NumberUtil.Mix(NumberUtil.ComputeHash(time), 1));
        GL.Uniform2f(compositeOffsetUniformLocation, rndX, rndY);
        
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        
        return true;
    }
    
    private int ComputeNoise(float time, bool colored)
    {
        GL.Viewport(0, 0, NoiseTextureWidth, NoiseTextureHeight);
        
        var phase = time - MathF.Floor(time);
        if (!colored)
            return ComputeNoiseSingleChannel(phase);
        
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, noiseTextureCombined, 0);
        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        
        // Generate noise for each channel with different phases
        var redChannel = ComputeNoiseSingleChannel(phase * 0.07f);
        
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One); // Additive blending to combine channels
        ComputeCombinedNoiseTexture(redChannel, noiseTextureCombined, 0);
        
        GL.Disable(EnableCap.Blend);
        var greenChannel = ComputeNoiseSingleChannel(phase * 0.11f);
        
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One); // Additive blending to combine channels
        ComputeCombinedNoiseTexture(greenChannel, noiseTextureCombined, 1);
        
        GL.Disable(EnableCap.Blend);
        var blueChannel = ComputeNoiseSingleChannel(phase * 0.13f);
        
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.One, BlendingFactor.One); // Additive blending to combine channels
        ComputeCombinedNoiseTexture(blueChannel, noiseTextureCombined, 2);
        
        GL.Disable(EnableCap.Blend);
        return noiseTextureCombined;
    }
    
    private void ComputeCombinedNoiseTexture(int texture, int target, int channel)
    {
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, target, 0);
        
        GL.UseProgram(noiseAddProgram);
        
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, texture);

        GL.Uniform1i(noiseAddChannelUniformLocation, channel);
        
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
    
    private int ComputeNoiseSingleChannel(float phase)
    {
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, noiseTextureA, 0);
        
        GL.UseProgram(noiseProgram);
        GL.Uniform1f(noisePhaseUniformLocation, phase);
        
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, noiseTextureB, 0);
        
        GL.UseProgram(filterProgram);
        
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2d, noiseTextureA);
        
        GL.Uniform2f(filterParamsUniformLocation, 2f, -12f);
        
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2d, noiseTextureA, 0);
        
        GL.BindTexture(TextureTarget.Texture2d, noiseTextureB);
        GL.Uniform2f(filterParamsUniformLocation, 2f, 4f);
        
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        
        return noiseTextureA;
    }
}