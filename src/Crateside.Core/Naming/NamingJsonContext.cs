using System.Text.Json.Serialization;

namespace Crateside.Core.Naming;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(Dictionary<string, ResolvedName>))]
internal sealed partial class NamingJsonContext : JsonSerializerContext { }
