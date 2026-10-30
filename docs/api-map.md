# RALSEI public API map

A map of everything the RALSEI engine exposes publicly: the `Ralsei*` projects. SWAG (`Swag*`) and `Pamx` are apps and libraries built *on* RALSEI and are not covered.

How to read it: a type is listed only if it is `public`. Members that are `internal` or `private` are left out even when the type around them is public. Signatures are abbreviated; the source is the authority. Paths are relative to the repository root.

- [1. Layers and projects](#1-layers-and-projects)
- [2. How an app is put together](#2-how-an-app-is-put-together)
- [3. The frame, thread by thread](#3-the-frame-thread-by-thread)
- [4. `Ralsei` core](#4-ralsei-core)
  - [4.1 Startup and resources](#41-startup-and-resources)
  - [4.2 Game loop and scenes](#42-game-loop-and-scenes)
  - [4.3 Input](#43-input)
  - [4.4 Audio](#44-audio)
  - [4.5 Canvas and 2D helpers](#45-canvas-and-2d-helpers)
  - [4.6 Text and fonts](#46-text-and-fonts)
  - [4.7 Rendering abstraction](#47-rendering-abstraction)
  - [4.8 Platform interfaces](#48-platform-interfaces)
  - [4.9 Data and utility types](#49-data-and-utility-types)
- [5. `Ralsei.Mathematics`](#5-ralseimathematics)
- [6. `Ralsei.Rendering.Common`](#6-ralseirenderingcommon)
- [7. The renderers](#7-the-renderers)
- [8. `Ralsei.Desktop`](#8-ralseidesktop)
- [9. `Ralsei.ShaderProcessor`](#9-ralseishaderprocessor)
- [10. Resource packages and redistributables](#10-resource-packages-and-redistributables)
- [11. Types from other projects that appear in the API](#11-types-from-other-projects-that-appear-in-the-api)
- [12. What a new host or renderer has to implement](#12-what-a-new-host-or-renderer-has-to-implement)
- [13. Things worth knowing](#13-things-worth-knowing)

---

## 1. Layers and projects

```
                      apps: Swag.*  (Desktop, Avalonia, Android, Wasm)
                                      │
   Ralsei.Desktop  (GLFW windows, BASS audio, pacing)       ← a host
        │                                                      (Android/Avalonia/Wasm are hosts too)
        ├── Ralsei.Rendering.OpenGL / .OpenGLES / .Vulkan   ← renderers
        │        └── Ralsei.Rendering.Common  (factory, shader loader, allocators)
        │                 └── Ralsei.ShaderProcessor  (GLSL preprocessor)
        └── Ralsei  (core: loop, scenes, input, audio interfaces, canvas, text,
                     rendering abstraction, platform interfaces)
                 ├── Ralsei.Mathematics
                 └── Tmpx  (font format, shaper)
```

| Project | Root namespace(s) | Role |
|---|---|---|
| `Ralsei` | `Ralsei`, `Ralsei.Core.*`, `Ralsei.Rendering.*`, `Ralsei.Platform.*`, `Ralsei.Data`, `Ralsei.Util` | The engine core. Depends on `Tmpx`, `Tmpx.Common`, `Ralsei.Mathematics`. |
| `Ralsei.Mathematics` | `Ralsei.Mathematics` | `Vector2i`, matrix builders, small math helpers. |
| `Ralsei.Rendering.Common` | `Ralsei.Rendering.Common` | What the renderers share, including the concrete `RenderingFactory`. |
| `Ralsei.Rendering.OpenGL` | `Ralsei.Rendering.OpenGL` | OpenGL 4.6 renderer (bindless textures when available). |
| `Ralsei.Rendering.OpenGLES` | `Ralsei.Rendering.OpenGLES` | OpenGL ES 3.0 renderer. |
| `Ralsei.Rendering.Vulkan` | `Ralsei.Rendering.Vulkan` | Vulkan 1.3 renderer. |
| `Ralsei.Desktop` | `Ralsei.Desktop` | Desktop host: GLFW windows, BASS audio, frame pacing, scripted input. |
| `Ralsei.ShaderProcessor` | `Ralsei.ShaderProcessor` | GLSL preprocessor, independent of the rest of RALSEI. |
| `Ralsei.ShaderProcessor.Cli` | (executable `shaderproc`) | Command line front end of the preprocessor. |
| `Ralsei.Resources.Compressed` / `.Raw` | `Ralsei.Resources.*` | The engine's fonts, compressed or raw. |
| `Ralsei.Redist` | (none) | Native BASS binaries, packed as a NuGet package. |

---

## 2. How an app is put together

An app is a `ServiceCollection`. The pieces register themselves with extension methods, in this order:

| Step | Call | Where | What it registers |
|---|---|---|---|
| 1 | `services.AddRalsei(Action<ResourceSourceFactories>? = null)` | `Ralsei` | `ResourceLoader`, `MeshService`, `FontService`, `TextShaper`, `SceneHost`, `InputManager`, `CanvasOptions` (a default one, via `TryAdd`), `Canvas` and `ICanvas`, `AtlasBuilder` (transient), `GameDirector`. Defaults (via `TryAdd`, so a host can override them): `NullAudioPlayer`, `NullTrackPlayer`, `MemoryClipboard`, `NullWindowControl`, `NullWindowManager`. Returns a `RalseiBuilder`. |
| 2 | `builder.UseSharedShaders()` | `Ralsei.Rendering.Common` | Makes the shared GLSL available to `ShaderLoader`. |
| 3 | `builder.UseOpenGLRenderer()` / `UseOpenGLESRenderer()` / `UseVulkanRenderer()` | each renderer | `RenderingFactory` (singleton) and `IRenderingFactory` (the same instance, by alias), `IFrameQueue` (a `FrameQueue`, scoped to the window), `BuiltinRenderingPrimitive` (internal) where the backend has one, `IRenderer` (scoped), and that backend's embedded shaders. |
| 4 | The host registers its surfaces | host | `IOpenGLSurface` or `IVulkanSurface`, plus whatever it overrides (`IAudioPlayer`, `ITrackPlayer`, `IClipboard`, `IWindowControl`, `IWindowManager`). |
| 5 | The app registers `IGameStartup` | app | Says which scene to begin with. |

`services.AddDesktop(RenderingBackend, out DesktopHost host)` in `Ralsei.Desktop` does 1, 3 and 4 for the desktop (and adds console logging). After building the provider, `host.Run(provider)` runs until the main window closes.

```csharp
var services = new ServiceCollection();
var ralsei = services.AddDesktop(RenderingBackend.Vulkan, out var host);
services.AddSingleton<IGameStartup, MyStartup>();
using var provider = services.BuildServiceProvider();
host.Run(provider);
```

`RalseiBuilder` (`Ralsei`): `Services`, `UseResourceSourceFactory(Func<IResourceSource>)`.
`ResourceSourceFactories : List<Func<IResourceSource>>`.

---

## 3. The frame, thread by thread

**Main thread**, once per rendered frame (`GameDirector.ProcessFrame(dt, targetSize)`):

0. If no frame buffer is free (`FreeFrameCount <= 0`) it returns `false` and does nothing: the render thread is behind. On the very first frame it asks the last-registered `IGameStartup` for the first scene.
1. The scene advances, once per frame: `IScene.Update(min(dt, GameDirector.MaxFrameTime) × TimeScale)` runs, then `SceneHost.ApplyPendingSwitch()` applies any scene change.
2. The scene's picture is built and handed over:
   - An `IFrameScene` builds the frame itself with `IFrameQueue.RequestFrameBuffer(...)` and `SubmitFrame(...)`, inside `Submit(frames, targetSize)`.
   - Any other scene is drawn into `ICanvas`, and `Canvas.Submit(...)` turns the recorded calls into a frame.

**Render thread** (one per window): loops on `IFrameQueue.WaitForWork(...)` and `FlushFrame(IRenderer)`. `FlushFrame` takes the next submitted frame and draws it with the renderer. The renderer first applies, to its own mirror of the resources, every change up to the version the frame was stamped with (`ResourceMirror.Apply`), so it makes the meshes, textures and texts in its own graphics context on its own thread.

Resources are created and destroyed from any thread and take effect at once: the handle is returned synchronously and can be put in a draw item right away. Every creation or destruction gets the next number (`IRenderingFactory.Version`), and `SubmitFrame` stamps the frame with the number at that moment. A renderer applies the changes numbered up to a frame's stamp, then draws it, so a destroy never reaches a frame that was submitted before it, and a frame never uses a resource that was made after it.

**Using a pass as a texture.** A mesh draw can paint with what an earlier pass of the same frame drew: set `MeshDrawItem.Texture` to `TextureHandle.FromPass(i)`. It is the output of pass `i` after that pass's post-processing, as a texture the size of the frame (not of its `Viewport`), with the first row at the top like any texture, so `MinUV`/`MaxUV` work as for an image. A draw can use any pass before its own, so an app composites however it likes: draw each stage as a full-screen quad of the one before it in the next pass, or draw them all in order and add a last pass that combines them. Only draws in `Normal` render mode use the texture. A draw whose pass index is not below the index of its own pass (so every draw in the first pass, and a single-pass frame) is an error: `IFrameQueue.SubmitFrame` throws `InvalidOperationException` on the main thread and drops the frame, and a renderer that is given such a frame anyway throws as well. The renderers copy the output of a pass only when a later pass uses it (an extra full-size copy for each such pass), and the passes are still layered into the frame as before, so a pass that is only there to be sampled can be left out of the layering with an `Opacity` of 0.

---

## 4. `Ralsei` core

### 4.1 Startup and resources

| Type | Members |
|---|---|
| `StartupExtension` (static) | `AddRalsei(...)` |
| `RalseiBuilder` | `Services`, `UseResourceSourceFactory(...)` |
| `ResourceSourceFactories` | `List<Func<IResourceSource>>` |
| `IResourceSource` (`Ralsei.Core`) | `Stream? OpenResource(name)`; default methods `ReadResourceBytes(name)`, `ReadResourceString(name)` |
| `EmbeddedResourceSource(Assembly)` | Reads embedded resources of an assembly |
| `ResourceLoader` | `OpenResource`, `ReadResourceBytes`, `ReadResourceString`. Asks each source in order. |
| `BuiltinMeshes` (static, `Ralsei.Core`) | Nested static classes, each with `Vertex[] Vertices` and `int[] Indices`: `SquareFilled`, `SquareOutline`, `SquareOutlineThin`, `CircleFilled`, `CircleOutline`, `CircleHalf`, `CircleHalfOutline`, `CircleOutlineThin`, `CircleQuarter`, `CircleQuarterOutline`, `CircleHalfQuarter`, `CircleHalfQuarterOutline`, `TriangleFilled`, `TriangleOutline`, `TriangleFilledBottomOrigin`, `TriangleOutlineBottomOrigin`, `TriangleRightFilled`, `TriangleRightOutline`, `Arrow`, `ArrowHead`, `HexagonFilled`, `HexagonOutline`, `HexagonOutlineThin`, `HexagonHalf`, `HexagonHalfOutline`, `HexagonHalfOutlineThin` |
| `MeshService` (`Ralsei.Core.Service`) | `MeshHandle SpriteQuadMesh`, `TryGetMeshForShape(int shape, int shapeOption, out MeshHandle)`, `Dispose()`. Ctor: `(IRenderingFactory, ILogger<MeshService>)`. |

### 4.2 Game loop and scenes

Namespace `Ralsei.Core.Game` (and `GameDirector` in `Ralsei.Core`).

| Type | Role |
|---|---|
| `IScene : IDisposable` | `Update(float deltaSeconds)` once per rendered frame; `Draw(ICanvas)` records this frame's draw calls. |
| `IFrameScene : IScene` | `Submit(IFrameQueue, Vector2i targetSize)`: builds and submits the frame itself (passes, cameras, post-processing). Its `Draw` does nothing. |
| `IGameStartup` | `IScene CreateInitialScene()`. Registered by the app; if several are registered, the last one is used. |
| `SceneHost : IDisposable` | `Current`, `Switch(IScene)` (applied after the scene's `Update`), `ApplyPendingSwitch()`, `Dispose()`. |
| `GameDirector` (`Ralsei.Core`) | Ctor `(IFrameQueue, MeshService, SceneHost, Canvas, IEnumerable<IGameStartup>)`. `const MaxFrameTime = 0.25`, `TimeScale` (1 by default; also set by `RALSEI_TIMESCALE`), `bool ProcessFrame(float dt, Vector2i targetSize)`. |
| `GameTask` | The game's own awaitable task for `async GameTask` methods on a single thread. `CompletedTask`, `IsCompleted`, `IsCanceled`, `IsFaulted`, `GetAwaiter()`, `Cancel()`. Also public because async state machines need them: `GameTaskAwaiter`, `GameTaskMethodBuilder`. |
| `Wait` (readonly struct) | What a script awaits: `Wait.Tick`, `Wait.Ticks(int)`, `Wait.Until(Func<bool>)`. A wait always lets at least one tick pass. Also `WaitAwaiter`. |
| `CoroutineRunner` | `GameTask Start(Func<GameTask> script)`, `Update()` (once per tick), `CancelAll()`. Scripts run in the order they were started. |

### 4.3 Input

Namespaces `Ralsei.Core.Input` and `Ralsei.Platform.Input`.

| Type | Members |
|---|---|
| `InputManager` | The entry point a host feeds; it hands each report to every receiver at once, on the reporting thread. `CreateReceiver(InputReceiverType)`, then `KeyDown/KeyUp/KeyRepeat(Keycode)`, `MouseMove(x, y)` (the event carries the `Delta` from the move before), `MouseDown/MouseUp(MouseButton)`, `Scroll(x, y)`, `TextInput(string)`, `FilesDropped(string[])`. |
| `InputReceiver : IDisposable` | Event-driven. `event Action<InputEvent> InputReceived` (every event, as it happens), `Type`, `MousePosition` and `Modifiers` (now), `IsDown(Keycode)` and `IsDown(MouseButton)`, `Dispose()`. |
| `InputReceiverType` | How `IsDown` answers. `Mailbox`: the state now, nothing kept (a tap over before the question is missed). `Fifo`: the changes of each key or button are queued (up to 64), and each `IsDown` takes the oldest one and says what it made of the key (a tap reads down, then up); with none left it says the state now. |
| `InputEvent` (record struct) | `(InputEventKind Kind, KeyModifiers Modifiers = None, Keycode Key, MouseButton Button, Vector2 Position, Vector2 Delta, Vector2 Scroll, string? Text, string[]? Paths)` plus `Shift`, `Control`, `Alt`. |
| `InputEventKind` | `KeyDown`, `KeyUp`, `KeyRepeat`, `MouseMove`, `MouseDown`, `MouseUp`, `Scroll`, `Text`, `FilesDropped` |
| `KeyModifiers` (flags) | `None`, `Shift`, `Control`, `Alt`, `Super` |
| `Keycode` (`Ralsei.Platform.Input`) | Numbered like GLFW key codes. `Unknown = -1`, `Space`, `Apostrophe`…`Slash`, `D0`–`D9`, `A`–`Z`, punctuation, `Escape`, `Enter`, `Tab`, `Backspace`, `Insert`, `Delete`, arrows, `PageUp/PageDown`, `Home`, `End`, `CapsLock`, `ScrollLock`, `NumLock`, `PrintScreen`, `Pause`, `F1`–`F25`, `KeyPad0`–`KeyPad9` and keypad operators, left/right `Shift/Control/Alt/Super`, `Menu`, `Count = 349`. |
| `MouseButton` | `Left`, `Right`, `Middle`, `Extra1`, `Extra2` |

### 4.4 Audio

Namespace `Ralsei.Core.Audio`. Two separate services:

| Type | Role |
|---|---|
| `IAudioPlayer` | A game's own sounds and looping music, by id. `PlaySound(id, volume = 1, pitch = 1)`, `PlayMusic(id, volume = 1)`, `StopMusic()`, `SetMusicVolume(v)`, `SuspendMusic()` (set the track aside), `ResumeMusic()`. |
| `NullAudioPlayer` | Silent default. |
| `IAudioLibrary` | Supplies encoded audio to an `IAudioPlayer`: `byte[]? GetSound(id)`, `byte[]? GetMusic(id)`. The game implements it. |
| `ITrackPlayer : IDisposable` | One timed track played from a file or decoded audio: the clock a rhythm player runs from. `IsLoaded`, `Load(string path)`, `Load(PcmAudio)`, `Position` (seconds; setting seeks), `Length`, `IsPlaying`, `Speed` (1 normal, 0 holds, negative plays backwards, which works only for audio loaded from `PcmAudio`), `Volume`, `Play()`, `Pause()`, `Stop()`. |
| `NullTrackPlayer` | Only the clock: no sound. |
| `PcmAudio` (record) | `(short[] Samples, int Channels, int SampleRate)`, `FrameCount`, `Duration`, `Reversed()`. |

Implementations: `BassAudioPlayer` and `BassTrackPlayer` in `Ralsei.Desktop`.

### 4.5 Canvas and 2D helpers

Namespace `Ralsei.Core.Graphics`.

| Type | Role |
|---|---|
| `ICanvas` | Immediate-mode 2D drawing in a space of `Width` × `Height` units (the window's size in pixels unless `CanvasOptions.FixedSize` says otherwise), origin top-left, Y down. `Offset` (added to everything after; used for screen shake), `DrawSprite(Sprite, frame, x, y, scaleX, scaleY, rotation, alpha, blend?, fog?)`, `DrawRegion(AtlasRegion, x, y, w, h, color, fog?)`, `FillRect`, `StrokeRect`, `DrawText(BitmapFont, text, x, y, color, scale)` and a per-axis scale overload. |
| `Canvas : ICanvas` | The recording implementation. Ctor `(CanvasOptions)`. Adds `Begin(Vector2i windowSize)` (sets `Width`/`Height`) and `Submit(IFrameQueue, MeshHandle quad, Vector2i windowSize)`, which builds the recorded calls into a frame, with bars on top when the size is fixed. |
| `CanvasOptions` | `Vector2i? FixedSize` (null by default: the canvas is the size of the window; set, the whole of it is kept in view and scaled to fit, with bars), `ColorRgba ClearColor` (opaque black by default; alpha 0 for a transparent window). Register one to change them. |
| `VirtualViewport` (static) | `CameraScale(screenW, screenH, windowW, windowH)`, `Bars(...)`: fits a fixed-size screen into any window shape. |
| `Sprite` | Named, possibly animated image with origin and collision `SpriteBounds`. `Name`, `Width`, `Height`, `OriginX`, `OriginY`, `Frames`, `Bounds`, `FrameCount`, `GetFrame(i)` (wraps). |
| `SpriteBounds` (record struct) | `(Left, Right, Top, Bottom)`, `Width`, `Height`. |
| `TextureAtlas` | A GPU texture page. `Width`, `Height`, `TextureHandle Texture`, `TryGetTexture(out TextureHandle)`. |
| `AtlasRegion` (record struct) | `(TextureAtlas Atlas, X, Y, Width, Height)`, `MinUV`, `MaxUV`, `Sub(x, y, w, h)`. |
| `AtlasBuilder(IRenderingFactory, pageSize = 2048)` | Shelf-packs RGBA images into pages. `AtlasEntry Add(w, h, rgba, extrudeEdges = false)`, `IReadOnlyList<TextureAtlas> Build()`. |
| `AtlasEntry` | `Width`, `Height`, `Region` (valid after `Build`). |
| `BitmapFont` / `BitmapGlyph` | Fixed-size raster font in one atlas: `Name`, `LineHeight`, `TryGetGlyph(char, out BitmapGlyph)`, `MeasureWidth(text, scale)`, `MeasureHeight(text, scale)`. |

### 4.6 Text and fonts

Namespaces `Ralsei.Core.Text` and `Ralsei.Core.Service`. Text is shaped on the CPU (by `Tmpx`) and drawn from glyph outlines on the GPU.

| Type | Members |
|---|---|
| `FontService : IDisposable, IFontResolver` | Ctor `(ResourceLoader, IRenderingFactory)`. `Shaper`, `AddFont(path, FontStyle)` (a `.tmpx` file from a resource source; uploads glyphs again), `AddFallbackChain(name, families)`, `GetFallbackNames(family)`, `GetFontChain(family, style)`, `GetGlobalShapeEntryBase(IFont)`, `Dispose()`. |
| `TextShaper(FontService)` | `ShapedRichText ShapeText(text, defaultFontName, horizontalAlignment, verticalAlignment)` and an overload that fills a given `ShapedRichText`. |
| `TextShaperExtension` | `ShapeText(this TextShaper, string text, Vector2 origin)` |
| `ShapedRichText` | `List<ShapedTextGlyph> Glyphs` |
| `ShapedTextGlyph` (struct) | `Transform`, `Color`, `ShapeEntryIndex` (index into the renderer's global shape buffer; -1 is a mark rectangle). |
| `FontInfo` | `Name`, `Style`, `Font`, `GlobalShapeEntryBase`. |

### 4.7 Rendering abstraction

Namespace `Ralsei.Rendering` (and `.Data`, `.Handle`). This is what a renderer implements and an app drives.

**Entry points**

| Type | Members |
|---|---|
| `IRenderingFactory` | The GPU resources of the app, and nothing else: safe to call from any thread, takes effect at once, never blocks on a render thread, and one instance serves every window. `long Version`. `CreateMesh(vertices, indices)` → `MeshHandle`, `DestroyMesh`, `CreateTexture(w, h, rgba8, TextureSampling)` → `TextureHandle`, `DestroyTexture`, `SetFontBuffers(curves, curveIndices, bandEntries, shapeEntries)`, `CreateText(ShapedRichText)` → `TextHandle`, `DestroyText`. |
| `IFrameQueue` | The frames of one window, between the main thread and its render thread. `FreeFrameCount`, `QueuedFrameCount`, `WaitForFreeFrame(ms)`, `WaitForWork(ms)`, `RequestFrameBuffer(ReadOnlySpan<PassSize>)`, `SubmitFrame(FrameBuffer)` (stamps it with `IRenderingFactory.Version`), `FlushFrame(IRenderer)` (render thread; draws the next frame, false if none). |
| `FrameQueueExtensions` | `RequestFrameBuffer(drawCommandCount, meshCount, textCount)` for a one-pass frame. |
| `IRenderer` | `ProcessFrame(IDrawDataProvider)`. Implemented by the three backends and `RecordingRenderer`. |

**Frame data**

| Type | Members |
|---|---|
| `FrameBuffer : IDrawDataProvider, IResettable` | A pooled frame. `Passes` (ReadOnlySpan<PassBuffer>), `FinalPostProcessingState` (post-processing of the layered result; null for none), `Resize(ReadOnlySpan<PassSize>)`, `Reset()`. |
| `PassSize` (record struct) | `(int DrawCommandCount, int MeshDrawItemCount, int TextDrawItemCount)` |
| `PassBuffer : IResettable` | One pass: everything drawn to one target. `ViewMatrix` (the app works it out; the renderer only multiplies by it), `PostProcessingState`, `ClearColor` (its alpha says how much shows through), `Opacity` (0–1, fades the pass as one), `Viewport` (a `ClipRect` of the target to draw in; default is all of it), spans `DrawCommands`, `MeshDrawItems`, `TextDrawItems`, plus `Resize(PassSize)` and `Reset()`. |
| `IDrawDataProvider` | What a renderer reads: `PassCount`, `FinalPostProcessingState`, `DrawData CreateDrawData(int passIndex)`. |
| `DrawData` (ref struct) | `ViewMatrix`, `PostProcessingState`, `ClearColor`, `Opacity`, `Viewport`, spans of `DrawCommand`, `MeshDrawItem`, `TextDrawItem`. |
| `DrawCommand` (struct) | `DrawType` (`Mesh` or `Text`), `DrawId` (index in the pass's items of that type), `Clip` (`ClipRect`; default is no cut). Commands are drawn in order, subject to the opaque/transparent split. |
| `MeshDrawItem` | `MeshHandle`, `Transform`, `Color1`, `Color2`, `RenderMode`, `GradientRotation`, `GradientScale`, `Texture` (a `TextureHandle`: a texture of the factory, or `TextureHandle.FromPass(i)` for what the earlier pass `i` of the frame drew; see section 3), `MinUV`, `MaxUV`, `Fog`. |
| `TextDrawItem` | `TextHandle`, `Transform`, `Color`. |
| `Buffer<T>` / `IReadOnlyBuffer<T>` | Growable unmanaged buffer: `Data`, `DataAsBytes`, `Length`, `LengthInBytes`, `EnsureSize`, `Append`, `Clear`, `AsWritableSpan`. |
| `RenderUtil` (static) | `GetRenderSize(size, aspectRatio?, out renderSize)`, `GetViewMatrix(in CameraState, Vector2i size)` (the camera's scale is half the height of what it sees), `ShouldUseTransparentDrawList(...)`., `ValidatePassTextures(IDrawDataProvider, Span<bool> used = default)` (throws `InvalidOperationException` if a mesh draw uses the output of a pass that is not before its own; marks the passes whose output is used). |

**`Ralsei.Rendering.Data`**

| Type | Members |
|---|---|
| `CameraState` | `Position`, `Scale`, `Rotation` |
| `ClipRect` | A pixel rectangle, origin top-left; `default` is no clip. `ClipRect(x, y, w, h)`, `Enabled`, `X`, `Y`, `Width`, `Height`, `Right`, `Bottom`, `Intersect(ClipRect)`, equality. |
| `Vertex` | `Position`, `UV` and constructors from `Vector2` or floats. |
| `RenderMode` | `Normal`, `RightToLeftGradient`, `LeftToRightGradient`, `OutwardsGradient`, `InwardsGradient`, `Ring` (colour 1 is the colour; the RGB of colour 2 are inner radius, outer radius, and fade). |
| `TextureSampling` (record struct) | `(MinificationFilter, MagnificationFilter, WrapModeU, WrapModeV, BorderColor = default)`; presets `Nearest`, `Linear`, `CrispEdges`; `IsCrisp`. |
| `TextureFilteringMode` | `Nearest`, `Linear`, `CrispEdges` (pixel art blended only across one screen pixel at texel edges; needs padded neighbours) |
| `TextureAddressMode` | `ClampToEdge`, `ClampToBorder`, `MirroredRepeat`, `Repeat` |
| `PostProcessingState` | `Time`, `HueShift`, `LegacyBloom`, `UniversalBloom`, `LensDistortion`, `ChromaticAberration`, `Vignette`, `Gradient`, `Glitch`, `Grain`, `DepthOfField`, and `HasFinalEffects`. Its effect structs (all in this file): `HueShiftEffectState`, `BloomEffectState`, `LensDistortionEffectState`, `ChromaticAberrationEffectState`, `VignetteEffectState`, `GradientEffectState`, `GlitchEffectState`, `GrainEffectState`, `DepthOfFieldEffectState`. |
| `VignetteMode` | `UseRounded`, `UseRoundness` |
| `GradientOverlayMode` | `Linear`, `Additive`, `Multiply`, `Screen` |

**`Ralsei.Rendering.Handle`**: `IRenderingResourceHandle` (`Id`), `MeshHandle`, `TextHandle`, `TextureHandle` (also `None`, `IsNone`; `default` is `None`; `FromPass(int)`, `IsPass`, `PassIndex` for the output of a pass of the frame; the `Id` of a pass is a negative number no texture has).

### 4.8 Platform interfaces

What a host provides so the engine can run on it. Namespace `Ralsei.Platform.*`.

| Type | Role |
|---|---|
| `IOpenGLSurface` | Where the frames go: `Framebuffer` (0 for the default one of a window), `FramebufferSize`, `RenderSize`, `IsContextLost`, `TryMakeCurrent()` (asked for when the renderer is made and before every frame), `SwapBuffers()` (does nothing by default). The renderer itself blits the frame into `Framebuffer` (in the middle of it, with nothing around it, if the frame is smaller) and then calls `SwapBuffers()`. |
| `OpenGLSettings` | `required` `MajorVersion`, `MinorVersion`, `IsES`. |
| `IVulkanSurface` | `RenderSize`, `FramebufferSize`, `VSync`, `LowLatency`, **`Transparent`** (show the alpha of the picture, pre-multiplied), `FramePresented()`, `IsHeadless`, `GetRequiredInstanceExtensions()`, `ulong CreateSurface(ulong instance)`. Handles are raw integers so this project needs no Vulkan bindings. |
| `IFrameReceiver` (`Ralsei.Platform`) | Implemented by a surface of any of the three renderers that wants the pixels of every frame: `ReceiveFrame(ReadOnlySpan<byte> rgba, Vector2i size)`, on the render thread before the frame is presented. Tightly packed RGBA8 (gamma encoded, premultiplied), top row first, valid until the next frame starts. For video capture and screenshots; a surface that does not implement it costs nothing. |
| `IWindowManager` | `IsSupported`, `IWindowHandle? Open(WindowRequest)` (null if the host cannot). |
| `NullWindowManager` | Supports nothing; `Open` returns null. |
| `WindowRequest` (record) | `(Title, Width, Height, Func<IServiceProvider, IScene> CreateScene)` with init-only options: `Decorated` (true), `Floating`, **`Transparent`** (needs `Decorated = false`; the scene clears to a colour that has alpha), `Visible`, `Resizable`, `TakeFocus`, `Position`. |
| `IWindowHandle` | `IsOpen`, `Closed` event, `Title`, `Control` (an `IWindowControl`), `Close()`. |
| `IWindowControl` | What a scene may ask of its window: `Title`, `ContentScale`, `RequestClose()`, `ForceClose()`, `CloseGuard` (return true to keep it open), `SetCursor(CursorShape)`, `Resize(w, h)`, `Position`, `CursorScreenPosition`, `ClientSize`, `PixelRatio`, `Visible`, `Focused`, `Focus()`, `WorkAreaAt(x, y)`. |
| `NullWindowControl` | Remembers the title, cursor, requested size and close request: `CloseRequested`, `Cursor`, `RequestedSize`. |
| `CursorShape` | `Arrow`, `IBeam`, `Crosshair`, `PointingHand`, `ResizeHorizontal`, `ResizeVertical`, `Move`, `ResizeNorthWestSouthEast`, `ResizeNorthEastSouthWest`, `NotAllowed` |
| `IClipboard` / `MemoryClipboard` | `GetText()`, `SetText(string)`; the memory one lives in the process only. |

### 4.9 Data and utility types

| Namespace | Type | Role |
|---|---|---|
| `Ralsei.Core.Data` | `ColorRgb` | Float RGB struct: byte and float constructors, `*` scalar, `Lerp`, equality, `ToVector()`, `Pack()`, `Unpack(uint)`. |
| | `ColorRgba` | Float RGBA struct with the same shape; default is opaque black. |
| | `BloomEffectState`, `GradientEffectState`, `VignetteEffectState`, `DepthOfFieldEffectState` | Effect values *with* `Lerp`, for animating. See [13](#13-things-worth-knowing). |
| `Ralsei.Data` | `SparseSet<T>` | Id-indexed set: `Count`, indexer, `Insert`, `Remove`, `TryGet`, `Contains`, enumerates `KeyValuePair<int, T>`. |
| | `ObservableSparseSet<T>` | Adds `ItemInserted` and `ItemRemoved` events, which carry an `ObservableSparseSetEventArgs<T>` (`Id`, `Item`). |
| | `Holder<T>` (`T : struct`) | `Value`, `Map(Action<T>)`, `GetValue()`. |
| `Ralsei.Util` | `IResettable`, `ConcurrentMemoryPool<T>` | Pool used for frame buffers: `TryRent`, `Return`, `Capacity`, `FreeCount`, `RentedCount`. |
| | `NumberUtil` | `PseudoRng` delegate, `CreatePseudoRng(seed)`, `Mix`, `SplitMix64`, `UlongToFloat01`, `FloatToOrderedUInt`, `HalfToOrderedUShort`, `ComputeHash` overloads. |
| | `CollectionExtension`, `CollectionUtil`, `EnumerableUtil` | `GetOrInsert`, `BinarySearchKey`, `EnsureCount`, `AdaptType`, `Indexed`. |

---

## 5. `Ralsei.Mathematics`

| Type | Members |
|---|---|
| `Vector2i` (struct) | `X`, `Y`, `Zero`, `One`, `+ - * /` and unary `-`, equality. |
| `FastMatrix` (static) | `Matrix3x2` builders: `GetTranslationMatrix`, `GetScaleMatrix`, `GetRotationMatrix`, and the combined `GetTranslationScale…`, `GetTranslationRotation…`, `GetRotationScale…`, `GetTranslationRotationScaleMatrix`. |
| `MathUtil` (static) | `DivideCeil`, `Lerp` (Vector2/3/4), `InverseLerp`, `DegreesToRadians`, `RadiansToDegrees`, `MapRange`. |

---

## 6. `Ralsei.Rendering.Common`

For renderer authors; apps normally only call `UseSharedShaders()`.

| Type | Role |
|---|---|
| `RalseiBuilderExtension.UseSharedShaders(this RalseiBuilder)` | Makes the shared GLSL available. |
| `RenderingFactory(ILogger<RenderingFactory>) : IRenderingFactory` | The concrete factory every backend uses: thread-safe CPU-side tables of the meshes, textures, texts and font buffers behind a lock. `CreateMirror()` makes the `ResourceMirror` of a renderer. |
| `ResourceMirror : IDisposable` | What one renderer sees of the resources. `Meshes`, `Textures`, `Texts` (`ResourceEvents<T>` with `ItemInserted`/`ItemRemoved`), `FontCurves`, `FontCurveIndices`, `FontBandEntries`, `FontShapeEntries`, event `FontBuffersUpdated`, and `Apply(long version)`, called on the render thread before each frame. A new mirror starts with everything that exists (version 0). |
| `FrameQueue(IRenderingFactory) : IFrameQueue` | The per-window frame queue; the pool of frame buffers and the stamping. |
| `FontBuffers` | The four font arrays, replaced as a whole. |
| `ShaderLoader` | `ForOpenGL(ResourceLoader, defines)`, `ForOpenGLES(...)`, `Load(name)` → `GlslPreprocessorResult`, `DescribeSourceFiles(result)`. |
| `RecordingRenderer : IRenderer` | Draws nothing and records frames, for tests and tools. `Frames`, `Last`. With `RecordedFrame(FinalPostProcessingState, Passes)` and `RecordedPass(ViewMatrix, PostProcessingState, ClearColor, Opacity, DrawCommands, MeshDrawItems, TextDrawItems, Viewport)`. |
| `TexturePager`, `TexturePage` | For backends without bindless textures: divides a pass's draw commands into pages that share a few textures. `Plan(...)`, `Pages`, `SlotOf(commandIndex)`, `TexturesOf(page)`, `TextureOf(...)`. |
| `DrawCommandWithDepth` | `DrawType`, `DrawId`, `Depth`, `Clip`. |
| `Mesh`, `Texture`, `Text`, `RenderGlyph` | The CPU-side data of a mesh, texture, text and glyph. |
| `BufferSuballocator`, `PooledSuballocator`, `Allocation`, `GrowBufferCallback` | GPU buffer sub-allocation. |
| `ColorUtil` (static) | `SrgbToLinear` / `LinearToSrgb` for floats, `ColorRgb` and `ColorRgba`. |

---

## 7. The renderers

Each backend is registered by one call (the static class `RalseiOptionsBuilderExtension` of its project) and then used only through `IRenderer`.

| Backend | Register with | `IRenderer` class | Needs from the host |
|---|---|---|---|
| OpenGL | `UseOpenGLRenderer()` | `Renderer(RenderingFactory, BuiltinRenderingPrimitive, IOpenGLSurface, ResourceLoader, ILogger<Renderer>)` | `IOpenGLSurface` |
| OpenGL ES | `UseOpenGLESRenderer()` | `Renderer(RenderingFactory, IOpenGLSurface, ResourceLoader, ILogger<Renderer>)` | `IOpenGLSurface` |
| Vulkan | `UseVulkanRenderer()` | `Renderer(RenderingFactory, BuiltinRenderingPrimitive, IVulkanSurface, ResourceLoader, ILogger<Renderer>)` | `IVulkanSurface` |

All three renderer classes are `internal`: an app gets one as `IRenderer` (`ProcessFrame(IDrawDataProvider)`, `Dispose()`), which is safe to dispose more than once.

Everything else in the three backend projects is `internal`: the `Renderer` classes, `BuiltinRenderingPrimitive`, the `Gpu*` structs, the indirect-command structs, `LoaderUtil` and the post-processing passes. What is public is each project's `RalseiOptionsBuilderExtension` and, in Vulkan, `VulkanException(string, VkResult?)` with `Result`.

The post-processing steps a frame can ask for are the fields of `PostProcessingState` (bloom, depth of field, grain, glitch, hue shift, lens distortion, chromatic aberration, vignette, gradient overlay).

---

## 8. `Ralsei.Desktop`

The host that apps run on. A `DesktopHost` runs every window on one thread, and each window is an engine of its own: its own renderer on its own render thread, with its own pictures, fonts and scenes, made from the same services as the first.

| Type | Members |
|---|---|
| `DesktopExtension` (static) | `TryRead(Arguments, out RenderingBackend, out string? error)` reads `--renderer gl\|gles\|vulkan` (Vulkan by default) and `--vsync on\|off\|adaptive`. `AddDesktop(this IServiceCollection, RenderingBackend, out DesktopHost)`. |
| `RenderingBackend` | `OpenGL`, `OpenGLES`, `Vulkan` |
| `DesktopHost(IServiceCollection) : IWindowManager` | `IsSupported`, `Run(IServiceProvider)` (until the main window closes), `Open(WindowRequest)` (opens an extra window). |
| `DesktopWindow : IDisposable` | One GLFW window: its input (fed to an `InputManager`), clipboard, cursors and size. Ctor `(InputManager, OpenGLSettings? = null, bool primary = true, string title = "RALSEI", int width = 0, int height = 0, WindowRequest? request = null)`. Public: `Clipboard`, `WindowControl`, `OpenGLSurface`, `VulkanSurface`, `FramebufferSize`, `ShouldClose`, `IsVulkan`, `RefreshRate`, `Pacing`, `BeginFrameStamp()`, `CompleteReveal()`, `PlayScript()`, static `PumpEvents()`, `Dispose()`. |
| `Arguments(IEnumerable<string>)` | `Has(name)`, `Value(name)`: `--name value` (the last one counts) and bare switches. |
| `BassAudioPlayer` | `IAudioPlayer` over BASS; ctor `(IAudioLibrary, ILogger<BassAudioPlayer>)`. Sounds are decoded once and reused. |
| `BassTrackPlayer` | `ITrackPlayer` over BASS; ctor `(ILogger<BassTrackPlayer>)`. A file is streamed; `PcmAudio` is held in memory so it can play backwards. |
| `BassDevice` (static) | `Init()`: starts BASS once, set for little delay. |
| `DesktopSettings` (static) | `SwapInterval` (1, 0 or -1), `LowLatency`, `VulkanRate`, `Parse(string?)`. |
| `FramePacing` | Builds each frame as late as it can be before the display needs it. `FrameStarted`, `PrepareTicks`, static `Enabled`, `Presented`, `Built`, `WaitForTurn()`, `WaitForRate(perSecond)`, static `RaiseTimerResolution()`. |
| `DesktopStats` (static) | Timings printed once a second with `RALSEI_STATS=1`: `Enabled`, `RecordMain/Wait/Render/Latency`, `Report()`. |
| `ScriptedInput(string path)` | Plays input from a file, one line per frame (`move`, `down`, `up`, `click`, `scroll`, `key`, `hold`, `release`, `text`). `Play(frame, key, move, button, scroll, text)`. |

Environment variables read by the host and renderers are in the README.

---

## 9. `Ralsei.ShaderProcessor`

A GLSL preprocessor that is not tied to RALSEI (no dependency on the engine).

| Type | Members |
|---|---|
| `GlslPreprocessor(IShaderSourceProvider)` | `GlslPreprocessorResult Process(string entryPath, GlslPreprocessorOptions? = null)`. Resolves `#include "file"` / `#include <file>`, evaluates conditionals, defines host macros, sets the `#version`. |
| `GlslPreprocessorOptions` | `Version` ("460 core", "300 es"), `Defines`, `IncludeDirectories`, `EmitLineDirectives`, `MaxIncludeDepth`. |
| `GlslPreprocessorResult` | `Source`, `SourceFiles` (index + 1 is the `#line` source-string number). |
| `IShaderSourceProvider` | `string? GetSource(string path)`. |
| `DelegateShaderSourceProvider`, `DictionaryShaderSourceProvider`, `FileSystemShaderSourceProvider` | Ready-made providers: from a function, from memory, from a directory (paths cannot escape it). |
| `ShaderProcessingException` | `File`, `Line`, ctor `(message, file?, line)`. |

**`shaderproc`** (`Ralsei.ShaderProcessor.Cli`): `shaderproc <shader> --root <dir> [--version <v>] [-D NAME[=VALUE]]… [-I <dir>]… [--line-directives] [-o <file>]`. Used by `compile-shaders.ps1/.sh` in the Vulkan project to make the checked-in SPIR-V.

---

## 10. Resource packages and redistributables

| Project | Public API |
|---|---|
| `Ralsei.Resources.Compressed` | `Extension.AddCompressedResources(this ResourceSourceFactories)`: the engine's fonts, compressed. |
| `Ralsei.Resources.Raw` | `Extension.AddRawResources(this ResourceSourceFactories)`: the same, not compressed. |
| `Ralsei.Redist` | No code. Native BASS libraries (`bass.dll`, `libbass.so`, `libbass.dylib`) for Windows, Linux and macOS, packed as the `Ralsei.Redist` NuGet package into `packages/`. Android's `libbass.so` files live in `Swag.Android`. |

---

## 11. Types from other projects that appear in the API

These come from `Tmpx` and show up in `Ralsei` signatures, so a caller needs them:

| From `Tmpx` | Used by |
|---|---|
| `FontStyle`, `TextHorizontalAlignment`, `TextVerticalAlignment` | `FontService.AddFont`, `TextShaper.ShapeText` |
| `Shaper`, `IFont`, `IFontResolver`, `Font` | `FontService` |
| `QuadraticCurve`, `BandEntry`, `ShapeEntry` (`Tmpx.Common`) | `IRenderingFactory.SetFontBuffers`, `ResourceMirror.Font*` |

Also: `Microsoft.Extensions.DependencyInjection` (`IServiceCollection`, `IServiceProvider`), `Microsoft.Extensions.Logging` (`ILogger<T>`), and `System.Numerics` (`Vector2`, `Matrix3x2`) are part of the signatures throughout.

---

## 12. What a new host or renderer has to implement

**A host** (like `Ralsei.Desktop`, or an Android or browser host):

1. Register `IOpenGLSurface` or `IVulkanSurface` for the chosen renderer.
2. Feed `InputManager` with what the user does.
3. Run a render thread that loops `IFrameQueue.WaitForWork` / `FlushFrame(renderer)`. One `RenderingFactory` can be shared by every window; each window has its own `IFrameQueue` and renderer.
4. Call `GameDirector.ProcessFrame(dt, targetSize)` once per frame on the main thread.
5. Optionally replace the defaults: `IAudioPlayer`, `ITrackPlayer`, `IClipboard`, `IWindowControl`, `IWindowManager`. Leaving them out gives the null versions, so nothing happens.

**A renderer**:

1. Implement `IRenderer.ProcessFrame(IDrawDataProvider)`. Draw the passes in order, layer them using each pass's `Opacity` and `Viewport`, and run each pass's `PostProcessingState` and then `FinalPostProcessingState`.
2. Make a `ResourceMirror` with `RenderingFactory.CreateMirror()`, create and destroy GPU resources from its `Meshes`, `Textures` and `Texts` events and its `Font*` buffers on the render thread, and call `Apply(frame.ResourceVersion)` before drawing each frame. Dispose it with the renderer.
3. Provide a `Use…Renderer` extension that registers `RenderingFactory` (and `IRenderingFactory` as an alias of it), `IFrameQueue`, `IRenderer` and its shaders.
