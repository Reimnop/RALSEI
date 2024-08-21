using Avalonia;
using Avalonia.Controls;

namespace Ralsei.Avalonia.Controls;

/// <summary>
/// Gives its children the largest area of a ratio that fits, centered, so that what is left is bars.
/// Unlocked, it gives them all of the space instead.
/// </summary>
public class AspectRatioPanel : Panel
{
    public static readonly StyledProperty<double> RatioProperty =
        AvaloniaProperty.Register<AspectRatioPanel, double>(nameof(Ratio), defaultValue: 16.0 / 9.0);

    public static readonly StyledProperty<bool> IsLockedProperty =
        AvaloniaProperty.Register<AspectRatioPanel, bool>(nameof(IsLocked), defaultValue: true);

    static AspectRatioPanel()
    {
        AffectsMeasure<AspectRatioPanel>(RatioProperty, IsLockedProperty);
        AffectsArrange<AspectRatioPanel>(RatioProperty, IsLockedProperty);
    }

    public double Ratio
    {
        get => GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    /// <summary>
    /// Whether the children keep the ratio.
    /// </summary>
    public bool IsLocked
    {
        get => GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = GetChildSize(availableSize);
        foreach (var child in Children)
            child.Measure(size);

        // The bars are part of the panel
        return new Size(
            double.IsInfinity(availableSize.Width) ? size.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? size.Height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = GetChildSize(finalSize);
        var area = new Rect((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2, size.Width, size.Height);

        foreach (var child in Children)
            child.Arrange(area);

        return finalSize;
    }

    private Size GetChildSize(Size available)
    {
        if (!IsLocked || available.Width == 0 || available.Height == 0 ||
            double.IsInfinity(available.Width) || double.IsInfinity(available.Height))
            return available;

        var heightFromWidth = available.Width / Ratio;
        return heightFromWidth <= available.Height
            ? new Size(available.Width, heightFromWidth)
            : new Size(available.Height * Ratio, available.Height);
    }
}
