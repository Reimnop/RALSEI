using Android.Content;

namespace Swag.Android.Settings;

/// <summary>Keeps <see cref="PlayerSettings"/> in the app's shared preferences.</summary>
public sealed class SettingsStore(Context context)
{
    private const string PreferencesName = "settings";

    private const string KeyBackend = "backend";
    private const string KeyLockAspectRatio = "lock_aspect_ratio";
    private const string KeyPostProcessing = "post_processing";
    private const string KeyTextRendering = "text_rendering";
    private const string KeyVSync = "vsync";
    private const string KeyRenderScale = "render_scale";
    private const string KeyFrameRateLimit = "frame_rate_limit";
    private const string KeyPlaybackSpeed = "playback_speed";
    private const string KeyVolume = "volume";
    private const string KeyLoop = "loop";
    private const string KeyRandomSeed = "random_seed";
    private const string KeySeed = "seed";
    private const string KeyShowStats = "show_stats";

    // Set when starting the Vulkan renderer failed, so the home screen can say why Vulkan is not being used
    private const string KeyVulkanFailure = "vulkan_failure";

    private readonly ISharedPreferences preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public string? VulkanFailure
    {
        get => preferences.GetString(KeyVulkanFailure, null);
        set => preferences.Edit()!.PutString(KeyVulkanFailure, value)!.Apply();
    }

    public PlayerSettings Load()
    {
        var defaults = new PlayerSettings();

        var backend = (RenderingBackend) preferences.GetInt(KeyBackend, (int) defaults.Backend);
        if (!Enum.IsDefined(backend))
            backend = defaults.Backend;

        return new PlayerSettings
        {
            Backend = backend,
            LockAspectRatio = preferences.GetBoolean(KeyLockAspectRatio, defaults.LockAspectRatio),
            PostProcessing = preferences.GetBoolean(KeyPostProcessing, defaults.PostProcessing),
            TextRendering = preferences.GetBoolean(KeyTextRendering, defaults.TextRendering),
            VSync = preferences.GetBoolean(KeyVSync, defaults.VSync),
            RenderScale = Snap(preferences.GetFloat(KeyRenderScale, defaults.RenderScale), PlayerSettings.RenderScales, defaults.RenderScale),
            FrameRateLimit = (int) Snap(preferences.GetInt(KeyFrameRateLimit, defaults.FrameRateLimit), PlayerSettings.FrameRateLimits.Select(x => (float) x).ToArray(), defaults.FrameRateLimit),
            PlaybackSpeed = Snap(preferences.GetFloat(KeyPlaybackSpeed, defaults.PlaybackSpeed), PlayerSettings.PlaybackSpeeds, defaults.PlaybackSpeed),
            Volume = Math.Clamp(preferences.GetFloat(KeyVolume, defaults.Volume), 0f, 1f),
            Loop = preferences.GetBoolean(KeyLoop, defaults.Loop),
            RandomSeed = preferences.GetBoolean(KeyRandomSeed, defaults.RandomSeed),
            Seed = ulong.TryParse(preferences.GetString(KeySeed, null), out var seed) ? seed : defaults.Seed,
            ShowStats = preferences.GetBoolean(KeyShowStats, defaults.ShowStats)
        };
    }

    public void Save(PlayerSettings settings)
    {
        preferences.Edit()!
            .PutInt(KeyBackend, (int) settings.Backend)!
            .PutBoolean(KeyLockAspectRatio, settings.LockAspectRatio)!
            .PutBoolean(KeyPostProcessing, settings.PostProcessing)!
            .PutBoolean(KeyTextRendering, settings.TextRendering)!
            .PutBoolean(KeyVSync, settings.VSync)!
            .PutFloat(KeyRenderScale, settings.RenderScale)!
            .PutInt(KeyFrameRateLimit, settings.FrameRateLimit)!
            .PutFloat(KeyPlaybackSpeed, settings.PlaybackSpeed)!
            .PutFloat(KeyVolume, settings.Volume)!
            .PutBoolean(KeyLoop, settings.Loop)!
            .PutBoolean(KeyRandomSeed, settings.RandomSeed)!
            .PutString(KeySeed, settings.Seed.ToString())!
            .PutBoolean(KeyShowStats, settings.ShowStats)!
            .Apply();
    }

    // Settings that came from a version with different choices should land on one that still exists
    private static float Snap(float value, float[] choices, float fallback)
        => choices.Contains(value) ? value : fallback;
}
