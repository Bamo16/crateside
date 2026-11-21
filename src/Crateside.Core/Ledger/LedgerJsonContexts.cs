using System.Text.Json.Serialization;

namespace Crateside.Core.Ledger;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(MachineDto))]
internal sealed partial class RunEvidenceJsonContext : JsonSerializerContext { }
