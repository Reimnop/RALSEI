using Android.Content;
using Android.Provider;
using Org.Json;
using Ralsei.Core;
using Uri = Android.Net.Uri;

namespace Ralsei.Android.Library;

/// <summary>
/// Finds the files of a level in a folder, and reads what there is to know about it.
/// </summary>
public static class LevelScanner
{
    private readonly record struct Entry(string DocumentId, string Name, string MimeType)
    {
        public string Extension => Path.GetExtension(Name).ToLowerInvariant();
        public string NameWithoutExtension => Path.GetFileNameWithoutExtension(Name).ToLowerInvariant();
    }

    // Folders hold more than the level itself: autosaves and backups of the beatmap, other formats of the same data
    private static readonly string[] IgnoredBeatmapPrefixes = ["autosave", "level-previous", "level-quit-backup"];
    private static readonly string[] PreferredAudioNames = ["level", "audio", "song", "music"];
    private static readonly string[] AudioExtensions = [".ogg", ".mp3", ".wav"];
    private static readonly string[] CoverExtensions = [".jpg", ".jpeg", ".png"];
    private static readonly string[] PreferredCoverNames = ["cover", "level"];

    /// <summary>
    /// Looks for a level in the folder behind <paramref name="treeUri"/>.
    /// </summary>
    /// <param name="problem">What is missing, if no level was found.</param>
    public static LevelInfo? ScanFolder(ContentResolver resolver, Uri treeUri, out string? problem)
    {
        problem = null;

        var rootId = DocumentsContract.GetTreeDocumentId(treeUri)!;
        var entries = ListChildren(resolver, treeUri, rootId);

        var beatmap = FindBeatmap(entries);
        if (beatmap is null)
        {
            problem = "No beatmap (.vgd or .lsb) was found in that folder";
            return null;
        }

        var audio = FindAudio(entries);
        if (audio is null)
        {
            problem = "No audio file (.ogg, .mp3 or .wav) was found in that folder";
            return null;
        }

        Uri DocumentUri(Entry entry) => DocumentsContract.BuildDocumentUriUsingTree(treeUri, entry.DocumentId)!;

        var format = beatmap.Value.Extension == ".lsb" ? BeatmapFormat.Lsb : BeatmapFormat.Vgd;

        var folderName = GetDocumentName(resolver, DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootId)!)
            ?? beatmap.Value.NameWithoutExtension;
        var metadata = entries
            .Where(x => x.NameWithoutExtension == "metadata" && x.Extension is ".vgm" or ".lsb")
            .OrderBy(x => x.Extension == ".vgm" ? 0 : 1)
            .FirstOrDefault();
        var info = ReadMetadata(resolver, metadata, treeUri);

