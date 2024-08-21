using System;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Microsoft.Extensions.DependencyInjection;
using OpenTK;
using OpenTK.Graphics;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering;

namespace Ralsei.Avalonia.Integration;

public class RalseiView : OpenGlControlBase
{
    private class BindingsContext(GlInterface gl) : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName)
            => gl.GetProcAddress(procName);
    }
    
    private class RenderContext
    {
        public required IRenderingFactory RenderingFactory { get; init; }
        public required IServiceScope Scope { get; init; }
        public required RalseiSurface Surface { get; init; }
        public required IRenderer Renderer { get; init; }
    }
    
    private RenderContext? renderContext;
    
    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        
        if (DataContext is not RalseiViewModel vm)
            throw new InvalidOperationException($"DataContext must be of type {nameof(RalseiViewModel)}");
        
        if (renderContext is not null)
            throw new InvalidOperationException("RenderContext is already initialized");
        
        var sp = vm.ServiceProvider;
        
        var renderingFactory = sp.GetRequiredService<IRenderingFactory>();
        var viewHolder = sp.GetRequiredService<RalseiViewHolder>();
        
        if (viewHolder.View is not null)
            throw new InvalidOperationException("Another view is already initialized");

        viewHolder.View = this;
        
        GLLoader.LoadBindings(new BindingsContext(gl));
        GlBindingsState.IsLoaded = true;

        var scope = sp.CreateScope();
        var renderer = scope.ServiceProvider.GetRequiredService<IRenderer>();
        var surface = (RalseiSurface)scope.ServiceProvider.GetRequiredService<IOpenGLSurface>();
        
        renderContext = new RenderContext
        {
            RenderingFactory = renderingFactory,
            Scope = scope,
            Surface = surface,
            Renderer = renderer
        };
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        base.OnOpenGlDeinit(gl);
        
        if (DataContext is not RalseiViewModel vm)
            throw new InvalidOperationException($"DataContext must be of type {nameof(RalseiViewModel)}");
        
        var sp = vm.ServiceProvider;
        var viewHolder = sp.GetRequiredService<RalseiViewHolder>();
        viewHolder.View = null;
        
        if (renderContext is null)
            return;
        
        renderContext.Scope.Dispose();
        renderContext = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (DataContext is not RalseiViewModel vm)
            throw new InvalidOperationException($"DataContext must be of type {nameof(RalseiViewModel)}");
        
        if (renderContext is null)
            return;

        var surface = renderContext.Surface;
        var renderingFactory = renderContext.RenderingFactory;
        
        surface.TargetFramebufferHandle = fb;
        
        vm.ProcessFrame();
        renderingFactory.FlushFrame(renderContext.Renderer);
        
        RequestNextFrameRendering(); 
    }
}