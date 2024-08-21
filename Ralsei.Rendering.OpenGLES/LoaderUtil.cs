using OpenTK.Graphics.OpenGL;
using Ralsei.Core;
using Ralsei.Rendering.Common;
using Ralsei.ShaderProcessor;

namespace Ralsei.Rendering.OpenGLES;

public static class LoaderUtil
{
    public static int LoadShaderProgram(ResourceLoader loader, string vertexShaderName, string fragmentShaderName, params KeyValuePair<string, string?>[] defines)
    {
        var shaders = ShaderLoader.ForOpenGLES(loader, defines);
        var vertexShader = CompileShader(ShaderType.VertexShader, shaders.Load(vertexShaderName));
        var fragmentShader = CompileShader(ShaderType.FragmentShader, shaders.Load(fragmentShaderName));

        var program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);

        var programLinkStatus = GL.GetProgrami(program, ProgramProperty.LinkStatus);
        if (programLinkStatus == 0)
        {
            GL.GetProgramInfoLog(program, out var infoLog);
            throw new Exception($"Failed to link program: {infoLog}");
        }

        // Clean up
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return program;
    }

    /// <summary>
    /// Compiles the vertex shader that the post-processing programs are linked with. The caller deletes it.
    /// </summary>
    public static int LoadPostProcessVertexShader(ResourceLoader loader)
    {
        return CompileShader(ShaderType.VertexShader, ShaderLoader.ForOpenGLES(loader).Load("PostProcess.vert"));
    }

    public static int LoadPostProcessingProgram(ResourceLoader loader, string shaderName, int vertexShader)
    {
        var fragmentShader = CompileShader(ShaderType.FragmentShader, ShaderLoader.ForOpenGLES(loader).Load($"PostProcessing/{shaderName}"));

        var program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);

        var programLinkStatus = GL.GetProgrami(program, ProgramProperty.LinkStatus);
        if (programLinkStatus == 0)
        {
            GL.GetProgramInfoLog(program, out var infoLog);
            throw new Exception($"Failed to link program: {infoLog}");
        }

        // Clean up
        GL.DetachShader(program, vertexShader);
        GL.DeleteShader(fragmentShader);

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
