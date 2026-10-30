namespace Ralsei.Core.Audio;

/// <summary>Supplies encoded audio (WAV/OGG) to an <see cref="IAudioPlayer"/> by id. A game module implements this.</summary>
public interface IAudioLibrary
{
    /// <summary>The file bytes of a sound effect, or null if the id is unknown.</summary>
    byte[]? GetSound(string id);

    /// <summary>The file bytes of a music track, or null if the id is unknown.</summary>
    byte[]? GetMusic(string id);
}
