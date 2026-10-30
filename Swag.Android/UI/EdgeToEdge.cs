using Android.App;
using Android.Graphics;
using Android.Views;

namespace Swag.Android.UI;

/// <summary>
/// Lets a window draw behind the system bars and the display cutout, and keeps its content out from under them.
/// Apps that target recent versions of Android are laid out like this whether they like it or not.
/// </summary>
public static class EdgeToEdge
{
    private class Listener(View view, bool left, bool top, bool right, bool bottom) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        private readonly int paddingLeft = view.PaddingLeft;
        private readonly int paddingTop = view.PaddingTop;
        private readonly int paddingRight = view.PaddingRight;
        private readonly int paddingBottom = view.PaddingBottom;

        public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
        {
            GetInsets(insets, out var insetLeft, out var insetTop, out var insetRight, out var insetBottom);

            v.SetPadding(
                paddingLeft + (left ? insetLeft : 0),
                paddingTop + (top ? insetTop : 0),
                paddingRight + (right ? insetRight : 0),
                paddingBottom + (bottom ? insetBottom : 0));

            // Everything underneath is laid out against these as well
            return insets;
        }
    }

    /// <summary>Makes the window extend behind the system bars, with transparent bars.</summary>
    public static void Enable(Activity activity)
    {
        var window = activity.Window!;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
#pragma warning disable CA1422
            window.SetDecorFitsSystemWindows(false);
#pragma warning restore CA1422
        }
        else
        {
#pragma warning disable CA1422
            window.DecorView.SystemUiFlags |=
                SystemUiFlags.LayoutStable | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation;
#pragma warning restore CA1422
        }

#pragma warning disable CA1422
        window.SetStatusBarColor(Color.Transparent);
        window.SetNavigationBarColor(Color.Transparent);
#pragma warning restore CA1422

        // A cutout (notch) is only used in the short edge by default, so that landscape has it at the side
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
            window.Attributes!.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
    }

    /// <summary>
    /// Pads the view, on top of the padding it has, by the amount that the system bars, the display cutout
    /// and the keyboard take up on the chosen sides.
    /// </summary>
    public static void PadForSystemBars(View view, bool left = true, bool top = true, bool right = true, bool bottom = true)
    {
        view.SetOnApplyWindowInsetsListener(new Listener(view, left, top, right, bottom));
        view.RequestApplyInsets();
    }

    private static void GetInsets(WindowInsets insets, out int left, out int top, out int right, out int bottom)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var types = WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout() | WindowInsets.Type.Ime();
            var result = insets.GetInsets(types);
            left = result.Left;
            top = result.Top;
            right = result.Right;
            bottom = result.Bottom;
            return;
        }

#pragma warning disable CA1422
        left = insets.SystemWindowInsetLeft;
        top = insets.SystemWindowInsetTop;
        right = insets.SystemWindowInsetRight;
        bottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1422

        if (OperatingSystem.IsAndroidVersionAtLeast(28) && insets.DisplayCutout is { } cutout)
        {
            left = Math.Max(left, cutout.SafeInsetLeft);
            top = Math.Max(top, cutout.SafeInsetTop);
            right = Math.Max(right, cutout.SafeInsetRight);
            bottom = Math.Max(bottom, cutout.SafeInsetBottom);
        }
    }
}
