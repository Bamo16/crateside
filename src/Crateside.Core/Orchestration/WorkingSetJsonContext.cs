using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Download;

namespace Crateside.Core.Orchestration;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(Acquisition))]
internal sealed partial class WorkingSetJsonContext : JsonSerializerContext { }
