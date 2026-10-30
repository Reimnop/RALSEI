using System.Numerics;
using Ralsei.Core.Data;
using Ralsei.Rendering;
using Ralsei.Rendering.Data;
using Ralsei.Mathematics;
using Ralsei.Rendering.Handle;

namespace Ralsei.Core.Graphics;

/// <summary>An <see cref="ICanvas"/> that records draw calls on the game thread and builds them into a frame for the render thread (<see cref="Submit"/>), so scenes never touch render-thread state.</summary>
public sealed class Canvas(CanvasOptions options) : ICanvas
{
    private DrawCommand[] commands = new DrawCommand[2048];
    private int count;

    public int Width { get; private set; } = 1;
    public int Height { get; private set; } = 1;

    private float HalfWidth => Width / 2f;
    private float HalfHeight => Height / 2f;

    public Vector2 Offset { get; set; }

    /// <summary>Starts a frame for a window of <paramref name="windowSize"/> pixels, which sets the size of the drawing space.</summary>
    public void Begin(Vector2i windowSize)
    {
        count = 0;
        Offset = Vector2.Zero;

        var size = options.FixedSize ?? windowSize;
        Width = Math.Max(size.X, 1);
        Height = Math.Max(size.Y, 1);
    }

    public void DrawSprite(
        Sprite sprite, int frame, float x, float y,
        float scaleX = 1f, float scaleY = 1f, float rotation = 0f, float alpha = 1f, ColorRgba? blend = null, ColorRgba? fog = null)
    {
        var region = sprite.GetFrame(frame);
        var color = blend ?? new ColorRgba(1f, 1f, 1f, 1f);
        color.A *= alpha;

        // Quad is centred on its own origin; shift so the sprite origin sits at (0, 0), scale/rotate about
        // it, then move it into place. World space has Y pointing up, hence the flipped signs.
        var transform =
            Matrix3x2.CreateScale(region.Width, region.Height) *
            Matrix3x2.CreateTranslation(region.Width / 2f - sprite.OriginX, sprite.OriginY - region.Height / 2f) *
            Matrix3x2.CreateScale(scaleX, scaleY) *
            Matrix3x2.CreateRotation(rotation * MathF.PI / 180f) *
            Matrix3x2.CreateTranslation(x + Offset.X - HalfWidth, HalfHeight - (y + Offset.Y));

        Push(region.Atlas, transform, color, region.MinUV, region.MaxUV, fog);
    }

    public void DrawRegion(AtlasRegion region, float x, float y, float width, float height, ColorRgba color, ColorRgba? fog = null)
    {
        var transform =
            Matrix3x2.CreateScale(width, height) *
            Matrix3x2.CreateTranslation(x + Offset.X + width / 2f - HalfWidth, HalfHeight - (y + Offset.Y + height / 2f));

        Push(region.Atlas, transform, color, region.MinUV, region.MaxUV, fog);
    }

    public void FillRect(float x, float y, float width, float height, ColorRgba color)
    {
        if (width <= 0f || height <= 0f)
            return;

        var transform =
            Matrix3x2.CreateScale(width, height) *
            Matrix3x2.CreateTranslation(x + Offset.X + width / 2f - HalfWidth, HalfHeight - (y + Offset.Y + height / 2f));

        Push(null, transform, color, Vector2.Zero, Vector2.One);
    }

    public void StrokeRect(float x, float y, float width, float height, float thickness, ColorRgba color)
    {
        FillRect(x, y, width, thickness, color);
        FillRect(x, y + height - thickness, width, thickness, color);
        FillRect(x, y + thickness, thickness, height - thickness * 2f, color);
        FillRect(x + width - thickness, y + thickness, thickness, height - thickness * 2f, color);
    }

    public void DrawText(BitmapFont font, string text, float x, float y, ColorRgba color, float scaleX, float scaleY)
    {
        var cursorX = x;
        var cursorY = y;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                cursorX = x;
                cursorY += font.LineHeight * scaleY;
                continue;
            }

            if (!font.TryGetGlyph(c, out var glyph))
                continue;

