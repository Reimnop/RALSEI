using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Swag.Avalonia.Services;
using Swag.Avalonia.Views;
using Reimnop.MvvmHelper;

namespace Swag.Avalonia.ViewModels;

/// <summary>What the app has logged, to look at when something does not work.</summary>
[ViewModelView<LogsView>]
public partial class LogsViewModel : ViewModelBase
{
    public ObservableCollection<LogEntry> VisibleEntries { get; } = [];

    [ObservableProperty]
    public partial bool ProblemsOnly { get; set; }

    /// <summary>How many warnings and errors there are, for the badge.</summary>
    [ObservableProperty]
    public partial int ProblemCount { get; set; }

    [ObservableProperty]
    public partial int EntryCount { get; set; }

    /// <summary>Raised when something was added, so that the list can show the end of it.</summary>
    public event EventHandler? EntryAdded;

    private readonly LogStore store;
    private readonly WindowServices windowServices;

    public LogsViewModel(LogStore store, WindowServices windowServices)
    {
        this.store = store;
        this.windowServices = windowServices;

        store.Entries.CollectionChanged += OnEntriesChanged;
        Rebuild();
    }

    partial void OnProblemsOnlyChanged(bool value)
        => Rebuild();

    [RelayCommand]
    private void Clear()
    {
        store.Clear();
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        var clipboard = windowServices.Clipboard;
        if (clipboard is null)
            return;

        var text = new StringBuilder();
        foreach (var entry in VisibleEntries)
            text.AppendLine($"{entry.TimeText} {entry.LevelText,-5} {entry.Category}: {entry.Message}");

        await clipboard.SetTextAsync(text.ToString());
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var launcher = windowServices.Launcher;
        if (launcher is null)
            return;

        await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(AppPaths.DataDirectory));
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (LogEntry entry in e.NewItems!)
                    if (Matches(entry))
                        VisibleEntries.Add(entry);
                break;
            case NotifyCollectionChangedAction.Remove:
                foreach (LogEntry entry in e.OldItems!)
                    VisibleEntries.Remove(entry);
                break;
            default:
                Rebuild();
                return;
        }

        UpdateCounts();
        EntryAdded?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        VisibleEntries.Clear();
        foreach (var entry in store.Entries.Where(Matches))
            VisibleEntries.Add(entry);

        UpdateCounts();
        EntryAdded?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCounts()
    {
        EntryCount = store.Entries.Count;
        ProblemCount = store.Entries.Count(x => x.IsProblem);
    }

    private bool Matches(LogEntry entry)
        => !ProblemsOnly || entry.IsProblem;
}
