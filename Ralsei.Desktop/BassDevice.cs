using ManagedBass;

namespace Ralsei.Desktop;

/// <summary>Starts BASS once, set for little delay between asking for a sound and hearing it (and between the song moving and the clock saying so).</summary>
public static class BassDevice
{
    private static readonly Lock Gate = new();
    private static bool initialized;

    public static bool Init()
    {
        lock (Gate)
        {
            if (initialized)
                return true;

            // The defaults buffer half a second, which is half a second between pressing a key and the sound; this is a tenth, filled every 5 ms
            Bass.Configure(Configuration.PlaybackBufferLength, 100);
            Bass.Configure(Configuration.UpdatePeriod, 5);
            Bass.Configure(Configuration.DeviceBufferLength, 10);

            // A device that was started already (by a test, say) is as good
            initialized = Bass.Init() || Bass.LastError == Errors.Already;
            return initialized;
        }
    }
}
