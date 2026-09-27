using System.Text.Json.Serialization;

namespace Crateside.Core.Playlist;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(IReadOnlyList<IntakeRecord>))]
internal sealed partial class IntakeJsonContext : JsonSerializerContext { }
