using System.Globalization;
using System.Text.Json;

namespace Swag.Levels;

/// <summary>What a level says about itself in its metadata file.</summary>
public sealed record LevelMetadata(string? Title, string? Artist, string? Creator, double? Bpm)
{
    public static readonly LevelMetadata Empty = new(null, null, null, null);

    /// <summary>
    /// Reads metadata.vgm or metadata.lsb. Both are JSON, with numbers that are sometimes written as text.
    /// A file that can not be read is the same as one that says nothing, a level is playable without a title.
    /// </summary>
    public static LevelMetadata Read(Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty;

            return new LevelMetadata(
                GetString(root, "song", "title"),
                GetString(root, "artist", "name"),
                GetString(root, "creator", "steam_name"),
                GetNumber(root, "song", "bpm"));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static JsonElement? Find(JsonElement root, string section, string name)
    {
        if (root.TryGetProperty(section, out var sectionElement) &&
            sectionElement.ValueKind == JsonValueKind.Object &&
            sectionElement.TryGetProperty(name, out var value))
            return value;

        return null;
    }

    private static string? GetString(JsonElement root, string section, string name)
    {
        var value = Find(root, section, name);
        var text = value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static double? GetNumber(JsonElement root, string section, string name)
    {
        var value = Find(root, section, name);
        if (value is null)
            return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null
        };
    }
}
