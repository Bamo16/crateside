using System.Text.Json.Serialization;

namespace Crateside.Core.WorkLists;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(QueueSettings))]
internal sealed partial class QueueSettingsJsonContext : JsonSerializerContext { }
