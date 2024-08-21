#!/usr/bin/env bash
# Compiles the GLSL shared by all the rendering backends to SPIR-V in Resources/Shaders/ (embedded into the assembly).
#
# The GLSL lives in ../Ralsei.Rendering.Common/Resources/Shaders. It has no #version and adapts to
# the graphics API through RALSEI_* macros, so it is first run through the shader preprocessor (shaderproc) with
# the Vulkan version and RALSEI_VULKAN, which is what ShaderLoader does for the other backends at run time.
#
# Requires glslangValidator (https://github.com/KhronosGroup/glslang), which ships with the Vulkan SDK.
# Set GLSLANG to point at a specific binary. The compiled .spv files are checked in, so this only
# needs to be re-run after editing a shader.
set -euo pipefail

cd "$(dirname "$0")"

GLSLANG="${GLSLANG:-glslangValidator}"
SHADERPROC_PROJECT=../Ralsei.ShaderProcessor.Cli
SRC=../Ralsei.Rendering.Common/Resources/Shaders
OUT=Resources/Shaders

if ! command -v "$GLSLANG" >/dev/null 2>&1; then
    echo "error: '$GLSLANG' not found. Install the Vulkan SDK or set GLSLANG to the glslangValidator path." >&2
    exit 1
fi

dotnet build "$SHADERPROC_PROJECT" -c Release -nologo -v q
SHADERPROC="$SHADERPROC_PROJECT/bin/Release/net10.0/shaderproc.dll"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# compile <shader, relative to $SRC and without .glsl> <stage: vert|frag|comp> <name of the module, as ShaderUtil loads it> [defines to add...]
compile() {
    local shader="$1" stage="$2" name="$3"
    shift 3
    local defines=()
    for define in "$@"; do defines+=(-D "$define"); done
    local out="$OUT/$name.spv"
    # glslangValidator takes the stage from the extension
    local preprocessed="$TMP/${shader//\//_}.$stage"

    mkdir -p "$(dirname "$out")"
    echo "$shader.glsl -> $out"
    dotnet "$SHADERPROC" "$shader.glsl" --root "$SRC" --version "460 core" -D RALSEI_VULKAN "${defines[@]}" -o "$preprocessed"
    "$GLSLANG" -V --target-env vulkan1.3 -o "$out" "$preprocessed"
}

# The textures are read either bindless (one array of all of them, see Texturing.glsl) or, on a device that cannot, a page of 16 at a time
compile Uber.vert vert Uber.vert RALSEI_BINDLESS
compile Uber.frag frag Uber.frag RALSEI_BINDLESS
compile Uber.frag frag Uber.paged.frag RALSEI_TEXTURE_PAGE_SIZE=16

# Every post-processing pass is a compute shader
while read -r file; do
    shader="${file%.glsl}"
    compile "$shader" comp "$shader.comp"
done < <(cd "$SRC" && find PostProcessing -type f -name '*.glsl' | sort)
