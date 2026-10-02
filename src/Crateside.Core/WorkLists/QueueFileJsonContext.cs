using System.Text.Json.Serialization;

namespace Crateside.Core.WorkLists;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(QueueDocument))]
internal sealed partial class QueueFileJsonContext : JsonSerializerContext { }
