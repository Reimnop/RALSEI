namespace Ralsei.Core.Audio;

/// <summary>Decoded sound: 16-bit samples, the frames of all the channels one after another.</summary>
/// <param name="Samples">Interleaved samples (one frame is one sample of each channel).</param>
public sealed record PcmAudio(short[] Samples, int Channels, int SampleRate)
{
    /// <summary>How many frames (a sample of every channel) there are.</summary>
    public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;

    /// <summary>How long it lasts, in seconds.</summary>
    public double Duration => SampleRate > 0 ? FrameCount / (double)SampleRate : 0d;

    /// <summary>The same sound played from its end to its start.</summary>
    public PcmAudio Reversed()
    {
        var reversed = new short[Samples.Length];
        var frames = FrameCount;
        for (var frame = 0; frame < frames; frame++)
            Array.Copy(Samples, frame * Channels, reversed, (frames - 1 - frame) * Channels, Channels);

        return this with { Samples = reversed };
    }
}
