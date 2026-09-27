using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Jobs;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(JobUpdate))]
[JsonSerializable(typeof(UserAction))]
[JsonSerializable(typeof(Deletion))]
internal sealed partial class JobsJsonContext : JsonSerializerContext { }
