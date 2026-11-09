using System.Text.Json.Serialization;

namespace Crateside.Core.Genres;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(List<GenreRecord>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
internal sealed partial class GenreJsonContext : JsonSerializerContext { }
