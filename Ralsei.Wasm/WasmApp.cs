using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Rendering;

namespace Ralsei.Wasm;

public class WasmApp : IDisposable
{
    public RandomSeedService RandomSeedService { get; }
    public BeatmapService BeatmapService { get; }

    private readonly ServiceProvider sp;
    
    private readonly IServiceScope scope;
    private readonly AppDirector appDirector;
    private readonly IRenderer renderer;

    private readonly IRenderingFactory renderingFactory;

    public WasmApp(ServiceProvider sp)
    {
        this.sp = sp;
        
        scope = sp.CreateScope();
        
        RandomSeedService = scope.ServiceProvider.GetRequiredService<RandomSeedService>();
        BeatmapService = scope.ServiceProvider.GetRequiredService<BeatmapService>();
        appDirector = scope.ServiceProvider.GetRequiredService<AppDirector>();
        renderer = scope.ServiceProvider.GetRequiredService<IRenderer>();
        renderingFactory = scope.ServiceProvider.GetRequiredService<IRenderingFactory>();
    }
    
    public void ProcessFrame(float time)
    {
        appDirector.SubmitFrame(time);
        renderingFactory.FlushFrame(renderer);
    }

    public void Dispose()
    {
        scope.Dispose();
        sp.Dispose();
    }
}