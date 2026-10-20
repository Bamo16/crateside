using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Keeps <c>%LOCALAPPDATA%\crateside\source-index.json</c> current: every track's source audio on
/// disk, held in the working set or kept as a sample, found by video ID or song (#133).
/// </summary>
public sealed class SourceIndex(WorkingSet workingSet, string? path = null)
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
            "crateside",
            "source-index.json"
        );

    private readonly Lock _gate = new();
    private string? _written;

    /// <summary>Rewrites the file when what it lists has changed.</summary>
    public void Refresh(ProcessedTracks processed, KeyLedger ledger)
    {
        List<SourceEntry> entries =
        [
            .. workingSet
                .VideoIds.Where(videoId => workingSet.TryGetSource(videoId, out _))
                .Select(videoId =>
                {
                    workingSet.TryGetSource(videoId, out var held);

                    return new SourceEntry(
                        videoId,
                        ledger.SongFor(videoId) ?? Path.GetFileNameWithoutExtension(held),
                        SourceLocation.WorkingSet,
                        held
                    );
                }),
            .. processed
                .StemsByVideoId.SelectMany(found => found.Value)
                .Where(stem => stem is { IsVerbatimSource: true, SourceVideoId: not null })
                .Select(stem => new SourceEntry(
                    stem.SourceVideoId!,
                    ledger.SongFor(stem.SourceVideoId!)
                        ?? Path.GetFileNameWithoutExtension(stem.Path),
                    SourceLocation.KeptSample,
                    stem.Path
                )),
        ];

        var json = JsonSerializer.Serialize(
            entries.OrderBy(entry => entry.Song).ThenBy(entry => entry.VideoId).ToList(),
            SourceIndexJsonContext.Default.ListSourceEntry
        );

        lock (_gate)
        {
            if (json == _written)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText($"{_path}.tmp", json);
                File.Move($"{_path}.tmp", _path, overwrite: true);
                _written = json;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>One source on disk: <paramref name="Path"/> is the file itself.</summary>
public sealed record SourceEntry(string VideoId, string Song, SourceLocation Location, string Path);

/// <summary>Whether a source is the working set's download or a sample kept in Staging or the library.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SourceLocation>))]
public enum SourceLocation
{
    WorkingSet,
    KeptSample,
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(List<SourceEntry>))]
internal sealed partial class SourceIndexJsonContext : JsonSerializerContext { }
