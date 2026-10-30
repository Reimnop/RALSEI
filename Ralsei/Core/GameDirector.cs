using Ralsei.Core.Game;
using Ralsei.Core.Graphics;
using Ralsei.Core.Input;
using Ralsei.Core.Service;
using Ralsei.Mathematics;
using Ralsei.Rendering;

namespace Ralsei.Core;

/// <summary>Drives a game one frame at a time: advances the current scene by the time that passed, then builds its picture and hands it to the render thread.</summary>
public class GameDirector(
    IFrameQueue frames,
    MeshService meshService,
    SceneHost sceneHost,
    Canvas canvas,
    IEnumerable<IGameStartup> startups)
{
    /// <summary>A frame that took longer than this many seconds (a stall, a breakpoint) is played as if it took this long.</summary>
    public const float MaxFrameTime = 0.25f;

    private bool started;

    /// <summary>Scales how fast game time passes (1 normal, 0.1 slow motion). Set for a whole run with the <c>RALSEI_TIMESCALE</c> environment variable.</summary>
    public float TimeScale { get; set; } = float.TryParse(
        Environment.GetEnvironmentVariable("RALSEI_TIMESCALE"), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var scale) && scale > 0f ? scale : 1f;

    /// <summary>Advances the scene by <paramref name="dt"/> seconds and submits a frame for a target of <paramref name="targetSize"/> pixels. Returns false, doing nothing, if the render thread is so far behind that no frame is free (<see cref="IFrameQueue.WaitForFreeFrame"/> waits for one).</summary>
    public bool ProcessFrame(float dt, Vector2i targetSize)
    {
        if (frames.FreeFrameCount <= 0)
            return false;

        if (!started)
        {
            started = true;
            var startup = startups.LastOrDefault();
            if (startup is not null)
                sceneHost.Switch(startup.CreateInitialScene());
            sceneHost.ApplyPendingSwitch();
        }

        // Advanced by the time that really passed, once per frame (input is not polled: scenes hear it as it arrives)
        sceneHost.Current?.Update(Math.Min(dt, MaxFrameTime) * TimeScale);
        sceneHost.ApplyPendingSwitch();

        if (sceneHost.Current is IFrameScene building)
        {
            building.Submit(frames, targetSize);
            return true;
        }

        canvas.Begin(targetSize);
        sceneHost.Current?.Draw(canvas);

        canvas.Submit(frames, meshService.SpriteQuadMesh, targetSize);

        return true;
    }
}
