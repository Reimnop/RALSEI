using System.Numerics;
using System.Runtime.InteropServices;
using Pamx.Objects;
using Ralsei.Core.Data;
using Swag.Model;
using Ralsei.Core.Service;
using Ralsei.Rendering;
using Ralsei.Mathematics;
using Ralsei.Rendering.Data;
using Ralsei.Rendering.Handle;
using BloomEffectState = Ralsei.Rendering.Data.BloomEffectState;
using GradientEffectState = Ralsei.Rendering.Data.GradientEffectState;
using RenderingDepthOfFieldEffectState = Ralsei.Rendering.Data.DepthOfFieldEffectState;
using VignetteEffectState = Ralsei.Rendering.Data.VignetteEffectState;

namespace Swag;

public class AppDirector(
    PlaybackObjectContainer playbackObjects,
    MeshService meshService,
    BeatmapService beatmapService,
    TextCacheService textCacheService,
    MeshCacheService meshCacheService,
    PlaybackObjectSortingService sortingService,
    IFrameQueue frames)
{
    // The background is seen by a perspective camera of its own, whose place in the scene is what these say. Its layers
    // are in front of it by the distance from the camera to the root of the background, and a layer's own depth.
    private const float BackgroundRootDistance = 42.0f;

    // The objects of a layer are a little in front of it
    private const float BackgroundObjectOffset = -0.1f;

    // The radius of the most the depth of field blurs, as a fraction of the height of the picture: 14 pixels at 1080
    private const float MaxDepthOfFieldRadius = 14.0f / 1080.0f;

    public bool EnablePostProcessing { get; set; } = true;
    public bool EnableTextRendering { get; set; } = true;

    // Scratch space for the frame being built, so that we know how big a buffer to ask the render thread for
    private sealed class PassContent
    {
        public readonly List<DrawCommand> DrawCommands = [];
        public readonly List<MeshDrawItem> MeshDrawItems = [];
        public readonly List<TextDrawItem> TextDrawItems = [];

        public void Clear()
        {
            DrawCommands.Clear();
            MeshDrawItems.Clear();
            TextDrawItems.Clear();
        }
    }

    // What is in front, and what is in each of the layers of the background
    private readonly PassContent foreground = new();
    private readonly PassContent[] backgroundLayers = Enumerable.Range(0, BeatmapParallaxSettings.LayerCount).Select(_ => new PassContent()).ToArray();
    private readonly List<int> backgroundOrder = [];

    /// <summary>
    /// Works out what the scene looks like at <paramref name="time"/>, for a target of <paramref name="renderSize"/> pixels, and hands it over to the render thread.
    /// </summary>
    public void SubmitFrame(float time, Vector2i renderSize)
    {
        beatmapService.ProcessBeatmap(time, out var themeColorState, out var eventState, out var drawItems);
        
        const float shakeMagic1 = 123.97f;
        const float shakeMagic2 = 423.42f;
        const float shakeFrequency = 10.0f;
        
        var shake = eventState.CameraShake;
        var shakeVector = new Vector2(
            MathF.Sin(time * MathF.PI * shakeFrequency + shakeMagic1) + MathF.Sin(time * shakeFrequency * 2.0f - shakeMagic1),
            MathF.Sin(time * MathF.PI * shakeFrequency - shakeMagic2) + MathF.Sin(time * shakeFrequency * 2.0f + shakeMagic2));
        shakeVector *= shake * 0.5f;
        
        var clearColor = new ColorRgba(themeColorState.Background);
        var cameraState = new CameraState
        {
            Position = eventState.CameraPosition + shakeVector,
            Scale = eventState.CameraScale,
            Rotation = eventState.CameraRotation
        };

        PostProcessingState postProcessingState;
        if (EnablePostProcessing)
        {
            postProcessingState = new PostProcessingState
            {
                Time = time,
                ChromaticAberration = new ChromaticAberrationEffectState
                {
                    Intensity = eventState.Chroma
                },
                LegacyBloom = beatmapService.BeatmapFormat == BeatmapFormat.Lsb
                    ? new BloomEffectState
                    {
                        Intensity = eventState.Bloom.Intensity,
                        Diffusion = 7f,
                        Color = new ColorRgb(1f, 1f, 1f)
                    }
                    : default,
                UniversalBloom = beatmapService.BeatmapFormat == BeatmapFormat.Vgd
                    ? new BloomEffectState
                    {
                        Intensity = eventState.Bloom.Intensity,
                        Diffusion = MathUtil.MapRange(eventState.Bloom.Diffusion, 5f, 30f, 0f, 1f),
                        Color = eventState.Bloom.Color
                    }
                    : default,
                Vignette = new VignetteEffectState
                {
                    Center = eventState.Vignette.Center,
                    Intensity = eventState.Vignette.Intensity,
                    Rounded = eventState.Vignette.Rounded,
                    Roundness = eventState.Vignette.Roundness,
                    Smoothness = eventState.Vignette.Smoothness,
                    Color = eventState.Vignette.Color,
                    Mode = beatmapService.BeatmapFormat == BeatmapFormat.Lsb 
                        ? VignetteMode.UseRoundness 
                        : VignetteMode.UseRounded
                },
                Gradient = new GradientEffectState
                {
                    Color1 = eventState.Gradient.Color1,
                    Color2 = eventState.Gradient.Color2,
                    Intensity = eventState.Gradient.Intensity,
                    Rotation = eventState.Gradient.Rotation * MathF.Tau,
                    Mode = eventState.Gradient.Mode
                },
                HueShift = new HueShiftEffectState
                {
                    Angle = eventState.Hue
                },
                LensDistortion = new LensDistortionEffectState
                {
                    Intensity = eventState.LensDistortion.Intensity,
                    Center = eventState.LensDistortion.Center
                },
                Glitch = new GlitchEffectState
                {
                    Speed = eventState.Glitch.Speed,
                    Intensity = 1.25f,
                    Amount = eventState.Glitch.Intensity,
                    StretchMultiplier = eventState.Glitch.Width
                },
                Grain = new GrainEffectState
                {
                    Colored = eventState.Grain.IsColored,
                    Intensity = eventState.Grain.Intensity,
                    Size = eventState.Grain.Size + 1f,
                    LuminanceContribution = 0.8f
                }
            };
        }
        else
        {
            postProcessingState = default;
        }

        foreground.Clear();
        foreach (var layer in backgroundLayers)
            layer.Clear();

        var renderLayers = sortingService.GetObjectIndexToRenderLayerMapping();

        foreach (ref var drawItem in drawItems)
        {
            var transform = drawItem.Transform;

            var scaleXSquared = transform.M11 * transform.M11 + transform.M12 * transform.M12;
            var scaleYSquared = transform.M21 * transform.M21 + transform.M22 * transform.M22;
            if (scaleXSquared < 0.001f || scaleYSquared < 0.001f)
                continue;

            if (!playbackObjects.TryGetItem(drawItem.ObjectIndex, out var playbackObject))
                continue;

            // The objects in the background are in the layer their render depth names, which is how far away they are
            var content = renderLayers[drawItem.ObjectIndex] == RenderLayer.Background
                ? backgroundLayers[Math.Clamp((int) playbackObject.RenderDepth, 0, backgroundLayers.Length - 1)]
                : foreground;

            if (playbackObject.Shape != ObjectShape.Text)
            {
                if (TryGetMesh(playbackObject, drawItem.ObjectIndex, out var mesh))
                {
                    var renderMode = playbackObject.RenderMode;

                    var color1Rgba = new ColorRgba(drawItem.Color1, drawItem.Opacity);
                    var color2Rgba = drawItem.Color1 == drawItem.Color2
                        ? new ColorRgba(drawItem.Color2, 0.0f)
                        : new ColorRgba(drawItem.Color2, drawItem.Opacity);

                    // TODO: Particles are drawn one mesh at a time. Draw each emitter with a single instanced draw instead.
                    content.DrawCommands.Add(new DrawCommand { DrawType = DrawType.Mesh, DrawId = content.MeshDrawItems.Count });
                    content.MeshDrawItems.Add(new MeshDrawItem
                    {
                        MeshHandle = mesh,
                        Transform = transform,
                        Color1 = color1Rgba,
                        Color2 = color2Rgba,
                        RenderMode = renderMode,
                        GradientRotation = playbackObject.GradientRotation,
                        GradientScale = playbackObject.GradientScale
                    });
                }
            }
            // particle emitters only support standard and custom shapes, not text
            else if (EnableTextRendering && playbackObject.Type != PlaybackObjectType.Particles)
            {
                if (textCacheService.TryGetText(drawItem.ObjectIndex, out var textHandle))
                {
                    var color1 = drawItem.Color1;
                    var color1Rgba = new ColorRgba(color1, drawItem.Opacity);
                    content.DrawCommands.Add(new DrawCommand { DrawType = DrawType.Text, DrawId = content.TextDrawItems.Count });
                    content.TextDrawItems.Add(new TextDrawItem
                    {
                        TextHandle = textHandle,
                        Transform = transform,
                        Color = color1Rgba
                    });
                }
            }
        }

        // The layers of the background that have something in them, the farthest first
        backgroundOrder.Clear();
        for (var i = 0; i < backgroundLayers.Length; i++)
        {
            if (backgroundLayers[i].DrawCommands.Count > 0)
                backgroundOrder.Add(i);
        }

        if (backgroundOrder.Count == 0)
        {
            // Everything is in the same layer: one pass, with all the effects
            var singleFrame = frames.RequestFrameBuffer(foreground.DrawCommands.Count, foreground.MeshDrawItems.Count, foreground.TextDrawItems.Count);
            FillPass(singleFrame.Passes[0], foreground, clearColor, cameraState, renderSize, postProcessingState);
            frames.SubmitFrame(singleFrame);
            return;
        }

        var parallax = beatmapService.BeatmapData.Parallax;
        backgroundOrder.Sort((a, b) => GetBackgroundDistance(parallax, b).CompareTo(GetBackgroundDistance(parallax, a)));

        // The background is seen by a camera of its own, which looks from a distance. Each layer of it is a pass, with the zoom of the
        // camera and the blur of the depth of field that go with its distance. The layers are blurred one by one, because that is all of
        // what is on a layer being the same distance away. They are laid over one another from the farthest, which has the background color,
        // and then comes what is in front of them, and then the effects of the camera are those of what all of them came to.
        var passSizes = new List<PassSize>(backgroundOrder.Count + 1);
        foreach (var layerIndex in backgroundOrder)
        {
            var layer = backgroundLayers[layerIndex];
            passSizes.Add(new PassSize(layer.DrawCommands.Count, layer.MeshDrawItems.Count, layer.TextDrawItems.Count));
        }

        var hasForeground = foreground.DrawCommands.Count > 0;
        if (hasForeground)
            passSizes.Add(new PassSize(foreground.DrawCommands.Count, foreground.MeshDrawItems.Count, foreground.TextDrawItems.Count));

        var frame = frames.RequestFrameBuffer(CollectionsMarshal.AsSpan(passSizes));
        var transparent = new ColorRgba(0f, 0f, 0f, 0f);
        for (var i = 0; i < backgroundOrder.Count; i++)
        {
            var layerIndex = backgroundOrder[i];
            var distance = GetBackgroundDistance(parallax, layerIndex);

            var backgroundCamera = cameraState;
            backgroundCamera.Scale = GetBackgroundCameraScale(eventState.CameraScale, distance);

            var backgroundPost = new PostProcessingState();
            if (EnablePostProcessing)
            {
                backgroundPost.Time = time;
                backgroundPost.DepthOfField = new RenderingDepthOfFieldEffectState
                {
                    Radius = eventState.DepthOfField.Active
                        ? GetDepthOfFieldRadius(eventState.DepthOfField.Focus, distance)
                        : 0f
                };
            }

            FillPass(frame.Passes[i], backgroundLayers[layerIndex], i == 0 ? clearColor : transparent, backgroundCamera, renderSize, backgroundPost);
        }

        if (hasForeground)
            FillPass(frame.Passes[backgroundOrder.Count], foreground, backgroundOrder.Count == 0 ? clearColor : transparent, cameraState, renderSize, default);

        frame.FinalPostProcessingState = postProcessingState;
        frames.SubmitFrame(frame);
    }

    private static void FillPass(PassBuffer pass, PassContent content, ColorRgba clearColor, CameraState cameraState, Vector2i renderSize, PostProcessingState postProcessingState)
    {
        pass.ClearColor = clearColor;
        pass.ViewMatrix = RenderUtil.GetViewMatrix(cameraState, renderSize);
        pass.PostProcessingState = postProcessingState;
        CollectionsMarshal.AsSpan(content.DrawCommands).CopyTo(pass.DrawCommands);
        CollectionsMarshal.AsSpan(content.MeshDrawItems).CopyTo(pass.MeshDrawItems);
        CollectionsMarshal.AsSpan(content.TextDrawItems).CopyTo(pass.TextDrawItems);
    }

    /// <summary>How far a layer of the background is from the camera that sees it.</summary>
    private static float GetBackgroundDistance(BeatmapParallaxSettings parallax, int layerIndex)
        => BackgroundRootDistance + parallax.GetLayerDepth(layerIndex) + BackgroundObjectOffset;

    /// <summary>
    /// What the camera of the background sees at a distance, as the half height of it. The camera looks with a perspective, whose
    /// field of view (in degrees) is the zoom, so what is farther away is larger in the view and smaller on the screen.
    /// </summary>
    private static float GetBackgroundCameraScale(float zoom, float distance)
    {
        var fieldOfView = Math.Clamp(zoom, 0.0f, 170.0f);
        return distance * MathF.Tan(MathUtil.DegreesToRadians(fieldOfView) * 0.5f);
    }

    /// <summary>
    /// How far out of focus the depth of field has something at a distance, as the radius of the blur in fractions of the
    /// height of the picture.
    /// </summary>
    private static float GetDepthOfFieldRadius(float focus, float distance)
    {
        // The camera is focused in front of the root of the background (which is the distance to the first layer)
        var focusDistance = MathF.Max(0.1f, focus + BackgroundRootDistance);

        // How blurred something is goes with how far from the focus it is as a fraction of its own distance: at the focus it is none,
        // farther away than that it comes closer to the most, and the same for what is in front. The most is the radius that
        // is blurred that far at a height of 1080 pixels (and not more than a twentieth of the height).
        var blur = Math.Clamp(1.0f - focusDistance / distance, -1.0f, 1.0f);
        return MathF.Abs(blur) * MaxDepthOfFieldRadius;
    }

    private bool TryGetMesh(PlaybackObject playbackObject, int objectIndex, out MeshHandle meshHandle)
    {
        playbackObject.Shape.ToSeparate(out var shapeIndex, out var shapeOptionIndex);

        if (meshService.TryGetMeshForShape(shapeIndex, shapeOptionIndex, out meshHandle))
            return true;

        if (playbackObject.Shape
            is ObjectShape.SquareCustom
            or ObjectShape.CircleCustom
            or ObjectShape.TriangleCustom
            or ObjectShape.Custom
            or ObjectShape.HexagonCustom)
            if (meshCacheService.TryGetMesh(objectIndex, out meshHandle))
                return true;

        meshHandle = default;
        return false;
    }
}
