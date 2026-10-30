# RALSEI

> **RALSEI** (Radical Animation Layering & Simulation Engine Infrastructure) is a lightweight, fast 2D engine for .NET, with three
> rendering backends, a game loop with scenes, input, audio and text, with a desktop host to run it all on.

## Features

- **Rendering** with OpenGL, OpenGL ES and Vulkan behind one interface. Frames are built on the main thread and drawn on a render
  thread of their own. The renderers take a view matrix (how the world is seen is up to the app) and draw meshes and text,
  with clip rectangles, sub-rectangle passes, per-pass opacity and post-processing (bloom, depth of field, grain, glitch, vignette and more).
- **Game loop and scenes**: fixed-rate or real-time scenes, scene switching, and coroutines (`GameTask`) for things that happen over time.
- **Input** through `InputManager`: the host tells it what the user does, and each `InputReceiver` raises it as an event the moment it happens
  (no polling; mouse moves carry their delta). `IsDown` answers with the state now (`Mailbox`, for a game) or takes the oldest unread change
  (`Fifo`, so that a tap is seen, for a user interface).
- **Audio**: `IAudioPlayer` for sounds and `ITrackPlayer` for a song that can be seeked and played backwards (BASS on the desktop).
- **Text** rasterized on the GPU from the glyph outlines with Eric Lengyel's [Slug algorithm](https://terathon.com/blog/decade-slug.html), so it stays sharp at any size, shaped with rich text support.
- **Graphics helpers**: a canvas for sprites, atlases and bitmap fonts.
- **Shader preprocessor** (`Ralsei.ShaderProcessor`): the same GLSL for every backend.
- **Platform interfaces** (`IOpenGLSurface`, `IVulkanSurface`, `IWindowControl`, `IWindowManager`, `IClipboard`), so that a host for
  another platform only has to implement them.
- **Desktop host** (`Ralsei.Desktop`): GLFW windows (including extra and transparent ones for menus and tool windows), BASS audio, frame
  pacing, scripted input and frame capture for testing.

## Projects

| Project | What it holds |
|---------|---------------|
| `Ralsei` | The core: game loop, scenes, input, audio interfaces, graphics helpers, text shaping, resource loading, the rendering abstractions and the platform interfaces. |
| `Ralsei.Mathematics` | Vectors, matrices and the like. |
| `Ralsei.Rendering.Common` | What the renderers share: the rendering factory, text and texture handling, and the GLSL. |
| `Ralsei.Rendering.OpenGL`, `.OpenGLES`, `.Vulkan` | The renderers. |
| `Ralsei.ShaderProcessor` (`.Cli`, `.Tests`) | The GLSL preprocessor, and a command line tool that runs it. |
| `Ralsei.Desktop` | The desktop host that apps are made of. |
| `Ralsei.Resources.Compressed`, `.Raw` | The engine's resources (fonts), compressed or not. |
| `Ralsei.Redist` | The native libraries (BASS), as a NuGet package. |
| `Tmpx` | TextMeshPro Extended: the font format, generator and text layout behind text, rendered with Slug. |

## Making an app

An app registers the engine and a desktop to run it on, adds what it is made of, and runs it:

```csharp
var services = new ServiceCollection();

// The windows, sound, clipboard and renderer (here, Vulkan)
var ralsei = services.AddDesktop(RenderingBackend.Vulkan, out var host);

// Say which scene to begin with
services.AddSingleton<IGameStartup, MyStartup>();

using var serviceProvider = services.BuildServiceProvider();
host.Run(serviceProvider);

sealed class MyStartup : IGameStartup
{
    public IScene CreateInitialScene() => new MyScene();
}

sealed class MyScene : IScene
{
    public void Update(float deltaSeconds) { }   // once per frame
    public void Draw(ICanvas canvas) { }   // records what to draw
}
```

A scene that wants fixed steps accumulates the time it is given, and a scene that builds the whole frame itself is an `IFrameScene`. The
canvas is the size of the window unless a `CanvasOptions` with a `FixedSize` is registered. Scenes can open more windows through `IWindowManager`, and each of them is
an engine of its own.

`DesktopExtension.TryRead` reads what every desktop app takes on its command line: `--renderer gl|gles|vulkan` (the default is Vulkan)
and `--vsync on|off|adaptive`.

## Building

### Dependencies

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Getting it

```sh
git clone https://github.com/Reimnop/RALSEI.git --recursive
```

