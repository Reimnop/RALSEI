namespace Ralsei.ShaderProcessor;

/// <summary>
/// Serves shader files from a function, e.g. a resource loader.
/// </summary>
public sealed class DelegateShaderSourceProvider(Func<string, string?> getSource) : IShaderSourceProvider
{
    public string? GetSource(string path) => getSource(path);
}

/// <summary>
/// Serves shader files from memory.
/// </summary>
public sealed class DictionaryShaderSourceProvider(IReadOnlyDictionary<string, string> sources) : IShaderSourceProvider
{
    public string? GetSource(string path) => sources.GetValueOrDefault(path);
}

/// <summary>
/// Serves shader files from a directory. Paths cannot escape the directory.
/// </summary>
public sealed class FileSystemShaderSourceProvider(string rootDirectory) : IShaderSourceProvider
{
    private readonly string root = Path.GetFullPath(rootDirectory);

    public string? GetSource(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;

        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }
}
