using System.Text.Json.Serialization;

namespace Crateside.Core.Genres;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(List<GenreRecord>))]
internal sealed partial class GenreJsonContext : JsonSerializerContext { }
