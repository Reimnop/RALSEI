namespace Ralsei.ShaderProcessor;

public sealed class GlslPreprocessorOptions
{
    /// <summary>The version to compile as, e.g. "460 core" or "300 es" (with or without "#version"), emitted as the first line. It always wins: <c>#version</c> in the sources is dropped. If null, the first <c>#version</c> in the sources is used, and the output has none if there is no such directive.</summary>
    public string? Version { get; init; }

    /// <summary>Macros defined before any source, as if every shader started with <c>#define NAME VALUE</c>. A null value defines an empty macro. Emitted sorted by name.</summary>
    public IReadOnlyDictionary<string, string?> Defines { get; init; } = new Dictionary<string, string?>();

    /// <summary>Directories (virtual paths) searched for <c>#include &lt;file&gt;</c>, and for <c>#include "file"</c> not found next to the includer. Defaults to the provider's root.</summary>
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [""];

    /// <summary>Emits <c>#line N S</c> directives when the output stops following one file, so compiler errors point into the original files. S is the 1-based index into <see cref="GlslPreprocessorResult.SourceFiles"/> (0 is the generated header).</summary>
    public bool EmitLineDirectives { get; init; }

    /// <summary>How deeply includes may nest, which stops an include cycle with no guard.</summary>
    public int MaxIncludeDepth { get; init; } = 32;
}

public sealed class GlslPreprocessorResult(string source, IReadOnlyList<string> sourceFiles)
{
    /// <summary>The shader source, ready to hand to the GLSL compiler.</summary>
    public string Source { get; } = source;

    /// <summary>Every file read, in order of first use, the entry file first. Index + 1 is the source-string number in <c>#line</c> directives.</summary>
    public IReadOnlyList<string> SourceFiles { get; } = sourceFiles;
}
