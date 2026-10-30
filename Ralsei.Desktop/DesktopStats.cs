using System.Diagnostics;

namespace Ralsei.Desktop;

/// <summary>Numbers about how the app runs, printed once a second when <c>RALSEI_STATS=1</c>: frames per second, main and render thread time per frame, and the time from a user action to the first picture showing it.</summary>
public static class DesktopStats
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("RALSEI_STATS") == "1";

    private static readonly Lock Gate = new();
    private static readonly List<double> Latencies = [];
    private static int frames;
    private static double mainTotal, mainMax, renderTotal, renderMax, waitTotal;
    private static long lastReport = Stopwatch.GetTimestamp();

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static void RecordMain(long ticks)
    {
        lock (Gate)
        {
            frames++;
            var ms = Ms(ticks);
            mainTotal += ms;
            mainMax = Math.Max(mainMax, ms);
        }
    }

    public static void RecordWait(long ticks)
    {
        lock (Gate)
            waitTotal += Ms(ticks);
    }

    public static void RecordRender(long ticks)
    {
        lock (Gate)
        {
            var ms = Ms(ticks);
            renderTotal += ms;
            renderMax = Math.Max(renderMax, ms);
        }
    }

    public static void RecordLatency(long ticks)
    {
        lock (Gate)
            Latencies.Add(Ms(ticks));
    }

    /// <summary>Prints the numbers if a second has gone by since they were last printed.</summary>
    public static void Report()
    {
        var now = Stopwatch.GetTimestamp();
        if (Ms(now - lastReport) < 1000.0)
            return;

        lock (Gate)
        {
            lastReport = now;
            if (frames == 0)
                return;

            Latencies.Sort();
            var latency = Latencies.Count == 0
                ? "no input"
                : $"input to picture avg {Latencies.Average():0.0} ms, p95 {Latencies[(int)(Latencies.Count * 0.95)]:0.0}, max {Latencies[^1]:0.0}";
            Console.WriteLine(
                $"[stats] {frames} frames; main {mainTotal / frames:0.00} ms avg {mainMax:0.0} max; render {renderTotal / frames:0.00} ms avg {renderMax:0.0} max; waited {waitTotal / frames:0.00} ms a frame; {latency}");

            frames = 0;
            mainTotal = mainMax = renderTotal = renderMax = waitTotal = 0;
            Latencies.Clear();
        }
    }
}
