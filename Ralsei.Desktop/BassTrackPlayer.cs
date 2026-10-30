using System.Runtime.InteropServices;
using ManagedBass;
using Microsoft.Extensions.Logging;
using Ralsei.Core.Audio;

namespace Ralsei.Desktop;

/// <summary>
/// Plays one track through BASS, with a position that can be read and moved and a speed. A track is either an audio file, which
/// is streamed, or sound that was decoded already, which is held in memory (as a WAV file) so that it can be played backwards as well.
/// </summary>
public sealed class BassTrackPlayer : ITrackPlayer
{
    private readonly ILogger<BassTrackPlayer> logger;
    private readonly bool available;

    // A file is a stream. Decoded sound is a stream of its own, from a WAV file made in memory, and a second one of it the other
    // way round, made when something first asks for it.
    private int stream;
    private PcmAudio? pcm;
    private HeldStream? forward;
    private HeldStream? reverse;

    private double baseFrequency;
    private float speed = 1f;
    private float volume = 1f;
    private bool wantsToPlay;

    public BassTrackPlayer(ILogger<BassTrackPlayer> logger)
    {
        this.logger = logger;

        // Another player in the same process may have started BASS already.
        available = BassDevice.Init();
        if (!available)
            logger.LogWarning("Audio is unavailable: BASS failed to initialise ({Error})", Bass.LastError);
    }

    public bool IsLoaded => stream != 0 || forward is not null;

    // The channel that plays now: backwards, it is the reversed sound
    private bool Backwards => speed < 0f && pcm is not null;

    private int Channel => stream != 0 ? stream : Backwards ? ReverseChannel() : forward?.Handle ?? 0;

