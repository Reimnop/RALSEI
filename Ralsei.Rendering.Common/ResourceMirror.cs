using System.Collections.Concurrent;
using Ralsei.Data;
using Tmpx.Common;

namespace Ralsei.Rendering.Common;

/// <summary>The events of a mirror for one kind of resource: what a renderer makes or deletes in its graphics context.</summary>
public sealed class ResourceEvents<T>
{
    public event EventHandler<ObservableSparseSetEventArgs<T>>? ItemInserted;
    public event EventHandler<ObservableSparseSetEventArgs<T>>? ItemRemoved;

    internal void RaiseInserted(object sender, int id, T item) => ItemInserted?.Invoke(sender, new ObservableSparseSetEventArgs<T>(id, item));

    internal void RaiseRemoved(object sender, int id, T item) => ItemRemoved?.Invoke(sender, new ObservableSparseSetEventArgs<T>(id, item));
}

/// <summary>What one renderer sees of a <see cref="RenderingFactory"/>'s resources. The factory posts changes from any thread; the renderer <see cref="Apply"/>s them on its render thread up to the version of the frame it is about to draw, raising the events there in order. A mirror starts with everything that exists.</summary>
public sealed class ResourceMirror : IDisposable
{
    internal enum ChangeKind
    {
        MeshCreated,
        MeshDestroyed,
        TextureCreated,
        TextureDestroyed,
        TextCreated,
        TextDestroyed,
        FontBuffers,
    }

    internal readonly record struct Change(long Version, ChangeKind Kind, int Id, object? Item);

    private readonly RenderingFactory factory;
    private readonly ConcurrentQueue<Change> changes = [];
    private bool disposed;

    internal ResourceMirror(RenderingFactory factory)
    {
        this.factory = factory;
    }

    public ResourceEvents<Mesh> Meshes { get; } = new();
    public ResourceEvents<Texture> Textures { get; } = new();
    public ResourceEvents<Text> Texts { get; } = new();

    /// <summary>Raised when the font buffers were replaced; the new ones are in <see cref="FontBuffers"/>.</summary>
    public event EventHandler? FontBuffersUpdated;

    /// <summary>The font buffers as of the last change that was applied.</summary>
    public FontBuffers FontBuffers { get; private set; } = FontBuffers.Empty;

    public QuadraticCurve[] FontCurves => FontBuffers.Curves;
    public int[] FontCurveIndices => FontBuffers.CurveIndices;
    public BandEntry[] FontBandEntries => FontBuffers.BandEntries;
    public ShapeEntry[] FontShapeEntries => FontBuffers.ShapeEntries;

    internal void Post(in Change change) => changes.Enqueue(change);

    /// <summary>Makes the changes up to <paramref name="version"/> not made yet. Called on the render thread before it draws a frame stamped with that version.</summary>
    public void Apply(long version)
    {
        while (changes.TryPeek(out var change) && change.Version <= version)
        {
            changes.TryDequeue(out change);
            switch (change.Kind)
            {
                case ChangeKind.MeshCreated:
                    Meshes.RaiseInserted(this, change.Id, (Mesh) change.Item!);
                    break;
                case ChangeKind.MeshDestroyed:
                    Meshes.RaiseRemoved(this, change.Id, (Mesh) change.Item!);
                    break;
                case ChangeKind.TextureCreated:
                    Textures.RaiseInserted(this, change.Id, (Texture) change.Item!);
                    break;
                case ChangeKind.TextureDestroyed:
                    Textures.RaiseRemoved(this, change.Id, (Texture) change.Item!);
                    break;
                case ChangeKind.TextCreated:
                    Texts.RaiseInserted(this, change.Id, (Text) change.Item!);
                    break;
                case ChangeKind.TextDestroyed:
                    Texts.RaiseRemoved(this, change.Id, (Text) change.Item!);
                    break;
                case ChangeKind.FontBuffers:
                    FontBuffers = (FontBuffers) change.Item!;
                    FontBuffersUpdated?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        factory.Remove(this);
    }
}
