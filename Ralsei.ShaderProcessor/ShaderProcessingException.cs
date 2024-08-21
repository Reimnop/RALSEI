namespace Ralsei.ShaderProcessor;

/// <summary>
/// Thrown when a shader cannot be preprocessed: a missing include, a malformed directive, an #error, etc.
/// </summary>
public class ShaderProcessingException : Exception
{
    /// <summary>The file the problem is in, if it is in a specific one.</summary>
    public string? File { get; }

    /// <summary>The 1-based line the problem is on, or 0 if it is not on a specific line.</summary>
    public int Line { get; }

    public ShaderProcessingException(string message, string? file = null, int line = 0)
        : base(file is null ? message : line > 0 ? $"{file}({line}): {message}" : $"{file}: {message}")
    {
        File = file;
        Line = line;
    }
}
