using Ralsei.Core;
using Ralsei.ShaderProcessor;

namespace Ralsei.Rendering.Common;

/// <summary>Loads the GLSL of Resources/Shaders, shared by the backends, for one graphics API.</summary>
/// <remarks>
/// The shaders have no <c>#version</c>. The loader supplies it, and a <c>RALSEI_OPENGL</c>, <c>RALSEI_GLES</c> or
/// <c>RALSEI_VULKAN</c> define that the shaders use to adapt to the API (see Common/Prelude.glsl).
/// Vulkan's shaders are compiled to SPIR-V offline with the same defines, see compile-shaders in the Vulkan project.
/// </remarks>
public sealed class ShaderLoader
{
    private readonly GlslPreprocessor preprocessor;
    private readonly GlslPreprocessorOptions options;

    private ShaderLoader(ResourceLoader loader, string version, IEnumerable<KeyValuePair<string, string?>> defines)
    {
        preprocessor = new GlslPreprocessor(new DelegateShaderSourceProvider(path => loader.ReadResourceString($"Shaders/{path}")));
        options = new GlslPreprocessorOptions
        {
            Version = version,
            Defines = defines.ToDictionary(),
#if DEBUG
            // Compiler errors then point into the original files, see DescribeSourceFiles
            EmitLineDirectives = true
#endif
        };
    }

    public static ShaderLoader ForOpenGL(ResourceLoader loader, params IEnumerable<KeyValuePair<string, string?>> defines)
        => new(loader, "460 core", defines.Append(new KeyValuePair<string, string?>("RALSEI_OPENGL", null)));

    public static ShaderLoader ForOpenGLES(ResourceLoader loader, params IEnumerable<KeyValuePair<string, string?>> defines) 
        => new(loader, "300 es", defines.Append(new KeyValuePair<string, string?>("RALSEI_GLES", null)));

    /// <summary>Loads and preprocesses a shader.</summary>
    /// <param name="name">The shader's path in Resources/Shaders without ".glsl", e.g. "Uber.vert" or "PostProcessing/Glitch".</param>
    public GlslPreprocessorResult Load(string name)
    {
        try
        {
            return preprocessor.Process($"{name}.glsl", options);
        }
        catch (ShaderProcessingException e)
        {
            throw new InvalidOperationException($"Could not load shader '{name}': {e.Message}", e);
        }
    }

    /// <summary>Says which file each source-string number in a compiler error message stands for (debug builds only: the n in "n:42: error").</summary>
    public static string DescribeSourceFiles(GlslPreprocessorResult result)
    {
        return "Source files: " + string.Join(", ", result.SourceFiles.Select((file, i) => $"{i + 1} = {file}"));
    }
}