            if (glyph.Region.Width > 0 && glyph.Region.Height > 0)
            {
                DrawRegion(
                    glyph.Region,
                    cursorX + glyph.Offset * scaleX, cursorY,
                    glyph.Region.Width * scaleX, glyph.Region.Height * scaleY,
                    color);
            }

            cursorX += glyph.Advance * scaleX;
        }
    }

    /// <summary>Builds the recorded calls into a frame, with the bars of a non-matching window on top, and submits it.</summary>
    /// <param name="quad">The unit quad that is drawn for every call.</param>
    /// <param name="windowSize">The size of the window being drawn to; a fixed-size screen is fitted inside it.</param>
    public void Submit(IFrameQueue frames, MeshHandle quad, Vector2i windowSize)
    {
        // Pages that have no texture yet (not built) are skipped for the frame.
        var meshCount = 0;
        for (var i = 0; i < count; i++)
        {
            if (commands[i].Atlas is { } atlas && !atlas.TryGetTexture(out _))
                continue;

            meshCount++;
        }

        // Bars over everything outside a fixed-size screen, drawn last so they hide anything that spilled past its edge.
        var bars = options.FixedSize is null
            ? []
            : VirtualViewport.Bars(Width, Height, windowSize.X, windowSize.Y).Where(bar => bar.Width > 0f && bar.Height > 0f).ToList();
        meshCount += bars.Count;

        // Without a fixed size one unit is one pixel, so the view is as tall as the window
        var scale = options.FixedSize is null ? Math.Max(windowSize.Y, 1) / 2f : VirtualViewport.CameraScale(Width, Height, windowSize.X, windowSize.Y);

        var frame = frames.RequestFrameBuffer(meshCount, meshCount, 0);
        var pass = frame.Passes[0];
        pass.ViewMatrix = RenderUtil.GetViewMatrix(
            new CameraState { Position = Vector2.Zero, Scale = scale, Rotation = 0f }, windowSize);
        pass.ClearColor = options.ClearColor;
        pass.PostProcessingState = default;

        var drawCommands = pass.DrawCommands;
        var meshItems = pass.MeshDrawItems;
        var commandIndex = 0;
        var meshIndex = 0;

        for (var i = 0; i < count; i++)
        {
            ref var command = ref commands[i];

            var texture = TextureHandle.None;
            if (command.Atlas is { } atlas && !atlas.TryGetTexture(out texture))
                continue;

            drawCommands[commandIndex++] = new Rendering.DrawCommand { DrawType = DrawType.Mesh, DrawId = meshIndex };
            meshItems[meshIndex++] = new MeshDrawItem
            {
                MeshHandle = quad,
                Transform = command.Transform,
                Color1 = command.Color,
                MinUV = command.MinUV,
                MaxUV = command.MaxUV,
                Texture = texture,
                Fog = command.Fog
            };
        }

        var barColor = options.ClearColor;
        foreach (var (x, y, width, height) in bars)
        {
            var transform =
                Matrix3x2.CreateScale(width, height) *
                Matrix3x2.CreateTranslation(x + width / 2f, y + height / 2f);

            drawCommands[commandIndex++] = new Rendering.DrawCommand { DrawType = DrawType.Mesh, DrawId = meshIndex };
            meshItems[meshIndex++] = new MeshDrawItem
            {
                MeshHandle = quad,
                Transform = transform,
                Color1 = barColor,
                MaxUV = Vector2.One
            };
        }

        frames.SubmitFrame(frame);
    }

    private void Push(
        TextureAtlas? atlas, in Matrix3x2 transform, ColorRgba color, Vector2 minUV, Vector2 maxUV, ColorRgba? fog = null)
    {
        if (count == commands.Length)
            Array.Resize(ref commands, commands.Length * 2);

        commands[count++] = new DrawCommand
        {
            Atlas = atlas,
            Transform = transform,
            Color = color,
            MinUV = minUV,
            MaxUV = maxUV,
            Fog = fog ?? default
        };
    }

    internal struct DrawCommand
    {
        public TextureAtlas? Atlas;
        public Matrix3x2 Transform;
        public ColorRgba Color;
        public Vector2 MinUV;
        public Vector2 MaxUV;
        public ColorRgba Fog;
    }
}
