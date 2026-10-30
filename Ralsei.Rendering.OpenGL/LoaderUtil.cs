using OpenTK.Graphics.OpenGL;
using Ralsei.Core;
using Ralsei.Rendering.Common;
using Ralsei.ShaderProcessor;

namespace Ralsei.Rendering.OpenGL;

internal static class LoaderUtil
{
    public static int LoadShaderProgram(ResourceLoader loader, string vertexShaderName, string fragmentShaderName, params KeyValuePair<string, string?>[] defines)
    {
        var shaders = ShaderLoader.ForOpenGL(loader, defines);
        var vertexShader = CompileShader(ShaderType.VertexShader, shaders.Load(vertexShaderName));
        var fragmentShader = CompileShader(ShaderType.FragmentShader, shaders.Load(fragmentShaderName));

        var program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);

        var linkStatus = GL.GetProgrami(program, ProgramProperty.LinkStatus);
        if (linkStatus == 0)
        {
            GL.GetProgramInfoLog(program, out var infoLog);
            throw new Exception($"Failed to link program: {infoLog}");
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return program;
    }

    public static int LoadComputeProgram(ResourceLoader loader, string shaderName)
    {
        var shader = CompileShader(ShaderType.ComputeShader, ShaderLoader.ForOpenGL(loader).Load(shaderName));

        var program = GL.CreateProgram();
        GL.AttachShader(program, shader);
        GL.LinkProgram(program);

        var linkStatus = GL.GetProgrami(program, ProgramProperty.LinkStatus);
        if (linkStatus == 0)
        {
            GL.GetProgramInfoLog(program, out var infoLog);
            throw new Exception($"Failed to link program: {infoLog}");
        }

        GL.DeleteShader(shader);

        return program;
    }

    private static int CompileShader(ShaderType type, GlslPreprocessorResult source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source.Source);
        GL.CompileShader(shader);

        var compileStatus = GL.GetShaderi(shader, ShaderParameterName.CompileStatus);
        if (compileStatus == 0)
        {
            GL.GetShaderInfoLog(shader, out var infoLog);
            throw new Exception($"Failed to compile {type}: {infoLog}\n{ShaderLoader.DescribeSourceFiles(source)}");
        }

        return shader;
    }
}
