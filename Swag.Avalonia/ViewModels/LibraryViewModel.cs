using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Swag.Avalonia.Models;
using Swag.Avalonia.Services;
using Swag.Avalonia.Views;
using Reimnop.MvvmHelper;

namespace Swag.Avalonia.ViewModels;

/// <summary>A level in the list of recent levels.</summary>
public partial class RecentLevelItem(RecentLevel recent, Action<RecentLevelItem> open, Action<RecentLevelItem> forget) : ObservableObject
{
    private const int CoverWidth = 360;

    public LevelInfo Level { get; } = recent.Level;

    public string Title => Level.Title;
    public string Subtitle => string.Join("  ·  ", new[] { Level.Artist, Level.Creator is { } c ? $"by {c}" : null }.Where(x => x is not null));
    public bool HasSubtitle => Subtitle.Length > 0;

    public string LastOpenedText { get; } = FormatAge(recent.LastOpened);

    /// <summary>Whether the files of the level are still where they were.</summary>
    public bool IsAvailable { get; } = recent.Level.FilesExist;

    [ObservableProperty]
    public partial Bitmap? Cover { get; set; }

    private bool coverRequested;

    [RelayCommand]
    private void Open()
        => open(this);

    [RelayCommand]
    private void Forget()
        => forget(this);

    /// <summary>Loads the cover picture, when the item is shown for the first time.</summary>
    public void LoadCover()
    {
        if (coverRequested || Level.CoverPath is not { } path)
            return;

        coverRequested = true;

        Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, CoverWidth);
            }
            catch (Exception)
            {
                // A cover that can not be read is no reason to complain, there is a placeholder
                return null;
            }
        }).ContinueWith(task =>
        {
            if (task.Result is { } bitmap)
                Dispatcher.UIThread.Post(() => Cover = bitmap);
        }, TaskScheduler.Default);
    }

    private static string FormatAge(DateTimeOffset opened)
    {
        var age = DateTimeOffset.Now - opened;
        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int) age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1))
            return $"{(int) age.TotalHours} h ago";
        if (age < TimeSpan.FromDays(7))
            return $"{(int) age.TotalDays} d ago";

        return opened.ToString("d MMM yyyy");
    }
}

/// <summary>What is shown while there is no level: the way to open one, and the levels that were opened before.</summary>
[ViewModelView<LibraryView>]
public partial class LibraryViewModel : ViewModelBase
{
    public ObservableCollection<RecentLevelItem> Recents { get; } = [];

    public bool HasRecents => Recents.Count > 0;

    public event EventHandler? OpenFolderRequested;
    public event EventHandler? OpenFilesRequested;
    public event EventHandler<LevelInfo>? LevelRequested;

    private readonly RecentLevelsService recentLevels;

    public LibraryViewModel(RecentLevelsService recentLevels)
    {
        this.recentLevels = recentLevels;

        recentLevels.Changed += (_, _) => Refresh();
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
        => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenFiles()
        => OpenFilesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ForgetAll()
        => recentLevels.Clear();

    private void OpenRecent(RecentLevelItem item)
        => LevelRequested?.Invoke(this, item.Level);

    private void Forget(RecentLevelItem item)
        => recentLevels.Remove(item.Level);

    private void Refresh()
    {
        Recents.Clear();
        foreach (var recent in recentLevels.Levels)
            Recents.Add(new RecentLevelItem(recent, OpenRecent, Forget));

        OnPropertyChanged(nameof(HasRecents));
    }
}
