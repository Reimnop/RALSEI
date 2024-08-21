namespace Ralsei.ShaderProcessor.Tests;

public class GlslPreprocessorTests
{
    private static GlslPreprocessorResult Run(
        Dictionary<string, string> files,
        string entry = "main.glsl",
        GlslPreprocessorOptions? options = null)
    {
        return new GlslPreprocessor(new DictionaryShaderSourceProvider(files)).Process(entry, options);
    }

    private static string Process(string source, GlslPreprocessorOptions? options = null)
    {
        return Run(new Dictionary<string, string> { ["main.glsl"] = source }, options: options).Source;
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void PlainSourceIsUnchanged()
    {
        var source = Lines("void main() {", "    gl_Position = vec4(0.0);", "}");
        Assert.Equal(source, Process(source));
    }

    [Fact]
    public void LineEndingsAndBomAreNormalized()
    {
        Assert.Equal(Lines("a", "b", "c"), Process("﻿a\r\nb\rc\n"));
    }

    [Fact]
    public void ExternalVersionComesFirst()
    {
        var result = Process("void main() {}\n", new GlslPreprocessorOptions { Version = "460 core" });
        Assert.Equal(Lines("#version 460 core", "void main() {}"), result);
    }

    [Theory]
    [InlineData("300 es", "#version 300 es")]
    [InlineData("#version 450", "#version 450")]
    [InlineData("  #  version   330   core ", "#version 330 core")]
    public void ExternalVersionIsNormalized(string version, string expected)
    {
        Assert.StartsWith(expected + "\n", Process("x\n", new GlslPreprocessorOptions { Version = version }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("core")]
    [InlineData("460 fancy")]
    public void InvalidExternalVersionIsRejected(string version)
    {
        Assert.Throws<ShaderProcessingException>(() => Process("x\n", new GlslPreprocessorOptions { Version = version }));
    }

    [Fact]
    public void ExternalVersionOverridesVersionInSource()
    {
        var result = Process(Lines("#version 330 core", "x"), new GlslPreprocessorOptions { Version = "460 core" });
        Assert.Equal(Lines("#version 460 core", "x"), result);
    }

    [Fact]
    public void VersionInSourceIsKeptAtTheTopWhenThereIsNoExternalOne()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("// header comment", "#include \"a.glsl\"", "#version 330 core", "x"),
            ["a.glsl"] = "a\n"
        };
        var result = Run(files, options: new GlslPreprocessorOptions { Defines = new Dictionary<string, string?> { ["FOO"] = "1" } });
        Assert.Equal(Lines("#version 330 core", "#define FOO 1", "// header comment", "a", "x"), result.Source);
    }

    [Fact]
    public void ConflictingVersionsInSourceAreRejected()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#version 330 core", "#include \"a.glsl\""),
            ["a.glsl"] = "#version 460 core\n"
        };
        Assert.Throws<ShaderProcessingException>(() => Run(files));
    }

    [Fact]
    public void NoVersionMeansNoVersionLine()
    {
        Assert.DoesNotContain("#version", Process("x\n"));
    }

    [Fact]
    public void DefinesAreEmittedAfterTheVersionSortedByName()
    {
        var options = new GlslPreprocessorOptions
        {
            Version = "460 core",
            Defines = new Dictionary<string, string?> { ["B_FLAG"] = null, ["A_COUNT"] = "16", ["C_EXPR"] = " (1 + 2) " }
        };
        Assert.Equal(
            Lines("#version 460 core", "#define A_COUNT 16", "#define B_FLAG", "#define C_EXPR (1 + 2)", "x"),
            Process("x\n", options));
    }

    [Theory]
    [InlineData("1BAD")]
    [InlineData("HAS SPACE")]
    [InlineData("")]
    [InlineData("GL_RESERVED")]
    public void InvalidDefineNamesAreRejected(string name)
    {
        var options = new GlslPreprocessorOptions { Defines = new Dictionary<string, string?> { [name] = "1" } };
        Assert.Throws<ShaderProcessingException>(() => Process("x\n", options));
    }

    [Fact]
    public void DefineValuesWithLineBreaksAreRejected()
    {
        var options = new GlslPreprocessorOptions { Defines = new Dictionary<string, string?> { ["X"] = "1\n#error" } };
        Assert.Throws<ShaderProcessingException>(() => Process("x\n", options));
    }

    [Fact]
    public void IncludeIsReplacedByTheFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("before", "#include \"a.glsl\"", "after"),
            ["a.glsl"] = Lines("in a")
        };
        Assert.Equal(Lines("before", "in a", "after"), Run(files).Source);
    }

    [Fact]
    public void QuotedIncludesAreRelativeToTheIncludingFile()
    {
        var files = new Dictionary<string, string>
        {
            ["Effects/main.glsl"] = Lines("#include \"Part.glsl\"", "#include \"../Common/Util.glsl\""),
            ["Effects/Part.glsl"] = Lines("part", "#include \"Deeper/Leaf.glsl\""),
            ["Effects/Deeper/Leaf.glsl"] = Lines("leaf", "#include \"../../Common/Util.glsl\""),
            ["Common/Util.glsl"] = Lines("util")
        };
        Assert.Equal(Lines("part", "leaf", "util", "util"), Run(files, "Effects/main.glsl").Source);
    }

    [Fact]
    public void QuotedIncludesFallBackToIncludeDirectories()
    {
        var files = new Dictionary<string, string>
        {
            ["Effects/main.glsl"] = Lines("#include \"Common/Util.glsl\""),
            ["Common/Util.glsl"] = Lines("util")
        };
        Assert.Equal(Lines("util"), Run(files, "Effects/main.glsl").Source);
    }

    [Fact]
    public void AngleIncludesOnlySearchIncludeDirectories()
    {
        var files = new Dictionary<string, string>
        {
            ["Effects/main.glsl"] = Lines("#include <Util.glsl>"),
            ["Effects/Util.glsl"] = Lines("local"),
            ["lib/Util.glsl"] = Lines("library")
        };
        var options = new GlslPreprocessorOptions { IncludeDirectories = ["lib"] };
        Assert.Equal(Lines("library"), Run(files, "Effects/main.glsl", options).Source);
    }

    [Fact]
    public void IncludePathsMayUseBackslashesAndLeadingSlashes()
    {
        var files = new Dictionary<string, string>
        {
            ["a/main.glsl"] = Lines("#include \"/b/c.glsl\"", "#include \"..\\b\\c.glsl\""),
            ["b/c.glsl"] = Lines("c")
        };
        Assert.Equal(Lines("c", "c"), Run(files, "a/main.glsl").Source);
    }

    [Fact]
    public void MissingIncludeReportsTheFileAndLine()
    {
        var files = new Dictionary<string, string> { ["main.glsl"] = Lines("a", "#include \"missing.glsl\"") };
        var e = Assert.Throws<ShaderProcessingException>(() => Run(files));
        Assert.Equal("main.glsl", e.File);
        Assert.Equal(2, e.Line);
        Assert.Contains("missing.glsl", e.Message);
    }

    [Fact]
    public void MissingEntryIsReported()
    {
        Assert.Throws<ShaderProcessingException>(() => Run(new Dictionary<string, string>(), "nope.glsl"));
    }

    [Fact]
    public void IncludesCannotLeaveTheRoot()
    {
        var files = new Dictionary<string, string> { ["main.glsl"] = "#include \"../secret.glsl\"\n", ["secret.glsl"] = "s\n" };
        Assert.Throws<ShaderProcessingException>(() => Run(files));
    }

    [Theory]
    [InlineData("#include")]
    [InlineData("#include foo.glsl")]
    [InlineData("#include \"foo.glsl")]
    [InlineData("#include \"\"")]
    public void MalformedIncludesAreRejected(string directive)
    {
        var files = new Dictionary<string, string> { ["main.glsl"] = directive + "\n", ["foo.glsl"] = "f\n" };
        Assert.Throws<ShaderProcessingException>(() => Run(files));
    }

    [Fact]
    public void FileIncludedTwiceAppearsTwice()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#include \"a.glsl\"", "#include \"a.glsl\""),
            ["a.glsl"] = Lines("a")
        };
        Assert.Equal(Lines("a", "a"), Run(files).Source);
    }

    [Fact]
    public void PragmaOnceIncludesAFileOnlyOnce()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#include \"a.glsl\"", "#include \"b.glsl\"", "#include \"a.glsl\""),
            ["a.glsl"] = Lines("#pragma once", "a"),
            ["b.glsl"] = Lines("#include \"a.glsl\"", "b")
        };
        Assert.Equal(Lines("a", "b"), Run(files).Source);
    }

    [Fact]
    public void IncludeGuardsWork()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#include \"a.glsl\"", "#include \"a.glsl\""),
            ["a.glsl"] = Lines("#ifndef A_GLSL", "#define A_GLSL", "a", "#endif")
        };
        Assert.Equal(Lines("#define A_GLSL", "a"), Run(files).Source);
    }

    [Fact]
    public void GuardedIncludeCyclesAreFine()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#include \"a.glsl\""),
            ["a.glsl"] = Lines("#ifndef A", "#define A", "#include \"b.glsl\"", "a", "#endif"),
            ["b.glsl"] = Lines("#ifndef B", "#define B", "#include \"a.glsl\"", "b", "#endif")
        };
        Assert.Equal(Lines("#define A", "#define B", "b", "a"), Run(files).Source);
    }

    [Fact]
    public void UnguardedIncludeCyclesAreRejected()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#include \"a.glsl\""),
            ["a.glsl"] = Lines("#include \"b.glsl\""),
            ["b.glsl"] = Lines("#include \"a.glsl\"")
        };
        var e = Assert.Throws<ShaderProcessingException>(() => Run(files));
        Assert.Contains("cycle", e.Message);
    }

    [Fact]
    public void ConditionalsKeepTheTakenBranch()
    {
        var source = Lines("#ifdef A", "a", "#else", "not a", "#endif", "#ifndef A", "no a", "#endif");
        var withA = new GlslPreprocessorOptions { Defines = new Dictionary<string, string?> { ["A"] = null } };

        Assert.Equal(Lines("#define A", "a"), Process(source, withA));
        Assert.Equal(Lines("not a", "no a"), Process(source));
    }

    [Fact]
    public void IfElifElseChoosesTheFirstTrueBranch()
    {
        var source = Lines("#if MODE == 1", "one", "#elif MODE == 2", "two", "#elif MODE == 3", "three", "#else", "other", "#endif");

        string For(string mode) => Process(source, new GlslPreprocessorOptions { Defines = new Dictionary<string, string?> { ["MODE"] = mode } })
            .Split('\n').Last(line => line.Length > 0);

        Assert.Equal("one", For("1"));
        Assert.Equal("two", For("2"));
        Assert.Equal("three", For("3"));
        Assert.Equal("other", For("4"));
    }

    [Fact]
    public void ElifIsNotEvaluatedAfterATakenBranch()
    {
        // The second condition would be an error if it were evaluated
        Assert.Equal(Lines("a"), Process(Lines("#if 1", "a", "#elif GL_UNKNOWN", "b", "#endif")));
    }

    [Theory]
    [InlineData("defined(A) && !defined(B)", true)]
    [InlineData("defined A && defined B", false)]
    [InlineData("A + 1 == 2", true)]
    [InlineData("(A << 2) == 4 && B == 0", true)]
    [InlineData("A ? 0 : 1", false)]
    [InlineData("0 && (1 / 0)", false)]
    [InlineData("1 || (1 / 0)", true)]
    [InlineData("UNDEFINED_MACRO", false)]
    [InlineData("0x10 == 16 && 010 == 8 && 5u == 5", true)]
    [InlineData("10 % 4 == 2 && 7 / 2 == 3 && -1 < 0 && ~0 == -1", true)]
    [InlineData("1 | 2 ^ 3 & 4", true)]
    [InlineData("LATER", true)]
    public void ConditionExpressions(string condition, bool expected)
    {
        var options = new GlslPreprocessorOptions
        {
            Defines = new Dictionary<string, string?> { ["A"] = "1", ["LATER"] = "A" }
        };
        var result = Process(Lines($"#if {condition}", "yes", "#else", "no", "#endif"), options);
        Assert.Equal(expected ? "yes" : "no", result.Split('\n').Last(line => line.Length > 0));
    }

    [Fact]
    public void ShaderDefinesAreVisibleToConditions()
    {
        var source = Lines("#define QUALITY 2", "#if QUALITY > 1", "high", "#endif", "#undef QUALITY", "#ifdef QUALITY", "still defined", "#endif");
        Assert.Equal(Lines("#define QUALITY 2", "high", "#undef QUALITY"), Process(source));
    }

    [Fact]
    public void DefinesInsideSkippedBranchesAreNotTracked()
    {
        var source = Lines("#if 0", "#define HIDDEN", "#endif", "#ifdef HIDDEN", "bad", "#endif");
        Assert.Equal("", Process(source));
    }

    [Fact]
    public void FunctionLikeMacrosCanBeCheckedWithDefinedButNotEvaluated()
    {
        var source = Lines("#define SQUARE(x) ((x) * (x))", "#ifdef SQUARE", "has square", "#endif");
        Assert.Equal(Lines("#define SQUARE(x) ((x) * (x))", "has square"), Process(source));

        Assert.Throws<ShaderProcessingException>(() => Process(Lines("#define F(x) x", "#if F(1)", "#endif")));
    }

    [Fact]
    public void IncludesInSkippedBranchesAreNotRead()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#ifdef USE_EXTRA", "#include \"does-not-exist.glsl\"", "#else", "#include \"base.glsl\"", "#endif"),
            ["base.glsl"] = Lines("base")
        };
        Assert.Equal(Lines("base"), Run(files).Source);
    }

    [Fact]
    public void NestedConditionalsInsideSkippedBranchesStaySkipped()
    {
        var source = Lines("#if 0", "#if 1", "inner", "#else", "inner else", "#endif", "#else", "outer else", "#endif");
        Assert.Equal(Lines("outer else"), Process(source));
    }

    [Fact]
    public void VersionBuiltinsAreAvailableToConditions()
    {
        var source = Lines("#ifdef GL_ES", "es", "#else", "desktop", "#endif", "#if __VERSION__ >= 400", "new", "#endif");

        Assert.Equal(Lines("#version 300 es", "es"), Process(source, new GlslPreprocessorOptions { Version = "300 es" }));
        Assert.Equal(Lines("#version 460 core", "desktop", "new"), Process(source, new GlslPreprocessorOptions { Version = "460 core" }));
    }

    [Theory]
    [InlineData("#if GL_ARB_gpu_shader5")]
    [InlineData("#if defined(GL_ARB_gpu_shader5)")]
    [InlineData("#if __LINE__ > 1")]
    public void MacrosOnlyTheCompilerKnowsAreRejectedInConditions(string condition)
    {
        Assert.Throws<ShaderProcessingException>(() => Process(condition + "\n#endif\n"));
    }

    [Theory]
    [InlineData("#if 1\nx\n")]
    [InlineData("#endif\n")]
    [InlineData("#else\n")]
    [InlineData("#elif 1\n")]
    [InlineData("#if 1\n#else\n#else\n#endif\n")]
    [InlineData("#if 1\n#else\n#elif 1\n#endif\n")]
    [InlineData("#if\n#endif\n")]
    [InlineData("#if 1 +\n#endif\n")]
    [InlineData("#if (1\n#endif\n")]
    [InlineData("#ifdef\n#endif\n")]
    public void BrokenConditionalsAreRejected(string source)
    {
        Assert.Throws<ShaderProcessingException>(() => Process(source));
    }

    [Fact]
    public void ConditionalsCannotSpanFiles()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#if 1", "#include \"a.glsl\""),
            ["a.glsl"] = Lines("#endif")
        };
        Assert.Throws<ShaderProcessingException>(() => Run(files));
    }

    [Fact]
    public void ErrorDirectiveFailsWhenReached()
    {
        var e = Assert.Throws<ShaderProcessingException>(() => Process(Lines("#error unsupported platform")));
        Assert.Contains("unsupported platform", e.Message);

        Assert.Equal("", Process(Lines("#if 0", "#error unsupported platform", "#endif")));
    }

    [Fact]
    public void DirectivesInCommentsAreIgnored()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines(
                "/* start",
                "#include \"missing.glsl\"",
                "#error not an error",
                "end */",
                "// #include \"missing.glsl\"",
                "#include \"a.glsl\" // trailing comment",
                "#include /* inline */ \"a.glsl\""),
            ["a.glsl"] = Lines("a")
        };
        var result = Run(files).Source;
        Assert.Equal(Lines("/* start", "#include \"missing.glsl\"", "#error not an error", "end */", "// #include \"missing.glsl\"", "a", "a"), result);
    }

    [Fact]
    public void BlockCommentOpenedInsideAConditionalSkipsDirectives()
    {
        var source = Lines("#if 1", "/*", "#endif", "*/", "#endif");
        Assert.Equal(Lines("/*", "#endif", "*/"), Process(source));
    }

    [Fact]
    public void ContinuedDirectivesAreCopiedWhole()
    {
        var source = Lines("#define LONG(a, b) \\", "    ((a) + \\", "    (b))", "x");
        Assert.Equal(source, Process(source));
    }

    [Fact]
    public void OtherDirectivesArePassedThrough()
    {
        var source = Lines("#extension GL_EXT_foo : enable", "#pragma optimize(off)", "#line 10", "#");
        Assert.Equal(source, Process(source));
    }

    [Fact]
    public void LineDirectivesFollowTheSourceFiles()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("one", "#include \"inc/a.glsl\"", "four", "#if 0", "skipped", "#endif", "seven"),
            ["inc/a.glsl"] = Lines("a one", "a two")
        };
        var result = Run(files, options: new GlslPreprocessorOptions { Version = "460 core", EmitLineDirectives = true });

        Assert.Equal(
            Lines("#version 460 core", "#line 1 1", "one", "#line 1 2", "a one", "a two", "#line 3 1", "four", "#line 7 1", "seven"),
            result.Source);
        Assert.Equal(["main.glsl", "inc/a.glsl"], result.SourceFiles);
    }

    [Fact]
    public void NoLineDirectivesByDefault()
    {
        var files = new Dictionary<string, string> { ["main.glsl"] = "#include \"a.glsl\"\n", ["a.glsl"] = "a\n" };
        Assert.DoesNotContain("#line", Run(files).Source);
    }

    [Fact]
    public void SourceFilesAreListedOnceInOrderOfUse()
    {
        var files = new Dictionary<string, string>
        {
            ["main.glsl"] = Lines("#include \"b.glsl\"", "#include \"a.glsl\"", "#include \"b.glsl\""),
            ["a.glsl"] = "a\n",
            ["b.glsl"] = "b\n"
        };
        Assert.Equal(["main.glsl", "b.glsl", "a.glsl"], Run(files).SourceFiles);
    }

    [Fact]
    public void FileSystemProviderReadsFilesAndStaysInsideItsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "glsl-preprocessor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "shaders"));
        try
        {
            File.WriteAllText(Path.Combine(root, "shaders", "a.glsl"), "a\n");
            File.WriteAllText(Path.Combine(root, "secret.glsl"), "secret\n");

            var provider = new FileSystemShaderSourceProvider(Path.Combine(root, "shaders"));
            Assert.Equal("a\n", provider.GetSource("a.glsl"));
            Assert.Null(provider.GetSource("missing.glsl"));
            Assert.Null(provider.GetSource("../secret.glsl"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