        var cover = FindCover(entries);
        return new LevelInfo(
            DocumentUri(beatmap.Value).ToString()!,
            format,
            DocumentUri(audio.Value).ToString()!,
            info.Title ?? folderName,
            info.Artist,
            info.Creator,
            info.Bpm,
            cover is null ? null : DocumentUri(cover.Value).ToString(),
            [treeUri.ToString()!]);
    }

    /// <summary>
    /// Builds the level for two files that were picked by hand.
    /// </summary>
    public static LevelInfo FromFiles(ContentResolver resolver, Uri beatmapUri, BeatmapFormat format, Uri audioUri)
    {
        var name = GetDocumentName(resolver, beatmapUri);
        var title = string.IsNullOrEmpty(name) ? "Untitled" : Path.GetFileNameWithoutExtension(name);
        return new LevelInfo(beatmapUri.ToString()!, format, audioUri.ToString()!, title, GrantedUris: [beatmapUri.ToString()!, audioUri.ToString()!]);
    }

    public static string? GetDocumentName(ContentResolver resolver, Uri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is null || !cursor.MoveToFirst())
                return null;

            var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
            return index < 0 ? null : cursor.GetString(index);
        }
        catch (Exception)
        {
            // Not every uri can be asked for its name (file:// uris from the debugger, providers that went away)
            return uri.LastPathSegment;
        }
    }

    public static bool TryGetFormat(string? fileName, out BeatmapFormat format)
    {
        switch (Path.GetExtension(fileName ?? "").ToLowerInvariant())
        {
            case ".lsb":
                format = BeatmapFormat.Lsb;
                return true;
            case ".vgd":
                format = BeatmapFormat.Vgd;
                return true;
            default:
                format = default;
                return false;
        }
    }

    private static List<Entry> ListChildren(ContentResolver resolver, Uri treeUri, string parentId)
    {
        var entries = new List<Entry>();
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentId)!;

        string[] columns =
        [
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType
        ];

        using var cursor = resolver.Query(childrenUri, columns, null, null, null);
        if (cursor is null)
            return entries;

        while (cursor.MoveToNext())
        {
            var mimeType = cursor.GetString(2) ?? "";
            if (mimeType == DocumentsContract.Document.MimeTypeDir)
                continue;

            entries.Add(new Entry(cursor.GetString(0) ?? "", cursor.GetString(1) ?? "", mimeType));
        }

        return entries;
    }

    private static Entry? FindBeatmap(List<Entry> entries)
    {
        var candidates = entries
            .Where(x => x.Extension is ".vgd" or ".lsb")
            .Where(x => !IgnoredBeatmapPrefixes.Any(prefix => x.NameWithoutExtension.StartsWith(prefix)))
            .ToList();

        // level.vgd is the current format, level.lsb is what older versions left next to it
        return candidates.OrderBy(x => x.NameWithoutExtension == "level" ? 0 : 1)
            .ThenBy(x => x.Extension == ".vgd" ? 0 : 1)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => (Entry?) x)
            .FirstOrDefault();
    }

    private static Entry? FindAudio(List<Entry> entries)
    {
        var candidates = entries
            .Where(x => AudioExtensions.Contains(x.Extension) || x.MimeType.StartsWith("audio/"))
            .ToList();

        return candidates.OrderBy(x =>
            {
                var preferred = Array.IndexOf(PreferredAudioNames, x.NameWithoutExtension);
                return preferred < 0 ? PreferredAudioNames.Length : preferred;
            })
            .ThenBy(x => Array.IndexOf(AudioExtensions, x.Extension) is var index and >= 0 ? index : AudioExtensions.Length)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => (Entry?) x)
            .FirstOrDefault();
    }

    private static Entry? FindCover(List<Entry> entries)
    {
        var candidates = entries.Where(x => CoverExtensions.Contains(x.Extension)).ToList();

        return candidates.OrderBy(x =>
            {
                var preferred = Array.IndexOf(PreferredCoverNames, x.NameWithoutExtension);
                return preferred < 0 ? PreferredCoverNames.Length : preferred;
            })
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => (Entry?) x)
            .FirstOrDefault();
    }

    private readonly record struct Metadata(string? Title, string? Artist, string? Creator, double? Bpm);

    private static Metadata ReadMetadata(ContentResolver resolver, Entry metadataEntry, Uri treeUri)
    {
        // metadata.vgm and metadata.lsb are both JSON, with numbers sometimes written as strings
        if (string.IsNullOrEmpty(metadataEntry.DocumentId))
            return default;

        try
        {
            var uri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, metadataEntry.DocumentId)!;
            using var stream = resolver.OpenInputStream(uri);
            if (stream is null)
                return default;

            using var reader = new StreamReader(stream);
            var json = new JSONObject(reader.ReadToEnd());

            var song = json.OptJSONObject("song");
            var artist = json.OptJSONObject("artist");
            var creator = json.OptJSONObject("creator");

            return new Metadata(
                song?.OptString("title"),
                artist?.OptString("name"),
                creator?.OptString("steam_name"),
                song is not null && song.Has("bpm") ? song.OptDouble("bpm") : null);
        }
        catch (Exception)
        {
            // The level is playable without a title
            return default;
        }
    }
}
