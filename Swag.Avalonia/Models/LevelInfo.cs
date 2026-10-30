using System;
using System.IO;
using Ralsei.Core;

namespace Swag.Avalonia.Models;

/// <summary>A level to play: where its files are, and what it says about itself.</summary>
public sealed record LevelInfo(
    string BeatmapPath,
    BeatmapFormat Format,
    string AudioPath,
    string Title,
    string? Artist = null,
    string? Creator = null,
    double? Bpm = null,
    string? CoverPath = null)
{
    public bool IsSameLevel(LevelInfo other)
        => string.Equals(BeatmapPath, other.BeatmapPath, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(AudioPath, other.AudioPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the files of the level are still where they were.</summary>
    public bool FilesExist => File.Exists(BeatmapPath) && File.Exists(AudioPath);
}

/// <summary>A level that was opened before.</summary>
public sealed record RecentLevel(LevelInfo Level, DateTimeOffset LastOpened);
