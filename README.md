# RALSEI

> **RALSEI** (Radical Animation Layering & Simulation Engine Interface) is a lightweight, fast animation player
> for keyframed 2D scenes.

## Features

- Plays keyframed scenes: objects, parenting, themes, prefabs and timed events
- Renders text objects
- Particle emitters
- Post-processing: bloom, depth of field, grain, glitch, vignette and more
- Multi-threaded animation evaluation
- Desktop, Android and WebAssembly front ends
- Video export through FFmpeg

## Usage

### Dependencies

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Building

1. Clone the repository:
    ```sh
    git clone https://github.com/Reimnop/RALSEI.git --recursive
    ```
   
2. Navigate to the cloned folder and run the project:
    ```sh
    dotnet run --project Ralsei.Desktop
    ```

This will run RALSEI and also build the RALSEI executable file in `Ralsei.Desktop\bin`.

The `Ralsei.Redist` project packs the native audio libraries. It is consumed as a NuGet package; see [Native libraries](#native-libraries).

### Arguments

RALSEI takes the following command line arguments:

| Argument                             | Default      | Description                                                          |
|--------------------------------------|--------------|----------------------------------------------------------------------|
| -b, --beatmap <beatmap>              | **required** | Path to the animation file (.lsb or .vgd)                            |
| -a, --audio <audio>                  | **required** | Path to the audio file                                               |
| --vsync                              | True         | Enable VSync.                                                        |
| --workers <workers>                  | 4            | Number of worker threads, set to -1 to use all available threads     |
| --seed <seed>                        | -1           | Seed for the random number generator, set to -1 to use a random seed |
| --speed <speed>                      | 1            | Sets the playback speed                                              |
| --backend <OpenGL\|OpenGLES\|Vulkan> | OpenGL       | Sets the rendering backend to use                                    |
| --post-processing                    | true         | Enable post-processing effects                                       |
| --text-rendering                     | true         | Enable text rendering                                                |
| --version                            |              | Show version information                                             |
| -?, -h, --help                       |              | Show help and usage information                                      |

Example usage:
 ```sh
 dotnet run --project Ralsei.Desktop -- -b level.vgd -a audio.ogg
 ```

Or, when running the executable directly:
```sh
 Ralsei.Desktop.exe -b level.vgd -a audio.ogg
 ```

### Native libraries

`Ralsei.Redist` holds the platform-specific native binaries (BASS audio) and is referenced as the `Ralsei.Redist` NuGet package.
Pack it once into the local feed that `nuget.config` points at:

```sh
dotnet pack Ralsei.Redist -c Release -o packages
```

## Troubleshooting

### Build errors after pulling

When updating your local repository, run `git pull --recurse-submodules` to ensure all submodules are updated when pulling changes.

If you encounter build errors after running git pull, run `git submodule update` to ensure submodules are updated.

### GPU compatibility

Certain GPUs, such as Intel integrated graphics, are not currently supported by the OpenGL backend.

### If your machine has multiple GPUs, and one of them is not Intel

Ensure RALSEI is configured to use the non-Intel GPU.
(For Windows, this is done by adding the RALSEI executable in the system Graphics settings and setting it to use "high performance".)

### If your machine only uses Intel GPU(s)

You can try running RALSEI with the OpenGL ES backend with `--backend opengles` to force RALSEI to use OpenGL ES.
**This mode is significantly slower, but should work on most systems.**

### Vulkan backend

Run RALSEI with `--backend vulkan` to use the Vulkan renderer. It needs a GPU and driver that support Vulkan 1.3 (including dynamic rendering and synchronization2).
It works with a window as well as when rendering to a video, where it does not need a window at all.

Set the `RALSEI_VULKAN_VALIDATION` environment variable to `1` to turn on the Khronos validation layers (these come with the [Vulkan SDK](https://vulkan.lunarg.com/)).
The problems they find are written to the log.

By default a discrete GPU is preferred. To use another one, set `RALSEI_VULKAN_DEVICE` to the index or a part of the name of the GPU (e.g. `amd`). The GPUs that were found are listed in the log.

The Vulkan shaders are the shared GLSL (see [Shaders](#shaders)), compiled to SPIR-V that is checked in under `Resources/Shaders` of the Vulkan project.
After changing a shader, run `compile-shaders.ps1` (or `compile-shaders.sh`) in that project to compile it again. This needs `glslangValidator`, which comes with the Vulkan SDK.

### Shaders

The OpenGL, OpenGL ES and Vulkan renderers share their GLSL, which lives in `Ralsei.Rendering.Common/Resources/Shaders`.
The shaders have no `#version`. They `#include` what they share (`Common/`) and adapt to the graphics API with `RALSEI_OPENGL`, `RALSEI_GLES` or `RALSEI_VULKAN`, which are defined when they are compiled (see `Common/Prelude.glsl` for the macros that hide the differences, e.g. `RALSEI_PARAM` is a push constant in Vulkan and a uniform elsewhere).
`Common/PostProcessing.glsl` explains how a post-processing pass is written once and becomes a compute shader (OpenGL, Vulkan) or a fragment shader (OpenGL ES).

This is done by `Ralsei.ShaderProcessor`, a GLSL preprocessor that is not tied to RALSEI. On top of what a GLSL compiler does it adds:

- `#include "file"` (relative to the including file) and `#include <file>` (searched in the include directories), with `#pragma once` and include guards.
- Macros defined by the host instead of the shader.
- A `#version` chosen by the host instead of the shader.

It also evaluates `#if`/`#ifdef`/`#elif`/`#else`/`#endif`, so that an `#include` that is not taken is not read. Everything else is left to the compiler.
The OpenGL and OpenGL ES backends run the shaders through it when they start, with `ShaderLoader`, and `Ralsei.ShaderProcessor.Cli` (`shaderproc`) does the same on the command line, which is what `compile-shaders` uses for Vulkan.

### Android app

`Ralsei.Android` is a phone app (RALSEI Mobile) that plays levels with touch controls.
Open the folder of a level (the app finds the animation file, song, cover and metadata in it), or pick the animation file and song by hand. The levels you play are kept in a recent list.

The home screen has the options: the renderer (OpenGL ES, or Vulkan on devices that have Vulkan 1.3), the resolution to render at, a frame rate cap, VSync, post-processing, text, looping and a fixed random seed.
While a level plays, touch the screen for the controls, double-tap the left or right side to skip 10 seconds, and open the sliders button for the speed, volume and effects.
If Vulkan does not start on a device, the app offers to go back to OpenGL ES.

To try a change without a device, use an emulator from Android Studio (one with a recent system image has OpenGL ES and Vulkan 1.3).
Debug builds can start a level straight from `adb`, see `ApplyDebugOverrides` in `PlayerActivity.cs` for the extras that it takes.

## Contributing

Contributions are welcome! Please feel free to submit issues, fork the repository, and send pull requests.

## License

This project is licensed under the GPLv3 License - see the [LICENSE](LICENSE) file for details.

## Acknowledgements

- [OpenTK](https://opentk.net/)
- [msdfgen](https://github.com/Chlumsky/msdfgen)
- [BASS](http://www.un4seen.com/)
- Fonts provided by [Google Fonts](https://fonts.google.com/), [DaFont](https://www.dafont.com/), [Fontsource](https://fontsource.org/), and [Code2000](https://www.code2001.com/code2000_page.htm)
