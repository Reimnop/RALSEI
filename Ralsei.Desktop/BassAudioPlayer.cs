using ManagedBass;
using Microsoft.Extensions.Logging;
using Ralsei.Core.Audio;

namespace Ralsei.Desktop;

/// <summary>Plays sound effects and looping music through BASS. Sounds are decoded once and reused.</summary>
public sealed class BassAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly IAudioLibrary library;
    private readonly ILogger<BassAudioPlayer> logger;
    private readonly Dictionary<string, int> samples = new();
    private readonly HashSet<string> missing = new();
    private readonly Lock gate = new();
    private readonly bool available;

    private int musicStream;
    private int suspendedStream;

    public BassAudioPlayer(IAudioLibrary library, ILogger<BassAudioPlayer> logger)
    {
        this.library = library;
        this.logger = logger;

        available = BassDevice.Init();
        if (!available)
            logger.LogWarning("Audio is unavailable: BASS failed to initialise ({Error})", Bass.LastError);
    }

    public void PlaySound(string id, float volume = 1f, float pitch = 1f)
    {
        if (!available)
            return;

        lock (gate)
        {
            if (!TryGetSample(id, out var sample))
                return;

            var channel = Bass.SampleGetChannel(sample);
            if (channel == 0)
                return;

            Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, volume);
            if (Math.Abs(pitch - 1f) > 0.001f && Bass.ChannelGetAttribute(channel, ChannelAttribute.Frequency, out var frequency))
                Bass.ChannelSetAttribute(channel, ChannelAttribute.Frequency, frequency * pitch);

            Bass.ChannelPlay(channel);
        }
    }

    public void PlayMusic(string id, float volume = 1f)
    {
        if (!available)
            return;

        lock (gate)
        {
            StopMusicLocked();

            var bytes = library.GetMusic(id);
            if (bytes is null)
            {
                logger.LogWarning("Music '{Id}' is not in the audio library", id);
                return;
            }

            musicStream = Bass.CreateStream(bytes, 0, bytes.Length, BassFlags.Loop);
            if (musicStream == 0)
            {
                logger.LogWarning("Could not play music '{Id}': {Error}", id, Bass.LastError);
                return;
            }

            Bass.ChannelSetAttribute(musicStream, ChannelAttribute.Volume, volume);
            Bass.ChannelPlay(musicStream);
        }
    }

    public void StopMusic()
    {
        lock (gate)
            StopMusicLocked();
    }

    public void SetMusicVolume(float volume)
    {
        lock (gate)
        {
            if (musicStream != 0)
                Bass.ChannelSetAttribute(musicStream, ChannelAttribute.Volume, volume);
        }
    }

    public void SuspendMusic()
    {
        lock (gate)
        {
            if (musicStream == 0)
                return;

            if (suspendedStream != 0)
                Bass.StreamFree(suspendedStream);

            Bass.ChannelPause(musicStream);
            suspendedStream = musicStream;
            musicStream = 0;
        }
    }

    public void ResumeMusic()
    {
        lock (gate)
        {
            StopMusicLocked();
            if (suspendedStream == 0)
                return;

            musicStream = suspendedStream;
            suspendedStream = 0;
            Bass.ChannelPlay(musicStream);
        }
    }

    private void StopMusicLocked()
    {
        if (musicStream == 0)
            return;

        Bass.StreamFree(musicStream);
        musicStream = 0;
    }

    private bool TryGetSample(string id, out int sample)
    {
        if (samples.TryGetValue(id, out sample))
            return true;

        if (missing.Contains(id))
            return false;

        var bytes = library.GetSound(id);
        sample = bytes is null ? 0 : Bass.SampleLoad(bytes, 0, bytes.Length, 16, BassFlags.Default);
        if (sample == 0)
        {
            missing.Add(id);
            logger.LogWarning("Sound '{Id}' is unavailable", id);
            return false;
        }

        samples[id] = sample;
        return true;
    }

    public void Dispose()
    {
        if (!available)
            return;

        lock (gate)
        {
            StopMusicLocked();
            if (suspendedStream != 0)
                Bass.StreamFree(suspendedStream);

            foreach (var sample in samples.Values)
                Bass.SampleFree(sample);

            samples.Clear();
        }

        Bass.Free();
    }
}
