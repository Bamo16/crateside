using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Loop;

// Case-insensitive because paused.json was PascalCase until 2026-09-27.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(PausedStages))]
internal sealed partial class PauseJsonContext : JsonSerializerContext { }
