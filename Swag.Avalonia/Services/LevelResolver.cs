using System.Collections.Generic;
using System.IO;
using System.Linq;
using Swag.Avalonia.Models;
using Swag.Levels;

namespace Swag.Avalonia.Services;

/// <summary>Turns what the user points at (a folder, a beatmap, a beatmap and a song) into a level.</summary>
public static class LevelResolver
{
    /// <summary>What was found, and what is still needed to have a level.</summary>
    /// <param name="Level">The level, if there is one.</param>
    /// <param name="Problem">What is wrong, if there is no level.</param>
    /// <param name="BeatmapNeedingAudio">Set when the beatmap is fine and only the song has to be chosen.</param>
    public sealed record Result(LevelInfo? Level, string? Problem = null, string? BeatmapNeedingAudio = null)
    {
        public bool NeedsAudio => BeatmapNeedingAudio is not null;
    }

    /// <summary>
    /// Works out the level from dropped or passed files and folders: a folder with a level in it, or a beatmap
    /// (with the song, if that is among them or next to it).
    /// </summary>
    public static Result Resolve(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return new Result(null, "Nothing was given to open.");

        var folder = paths.FirstOrDefault(Directory.Exists);
        if (folder is not null)
            return FromFolder(folder);

        var beatmap = paths.FirstOrDefault(x => LevelLocator.GetFormat(x) is not null);
        if (beatmap is null)
            return new Result(null, "That is not a level. Open a folder with a level in it, or a .vgd or .lsb beatmap.");

        var audio = paths.FirstOrDefault(LevelLocator.IsAudio);
        return audio is not null ? FromFiles(beatmap, audio) : FromBeatmap(beatmap);
    }

    public static Result FromFolder(string folder)
    {
        var found = LevelLocator.Find(ListFiles(folder), out var problem);
        if (found is null)
            return new Result(null, problem);

        return new Result(Build(folder, found, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))));
    }

    /// <summary>A beatmap that was picked on its own: the song is looked for next to it.</summary>
    public static Result FromBeatmap(string beatmapPath)
    {
        if (!File.Exists(beatmapPath))
            return new Result(null, "That file does not exist.");

        var folder = Path.GetDirectoryName(Path.GetFullPath(beatmapPath))!;
        var files = ListFiles(folder);

        var audio = LevelLocator.FindAudioFor(Path.GetFileName(beatmapPath), files);
        if (audio is null)
            return new Result(null, "The song of that level was not found next to it.", beatmapPath);

        var found = LevelLocator.Find(files, out _);
        var level = Build(folder, new LevelFiles(
            Path.GetFileName(beatmapPath),
            LevelLocator.GetFormat(beatmapPath)!.Value,
            audio,
            found?.CoverName,
            found?.MetadataName), Path.GetFileNameWithoutExtension(beatmapPath));

        return new Result(level);
    }

    public static Result FromFiles(string beatmapPath, string audioPath)
    {
        if (LevelLocator.GetFormat(beatmapPath) is not { } format)
            return new Result(null, "The beatmap has to be a .vgd or .lsb file.");

        var folder = Path.GetDirectoryName(Path.GetFullPath(beatmapPath))!;
        var found = LevelLocator.Find(ListFiles(folder), out _);

        var level = Build(folder, new LevelFiles(
            Path.GetFileName(beatmapPath),
            format,
            Path.GetFileName(audioPath),
            found?.CoverName,
            found?.MetadataName), Path.GetFileNameWithoutExtension(beatmapPath));

        // The song can be anywhere, it is not necessarily in the folder of the beatmap
        return new Result(level with { AudioPath = Path.GetFullPath(audioPath) });
    }

    private static List<string> ListFiles(string folder)
        => Directory.EnumerateFiles(folder)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();

    private static LevelInfo Build(string folder, LevelFiles files, string? fallbackTitle)
    {
        var metadata = LevelMetadata.Empty;
        if (files.MetadataName is not null)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(folder, files.MetadataName));
                metadata = LevelMetadata.Read(stream);
            }
            catch (IOException)
            {
                // The level is playable without a title
            }
        }

        return new LevelInfo(
            Path.GetFullPath(Path.Combine(folder, files.BeatmapName)),
            files.Format,
            Path.GetFullPath(Path.Combine(folder, files.AudioName)),
            metadata.Title ?? fallbackTitle ?? files.BeatmapName,
            metadata.Artist,
            metadata.Creator,
            metadata.Bpm,
            files.CoverName is null ? null : Path.GetFullPath(Path.Combine(folder, files.CoverName)));
    }
}
