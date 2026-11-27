using System.Text.Json.Serialization;

namespace Crateside.Core.Familiarity;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ListenBrainzQuery))]
[JsonSerializable(typeof(List<ListenBrainzPopularity>))]
internal sealed partial class FamiliarityJsonContext : JsonSerializerContext { }

internal sealed record ListenBrainzQuery(IReadOnlyList<string> RecordingMbids);

internal sealed record ListenBrainzPopularity(string RecordingMbid, int? TotalUserCount);
