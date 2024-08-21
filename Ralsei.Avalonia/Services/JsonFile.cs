using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Ralsei.Avalonia.Services;

/// <summary>
/// Reads and writes the small JSON files of the app. A file that is missing or damaged is the same as no file,
/// and a write never leaves half a file behind.
/// </summary>
internal static class JsonFile
{
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo, Action<Exception>? onError = null) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            onError?.Invoke(e);
            return null;
        }
    }

    public static bool Write<T>(string path, T value, JsonTypeInfo<T> typeInfo, Action<Exception>? onError = null)
    {
        try
        {
            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, value, typeInfo);

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            onError?.Invoke(e);
            return false;
        }
    }
}
