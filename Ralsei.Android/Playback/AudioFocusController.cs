using Android.Content;
using Android.Media;

namespace Ralsei.Android.Playback;

/// <summary>
/// Asks the system for the right to play sound, and says when it is taken away (a call comes in, another app
/// starts playing) or given back.
/// </summary>
public sealed class AudioFocusController : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
{
    private readonly AudioManager? audioManager;
    private readonly Action lost;
    private readonly Action gained;

    private AudioFocusRequestClass? request;
    private bool held;

    /// <param name="context">Any context.</param>
    /// <param name="lost">Called when something else is playing now.</param>
    /// <param name="gained">Called when it is over, and the song may go on.</param>
    public AudioFocusController(Context context, Action lost, Action gained)
    {
        audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
        this.lost = lost;
        this.gained = gained;
    }

    public void Request()
    {
        if (audioManager is null || held)
            return;

        AudioFocusRequest result;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var attributes = new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Music)!
                .Build()!;

            request = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(attributes)!
                .SetOnAudioFocusChangeListener(this)!
                .Build()!;

            result = audioManager.RequestAudioFocus(request);
        }
        else
        {
#pragma warning disable CA1422
            result = audioManager.RequestAudioFocus(this, global::Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
        }

        held = result == AudioFocusRequest.Granted;
    }

    public void Abandon()
    {
        if (audioManager is null || !held)
            return;

        held = false;

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (request is not null)
                audioManager.AbandonAudioFocusRequest(request);
        }
        else
        {
#pragma warning disable CA1422
            audioManager.AbandonAudioFocus(this);
#pragma warning restore CA1422
        }
    }

    public void OnAudioFocusChange(AudioFocus focusChange)
    {
        switch (focusChange)
        {
            case AudioFocus.Gain:
                gained();
                break;
            case AudioFocus.Loss:
            case AudioFocus.LossTransient:
            case AudioFocus.LossTransientCanDuck:
                lost();
                break;
        }
    }
}
