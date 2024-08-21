# Compiles the GLSL shared by all the rendering backends to SPIR-V in Resources/Shaders/ (embedded into the assembly).
#
# The GLSL lives in ../Ralsei.Rendering.Common/Resources/Shaders. It has no #version and adapts to
# the graphics API through RALSEI_* macros, so it is first run through the shader preprocessor (shaderproc) with
# the Vulkan version and RALSEI_VULKAN, which is what ShaderLoader does for the other backends at run time.
#
# Requires glslangValidator (https://github.com/KhronosGroup/glslang), which ships with the Vulkan SDK.
# Pass -Glslang to point at a specific binary. The compiled .spv files are checked in, so this only
# needs to be re-run after editing a shader.
param(
    [string]$Glslang = "glslangValidator"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command $Glslang -ErrorAction SilentlyContinue)) {
    Write-Error "'$Glslang' not found. Install the Vulkan SDK or pass -Glslang with the glslangValidator path."
    exit 1
}

$shaderProcProject = Join-Path $PSScriptRoot "../Ralsei.ShaderProcessor.Cli"
$src = Join-Path $PSScriptRoot "../Ralsei.Rendering.Common/Resources/Shaders"
$out = Join-Path $PSScriptRoot "Resources/Shaders"

dotnet build $shaderProcProject -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$shaderProc = Join-Path $shaderProcProject "bin/Release/net10.0/shaderproc.dll"

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

# $shader is relative to the source directory and has no .glsl, $stage is vert, frag or comp,
# $name is the name of the module as ShaderUtil loads it
function Compile-Shader([string]$shader, [string]$stage, [string]$name, [string[]]$defines = @()) {
    $dest = Join-Path $out "$name.spv"
    # glslangValidator takes the stage from the extension
    $preprocessed = Join-Path $tmp ("$($shader -replace '[\\/]', '_').$stage")

    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Write-Host "$shader.glsl -> $dest"
    $defineArgs = @()
    foreach ($define in $defines) { $defineArgs += @("-D", $define) }
    dotnet $shaderProc "$shader.glsl" --root $src --version "460 core" -D RALSEI_VULKAN @defineArgs -o $preprocessed
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $Glslang -V --target-env vulkan1.3 -o $dest $preprocessed
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

try {
    # The textures are read either bindless (one array of all of them, see Texturing.glsl) or, on a device that cannot, a page of 16 at a time
    Compile-Shader "Uber.vert" "vert" "Uber.vert" @("RALSEI_BINDLESS")
    Compile-Shader "Uber.frag" "frag" "Uber.frag" @("RALSEI_BINDLESS")
    Compile-Shader "Uber.frag" "frag" "Uber.paged.frag" @("RALSEI_TEXTURE_PAGE_SIZE=16")

    # Every post-processing pass is a compute shader
    $postProcessing = Join-Path $src "PostProcessing"
    Get-ChildItem -Path $postProcessing -Recurse -File -Filter "*.glsl" | Sort-Object FullName | ForEach-Object {
        $shader = [System.IO.Path]::GetRelativePath($src, $_.FullName) -replace '\\', '/'
        $shader = $shader.Substring(0, $shader.Length - ".glsl".Length)
        Compile-Shader $shader "comp" "$shader.comp"
    }
}
finally {
    Remove-Item -Recurse -Force $tmp
}
