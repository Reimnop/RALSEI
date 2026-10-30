using Microsoft.Extensions.DependencyInjection;
using Ralsei.Core;

namespace Ralsei;

public class RalseiBuilder
{
    public IServiceCollection Services { get; }
    
    private readonly ResourceSourceFactories resourceSourceFactories;

    public RalseiBuilder(IServiceCollection services, ResourceSourceFactories resourceSourceFactories)
    {
        Services = services;
        this.resourceSourceFactories = resourceSourceFactories;
    }

    public RalseiBuilder UseResourceSourceFactory(Func<IResourceSource> factory)
    {
        resourceSourceFactories.Add(factory);
        return this;
    }
}