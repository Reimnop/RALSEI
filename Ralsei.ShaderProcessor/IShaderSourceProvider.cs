namespace Ralsei.ShaderProcessor;

/// <summary>Supplies the text of shader files to <see cref="GlslPreprocessor"/>.</summary>
public interface IShaderSourceProvider
{
    /// <summary>Returns the source of the file at <paramref name="path"/>, or null if there is none.</summary>
    /// <param name="path">A normalized virtual path: '/'-separated, relative to the provider's root, with no "." or ".." segments.</param>
    string? GetSource(string path);
}
