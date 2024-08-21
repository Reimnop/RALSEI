namespace Ralsei.ShaderProcessor;

public sealed class GlslPreprocessorOptions
{
    /// <summary>
    /// The version to compile the shader as, e.g. "460 core" or "300 es" (with or without a leading "#version").
    /// It is emitted as the first line of the output. This always wins: <c>#version</c> directives in the sources
    /// are dropped. If it is null, the first <c>#version</c> found in the sources is used instead, and the
    /// output has none if there is no such directive.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Macros defined before any source code, as if every shader started with <c>#define NAME VALUE</c>.
    /// A null value defines an empty macro (<c>#define NAME</c>). They are emitted sorted by name.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Defines { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// Directories (virtual paths) searched for <c>#include &lt;file&gt;</c>, and for <c>#include "file"</c>
    /// when the file is not found next to the including file. The default is the provider's root.
    /// </summary>
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [""];

    /// <summary>
    /// Emits <c>#line N S</c> directives whenever the output stops following one file, so that compiler errors
    /// point into the original files. S is the 1-based index into <see cref="GlslPreprocessorResult.SourceFiles"/>
    /// (0 is the generated header).
    /// </summary>
    public bool EmitLineDirectives { get; init; }

    /// <summary>
    /// How deeply includes may nest. This is what stops an include cycle that has no include guard.
    /// </summary>
    public int MaxIncludeDepth { get; init; } = 32;
}

public sealed class GlslPreprocessorResult(string source, IReadOnlyList<string> sourceFiles)
{
    /// <summary>The shader source, ready to hand to the GLSL compiler.</summary>
    public string Source { get; } = source;

    /// <summary>
    /// Every file that was read, in order of first use. The entry file is first. Index + 1 is the
    /// source-string number in <c>#line</c> directives.
    /// </summary>
    public IReadOnlyList<string> SourceFiles { get; } = sourceFiles;
}
