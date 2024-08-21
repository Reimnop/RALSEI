using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ralsei.ShaderProcessor;

/// <summary>
/// A preprocessor for GLSL that adds what the language lacks: <c>#include</c>, macros defined by the host
/// instead of the shader, and a <c>#version</c> chosen by the host instead of the shader.
/// </summary>
/// <remarks>
/// <para>
/// Everything else is left to the GLSL compiler. <c>#define</c>, <c>#extension</c>, <c>#pragma</c> and the rest are
/// copied to the output, and macros are never expanded in the output. The one exception is conditional compilation:
/// <c>#if</c>, <c>#ifdef</c>, <c>#ifndef</c>, <c>#elif</c>, <c>#else</c> and <c>#endif</c> are evaluated here and the
/// parts that are not taken are removed, because an <c>#include</c> in a part that is not taken must not be read.
/// The macros a condition can see are the host's defines, the shader's own <c>#define</c>s and
/// <c>GL_ES</c>/<c>__VERSION__</c>. Function-like macros and other macros the compiler provides can't be used in a
/// condition.
/// </para>
/// <para>
/// <c>#include "file"</c> looks next to the including file first, <c>#include &lt;file&gt;</c> only in
/// <see cref="GlslPreprocessorOptions.IncludeDirectories"/>. <c>#pragma once</c> and ordinary include guards both work.
/// </para>
/// </remarks>
public sealed partial class GlslPreprocessor(IShaderSourceProvider provider)
{
    /// <summary>
    /// Preprocesses the shader at <paramref name="entryPath"/>.
    /// </summary>
    /// <exception cref="ShaderProcessingException">The shader can't be preprocessed.</exception>
    public GlslPreprocessorResult Process(string entryPath, GlslPreprocessorOptions? options = null)
    {
        return new Run(provider, options ?? new GlslPreprocessorOptions()).Execute(entryPath);
    }

