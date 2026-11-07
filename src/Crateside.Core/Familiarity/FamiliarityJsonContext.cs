using System.Text.Json.Serialization;

namespace Crateside.Core.Familiarity;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(List<FamiliarityRecord>))]
internal sealed partial class FamiliarityJsonContext : JsonSerializerContext { }
