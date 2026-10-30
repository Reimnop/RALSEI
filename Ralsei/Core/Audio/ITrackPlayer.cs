using System.Diagnostics;

namespace Ralsei.Core.Audio;

/// <summary>One music track played from a file that can be timed and moved about in: the clock a rhythm-synced player runs its picture from. (<see cref="IAudioPlayer"/> is for a game's own looping music and sounds, by id.)</summary>
public interface ITrackPlayer : IDisposable
{
    bool IsLoaded { get; }

    /// <summary>Loads the audio file at <paramref name="path"/>, stopped at its start. Replaces any track already loaded.</summary>
    void Load(string path);

    /// <summary>Loads sound that has been decoded already, stopped at its start. Replaces any track already loaded.</summary>
    void Load(PcmAudio audio);

    /// <summary>Where the track is, in seconds. Setting it seeks.</summary>
    double Position { get; set; }

    /// <summary>How long the track is, in seconds.</summary>
    double Length { get; }

    bool IsPlaying { get; }

    /// <summary>How fast it plays: 1 normal, 0 holds, negative plays backwards (only for sound loaded with <see cref="Load(PcmAudio)"/>; files only play forwards).</summary>
    float Speed { get; set; }

    /// <summary>How loud it is, from 0 (silent) to 1 (as recorded). Kept across loads. A player that can not change it stays at 1.</summary>
    float Volume
    {
        get => 1f;
        set { }
    }

    void Play();

    void Pause();

    void Stop();
}

/// <summary>A track with no sound, only the clock, for hosts with no audio backend (as long as the time that has passed).</summary>
public sealed class NullTrackPlayer : ITrackPlayer
{
    private readonly Stopwatch watch = new();
    private double offset;
    private float speed = 1f;
    private double length = double.PositiveInfinity;
    private bool playing;

    public bool IsLoaded { get; private set; }

    public double Length => length;

    public bool IsPlaying => playing;

    public double Position
    {
        get => Math.Clamp(offset + (watch.Elapsed.TotalSeconds * speed), 0d, length);
        set
        {
            offset = Math.Clamp(value, 0d, length);
            watch.Restart();
            if (!playing)
                watch.Stop();
        }
    }

    public float Speed
    {
        get => speed;
        set
        {
            // The time already passed is kept at the old speed.
            offset = Position;
            watch.Restart();
            speed = value;
            if (!playing)
                watch.Stop();
        }
    }

    public float Volume { get; set; } = 1f;

    public void Load(string path)
    {
        IsLoaded = true;
        length = double.PositiveInfinity;
        playing = false;
        offset = 0;
        watch.Reset();
    }

    public void Load(PcmAudio audio)
    {
        Load(string.Empty);
        length = audio.Duration;
    }

    public void Play()
    {
        if (playing)
            return;

        playing = true;
        watch.Restart();
    }

    public void Pause()
    {
        offset = Position;
        playing = false;
        watch.Reset();
    }

    public void Stop()
    {
        playing = false;
        watch.Reset();
        offset = 0;
    }

    public void Dispose()
    {
    }
}
