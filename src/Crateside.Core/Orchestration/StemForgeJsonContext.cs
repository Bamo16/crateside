using System.Text.Json.Serialization;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Plans;

namespace Crateside.Core.Orchestration;

/// <summary>What <c>stemforge-cli</c> prints with <c>--json</c>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<DownloadOutput>))]
[JsonSerializable(typeof(List<ProbeOutput>))]
[JsonSerializable(typeof(List<FormatListOutput>))]
[JsonSerializable(typeof(List<SeparationOutput>))]
[JsonSerializable(typeof(List<SeparatorPreset>))]
internal sealed partial class StemForgeJsonContext : JsonSerializerContext { }
