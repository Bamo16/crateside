using System.Text.Json.Serialization;

namespace Crateside.Core.Ledger;

// Two contexts because each DTO set nests its own VerdictDto and BpmDto.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(LedgerRunDto))]
internal sealed partial class KeyLedgerJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(RunEvidenceDto))]
internal sealed partial class RunEvidenceJsonContext : JsonSerializerContext { }