    [GeneratedRegex(@"^(?:#\s*version\s+)?(\d+)(?:\s+(core|compatibility|es))?$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("""^(?:"([^"]+)"|<([^>]+)>)$""")]
    private static partial Regex IncludePattern();

    private sealed class ConditionalFrame(bool parentActive, int line)
    {
        public readonly bool ParentActive = parentActive;
        public readonly int Line = line;
        public bool Active;
        public bool Taken;
        public bool SawElse;
    }

    private sealed partial class Run
    {
        private readonly IShaderSourceProvider provider;
        private readonly GlslPreprocessorOptions options;

        private readonly Dictionary<string, Macro> macros = new();
        private readonly List<string> sourceFiles = [];
        private readonly Dictionary<string, int> sourceFileIds = new();
        private readonly HashSet<string> onceFiles = [];
        private readonly StringBuilder body = new();

        private string? version;
        private int versionNumber;
        private string versionProfile = "";

        // Where the next line of output will be according to the last #line directive, to know when a new one is needed
        private int outputFile;
        private int outputLine;

        public Run(IShaderSourceProvider provider, GlslPreprocessorOptions options)
        {
            this.provider = provider;
            this.options = options;

            if (options.Version is not null)
                SetVersion(options.Version, null, 0);

            foreach (var (name, value) in options.Defines)
            {
                if (!ConditionEvaluator.IsIdentifier(name) || name.StartsWith("GL_", StringComparison.Ordinal))
                    throw new ShaderProcessingException($"'{name}' is not a valid macro name");
                if (value is not null && value.AsSpan().ContainsAny('\r', '\n'))
                    throw new ShaderProcessingException($"The value of macro '{name}' contains a line break");

                macros[name] = new Macro(false, value?.Trim() ?? "");
            }
        }

        public GlslPreprocessorResult Execute(string entryPath)
        {
            var path = TryNormalizePath(entryPath, null) ?? throw new ShaderProcessingException($"Shader '{entryPath}' leaves the shader root");
            var source = provider.GetSource(path) ?? throw new ShaderProcessingException($"Shader '{path}' was not found");
            ProcessFile(path, source, 0);

            var output = new StringBuilder();
            if (version is not null)
                output.Append("#version ").Append(version).Append('\n');
            foreach (var (name, value) in options.Defines.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                output.Append("#define ").Append(name);
                if (!string.IsNullOrWhiteSpace(value))
                    output.Append(' ').Append(value.Trim());
                output.Append('\n');
            }

            output.Append(body);
            return new GlslPreprocessorResult(output.ToString(), sourceFiles);
        }

        private void SetVersion(string text, string? file, int line)
        {
            var match = VersionPattern().Match(text.Trim());
            if (!match.Success)
                throw new ShaderProcessingException($"'{text.Trim()}' is not a valid GLSL version", file, line);

            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var profile = match.Groups[2].Value;
            var normalized = profile.Length > 0 ? $"{number} {profile}" : number.ToString(CultureInfo.InvariantCulture);

            if (version is not null && version != normalized)
                throw new ShaderProcessingException($"Conflicting versions '{version}' and '{normalized}'", file, line);

            version = normalized;
            versionNumber = number;
            versionProfile = profile;
        }

        private Macro? LookupMacro(string name)
        {
            if (macros.TryGetValue(name, out var macro))
                return macro;

            // What the compiler defines that a condition can sensibly depend on, as far as the version tells
            if (version is not null)
            {
                if (name == "__VERSION__")
                    return new Macro(false, versionNumber.ToString(CultureInfo.InvariantCulture));
                if (name == "GL_ES" && versionProfile == "es")
                    return new Macro(false, "1");
                if (name == "GL_core_profile" && versionProfile == "core")
                    return new Macro(false, "1");
            }

            return null;
        }

        // With the version known, the compiler's macros that depend on it are certain to be absent when LookupMacro doesn't have them
        private bool IsKnownUndefined(string name)
        {
            return version is not null && name is "GL_ES" or "GL_core_profile" or "__VERSION__";
        }

        private static void ThrowIfCompilerMacro(string name, string file, int line)
        {
            try
            {
                ConditionEvaluator.ThrowIfCompilerMacro(name);
            }
            catch (ConditionException e)
            {
                throw new ShaderProcessingException(e.Message, file, line);
            }
        }

        private bool IsConditionTrue(string expression, string file, int line)
        {
            try
            {
                return ConditionEvaluator.IsTrue(expression, LookupMacro, IsKnownUndefined);
            }
            catch (ConditionException e)
            {
                throw new ShaderProcessingException(e.Message, file, line);
            }
        }

        /// <summary>
        /// Turns a path into a normalized virtual path. A relative path is resolved against
        /// <paramref name="directory"/> (the root if null). Returns null if the path leaves the root.
        /// </summary>
        private static string? TryNormalizePath(string path, string? directory)
        {
            var segments = new List<string>();
            var combined = path.Replace('\\', '/');
            if (directory is { Length: > 0 } && !combined.StartsWith('/'))
                combined = directory + "/" + combined;

            foreach (var segment in combined.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                    continue;

                if (segment == "..")
                {
                    if (segments.Count == 0)
                        return null;
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add(segment);
                }
            }

            return string.Join('/', segments);
        }

        private static string DirectoryOf(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path[..slash];
        }

        private static List<string> SplitLines(string source)
        {
            if (source.Length > 0 && source[0] == '﻿')
                source = source[1..];

            var lines = new List<string>();
            var start = 0;
            for (var i = 0; i < source.Length; i++)
            {
                if (source[i] != '\n' && source[i] != '\r')
                    continue;

                lines.Add(source[start..i]);
                if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                start = i + 1;
            }

            if (start < source.Length)
                lines.Add(source[start..]);
            return lines;
        }

        /// <summary>
        /// Removes comments from <paramref name="text"/> and tracks whether a block comment is still open at its end.
        /// </summary>
        private static string StripComments(string text, ref bool inBlockComment)
        {
            if (!inBlockComment && !text.Contains('/'))
                return text;

            var result = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                if (inBlockComment)
                {
                    if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                }
                else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    break;
                }
                else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    inBlockComment = true;
                    result.Append(' ');
                    i++;
                }
                else
                {
                    result.Append(text[i]);
                }
            }

            return result.ToString();
        }

        private static bool StartsWithDirective(string line)
        {
            foreach (var c in line)
            {
                if (c == '#')
                    return true;
                if (!char.IsWhiteSpace(c))
                    return false;
            }

            return false;
        }

        private void Emit(int fileId, int line, List<string> rawLines)
        {
            if (options.EmitLineDirectives && (outputFile != fileId || outputLine != line))
                body.Append("#line ").Append(line).Append(' ').Append(fileId).Append('\n');

            foreach (var rawLine in rawLines)
                body.Append(rawLine).Append('\n');

            outputFile = fileId;
            outputLine = line + rawLines.Count;
        }

        private void ProcessFile(string path, string source, int depth)
        {
            if (!sourceFileIds.TryGetValue(path, out var fileId))
            {
                sourceFiles.Add(path);
                fileId = sourceFiles.Count;
                sourceFileIds[path] = fileId;
            }

            var lines = SplitLines(source);
            var conditionals = new Stack<ConditionalFrame>();
            var inBlockComment = false;

            bool IsActive() => conditionals.Count == 0 || conditionals.Peek().Active;

            for (var i = 0; i < lines.Count; i++)
            {
                var firstLine = i + 1;
                var raw = lines[i];

                if (inBlockComment || !StartsWithDirective(raw))
                {
                    StripComments(raw, ref inBlockComment);
                    if (IsActive())
                        Emit(fileId, firstLine, [raw]);
                    continue;
                }

                // A directive, possibly continued on the following lines with a trailing backslash
                var rawLines = new List<string> { raw };
                var logical = new StringBuilder(raw);
                while (logical[^1] == '\\' && i + 1 < lines.Count)
                {
                    logical.Length--;
                    i++;
                    rawLines.Add(lines[i]);
                    logical.Append(lines[i]);
                }

                var text = StripComments(logical.ToString(), ref inBlockComment).Trim();
                text = text[1..].TrimStart();
                var nameLength = 0;
                while (nameLength < text.Length && char.IsAsciiLetter(text[nameLength]))
                    nameLength++;
                var directive = text[..nameLength];
                var arguments = text[nameLength..].Trim();

                switch (directive)
                {
                    case "if":
                    case "ifdef":
                    case "ifndef":
                    {
                        var frame = new ConditionalFrame(IsActive(), firstLine);
                        if (frame.ParentActive)
                        {
                            bool condition;
                            if (directive == "if")
                            {
                                condition = IsConditionTrue(arguments, path, firstLine);
                            }
                            else
                            {
                                if (!ConditionEvaluator.IsIdentifier(arguments))
                                    throw new ShaderProcessingException($"#{directive} needs a macro name", path, firstLine);
                                var isDefined = LookupMacro(arguments) is not null;
                                if (!isDefined && !IsKnownUndefined(arguments))
                                    ThrowIfCompilerMacro(arguments, path, firstLine);
                                condition = isDefined == (directive == "ifdef");
                            }

                            frame.Active = condition;
                            frame.Taken = condition;
                        }

                        conditionals.Push(frame);
                        break;
                    }
                    case "elif":
                    {
                        if (conditionals.Count == 0)
                            throw new ShaderProcessingException("#elif without #if", path, firstLine);
                        var frame = conditionals.Peek();
                        if (frame.SawElse)
                            throw new ShaderProcessingException("#elif after #else", path, firstLine);

                        if (frame.ParentActive && !frame.Taken)
                        {
                            frame.Active = IsConditionTrue(arguments, path, firstLine);
                            frame.Taken = frame.Active;
                        }
                        else
                        {
                            frame.Active = false;
                        }

                        break;
                    }
                    case "else":
                    {
                        if (conditionals.Count == 0)
                            throw new ShaderProcessingException("#else without #if", path, firstLine);
                        var frame = conditionals.Peek();
                        if (frame.SawElse)
                            throw new ShaderProcessingException("#else after #else", path, firstLine);

                        frame.SawElse = true;
                        frame.Active = frame.ParentActive && !frame.Taken;
                        frame.Taken = true;
                        break;
                    }
                    case "endif":
                    {
                        if (conditionals.Count == 0)
                            throw new ShaderProcessingException("#endif without #if", path, firstLine);
                        conditionals.Pop();
                        break;
                    }
                    default:
                        if (IsActive())
                            ProcessDirective(directive, arguments, rawLines, path, fileId, firstLine, depth);
                        break;
                }
            }

            if (conditionals.Count > 0)
                throw new ShaderProcessingException("#if is missing its #endif", path, conditionals.Peek().Line);
        }

        private void ProcessDirective(string directive, string arguments, List<string> rawLines, string path, int fileId, int line, int depth)
        {
            switch (directive)
            {
                case "include":
                    ProcessInclude(arguments, path, line, depth);
                    break;

                case "define":
                {
                    var match = DefinePattern().Match(arguments);
                    if (!match.Success)
                        throw new ShaderProcessingException("#define needs a macro name", path, line);

                    var isFunctionLike = match.Groups[2].Success;
                    macros[match.Groups[1].Value] = new Macro(isFunctionLike, match.Groups[3].Value.Trim());
                    Emit(fileId, line, rawLines);
                    break;
                }

                case "undef":
                    if (!ConditionEvaluator.IsIdentifier(arguments))
                        throw new ShaderProcessingException("#undef needs a macro name", path, line);
                    macros.Remove(arguments);
                    Emit(fileId, line, rawLines);
                    break;

                case "version":
                    if (options.Version is null)
                        SetVersion(arguments, path, line);
                    break;

                case "error":
                    throw new ShaderProcessingException($"#error {arguments}", path, line);

                case "pragma" when arguments == "once":
                    onceFiles.Add(path);
                    break;

                default:
                    Emit(fileId, line, rawLines);
                    break;
            }
        }

        [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)(\([^)]*\))?(.*)$", RegexOptions.Singleline)]
        private static partial Regex DefinePattern();

        private static void AddCandidate(List<string> candidates, string? candidate)
        {
            if (candidate is not null && !candidates.Contains(candidate))
                candidates.Add(candidate);
        }

        private void ProcessInclude(string arguments, string path, int line, int depth)
        {
            var match = IncludePattern().Match(arguments);
            if (!match.Success)
                throw new ShaderProcessingException("#include needs \"file\" or <file>", path, line);

            if (depth >= options.MaxIncludeDepth)
                throw new ShaderProcessingException(
                    $"Includes are nested more than {options.MaxIncludeDepth} deep, is there an include cycle without a guard?", path, line);

            var isQuoted = match.Groups[1].Success;
            var requested = isQuoted ? match.Groups[1].Value : match.Groups[2].Value;

            // Candidates that would leave the root are not candidates, so that fallbacks still get their turn
            var candidates = new List<string>();
            if (isQuoted)
                AddCandidate(candidates, TryNormalizePath(requested, DirectoryOf(path)));
            foreach (var directory in options.IncludeDirectories)
                AddCandidate(candidates, TryNormalizePath(requested, directory));

            if (candidates.Count == 0)
                throw new ShaderProcessingException($"Include '{requested}' leaves the shader root", path, line);

            foreach (var candidate in candidates)
            {
                var source = provider.GetSource(candidate);
                if (source is null)
                    continue;

                if (!onceFiles.Contains(candidate))
                    ProcessFile(candidate, source, depth + 1);
                return;
            }

            throw new ShaderProcessingException(
                $"Could not find include '{requested}' (looked for {string.Join(", ", candidates.Select(c => $"'{c}'"))})", path, line);
        }
    }
}
