using System.Reflection;

namespace Ralsei.Core;

public class EmbeddedResourceSource(Assembly assembly) : IResourceSource
{
    public Stream? OpenResource(string resourceName)
    {
        var resourcePath = $"{assembly.GetName().Name}.Resources.{resourceName.Replace('/', '.')}";
        return assembly.GetManifestResourceStream(resourcePath);
    }
}
