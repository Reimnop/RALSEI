namespace Ralsei.Core.Audio;

/// <summary>Plays short sound effects and looping music. Platform projects provide the real implementation.</summary>
public interface IAudioPlayer
{
    /// <summary>Plays a sound effect by asset id. Unknown ids are ignored.</summary>
    void PlaySound(string id, float volume = 1f, float pitch = 1f);

    /// <summary>Starts looping music, replacing whatever was playing.</summary>
    void PlayMusic(string id, float volume = 1f);

    void StopMusic();

    /// <summary>Changes the volume of the music that is playing.</summary>
    void SetMusicVolume(float volume);

    /// <summary>Pauses the current track so <see cref="ResumeMusic"/> can pick it up again later; other music can play meanwhile.</summary>
    void SuspendMusic();

    /// <summary>Stops whatever music is playing and continues the track set aside by <see cref="SuspendMusic"/>, if any.</summary>
    void ResumeMusic();
}

/// <summary>Silent fallback used when a platform has no audio backend.</summary>
public sealed class NullAudioPlayer : IAudioPlayer
{
    public void PlaySound(string id, float volume = 1f, float pitch = 1f)
    {
    }

    public void PlayMusic(string id, float volume = 1f)
    {
    }

    public void StopMusic()
    {
    }

    public void SetMusicVolume(float volume)
    {
    }

    public void SuspendMusic()
    {
    }

    public void ResumeMusic()
    {
    }
}
