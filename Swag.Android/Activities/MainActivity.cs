using System.Globalization;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Button;
using Google.Android.Material.Color;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.Snackbar;
using Google.Android.Material.TextField;
using Google.Android.Material.TextView;
using Swag.Android.Library;
using Swag.Android.Platform;
using Swag.Android.Settings;
using Swag.Android.UI;
using Ralsei.Core;
using Activity = Android.App.Activity;
using Uri = Android.Net.Uri;

namespace Swag.Android.Activities;

/// <summary>The home screen: pick a level, tune how it is played, and play it.</summary>
[Activity(
    Name = "com.reimnop.swag.MainActivity",
    Label = "@string/app_name",
    Theme = "@style/app_theme",
    MainLauncher = true,
    Exported = true,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : Activity
{
    private const int RequestFolder = 1;
    private const int RequestBeatmap = 2;
    private const int RequestAudio = 3;

    private SettingsStore settingsStore = null!;
    private LevelHistory history = null!;
    private PlayerSettings settings = null!;

    private LevelInfo? level;

    // Choosing files by hand takes two steps, this is what the first one found
    private Uri? pickedBeatmap;
    private BeatmapFormat pickedFormat;

    private ImageView cover = null!;
    private MaterialTextView levelTitle = null!;
    private MaterialTextView levelSubtitle = null!;
    private MaterialTextView levelDetails = null!;
    private LinearLayout recentContainer = null!;
    private MaterialTextView recentEmpty = null!;
    private MaterialButton playButton = null!;
    private MaterialTextView backendStatus = null!;

    private bool binding;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Colors from the wallpaper, where the system has them
        DynamicColors.ApplyToActivityIfAvailable(this);

        base.OnCreate(savedInstanceState);

        EdgeToEdge.Enable(this);
        SetContentView(Resource.Layout.activity_main);

        EdgeToEdge.PadForSystemBars(FindViewById<View>(Resource.Id.scroll)!, bottom: false);
        EdgeToEdge.PadForSystemBars(FindViewById<View>(Resource.Id.bottom_bar)!, top: false);

        settingsStore = new SettingsStore(this);
        history = new LevelHistory(this);
        settings = settingsStore.Load();
        level = history.Current;

        cover = FindViewById<ImageView>(Resource.Id.cover)!;
        levelTitle = FindViewById<MaterialTextView>(Resource.Id.level_title)!;
        levelSubtitle = FindViewById<MaterialTextView>(Resource.Id.level_subtitle)!;
        levelDetails = FindViewById<MaterialTextView>(Resource.Id.level_details)!;
        recentContainer = FindViewById<LinearLayout>(Resource.Id.recent_container)!;
        recentEmpty = FindViewById<MaterialTextView>(Resource.Id.recent_empty)!;
        playButton = FindViewById<MaterialButton>(Resource.Id.play_button)!;
        backendStatus = FindViewById<MaterialTextView>(Resource.Id.backend_status)!;

        FindViewById<View>(Resource.Id.open_folder)!.Click += (_, _) => PickFolder();
        FindViewById<View>(Resource.Id.choose_files)!.Click += (_, _) => PickBeatmap();
        playButton.Click += (_, _) => OnPlayClicked();

        BindSettings();
        ShowLevel();
    }

    protected override void OnResume()
    {
        base.OnResume();

        // The player may have found out that Vulkan does not work here
        UpdateBackendStatus();
        ShowRecents();
    }

    #region Picking

    private void PickFolder()
    {
        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(
            ActivityFlags.GrantReadUriPermission |
            ActivityFlags.GrantPersistableUriPermission |
            ActivityFlags.GrantPrefixUriPermission);
        StartActivityForResult(intent, RequestFolder);
    }

    private void PickBeatmap()
    {
        Toast.MakeText(this, Resource.String.choose_beatmap_title, ToastLength.Short)!.Show();
        StartActivityForResult(CreateFilePicker("*/*", null), RequestBeatmap);
    }

    private void PickAudio()
    {
        Toast.MakeText(this, Resource.String.choose_audio_title, ToastLength.Short)!.Show();

        // Ogg files are not always called audio/ogg
        StartActivityForResult(CreateFilePicker("*/*", ["audio/*", "application/ogg"]), RequestAudio);
    }

    private static Intent CreateFilePicker(string type, string[]? mimeTypes)
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(type);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        if (mimeTypes is not null)
            intent.PutExtra(Intent.ExtraMimeTypes, mimeTypes);
        return intent;
    }

#pragma warning disable CA1422
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
#pragma warning restore CA1422
    {
        base.OnActivityResult(requestCode, resultCode, data);

        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri is null)
        {
            // Backing out of the second step gives up on the first one as well
            pickedBeatmap = null;
            return;
        }

        switch (requestCode)
        {
            case RequestFolder:
                OnFolderPicked(uri);
                break;
            case RequestBeatmap:
                OnBeatmapPicked(uri);
                break;
            case RequestAudio:
                OnAudioPicked(uri);
                break;
        }
    }

    private void OnFolderPicked(Uri treeUri)
    {
        history.TryKeepAccess(treeUri);

        var resolver = ContentResolver!;
        Task.Run(() =>
        {
            LevelInfo? found;
            string? problem;
            try
            {
                found = LevelScanner.ScanFolder(resolver, treeUri, out problem);
            }
            catch (Exception e)
            {
                found = null;
                problem = e.Message;
            }

            RunOnUiThread(() =>
            {
                if (found is null)
                {
                    Message($"{GetString(Resource.String.folder_problem)}: {problem}");
                    return;
                }

                SelectLevel(found);
            });
        });
    }

    private void OnBeatmapPicked(Uri uri)
    {
        var name = LevelScanner.GetDocumentName(ContentResolver!, uri);
        if (!LevelScanner.TryGetFormat(name, out var format))
        {
            Message(GetString(Resource.String.not_a_beatmap));
            return;
        }

        history.TryKeepAccess(uri);
        pickedBeatmap = uri;
        pickedFormat = format;

        // The song is the other half of a level
        PickAudio();
    }

    private void OnAudioPicked(Uri uri)
    {
        if (pickedBeatmap is null)
            return;

        history.TryKeepAccess(uri);
        SelectLevel(LevelScanner.FromFiles(ContentResolver!, pickedBeatmap, pickedFormat, uri));
        pickedBeatmap = null;
    }

    private void SelectLevel(LevelInfo selected)
    {
        var previous = level;
        level = selected;
        history.Current = selected;

        // What it replaced may be forgotten about by now
        if (previous is not null && !previous.IsSameLevel(selected))
            history.AddRecent(previous);

        ShowLevel();
        ShowRecents();
    }

    #endregion

    #region Level

    private void ShowLevel()
    {
        if (level is null)
        {
            levelTitle.SetText(Resource.String.no_level_title);
            levelSubtitle.SetText(Resource.String.no_level_hint);
            levelDetails.Visibility = ViewStates.Gone;
            SetCoverPlaceholder(cover);
            playButton.Enabled = false;
            return;
        }

        levelTitle.Text = level.Title;
        levelSubtitle.Text = level.Artist ?? "";
        levelSubtitle.Visibility = level.Artist is null ? ViewStates.Gone : ViewStates.Visible;

        var details = new List<string>();
        if (level.Creator is { } creator)
            details.Add(GetString(Resource.String.by_creator, new Java.Lang.String(creator)));
        if (level.Bpm is { } bpm and > 0)
            details.Add($"{bpm.ToString("0.##", CultureInfo.InvariantCulture)} BPM");
        details.Add(level.Format.ToString().ToUpperInvariant());

        levelDetails.Text = string.Join("  ·  ", details);
        levelDetails.Visibility = ViewStates.Visible;

        CoverLoader.Load(this, level.CoverUri, cover, ToPixels(88), ShowCover, SetCoverPlaceholder);
        playButton.Enabled = true;
    }

    private void ShowRecents()
    {
        recentContainer.RemoveAllViews();

        var recents = history.GetRecents().Where(x => level is null || !x.IsSameLevel(level)).ToList();
        recentEmpty.Visibility = recents.Count == 0 ? ViewStates.Visible : ViewStates.Gone;

        foreach (var recent in recents)
        {
            var row = LayoutInflater.Inflate(Resource.Layout.item_recent_level, recentContainer, false)!;
            row.FindViewById<MaterialTextView>(Resource.Id.recent_title)!.Text = recent.Title;
            row.FindViewById<MaterialTextView>(Resource.Id.recent_subtitle)!.Text =
                string.Join("  ·  ", new[] { recent.Artist, recent.Creator }.Where(x => x is not null));

            var rowCover = row.FindViewById<ImageView>(Resource.Id.recent_cover)!;
            CoverLoader.Load(this, recent.CoverUri, rowCover, ToPixels(48), ShowCover, SetCoverPlaceholder);

            var chosen = recent;
            row.Click += (_, _) =>
            {
                SelectLevel(chosen);
                StartPlaying(chosen);
            };

            var more = row.FindViewById<View>(Resource.Id.recent_more)!;
            more.Click += (_, _) => ShowRecentMenu(more, chosen);
            row.LongClick += (_, _) => ShowRecentMenu(more, chosen);

            recentContainer.AddView(row);
        }
    }

    private void ShowRecentMenu(View anchor, LevelInfo recent)
    {
        var menu = new PopupMenu(this, anchor);
        menu.Menu!.Add(0, 1, 0, Resource.String.remove_from_recent);
        menu.MenuItemClick += (_, _) =>
        {
            history.RemoveRecent(recent);
            ShowRecents();
        };
        menu.Show();
    }

    private static void ShowCover(ImageView view, Bitmap bitmap)
    {
        view.SetPadding(0, 0, 0, 0);
        view.SetScaleType(ImageView.ScaleType.CenterCrop);
        view.SetImageBitmap(bitmap);
    }

    private void SetCoverPlaceholder(ImageView view)
    {
        var padding = view.LayoutParameters!.Width / 4;
        view.SetPadding(padding, padding, padding, padding);
        view.SetScaleType(ImageView.ScaleType.CenterInside);
        view.SetImageResource(Resource.Drawable.ic_music_note);
    }

    private int ToPixels(int dp)
        => (int) (dp * Resources!.DisplayMetrics!.Density);

    #endregion

    #region Playing

    private void OnPlayClicked()
    {
        if (level is null)
        {
            Message(GetString(Resource.String.select_level_first));
            return;
        }

        StartPlaying(level);
    }

    private void StartPlaying(LevelInfo toPlay)
    {
        // Not everything the settings say is still true (the device is not the one the settings were made on)
        if (settings.Backend == RenderingBackend.Vulkan && !VulkanSupport.IsSupported(this))
        {
            settings.Backend = RenderingBackend.OpenGLES;
            settingsStore.Save(settings);
        }

        StartActivity(CreatePlayerIntent(toPlay));
    }

    private Intent CreatePlayerIntent(LevelInfo toPlay)
    {
        var intent = new Intent(this, typeof(PlayerActivity));
        intent.PutExtra(PlayerActivity.ExtraLevel, toPlay.ToJson().ToString());

        // Access that was only granted for this visit goes along to the player
        var uris = new[] { toPlay.BeatmapUri, toPlay.AudioUri }
            .Select(x => Uri.Parse(x)!)
            .Where(x => x.Scheme == "content")
            .ToList();
        if (uris.Count > 0)
        {
            var clip = ClipData.NewRawUri("level", uris[0]);
            foreach (var uri in uris.Skip(1))
                clip!.AddItem(new ClipData.Item(uri));
            intent.ClipData = clip;
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        }

        return intent;
    }

    private void Message(string text)
    {
        // Above the play button, which would otherwise cover it
        var snackbar = Snackbar.Make(FindViewById<View>(Resource.Id.root)!, text, Snackbar.LengthLong)!;
        snackbar.SetAnchorView(FindViewById<View>(Resource.Id.bottom_bar));
        snackbar.Show();
    }

    #endregion

    #region Settings

    private void BindSettings()
    {
        binding = true;

        var backendToggle = FindViewById<MaterialButtonToggleGroup>(Resource.Id.backend_toggle)!;
        var vulkanSupported = VulkanSupport.IsSupported(this);
        FindViewById<View>(Resource.Id.backend_vulkan)!.Enabled = vulkanSupported;
        backendToggle.Check(settings.Backend == RenderingBackend.Vulkan && vulkanSupported
            ? Resource.Id.backend_vulkan
            : Resource.Id.backend_gles);
        backendToggle.AddOnButtonCheckedListener(new ToggleListener((id, isChecked) =>
        {
            if (!isChecked)
                return;

            settings.Backend = id == Resource.Id.backend_vulkan ? RenderingBackend.Vulkan : RenderingBackend.OpenGLES;
            Save();
            UpdateBackendStatus();
        }));

        BindSwitch(Resource.Id.switch_lock_aspect, settings.LockAspectRatio, on => settings.LockAspectRatio = on);
        BindSwitch(Resource.Id.switch_vsync, settings.VSync, on => settings.VSync = on);

        BindChoice(Resource.Id.scale_toggle, settings.RenderScale,
            [(Resource.Id.scale_100, 1f), (Resource.Id.scale_75, 0.75f), (Resource.Id.scale_50, 0.5f)],
            value => settings.RenderScale = value);

        BindChoice(Resource.Id.fps_toggle, settings.FrameRateLimit,
            [(Resource.Id.fps_auto, 0), (Resource.Id.fps_30, 30), (Resource.Id.fps_60, 60)],
            value => settings.FrameRateLimit = value);

        BindSwitch(Resource.Id.switch_post_processing, settings.PostProcessing, on => settings.PostProcessing = on);
        BindSwitch(Resource.Id.switch_text, settings.TextRendering, on => settings.TextRendering = on);

        BindSwitch(Resource.Id.switch_loop, settings.Loop, on => settings.Loop = on);
        BindSwitch(Resource.Id.switch_stats, settings.ShowStats, on => settings.ShowStats = on);

        var seedLayout = FindViewById<TextInputLayout>(Resource.Id.seed_layout)!;
        var seedInput = FindViewById<TextInputEditText>(Resource.Id.seed_input)!;
        seedInput.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
        seedInput.TextChanged += (_, _) =>
        {
            if (!binding && ulong.TryParse(seedInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
            {
                settings.Seed = seed;
                Save();
            }
        };

        BindSwitch(Resource.Id.switch_random_seed, settings.RandomSeed, on =>
        {
            settings.RandomSeed = on;
            seedLayout.Enabled = !on;
            seedInput.Enabled = !on;
        });
        seedLayout.Enabled = !settings.RandomSeed;
        seedInput.Enabled = !settings.RandomSeed;

        UpdateBackendStatus();
        binding = false;
    }

    private void UpdateBackendStatus()
    {
        if (settings.Backend == RenderingBackend.OpenGLES)
        {
            backendStatus.SetText(Resource.String.backend_status_gles);
            return;
        }

        var failure = settingsStore.VulkanFailure;
        if (!VulkanSupport.IsSupported(this))
            backendStatus.SetText(Resource.String.backend_status_vulkan_unavailable);
        else if (failure is not null)
            backendStatus.Text = GetString(Resource.String.backend_status_vulkan_failed, new Java.Lang.String(failure));
        else
            backendStatus.SetText(Resource.String.backend_status_vulkan);
    }

    private void BindSwitch(int id, bool value, Action<bool> changed)
    {
        var toggle = FindViewById<MaterialSwitch>(id)!;
        toggle.Checked = value;
        toggle.CheckedChange += (_, e) =>
        {
            if (binding)
                return;

            changed(e.IsChecked);
            Save();
        };
    }

    private void BindChoice<T>(int groupId, T current, (int Id, T Value)[] choices, Action<T> changed)
    {
        var group = FindViewById<MaterialButtonToggleGroup>(groupId)!;
        var selected = choices.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, current));
        group.Check(selected.Id != 0 ? selected.Id : choices[0].Id);

        group.AddOnButtonCheckedListener(new ToggleListener((id, isChecked) =>
        {
            if (!isChecked || binding)
                return;

            changed(choices.First(x => x.Id == id).Value);
            Save();
        }));
    }

    private void Save()
        => settingsStore.Save(settings);

    private class ToggleListener(Action<int, bool> changed) : Java.Lang.Object, MaterialButtonToggleGroup.IOnButtonCheckedListener
    {
        public void OnButtonChecked(MaterialButtonToggleGroup? group, int checkedId, bool isChecked)
            => changed(checkedId, isChecked);
    }

    #endregion
}
