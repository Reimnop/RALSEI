using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ralsei.Desktop;

/// <summary>Builds a frame as late as it can be before the display needs it. With vertical sync, a frame built at once when the previous one is shown waits a whole refresh and its input is already old; building just in time (taking in input just before) removes nearly that refresh from key-to-picture time. How long a frame needs is learned from earlier frames (the margin grows when one is late, shrinks when none are).</summary>
public sealed class FramePacing
{
    private const double MinSlackMs = 0.5;
    private const double MaxSlackMs = 6.0;

    private readonly Lock gate = new();
    private long lastPresent;
    private double intervalMs = 1000.0 / 60.0;
    private double renderMs = 2.0;
    private double buildMs = 1.0;
    private double slackMs = 1.5;
    private int calm;

    /// <summary>When the render thread began the frame it is drawing, and how long it took to have it drawn (set by the render thread, read by it).</summary>
    public long FrameStarted;

    public long PrepareTicks;

    public static readonly bool Enabled = Environment.GetEnvironmentVariable("RALSEI_PACING") != "0";

    private static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static long ToTicks(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

    /// <summary>The render thread has handed a frame to the display, which took <paramref name="renderTicks"/> to draw.</summary>
    public void Presented(long renderTicks)
    {
        var now = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (lastPresent != 0)
            {
                var interval = ToMs(now - lastPresent);

                // Only intervals that look like one refresh (a stall, a dragged window or a mode change are not what is to be learned)
                if (interval is > 2.0 and < 70.0)
                {
                    if (interval > intervalMs * 1.5)
                    {
                        // A refresh was missed: the frame was late
                        slackMs = Math.Min(slackMs + 0.75, MaxSlackMs);
                        calm = 0;
                    }
                    else
                    {
                        intervalMs += (interval - intervalMs) * 0.1;
                        if (++calm >= 60)
                        {
                            calm = 0;
                            slackMs = Math.Max(slackMs - 0.1, MinSlackMs);
                        }
                    }
                }
            }

            renderMs += (ToMs(renderTicks) - renderMs) * 0.2;
            lastPresent = now;
        }
    }

    /// <summary>How long the main thread took to build a frame.</summary>
    public void Built(long buildTicks)
    {
        lock (gate)
            buildMs += (ToMs(buildTicks) - buildMs) * 0.2;
    }

    /// <summary>Waits until it is time to take in input and build the frame that will be shown at the next refresh.</summary>
    public void WaitForTurn()
    {
        long target;
        lock (gate)
        {
            if (lastPresent == 0)
                return;

            var budget = buildMs * 1.5 + renderMs * 1.3 + slackMs;
            var now = Stopwatch.GetTimestamp();

            // The first refresh after now that the frame can still make
            var sinceLast = ToMs(now - lastPresent) + budget;
            var refreshes = Math.Max(Math.Ceiling(sinceLast / intervalMs), 1.0);
            target = lastPresent + ToTicks(refreshes * intervalMs - budget);
        }

        SleepUntil(target);
    }

    private long lastCapped;

    /// <summary>Waits so that frames are made no more than <paramref name="perSecond"/> times a second (for when the display takes the newest of them).</summary>
    public void WaitForRate(double perSecond)
    {
        if (perSecond <= 0.0)
            return;

        // A frame that was late does not make the next ones come in a rush
        var target = Math.Max(lastCapped + ToTicks(1000.0 / perSecond), Stopwatch.GetTimestamp() - ToTicks(1000.0 / perSecond));
        SleepUntil(target);
        lastCapped = target;
    }

    private static void SleepUntil(long target)
    {
        while (true)
        {
            var remaining = ToMs(target - Stopwatch.GetTimestamp());
            if (remaining <= 0.0)
                return;

            // Sleeping is not exact; the last part is spent looking at the clock
            if (remaining > 2.0)
                Thread.Sleep(1);
            else if (remaining > 0.3)
                Thread.Yield();
            else
                Thread.SpinWait(20);
        }
    }

    /// <summary>Asks Windows for timers that can wake a thread after a millisecond and not after fifteen.</summary>
    public static void RaiseTimerResolution()
    {
        if (OperatingSystem.IsWindows())
            timeBeginPeriod(1);
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint milliseconds);
}
