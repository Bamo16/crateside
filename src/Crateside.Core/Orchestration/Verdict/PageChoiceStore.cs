using System.Text.Json.Serialization;
using Crateside.Core.Watching;

namespace Crateside.Core.Orchestration.Verdict;

public enum PageChoiceKind
{
    /// <summary>A page the lookup found, said not to be this song.</summary>
    Rejected,

    /// <summary>A title-only candidate, said to be this song.</summary>
    Promoted,

    /// <summary>A page the person gave by address.</summary>
    Added,
}

/// <summary>One thing the person said about a page for a track (ADR 0078).</summary>
public sealed record PageChoice
{
    public required string Url { get; init; }
    public required PageChoiceKind Kind { get; init; }

    /// <summary>The page as the person saw it, so it counts or stays visible without another fetch.</summary>
    public required FetchedPage Page { get; init; }

    public required DateTimeOffset At { get; init; }
    public DateTimeOffset? UndoneAt { get; init; }

    [JsonIgnore]
    public bool IsActive => UndoneAt is null;
}

/// <summary>The pages the person has rejected, counted or added, kept in <c>%APPDATA%\crateside\page-choices.json</c> by track key (ADR 0078).</summary>
public sealed class PageChoiceStore
{
    private readonly string _path;
    private readonly Lock _gate = new();

    private Dictionary<string, List<PageChoice>>? _cached;

    /// <summary>Raised after a choice is made or undone, with the track it was made on.</summary>
    public event Action<string>? Changed;

    public PageChoiceStore(CratesidePaths paths, FileChanges changes)
        : this(paths.PageChoicesFile) => Changed += _ => changes.NotifyNow();

    internal PageChoiceStore(string path) => _path = path;

    /// <summary>The choices in force for the track.</summary>
    public IReadOnlyList<PageChoice> ActiveFor(string trackKey)
    {
        lock (_gate)
            return
            [
                .. Records().GetValueOrDefault(trackKey)?.Where(choice => choice.IsActive) ?? [],
            ];
    }

    /// <summary>The latest time a choice for the track was made or undone, or null for a track never corrected.</summary>
    public DateTimeOffset? LastChange(string trackKey)
    {
        lock (_gate)
            return (Records().GetValueOrDefault(trackKey) ?? [])
                .SelectMany(choice => new[] { choice.At, choice.UndoneAt ?? choice.At })
                .Order()
                .Select(time => (DateTimeOffset?)time)
                .LastOrDefault();
    }

    /// <summary>Records the choice, ending any choice still in force for the same page.</summary>
    public void Choose(string trackKey, PageChoice choice)
    {
        lock (_gate)
        {
            var records = Records();
            var held = records.GetValueOrDefault(trackKey) ?? [];

            records[trackKey] =
            [
                .. held.Select(existing =>
                    existing.IsActive && SameUrl(existing.Url, choice.Url)
                        ? existing with
                        {
                            UndoneAt = choice.At,
                        }
                        : existing
                ),
                choice,
            ];

            Write(records);
        }

        Changed?.Invoke(trackKey);
    }

    /// <summary>Ends the choice in force for the page; false when there was none.</summary>
    public bool Undo(string trackKey, string url, DateTimeOffset at)
    {
        lock (_gate)
        {
            var records = Records();

            if (
                records.GetValueOrDefault(trackKey) is not { } held
                || !held.Any(choice => choice.IsActive && SameUrl(choice.Url, url))
            )
                return false;

            records[trackKey] =
            [
                .. held.Select(choice =>
                    choice.IsActive && SameUrl(choice.Url, url)
                        ? choice with
                        {
                            UndoneAt = at,
                        }
                        : choice
                ),
            ];

            Write(records);
        }

        Changed?.Invoke(trackKey);

        return true;
    }

    internal static bool SameUrl(string a, string b) =>
        a.TrimEnd('/').Equals(b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private Dictionary<string, List<PageChoice>> Records() => _cached ??= TryReadFile();

    private void Write(Dictionary<string, List<PageChoice>> records)
    {
        try
        {
            PageChoiceJsonContext.Default.ListPageChoicesRecord.WriteFile(
                _path,
                [
                    .. records
                        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                        .Select(entry => new PageChoicesRecord(entry.Key, entry.Value)),
                ]
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private Dictionary<string, List<PageChoice>> TryReadFile() =>
        (PageChoiceJsonContext.Default.ListPageChoicesRecord.ReadFileOrDefault(_path) ?? [])
            .Where(record => record.Choices.Count > 0)
            .ToDictionary(record => record.TrackKey, record => record.Choices);
}

/// <summary>One track's choices, as the file keeps them.</summary>
public sealed record PageChoicesRecord(string TrackKey, List<PageChoice> Choices);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(List<PageChoicesRecord>))]
internal sealed partial class PageChoiceJsonContext : JsonSerializerContext { }
