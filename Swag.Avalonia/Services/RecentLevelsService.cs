using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Swag.Avalonia.Models;

namespace Swag.Avalonia.Services;

/// <summary>The levels that were opened before, most recent first.</summary>
public sealed class RecentLevelsService
{
    private const int MaxRecents = 12;

    public IReadOnlyList<RecentLevel> Levels => levels;

    /// <summary>Raised when the list changed.</summary>
    public event EventHandler? Changed;

    private readonly ILogger<RecentLevelsService> logger;
    private List<RecentLevel> levels;

    public RecentLevelsService(ILogger<RecentLevelsService> logger)
    {
        this.logger = logger;

        levels = JsonFile.Read(AppPaths.RecentLevelsFile, AppJsonContext.Default.ListRecentLevel,
            e => logger.LogWarning(e, "The list of recent levels could not be read")) ?? [];
    }

    /// <summary>Puts the level on top of the list.</summary>
    public void Add(LevelInfo level)
    {
        levels = levels
            .Where(x => !x.Level.IsSameLevel(level))
            .Prepend(new RecentLevel(level, DateTimeOffset.Now))
            .Take(MaxRecents)
            .ToList();
        Save();
    }

    public void Remove(LevelInfo level)
    {
        levels = levels.Where(x => !x.Level.IsSameLevel(level)).ToList();
        Save();
    }

    public void Clear()
    {
        levels = [];
        Save();
    }

    private void Save()
    {
        JsonFile.Write(AppPaths.RecentLevelsFile, levels, AppJsonContext.Default.ListRecentLevel,
            e => logger.LogWarning(e, "The list of recent levels could not be saved"));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