    public double Length => pcm?.Duration ?? (stream == 0 ? 0d : Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream)));

    public double Position
    {
        get
        {
            var channel = Channel;
            if (channel == 0)
                return 0d;

            var position = Bass.ChannelBytes2Seconds(channel, Bass.ChannelGetPosition(channel));
            return Backwards ? Math.Max(Length - position, 0d) : position;
        }
        set
        {
            var channel = Channel;
            if (channel == 0)
                return;

            var clamped = Math.Clamp(value, 0d, Length > 0d ? Length : value);
            var target = Backwards ? Length - clamped : clamped;
            Bass.ChannelSetPosition(channel, Bass.ChannelSeconds2Bytes(channel, Math.Max(target, 0d)));
        }
    }

    // Asked to play, and has not run out: a held track (speed 0) is still playing
    public bool IsPlaying => wantsToPlay && (speed == 0f || Channel == 0 || Bass.ChannelIsActive(Channel) != PlaybackState.Stopped);

    public float Speed
    {
        get => speed;
        set
        {
            if (value == speed)
                return;

            var wasBackwards = Backwards;
            var position = Position;
            var previous = Channel;

            speed = value;
            if (Backwards != wasBackwards && previous != 0)
                Bass.ChannelStop(previous);

            ApplySpeed();
            Position = position;
            Resume();
        }
    }

    public float Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0f, 1f);
            ApplyVolume(stream);
            ApplyVolume(forward?.Handle ?? 0);
            ApplyVolume(reverse?.Handle ?? 0);
        }
    }

    public void Load(string path)
    {
        if (!available)
            throw new InvalidOperationException("Audio is unavailable.");

        Free();

        stream = Bass.CreateStream(path);
        if (stream == 0)
            throw new InvalidOperationException($"Failed to load audio file '{path}': {Bass.LastError}");

        Bass.ChannelGetAttribute(stream, ChannelAttribute.Frequency, out float frequency);
        baseFrequency = frequency;
        ApplySpeed();
        ApplyVolume(stream);

        logger.LogInformation("Loaded {Path}, {Length:F1}s", path, Length);
    }

    public void Load(PcmAudio audio)
    {
        if (!available)
            throw new InvalidOperationException("Audio is unavailable.");

        Free();

        forward = MakeStream(audio);
        if (forward is null)
            throw new InvalidOperationException($"Failed to load sound: {Bass.LastError}");

        pcm = audio;
        baseFrequency = audio.SampleRate;
        ApplySpeed();
        ApplyVolume(forward.Handle);

        logger.LogInformation("Loaded sound, {Length:F1}s", Length);
    }

    public void Play()
    {
        wantsToPlay = true;
        Resume();
    }

    public void Pause()
    {
        wantsToPlay = false;
        var channel = Channel;
        if (channel != 0)
            Bass.ChannelPause(channel);
    }

    public void Stop()
    {
        wantsToPlay = false;
        var channel = Channel;
        if (channel != 0)
        {
            Bass.ChannelStop(channel);
            Position = 0d;
        }
    }

    public void Dispose() => Free();

    // Plays the channel, unless the speed is nothing, which holds it
    private void Resume()
    {
        var channel = Channel;
        if (channel == 0 || !wantsToPlay)
            return;

        if (speed == 0f)
            Bass.ChannelPause(channel);
        else if (Bass.ChannelIsActive(channel) != PlaybackState.Playing)
            Bass.ChannelPlay(channel);
    }

    private void ApplySpeed()
    {
        var channel = Channel;
        if (channel == 0 || baseFrequency <= 0d)
            return;

        // A frequency of 0 would be the original one, so a held track keeps one that is slow enough to be silent while it is paused
        Bass.ChannelSetAttribute(channel, ChannelAttribute.Frequency, baseFrequency * Math.Max(Math.Abs(speed), 0.01f));
    }

    private void ApplyVolume(int channel)
    {
        if (channel != 0)
            Bass.ChannelSetAttribute(channel, ChannelAttribute.Volume, volume);
    }

    private int ReverseChannel()
    {
        if (reverse is null && pcm is not null)
        {
            reverse = MakeStream(pcm.Reversed());
            ApplyVolume(reverse?.Handle ?? 0);
        }

        return reverse?.Handle ?? 0;
    }

    // The sound as a WAV file in memory, which BASS plays as it would a file
    private static HeldStream? MakeStream(PcmAudio audio)
    {
        var dataLength = audio.Samples.Length * sizeof(short);
        var bytes = new byte[44 + dataLength];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), 0x46464952);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), 36 + dataLength);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), 0x45564157);
        BitConverter.TryWriteBytes(bytes.AsSpan(12, 4), 0x20746d66);
        BitConverter.TryWriteBytes(bytes.AsSpan(16, 4), 16);
        BitConverter.TryWriteBytes(bytes.AsSpan(20, 2), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(22, 2), (short)audio.Channels);
        BitConverter.TryWriteBytes(bytes.AsSpan(24, 4), audio.SampleRate);
        BitConverter.TryWriteBytes(bytes.AsSpan(28, 4), audio.SampleRate * audio.Channels * sizeof(short));
        BitConverter.TryWriteBytes(bytes.AsSpan(32, 2), (short)(audio.Channels * sizeof(short)));
        BitConverter.TryWriteBytes(bytes.AsSpan(34, 2), (short)16);
        BitConverter.TryWriteBytes(bytes.AsSpan(36, 4), 0x61746164);
        BitConverter.TryWriteBytes(bytes.AsSpan(40, 4), dataLength);
        Buffer.BlockCopy(audio.Samples, 0, bytes, 44, dataLength);

        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var handle = Bass.CreateStream(pin.AddrOfPinnedObject(), 0, bytes.Length, BassFlags.Default);
        if (handle == 0)
        {
            pin.Free();
            return null;
        }

        return new HeldStream(handle, pin);
    }

    // BASS reads the memory as it plays, so it stays where it is until the stream is gone
    private sealed class HeldStream(int handle, GCHandle pin)
    {
        public int Handle => handle;

        public void Free()
        {
            Bass.StreamFree(handle);
            pin.Free();
        }
    }

    private void Free()
    {
        wantsToPlay = false;
        if (stream != 0)
        {
            Bass.StreamFree(stream);
            stream = 0;
        }

        forward?.Free();
        forward = null;
        reverse?.Free();
        reverse = null;

        pcm = null;
        baseFrequency = 0d;
    }
}
