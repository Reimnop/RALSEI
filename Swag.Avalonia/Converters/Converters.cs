using System;
using Avalonia.Data.Converters;
using Lucide.Avalonia;

namespace Swag.Avalonia.Converters;

public static class Converters
{
    /// <summary>Seconds as 2:05, or 1:02:05 for an hour or more.</summary>
    public static readonly FuncValueConverter<double, string> SecondsToText = new(seconds =>
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return time.TotalHours >= 1
            ? $"{(int) time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    });

    public static readonly FuncValueConverter<bool, LucideIconKind> FullScreenIcon = new(isFullScreen =>
        isFullScreen ? LucideIconKind.Minimize : LucideIconKind.Maximize);

    public static readonly FuncValueConverter<bool, LucideIconKind> FoundIcon = new(found =>
        found ? LucideIconKind.CircleCheck : LucideIconKind.CircleAlert);

    public static readonly FuncValueConverter<double, string> RatioToPercentText = new(value =>
        $"{Math.Round(value * 100)}%");
}
