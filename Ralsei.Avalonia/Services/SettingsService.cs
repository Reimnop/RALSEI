using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Ralsei.Avalonia.Models;

namespace Ralsei.Avalonia.Services;

/// <summary>
/// Holds the settings and keeps them on disk. Saving is put off for a moment, so that dragging a slider
/// is a single write.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    public AppSettings Settings { get; }

    private readonly ILogger<SettingsService> logger;
    private readonly Timer saveTimer;
    private readonly object gate = new();
    private bool dirty;

    public SettingsService(ILogger<SettingsService> logger)
    {
        this.logger = logger;

        Settings = JsonFile.Read(AppPaths.SettingsFile, AppJsonContext.Default.AppSettings, OnReadError) ?? new AppSettings();
        saveTimer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Says that the settings were changed, and need to be written.
    /// </summary>
    public void Save()
    {
        lock (gate)
        {
            dirty = true;
            saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Writes the settings now, if they changed.
    /// </summary>
    public void Flush()
    {
        lock (gate)
        {
            if (!dirty)
                return;

            dirty = false;
            JsonFile.Write(AppPaths.SettingsFile, Settings, AppJsonContext.Default.AppSettings, e =>
                logger.LogWarning(e, "The settings could not be saved"));
        }
    }

    public void Dispose()
    {
        Flush();
        saveTimer.Dispose();
    }

    private void OnReadError(Exception e)
        => logger.LogWarning(e, "The settings could not be read, the defaults are used");
}
