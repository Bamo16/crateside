using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration;

/// <summary>
/// How long a track's source download is kept after its last stem leaves staging for the library:
/// a number of days, or forever (#133). Zero days discards at once, as before.
/// </summary>
public sealed record SourceRetention
{
    public int Days { get; init; }

    public bool Forever { get; init; }

    [JsonIgnore]
    public bool KeepsNothing => this is { Forever: false, Days: <= 0 };

    public bool HasLapsed(DateTimeOffset since, DateTimeOffset now) =>
        !Forever && now - since >= TimeSpan.FromDays(Days);
}

/// <summary>The retention the user chose in Settings, kept in <c>%APPDATA%\crateside\source-retention.json</c>.</summary>
public sealed class SourceRetentionStore(string? path = null)
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "source-retention.json"
        );

    private readonly Lock _gate = new();

    /// <summary>Never throws: an unreadable file reads as the default, zero days.</summary>
    public SourceRetention Read()
    {
        lock (_gate)
            return SourceRetentionJsonContext.Default.SourceRetention.ReadFileOrDefault(_path)
                ?? new SourceRetention();
    }

    public void Remember(SourceRetention chosen)
    {
        lock (_gate)
        {
            try
            {
                SourceRetentionJsonContext.Default.SourceRetention.WriteFile(
                    _path,
                    chosen with
                    {
                        Days = Math.Max(0, chosen.Days),
                    }
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(SourceRetention))]
internal sealed partial class SourceRetentionJsonContext : JsonSerializerContext { }
