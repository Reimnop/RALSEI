using Org.Json;
using Ralsei.Core;

namespace Swag.Android.Library;

/// <summary>
/// A level to play: where its files are, and what to show for it. Uris are kept as strings so that the
/// record can be stored and passed around without caring about <see cref="global::Android.Net.Uri"/>.
/// </summary>
public sealed record LevelInfo(
    string BeatmapUri,
    BeatmapFormat Format,
    string AudioUri,
    string Title,
    string? Artist = null,
    string? Creator = null,
    double? Bpm = null,
    string? CoverUri = null,
    IReadOnlyList<string>? GrantedUris = null)
{
    /// <summary>
    /// The uris that read access was kept for (a folder, or each picked file), so that it can be given back
    /// once the level is forgotten.
    /// </summary>
    public IReadOnlyList<string> GrantedUris { get; init; } = GrantedUris ?? [];

    public bool IsSameLevel(LevelInfo other)
        => BeatmapUri == other.BeatmapUri && AudioUri == other.AudioUri;

    public JSONObject ToJson()
    {
        var json = new JSONObject();
        json.Put("beatmap", BeatmapUri);
        json.Put("format", (int) Format);
        json.Put("audio", AudioUri);
        json.Put("title", Title);
        json.Put("artist", Artist);
        json.Put("creator", Creator);
        if (Bpm.HasValue)
            json.Put("bpm", Bpm.Value);
        json.Put("cover", CoverUri);

        var granted = new JSONArray();
        foreach (var uri in GrantedUris)
            granted.Put(uri);
        json.Put("granted", granted);
        return json;
    }

    public static LevelInfo? FromJson(JSONObject json)
    {
        var beatmap = json.OptString("beatmap", "");
        var audio = json.OptString("audio", "");
        if (string.IsNullOrEmpty(beatmap) || string.IsNullOrEmpty(audio))
            return null;

        var format = (BeatmapFormat) json.OptInt("format", (int) BeatmapFormat.Vgd);
        if (!Enum.IsDefined(format))
            return null;

        var granted = new List<string>();
        var grantedJson = json.OptJSONArray("granted");
        for (var i = 0; grantedJson is not null && i < grantedJson.Length(); i++)
            if (grantedJson.OptString(i, null) is { } uri)
                granted.Add(uri);

        return new LevelInfo(
            beatmap,
            format,
            audio,
            json.OptString("title", "Untitled"),
            NullIfEmpty(json.OptString("artist")),
            NullIfEmpty(json.OptString("creator")),
            json.Has("bpm") ? json.OptDouble("bpm") : null,
            NullIfEmpty(json.OptString("cover")),
            granted);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
