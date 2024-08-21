using Ralsei.Core;

namespace Ralsei.Resources.Compressed;

public static class Extension
{
    public static ResourceSourceFactories AddCompressedResources(this ResourceSourceFactories factories)
    {
        factories.Add(() => new EmbeddedResourceSource(typeof(Extension).Assembly));
        return factories;
    }
}
