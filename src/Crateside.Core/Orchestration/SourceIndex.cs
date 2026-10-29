using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Keeps <c>%LOCALAPPDATA%\crateside\source-index.json</c> current: every track's source audio on
/// disk, held in the working set or kept as a sample, found by video ID or song (#133).
/// </summary>
public sealed class SourceIndex(WorkingSet workingSet, string? path = null) : ISourceLocator
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
            "crateside",
            "source-index.json"
        );

    private readonly Lock _gate = new();
    private string? _written;
    private volatile IReadOnlyDictionary<string, string>? _known;

    public string? PathOf(string videoId) =>
        workingSet.TryGetSource(videoId, out var held) ? held
        : (_known ??= KnownFromFile()).GetValueOrDefault(videoId) is { } known && File.Exists(known)
            ? known
        : null;

    /// <summary>Every source on disk: the working set's downloads first, then the samples kept.</summary>
    public IReadOnlyList<SourceEntry> Entries(ProcessedTracks processed, KeyLedger ledger) =>
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

    /// <summary>Rewrites the file when what it lists has changed.</summary>
    public void Refresh(ProcessedTracks processed, KeyLedger ledger)
    {
        var entries = Entries(processed, ledger);

        _known = ByVideoId(entries);

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

    private static Dictionary<string, string> ByVideoId(IEnumerable<SourceEntry> entries) =>
        entries
            .GroupBy(entry => entry.VideoId)
            .ToDictionary(group => group.Key, group => group.First().Path);

    private Dictionary<string, string> KnownFromFile() =>
        SourceIndexJsonContext.Default.ListSourceEntry.ReadFileOrDefault(_path) is { } listed
            ? ByVideoId(listed)
            : [];
}

/// <summary>Where an upload's source audio is, if crateside holds it.</summary>
public interface ISourceLocator
{
    /// <summary>The working set's download, else the sample kept, as last indexed; null when neither is on disk.</summary>
    string? PathOf(string videoId);
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
