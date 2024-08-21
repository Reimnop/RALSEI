namespace Ralsei.ShaderProcessor.Tests;

/// <summary>
/// Preprocesses the real shaders of the rendering backends the way the backends do, to catch a missing include,
/// an unbalanced #if or a shader that is no backend's. It does not compile them, there is no GPU here.
/// </summary>
public class SharedShaderTests
{
    private record Api(string Name, string Version, string Define);

    private static readonly Api OpenGL = new("OpenGL", "460 core", "RALSEI_OPENGL");
    private static readonly Api OpenGLES = new("OpenGLES", "300 es", "RALSEI_GLES");
    private static readonly Api Vulkan = new("Vulkan", "460 core", "RALSEI_VULKAN");

    private static string ShaderRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ralsei.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "Ralsei.Rendering.Common", "Resources", "Shaders");
    }

    /// <summary>The shaders that are compiled by themselves: everything but the headers in Common.</summary>
    private static IEnumerable<string> EntryShaders()
    {
        var root = ShaderRoot();
        return Directory.EnumerateFiles(root, "*.glsl", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.StartsWith("Common/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<Api> ApisOf(string shader)
    {
        if (shader.StartsWith("PostProcessing/", StringComparison.Ordinal))
            return [OpenGL, OpenGLES, Vulkan];
        if (shader.StartsWith("Uber.", StringComparison.Ordinal))
            return [OpenGL, Vulkan];
        if (shader.StartsWith("ImGui.", StringComparison.Ordinal))
            return [OpenGL, OpenGLES];
        if (shader.StartsWith("Unlit.", StringComparison.Ordinal) || shader.StartsWith("Text.", StringComparison.Ordinal) || shader == "PostProcess.vert.glsl")
            return [OpenGLES];

        return [];
    }

    /// <summary>Every shader for every API it is for, with the textures read as pages of them; and the Uber shaders also bindless.</summary>
    public static IEnumerable<object[]> Cases()
    {
        return EntryShaders().SelectMany(shader => ApisOf(shader).SelectMany(api =>
        {
            var cases = new List<object[]> { new object[] { shader, api.Name, false } };
            if (shader.StartsWith("Uber.", StringComparison.Ordinal))
                cases.Add([shader, api.Name, true]);
            return cases;
        }));
    }

    [Fact]
    public void EveryShaderBelongsToABackend()
    {
        var orphans = EntryShaders().Where(shader => !ApisOf(shader).Any()).ToList();
        Assert.True(orphans.Count == 0, $"Shaders no backend uses (add them to ApisOf if one does): {string.Join(", ", orphans)}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ShaderPreprocesses(string shader, string apiName, bool bindless)
    {
        var api = new[] { OpenGL, OpenGLES, Vulkan }.Single(a => a.Name == apiName);
        var defines = new Dictionary<string, string?> { [api.Define] = null };

        // The host says how the shaders read textures: all of them by a handle or an index, or a page of at most 16 of them
        if (bindless)
            defines["RALSEI_BINDLESS"] = null;
        else
            defines["RALSEI_TEXTURE_PAGE_SIZE"] = "16";

        var preprocessor = new GlslPreprocessor(new FileSystemShaderSourceProvider(ShaderRoot()));
        var result = preprocessor.Process(shader, new GlslPreprocessorOptions
        {
            Version = api.Version,
            Defines = defines
        });

        var lines = result.Source.Split('\n');
        Assert.Equal($"#version {api.Version}", lines[0]);

        // The defines follow, sorted by name
        Assert.Contains($"#define {api.Define}", lines.Skip(1).Take(defines.Count));

        // The shader on its own needs nothing else: all that the preprocessor handles is gone
        Assert.Single(lines, line => line.StartsWith("#version", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("#include", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("#pragma once", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("#error", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("void main()", StringComparison.Ordinal));
    }
}
