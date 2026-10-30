using System;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Microsoft.Extensions.DependencyInjection;
using OpenTK;
using OpenTK.Graphics;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering;

namespace Swag.Avalonia.Integration;

public class SwagView : OpenGlControlBase
{
    private class BindingsContext(GlInterface gl) : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => gl.GetProcAddress(procName);
    }
    
    private class RenderContext
    {
        public required IFrameQueue Frames { get; init; }
        public required IServiceScope Scope { get; init; }
        public required SwagSurface Surface { get; init; }
        public required IRenderer Renderer { get; init; }
    }
    
    private RenderContext? renderContext;
    
    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        
        if (DataContext is not SwagViewModel vm)
            throw new InvalidOperationException($"DataContext must be of type {nameof(SwagViewModel)}");
        
        if (renderContext is not null)
            throw new InvalidOperationException("RenderContext is already initialized");
        
        var sp = vm.ServiceProvider;
        
        var frames = sp.GetRequiredService<IFrameQueue>();
        var viewHolder = sp.GetRequiredService<SwagViewHolder>();
        
        if (viewHolder.View is not null)
            throw new InvalidOperationException("Another view is already initialized");

        viewHolder.View = this;
        
        GLLoader.LoadBindings(new BindingsContext(gl));
        GlBindingsState.IsLoaded = true;

        var scope = sp.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IRenderer>();
        var surface = (SwagSurface)scope.ServiceProvider.GetRequiredService<IOpenGLSurface>();
        
        renderContext = new RenderContext
        {
            Frames = frames,
            Scope = scope,
            Surface = surface,
            Renderer = renderer
        };
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        base.OnOpenGlDeinit(gl);
        
        if (DataContext is not SwagViewModel vm)
            throw new InvalidOperationException($"DataContext must be of type {nameof(SwagViewModel)}");
        
        var sp = vm.ServiceProvider;
        var viewHolder = sp.GetRequiredService<SwagViewHolder>();
        viewHolder.View = null;
        
        if (renderContext is null)
            return;
        
        renderContext.Scope.Dispose();
        renderContext = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (DataContext is not SwagViewModel vm)
            throw new InvalidOperationException($"DataContext must be of type {nameof(SwagViewModel)}");
        
        if (renderContext is null)
            return;

        var surface = renderContext.Surface;
        var frames = renderContext.Frames;
        
        surface.TargetFramebufferHandle = fb;
        
        vm.ProcessFrame(surface.RenderSize);
        frames.FlushFrame(renderContext.Renderer);
        
        RequestNextFrameRendering(); 
    }
}