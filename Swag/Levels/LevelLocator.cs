namespace Swag.Levels;

/// <summary>The files of a level that were picked out of the files of a folder.</summary>
public sealed record LevelFiles(
    string BeatmapName,
    BeatmapFormat Format,
    string AudioName,
    string? CoverName,
    string? MetadataName);

/// <summary>Works out which of the files in a level's folder are the beatmap, the song, the cover and the metadata.</summary>
/// <remarks>
/// Folders hold more than the level itself: autosaves and backups of the beatmap, and the beatmap in the format of an
/// older version next to the current one. This only looks at names, so that it works for any kind of storage.
/// </remarks>
public static class LevelLocator
{
    private static readonly string[] IgnoredBeatmapPrefixes = ["autosave", "level-previous", "level-quit-backup"];
    private static readonly string[] PreferredAudioNames = ["level", "audio", "song", "music"];
    private static readonly string[] PreferredCoverNames = ["cover", "level"];

    public static readonly string[] AudioExtensions = [".ogg", ".mp3", ".wav"];
    public static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png"];

    /// <summary>Picks the files of a level out of the names of the files in a folder.</summary>
    /// <param name="problem">What is missing, if there is no level.</param>
    public static LevelFiles? Find(IEnumerable<string> fileNames, out string? problem)
    {
        var names = fileNames.ToList();

        var beatmap = FindBeatmap(names);
        if (beatmap is null)
        {
            problem = "There is no beatmap (.vgd or .lsb) in that folder.";
            return null;
        }

        var audio = FindAudio(names);
        if (audio is null)
        {
            problem = "There is no song (.ogg, .mp3 or .wav) in that folder.";
            return null;
        }

        problem = null;
        return new LevelFiles(
            beatmap,
            GetFormat(beatmap)!.Value,
            audio,
            FindCover(names),
            FindMetadata(names));
    }

    /// <summary>The format of a beatmap from its name, if it is one.</summary>
    public static BeatmapFormat? GetFormat(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".vgd" => BeatmapFormat.Vgd,
            ".lsb" => BeatmapFormat.Lsb,
            _ => null
        };

    public static bool IsAudio(string fileName)
        => AudioExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>
    /// The song that goes with a beatmap, for when only the beatmap was picked: the same file name, or else
    /// what the folder it is in has.
    /// </summary>
    public static string? FindAudioFor(string beatmapName, IEnumerable<string> fileNames)
    {
        var names = fileNames.ToList();
        var baseName = Path.GetFileNameWithoutExtension(beatmapName);

        var sameName = names.FirstOrDefault(x => IsAudio(x) &&
            string.Equals(Path.GetFileNameWithoutExtension(x), baseName, StringComparison.OrdinalIgnoreCase));
        return sameName ?? FindAudio(names);
    }

    private static string? FindBeatmap(List<string> names)
    {
        return names
            .Where(x => GetFormat(x) is not null)
            .Where(x => !IgnoredBeatmapPrefixes.Any(prefix => Stem(x).StartsWith(prefix)))
            // level.vgd is the current format, level.lsb is what older versions left next to it
            .OrderBy(x => Stem(x) == "level" ? 0 : 1)
            .ThenBy(x => GetFormat(x) == BeatmapFormat.Vgd ? 0 : 1)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindAudio(List<string> names)
    {
        return names
            .Where(IsAudio)
            .OrderBy(x => Rank(PreferredAudioNames, Stem(x)))
            .ThenBy(x => Array.IndexOf(AudioExtensions, Path.GetExtension(x).ToLowerInvariant()))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindCover(List<string> names)
    {
        return names
            .Where(x => CoverExtensions.Contains(Path.GetExtension(x).ToLowerInvariant()))
            .OrderBy(x => Rank(PreferredCoverNames, Stem(x)))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? FindMetadata(List<string> names)
    {
        return names
            .Where(x => Stem(x) == "metadata" && Path.GetExtension(x).ToLowerInvariant() is ".vgm" or ".lsb")
            .OrderBy(x => Path.GetExtension(x).Equals(".vgm", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
    }

    private static string Stem(string name)
        => Path.GetFileNameWithoutExtension(name).ToLowerInvariant();

    private static int Rank(string[] preferred, string stem)
    {
        var index = Array.IndexOf(preferred, stem);
        return index < 0 ? preferred.Length : index;
    }
}
