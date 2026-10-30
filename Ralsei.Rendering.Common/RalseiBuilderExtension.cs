using Ralsei.Core;

namespace Ralsei.Rendering.Common;

public static class RalseiBuilderExtension
{
    /// <summary>Makes the GLSL the backends share available to <see cref="ShaderLoader"/>.</summary>
    public static RalseiBuilder UseSharedShaders(this RalseiBuilder builder)
    {
        return builder.UseResourceSourceFactory(() => new EmbeddedResourceSource(typeof(ShaderLoader).Assembly));
    }
}
