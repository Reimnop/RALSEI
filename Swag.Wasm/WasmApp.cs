using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Platform.OpenGL;
using Ralsei.Rendering;

namespace Swag.Wasm;

public class WasmApp : IDisposable
{
    public RandomSeedService RandomSeedService { get; }
    public BeatmapService BeatmapService { get; }

    private readonly ServiceProvider sp;
    
    private readonly IServiceScope scope;
    private readonly AppDirector appDirector;
    private readonly IRenderer renderer;
    private readonly IOpenGLSurface surface;

    private readonly IFrameQueue frames;

    public WasmApp(ServiceProvider sp)
    {
        this.sp = sp;
        
        scope = sp.CreateScope();
        
        RandomSeedService = scope.ServiceProvider.GetRequiredService<RandomSeedService>();
        BeatmapService = scope.ServiceProvider.GetRequiredService<BeatmapService>();
        appDirector = scope.ServiceProvider.GetRequiredService<AppDirector>();
        renderer = scope.ServiceProvider.GetRequiredService<IRenderer>();
        frames = scope.ServiceProvider.GetRequiredService<IFrameQueue>();
        surface = scope.ServiceProvider.GetRequiredService<IOpenGLSurface>();
    }
    
    public void ProcessFrame(float time)
    {
        appDirector.SubmitFrame(time, surface.RenderSize);
        frames.FlushFrame(renderer);
    }

    public void Dispose()
    {
        scope.Dispose();
        sp.Dispose();
    }
}