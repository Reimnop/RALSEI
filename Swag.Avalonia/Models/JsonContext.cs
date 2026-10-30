using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Swag.Avalonia.Models;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<RecentLevel>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
