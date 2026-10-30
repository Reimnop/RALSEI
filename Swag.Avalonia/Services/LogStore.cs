using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Swag.Avalonia.Services;

public sealed record LogEntry(DateTime Time, LogLevel Level, string Category, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string LevelText => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "FATAL",
        _ => ""
    };

    public bool IsProblem => Level >= LogLevel.Warning;
    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level >= LogLevel.Error;
}

/// <summary>Keeps what is logged, to be shown in the app. Logging can happen on any thread, the entries show up on the UI thread.</summary>
public sealed class LogStore : ILoggerProvider
{
    private const int MaxEntries = 2000;

    /// <summary>Only to be used from the UI thread.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Raised on the UI thread, when entries were added.</summary>
    public event EventHandler? EntriesAdded;

    private readonly ConcurrentQueue<LogEntry> pending = new();
    private int flushScheduled;

    public ILogger CreateLogger(string categoryName)
        => new StoreLogger(this, ShortenCategory(categoryName));

    public void Clear()
    {
        Entries.Clear();
    }

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        pending.Enqueue(entry);

        // One flush takes everything that is there, so a burst of messages is a single update of the UI
        if (Interlocked.Exchange(ref flushScheduled, 1) == 0)
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    private void Flush()
    {
        Interlocked.Exchange(ref flushScheduled, 0);

        var added = false;
        while (pending.TryDequeue(out var entry))
        {
            Entries.Add(entry);
            added = true;
        }

        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(0);

        if (added)
            EntriesAdded?.Invoke(this, EventArgs.Empty);
    }

    // Ralsei.Core.Service.BeatmapService is a lot to read, BeatmapService is what matters
    private static string ShortenCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot < 0 ? category : category[(dot + 1)..];
    }

    private sealed class StoreLogger(LogStore store, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message += Environment.NewLine + exception;

            store.Add(new LogEntry(DateTime.Now, logLevel, category, message));
        }
    }
}
