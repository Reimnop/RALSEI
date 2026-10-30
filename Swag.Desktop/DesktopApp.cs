using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Ralsei.Core;
using Ralsei.Core.Service;
using Ralsei.Mathematics;
using Ralsei.Rendering;
using Ralsei.Util;

namespace Swag.Desktop;

public sealed class DesktopApp(IServiceProvider serviceProvider)
{
    private enum ButtonAction
    {
        None,
        Forward10,
        Backward10,
        Forward5,
        Backward5,
        PlayPause,
        RandomizeSeed
    }
    
    private struct FullscreenData
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }
    
    private volatile bool appRunning = true;
    
    private ButtonAction buttonAction;
    private FullscreenData? fullscreenData;

    // The window belongs to the render thread, which says how big its picture is (the director draws for that)
    private volatile int renderWidth = 1920;
    private volatile int renderHeight = 1080;
    
    public void StartApp(string beatmapPath, string audioPath, ulong? randomSeed, bool enablePostProcessing, bool enableTextRendering)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        
        var beatmapService = sp.GetRequiredService<BeatmapService>();
        beatmapService.LoadBeatmapFromPath(beatmapPath);
        
        var frames = sp.GetRequiredService<IFrameQueue>();
        var appDirector = sp.GetRequiredService<AppDirector>();
        appDirector.EnablePostProcessing = enablePostProcessing;
        appDirector.EnableTextRendering = enableTextRendering;
        
        var rss = sp.GetRequiredService<RandomSeedService>();
        rss.Seed = randomSeed ?? NumberUtil.SplitMix64((ulong)DateTimeOffset.Now.ToUnixTimeSeconds());
        
        using var audioPlayer = AudioPlayer.Load(audioPath);
        audioPlayer.Play();
        
        var renderThread = new Thread(StartRenderThread);
        renderThread.Start();
        
        while (appRunning)
        {
            if (buttonAction != ButtonAction.None)
            {
                switch (buttonAction)
                {
                    case ButtonAction.Forward10:
                        audioPlayer.Position += 10.0f;
                        break;
                    case ButtonAction.Backward10:
                        audioPlayer.Position -= 10.0f;
                        break;
                    case ButtonAction.Forward5:
                        audioPlayer.Position += 5.0f;
                        break;
                    case ButtonAction.Backward5:
                        audioPlayer.Position -= 5.0f;
                        break;
                    case ButtonAction.PlayPause:
                        if (audioPlayer.Playing)                            
                            audioPlayer.Pause();
                        else                            
                            audioPlayer.Play();
                        break;
                    case ButtonAction.RandomizeSeed:
                        rss.Seed = NumberUtil.SplitMix64((ulong)DateTimeOffset.Now.ToUnixTimeSeconds());
                        break;
                }

                buttonAction = ButtonAction.None;
            }
            
            appDirector.SubmitFrame((float) audioPlayer.Position, new Vector2i(renderWidth, renderHeight));
            
            while (frames.FreeFrameCount == 0)
                Thread.Yield();
        }
        
        audioPlayer.Stop();
        
        renderThread.Join();
    }

    private void StartRenderThread()
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        
        var frames = serviceProvider.GetRequiredService<IFrameQueue>();
        
        var renderer = sp.GetRequiredService<IRenderer>();
        var surface = sp.GetRequiredService<DesktopWindow>();
        
        GCHandle keyCallbackHandle;
        
        unsafe
        {
            GLFWCallbacks.KeyCallback keyCallback = OnKey;
            keyCallbackHandle = GCHandle.Alloc(keyCallback);
            
            var windowPtr = surface.WindowPtr;
            GLFW.SetKeyCallback(windowPtr, keyCallback);
        }
        
        var size = surface.RenderSize;
        renderWidth = size.X;
        renderHeight = size.Y;
        while (frames.QueuedFrameCount > 0 || !surface.ShouldClose)
        {
            if (surface.ShouldClose)
                appRunning = false;
            
            surface.PollEvents();
            frames.FlushFrame(renderer);
        }
        
        keyCallbackHandle.Free();
    }
    
    private unsafe void OnKey(Window* window, Keys key, int scanCode, InputAction action, KeyModifiers mods)
    {
        if (action == InputAction.Press)
        {
            switch (key)
            {
                case Keys.Escape:
                    GLFW.SetWindowShouldClose(window, true);
                    break;
                case Keys.F11:
                {
                    if (fullscreenData == null)
                    {
                        var monitor = GLFW.GetPrimaryMonitor();
                        var mode = GLFW.GetVideoMode(monitor);
                        GLFW.GetWindowPos(window, out var x, out var y);
                        GLFW.GetWindowSize(window, out var width, out var height);
                        fullscreenData = new FullscreenData
                        {
                            X = x,
                            Y = y,
                            Width = width,
                            Height = height
                        };
                        GLFW.SetWindowMonitor(window, monitor, 0, 0, mode->Width, mode->Height, mode->RefreshRate);
                    }
                    else
                    {
                        GLFW.SetWindowMonitor(
                            window, 
                            null, 
                            fullscreenData.Value.X, 
                            fullscreenData.Value.Y,
                            fullscreenData.Value.Width, 
                            fullscreenData.Value.Height, 
                            0);
                        fullscreenData = null;
                    }
                    break;
                }
                case Keys.J:
                    buttonAction = ButtonAction.Backward10;
                    break;
                case Keys.L:
                    buttonAction = ButtonAction.Forward10;
                    break;
                case Keys.Left:
                    buttonAction = ButtonAction.Backward5;
                    break;
                case Keys.Right:
                    buttonAction = ButtonAction.Forward5;
                    break;
                case Keys.Space:
                    buttonAction = ButtonAction.PlayPause;
                    break;
                case Keys.R:
                    buttonAction = ButtonAction.RandomizeSeed;
                    break;
            }
        }
    }
}