The `Ralsei.Redist` project packs the native audio libraries. It is consumed as a NuGet package, so pack it once into the local feed that
`nuget.config` points at:

```sh
dotnet pack Ralsei.Redist -c Release -o packages
```

Then build everything with `dotnet build Ralsei.slnx` (the Android app needs the Android workload), and run SWAG:

```sh
dotnet run --project Swag.Desktop -- run -b level.vgd -a audio.ogg
```

### Environment variables

The desktop host and the renderers read these, which is mostly of use when testing:

| Variable | What it does |
|----------|--------------|
| `RALSEI_VSYNC` | `on`, `off` or `adaptive`, as `--vsync` |
| `RALSEI_LOW_LATENCY` | `0` stops the host waiting for the driver after each frame (a little less work, a longer delay from a key to its picture) |
| `RALSEI_VULKAN_RATE` | With Vulkan, how many frames are made for each refresh of the display (3 by default) |
| `RALSEI_PACING` | `0` turns off frame pacing |
| `RALSEI_RENDER_AHEAD` | How many frames the main thread may be ahead of the render thread (2 by default) |
| `RALSEI_STATS` | `1` prints frame timings |
| `RALSEI_TIMESCALE` | Runs the game at a multiple of real time |
| `RALSEI_WINDOW_SIZE` | The size of the first window, like `1280x720` |
| `RALSEI_CONTENT_SCALE` | Forces the scale the user interface is drawn at |
| `RALSEI_CAPTURE_PATH` | Saves a frame as a PNG there and closes the app. `RALSEI_CAPTURE_FRAME` (120 by default) says which frame, and `RALSEI_CAPTURE_FRAME_WINDOW` which one of the other windows (their pictures are saved as `name.window.png`) |
| `RALSEI_INPUT_SCRIPT` | A file of input to play, a line for a frame: `10 move 400 300`, `12 click left`, `30 key LeftControl+Z`, `40 hold D`, `50 release D`, `60 scroll 0 -1`, `70 text abc` |
| `RALSEI_NO_BINDLESS` | Turns off bindless textures in the OpenGL renderer |
| `RALSEI_TEXTURE_PAGE_SIZE` | The size of a texture page |
| `RALSEI_VULKAN_VALIDATION` | `1` turns on the Khronos validation layers (they come with the [Vulkan SDK](https://vulkan.lunarg.com/)), and what they find is written to the log |
| `RALSEI_VULKAN_DEVICE` | Picks the GPU by index or a part of its name (e.g. `amd`). A discrete GPU is preferred, and the GPUs that were found are listed in the log |

## Rendering

### Vulkan

The Vulkan renderer needs a GPU and driver that support Vulkan 1.3 (including dynamic rendering and synchronization2). It works with a window
as well as when rendering to a video, where it does not need a window at all.

The Vulkan shaders are the shared GLSL (see [Shaders](#shaders)), compiled to SPIR-V that is checked in under `Resources/Shaders` of the
Vulkan project. After changing a shader, run `compile-shaders.ps1` (or `compile-shaders.sh`) in that project to compile it again. This needs
`glslangValidator`, which comes with the Vulkan SDK.

### Shaders

The OpenGL, OpenGL ES and Vulkan renderers share their GLSL, which lives in `Ralsei.Rendering.Common/Resources/Shaders`.
The shaders have no `#version`. They `#include` what they share (`Common/`) and adapt to the graphics API with `RALSEI_OPENGL`, `RALSEI_GLES`
or `RALSEI_VULKAN`, which are defined when they are compiled (see `Common/Prelude.glsl` for the macros that hide the differences, e.g.
`RALSEI_PARAM` is a push constant in Vulkan and a uniform elsewhere). `Common/PostProcessing.glsl` explains how a post-processing pass is
written once and becomes a compute shader (OpenGL, Vulkan) or a fragment shader (OpenGL ES).

This is done by `Ralsei.ShaderProcessor`, a GLSL preprocessor that is not tied to RALSEI. On top of what a GLSL compiler does it adds:

- `#include "file"` (relative to the including file) and `#include <file>` (searched in the include directories), with `#pragma once` and include guards.
- Macros defined by the host instead of the shader.
- A `#version` chosen by the host instead of the shader.

It also evaluates `#if`/`#ifdef`/`#elif`/`#else`/`#endif`, so that an `#include` that is not taken is not read. Everything else is left to the
compiler. The OpenGL and OpenGL ES backends run the shaders through it when they start, with `ShaderLoader`, and `Ralsei.ShaderProcessor.Cli`
(`shaderproc`) does the same on the command line, which is what `compile-shaders` uses for Vulkan.

## SWAG

> **SWAG** (SWArm Graphics) is a lightweight, fast animation player for keyframed 2D scenes, built on RALSEI.

### Running it

`Swag.Desktop` has two commands, `run` (a window with real-time rendering) and `render` (a video file, through FFmpeg):

```sh
dotnet run --project Swag.Desktop -- run -b level.vgd -a audio.ogg
dotnet run --project Swag.Desktop -- run -b level.vgd -a audio.ogg --backend vulkan
```

or, when running the executable directly:

```sh
Swag.Desktop.exe run -b level.vgd -a audio.ogg
```

| Argument | Default | Description |
|----------|---------|-------------|
| `-b`, `--beatmap <beatmap>` | **required** | Path to the animation file (.lsb or .vgd) |
| `-a`, `--audio <audio>` | **required** | Path to the audio file |
| `-w`, `--width <width>`, `-h`, `--height <height>` | 1366 x 768 | The size of the window |
| `--backend <OpenGL\|OpenGLES\|Vulkan>` | OpenGL | The rendering backend |
| `--vsync` | True | Enable VSync (`run` only) |
| `--lock-aspect` | True | Lock the aspect ratio to 16:9 (`run` only) |
| `--seed <seed>` | random | Seed for the random number generator |
| `--post-processing` | True | Enable post-processing effects |
| `--text-rendering` | True | Enable text rendering |
| `--use-egl` | False | Use EGL for context creation |
| `--version` | | Show version information |
| `-?`, `-h`, `--help` | | Show help and usage information |

Run `Swag.Desktop render --help` for the options of the video exporter.

### GPU compatibility

Certain GPUs, such as Intel integrated graphics, are not currently supported by the OpenGL backend.

- If your machine has multiple GPUs, and one of them is not Intel, make sure the app is configured to use the non-Intel GPU. (On Windows,
  add the executable in the system Graphics settings and set it to use "high performance".)
- If your machine only uses Intel GPU(s), run with `--backend opengles` to use OpenGL ES. **This mode is significantly slower, but should work on most systems.**
- `--backend vulkan` uses the Vulkan renderer (see [Vulkan](#vulkan)).

### Android app

`Swag.Android` is a phone app (SWAG Mobile) that plays levels with touch controls.
Open the folder of a level (the app finds the animation file, song, cover and metadata in it), or pick the animation file and song by hand.
The levels you play are kept in a recent list.

The home screen has the options: the renderer (OpenGL ES, or Vulkan on devices that have Vulkan 1.3), the resolution to render at, a frame
rate cap, VSync, post-processing, text, looping and a fixed random seed. While a level plays, touch the screen for the controls, double-tap
the left or right side to skip 10 seconds, and open the sliders button for the speed, volume and effects. If Vulkan does not start on a
device, the app offers to go back to OpenGL ES.

To try a change without a device, use an emulator from Android Studio (one with a recent system image has OpenGL ES and Vulkan 1.3).
Debug builds can start a level straight from `adb`, see `ApplyDebugOverrides` in `PlayerActivity.cs` for the extras that it takes.

## Troubleshooting

### Build errors after pulling

When updating your local repository, run `git pull --recurse-submodules` to ensure all submodules are updated when pulling changes.

If you encounter build errors after running git pull, run `git submodule update` to ensure submodules are updated.

## Contributing

Contributions are welcome! Please feel free to submit issues, fork the repository, and send pull requests.

## License

This project is licensed under the GPLv3 License - see the [LICENSE](LICENSE) file for details.

### Fork rules

Forks and contributions are welcome, but please note that the following rules apply:
- Forks must be open source and licensed under the GPLv3 License.
- Forks must not contain copyrighted code or content from third parties.
- Forks must not be made for the purpose of creating a competing product or service to any proprietary software or service.
- Forks must contain this notice and the original authorship information.

## Acknowledgements

- [OpenTK](https://opentk.net/)
- [Slug](https://terathon.com/blog/decade-slug.html)
- [BASS](http://www.un4seen.com/)
- Fonts provided by [Google Fonts](https://fonts.google.com/), [DaFont](https://www.dafont.com/), [Fontsource](https://fontsource.org/), and [Code2000](https://www.code2001.com/code2000_page.htm)
