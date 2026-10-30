using ManagedBass;

namespace Swag.Android.Playback;

/// <summary>Plays a song using BASS. The position of the song is what a level is animated by.</summary>
public sealed class AudioPlayer : IDisposable
{
    private static readonly object InitLock = new();
    private static bool initialized;

    /// <summary>Set when there is no audio output to use: the song still advances, there is just nothing to hear.</summary>
    public static bool IsSilent { get; private set; }

    public double Position
    {
        get => Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetPosition(stream));
        set => Bass.ChannelSetPosition(stream, Bass.ChannelSeconds2Bytes(stream, Math.Clamp(value, 0, Length)));
    }

    public double Length { get; }

    public bool IsPlaying => Bass.ChannelIsActive(stream) == PlaybackState.Playing;

    /// <summary>Whether the song played through to its end, and stopped there.</summary>
    public bool HasEnded => Bass.ChannelIsActive(stream) == PlaybackState.Stopped;

    /// <summary>How fast the song plays, with 1 being normal. The pitch follows.</summary>
    public double Speed
    {
        get => speed;
        set
        {
            speed = value;
            Bass.ChannelSetAttribute(stream, ChannelAttribute.Frequency, baseFrequency * value);
        }
    }

    public float Volume
    {
        get => volume;
        set
        {
            volume = value;
            Bass.ChannelSetAttribute(stream, ChannelAttribute.Volume, value);
        }
    }

    private readonly int stream;
    private readonly double baseFrequency;
    private double speed = 1;
    private float volume = 1;

    private AudioPlayer(int stream)
    {
        this.stream = stream;

        Length = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
        baseFrequency = Bass.ChannelGetAttribute(stream, ChannelAttribute.Frequency);
    }

    public void Play()
    {
        // A song that played through to its end starts over. Anything else goes on from where it is, including a
        // song that was not started yet and was moved to somewhere in the middle, which BASS also calls stopped.
        var playedToEnd = HasEnded && Position >= Length - 0.05;
        Bass.ChannelPlay(stream, playedToEnd);
    }

    public void Pause()
    {
        Bass.ChannelPause(stream);
    }

    public void Stop()
    {
        Bass.ChannelStop(stream);
    }

    public static AudioPlayer Load(byte[] data)
    {
        EnsureInitialized();

        var stream = Bass.CreateStream(data, 0, data.Length, BassFlags.Default);
        if (stream == 0)
            throw new InvalidOperationException($"The audio file could not be played ({Bass.LastError}). Ogg, MP3 and WAV files are supported.");

        return new AudioPlayer(stream);
    }

    public void Dispose()
    {
        Bass.StreamFree(stream);
    }

    private static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (initialized)
                return;

            // Device 0 is the no sound device, for phones that have no audio output to open
            if (!Bass.Init())
            {
                if (!Bass.Init(0))
                    throw new InvalidOperationException($"The audio system could not be started ({Bass.LastError})");

                IsSilent = true;
            }

            initialized = true;
        }
    }
}
