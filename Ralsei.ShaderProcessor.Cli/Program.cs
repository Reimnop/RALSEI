using System.Text;
using Ralsei.ShaderProcessor;

const string usage = """
    usage: shaderproc <shader> --root <directory> [options]

    Preprocesses a GLSL shader: resolves #include, adds the #version and the #defines the shader doesn't
    contain itself, and prints the result (or writes it to a file).

      <shader>             path of the shader, relative to the root
      --root <directory>   directory the shader and its includes are in (required)
      --version <version>  the #version to compile as, e.g. "460 core"
      -D NAME[=VALUE]      define a macro (repeatable)
      -I <directory>       additional include directory, relative to the root (repeatable)
      --line-directives    emit #line directives that point errors into the original files
      -o <file>            write the result to a file instead of the standard output
    """;

string? shader = null;
string? root = null;
string? version = null;
string? output = null;
var lineDirectives = false;
var defines = new Dictionary<string, string?>();
var includeDirectories = new List<string> { "" };

try
{
    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

        switch (args[i])
        {
            case "--root": root = Next(); break;
            case "--version": version = Next(); break;
            case "-o": output = Next(); break;
            case "-I": includeDirectories.Add(Next()); break;
            case "--line-directives": lineDirectives = true; break;
            case "-h" or "--help":
                Console.WriteLine(usage);
                return 0;
            case "-D":
            {
                var define = Next();
                var equals = define.IndexOf('=');
                if (equals < 0)
                    defines[define] = null;
                else
                    defines[define[..equals]] = define[(equals + 1)..];
                break;
            }
            default:
                if (args[i].StartsWith('-') || shader is not null)
                    throw new ArgumentException($"Unexpected argument '{args[i]}'");
                shader = args[i];
                break;
        }
    }

    if (shader is null || root is null)
        throw new ArgumentException("A shader and --root are required");
}
catch (ArgumentException e)
{
    Console.Error.WriteLine($"shaderproc: {e.Message}");
    Console.Error.WriteLine(usage);
    return 2;
}

try
{
    var preprocessor = new GlslPreprocessor(new FileSystemShaderSourceProvider(root));
    var result = preprocessor.Process(shader, new GlslPreprocessorOptions
    {
        Version = version,
        Defines = defines,
        IncludeDirectories = includeDirectories,
        EmitLineDirectives = lineDirectives
    });

    if (output is null)
        Console.Out.Write(result.Source);
    else
        File.WriteAllText(output, result.Source, new UTF8Encoding(false));
    return 0;
}
catch (ShaderProcessingException e)
{
    Console.Error.WriteLine($"shaderproc: {e.Message}");
    return 1;
}
