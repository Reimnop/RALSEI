using System.Diagnostics;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace Swag.Android.UI;

public delegate void SurfaceCreatedCallback(ISurfaceHolder holder);
public delegate void SurfaceDestroyedCallback(ISurfaceHolder holder);
public delegate void SurfaceChangedCallback(ISurfaceHolder holder, Format format, int width, int height);

/// <summary>The view that a level is drawn on.</summary>
public class GraphicsSurfaceView : SurfaceView, ISurfaceHolderCallback
{
    public SurfaceCreatedCallback? SurfaceCreatedCallback { get; set; }
    public SurfaceDestroyedCallback? SurfaceDestroyedCallback { get; set; }
    public SurfaceChangedCallback? SurfaceChangedCallback { get; set; }

    /// <summary>
    /// The fraction of the size of the view to render at. The system scales what is rendered up to the view, which
    /// costs the GPU nothing, so that less is drawn for the same picture.
    /// </summary>
    public float RenderScale
    {
        get => renderScale;
        set
        {
            if (renderScale == value)
                return;

            renderScale = value;
            ApplyRenderScale(Width, Height);
        }
    }

    private float renderScale = 1f;

    public GraphicsSurfaceView(Context? context) : base(context)
    {
        var holder = Holder;
        Debug.Assert(holder is not null);

        holder.AddCallback(this);
    }

    public void SurfaceCreated(ISurfaceHolder holder)
        => SurfaceCreatedCallback?.Invoke(holder);

    public void SurfaceDestroyed(ISurfaceHolder holder)
        => SurfaceDestroyedCallback?.Invoke(holder);

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        => SurfaceChangedCallback?.Invoke(holder, format, width, height);

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        ApplyRenderScale(w, h);
    }

    private void ApplyRenderScale(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        var holder = Holder!;
        if (renderScale >= 1f)
            holder.SetSizeFromLayout();
        else
            holder.SetFixedSize(Math.Max(1, (int) (width * renderScale)), Math.Max(1, (int) (height * renderScale)));
    }
}
