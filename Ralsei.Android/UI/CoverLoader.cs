using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Widget;
using Uri = Android.Net.Uri;

namespace Ralsei.Android.UI;

/// <summary>
/// Loads the cover pictures of levels for display, without holding up the UI.
/// </summary>
public static class CoverLoader
{
    private static readonly Dictionary<string, Bitmap?> Cache = [];
    private static readonly Handler MainHandler = new(Looper.MainLooper!);

    /// <summary>
    /// Shows the cover in the image view once it is loaded. Anything that goes wrong leaves the view as it is.
    /// </summary>
    /// <param name="sizePixels">How big the picture is going to be, which it is loaded at.</param>
    public static void Load(Context context, string? uri, ImageView target, int sizePixels, Action<ImageView, Bitmap> show, Action<ImageView> showPlaceholder)
    {
        if (string.IsNullOrEmpty(uri))
        {
            showPlaceholder(target);
            return;
        }

        // The view is reused for other levels while this is loading
        target.Tag = uri;

        lock (Cache)
        {
            if (Cache.TryGetValue(uri, out var cached))
            {
                if (cached is null)
                    showPlaceholder(target);
                else
                    show(target, cached);
                return;
            }
        }

        showPlaceholder(target);

        var resolver = context.ContentResolver!;
        Task.Run(() =>
        {
            var bitmap = Decode(resolver, Uri.Parse(uri)!, sizePixels);
            lock (Cache)
                Cache[uri] = bitmap;

            if (bitmap is null)
                return;

            MainHandler.Post(() =>
            {
                if (target.Tag?.ToString() == uri)
                    show(target, bitmap);
            });
        });
    }

    private static Bitmap? Decode(ContentResolver resolver, Uri uri, int sizePixels)
    {
        try
        {
            // First only the size, to be able to load it smaller than it is
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            using (var stream = resolver.OpenInputStream(uri))
                BitmapFactory.DecodeStream(stream, null, bounds);

            var sample = 1;
            while (bounds.OutWidth / (sample * 2) >= sizePixels && bounds.OutHeight / (sample * 2) >= sizePixels)
                sample *= 2;

            var options = new BitmapFactory.Options { InSampleSize = sample };
            using var pictureStream = resolver.OpenInputStream(uri);
            return BitmapFactory.DecodeStream(pictureStream, null, options);
        }
        catch (Exception)
        {
            // A cover that can not be read is no reason to complain, there is a placeholder
            return null;
        }
    }
}
