namespace Ralsei.Desktop;

/// <summary>How the desktop host runs, from the command line and the environment.</summary>
public static class DesktopSettings
{
    /// <summary>How many display refreshes a frame waits for: 1 (default) shows every frame whole, 0 shows it as soon as it is drawn (less delay, can tear), -1 waits except when a frame is late. <c>--vsync on|off|adaptive</c> or <c>RALSEI_VSYNC</c>.</summary>
    public static int SwapInterval { get; set; } = Parse(Environment.GetEnvironmentVariable("RALSEI_VSYNC")) ?? 1;

    /// <summary>Waits for the graphics driver after each frame so it doesn't queue frames of its own (a bit more render-thread work, shorter delay from key to picture). On by default; <c>RALSEI_LOW_LATENCY=0</c> turns it off.</summary>
    public static bool LowLatency { get; set; } = Environment.GetEnvironmentVariable("RALSEI_LOW_LATENCY") != "0";

    /// <summary>With Vulkan, how many frames are made per display refresh (the display shows the newest): more is less delay and more work. <c>RALSEI_VULKAN_RATE</c>, 3 by default.</summary>
    public static double VulkanRate { get; set; } = double.TryParse(Environment.GetEnvironmentVariable("RALSEI_VULKAN_RATE"), System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate > 0 ? rate : 3.0;

    public static int? Parse(string? text)
        => text?.ToLowerInvariant() switch
        {
            "off" or "false" or "0" => 0,
            "on" or "true" or "1" => 1,
            "adaptive" or "-1" => -1,
            _ => null
        };
}
