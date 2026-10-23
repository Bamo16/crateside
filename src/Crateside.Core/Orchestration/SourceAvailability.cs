using System.Net;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Asks YouTube's oEmbed which missing sources can still be downloaded, and remembers the ones
/// that cannot so they stop counting as missing.
/// </summary>
public sealed class SourceAvailability(
    IHttpClientFactory clients,
    UnavailableSources store,
    LedgerStore ledger,
    TimeProvider time
)
{
    public const string ClientName = "youtube-oembed";

    private const int Concurrency = 4;

    /// <summary>Splits <paramref name="missing"/> into what can be downloaded and what is known to be gone.</summary>
    public async Task<AvailabilityReport> SplitAsync(
        IReadOnlyList<string> missing,
        bool recheckAll = false,
        CancellationToken ct = default
    )
    {
        var now = time.GetUtcNow();
        var known = store.All();
        var toCheck = missing
            .Where(videoId =>
                known.GetValueOrDefault(videoId) is not { } entry
                || recheckAll
                || entry.Checked.AddMonths(1) <= now
            )
            .ToList();

        await Parallel.ForEachAsync(
            toCheck,
            new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct },
            async (videoId, token) =>
            {
                switch (await CheckAsync(videoId, token))
                {
                    case VideoStatus.Available:
                        store.Forget(videoId);

                        break;
                    case VideoStatus.Unavailable(var reason):
                        store.Mark(videoId, reason, now);

                        break;
                }
            }
        );

        var after = store.All();
        var gone = missing.Where(after.ContainsKey).ToList();
        var ledgerNow = gone.Count > 0 ? ledger.Read() : null;

        return new AvailabilityReport(
            [.. missing.Where(videoId => !after.ContainsKey(videoId))],
            [
                .. gone.Select(videoId => new UnavailableSource(
                    videoId,
                    ledgerNow?.SongFor(videoId) ?? videoId,
                    after[videoId].Reason,
                    after[videoId].FirstSeen
                )),
            ]
        );
    }

    /// <summary>Unknown on any status or failure that does not say the video is gone.</summary>
    public async Task<VideoStatus> CheckAsync(string videoId, CancellationToken ct = default)
    {
        try
        {
            using var response = await clients
                .CreateClient(ClientName)
                .GetAsync(
                    $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json",
                    ct
                );

            return StatusOf(response.StatusCode);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested
            )
        {
            return new VideoStatus.Unknown();
        }
    }

    public static VideoStatus StatusOf(HttpStatusCode status) =>
        status switch
        {
            HttpStatusCode.OK => new VideoStatus.Available(),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new VideoStatus.Unavailable(
                UnavailableReason.Private
            ),
            HttpStatusCode.NotFound => new VideoStatus.Unavailable(UnavailableReason.Removed),
            _ => new VideoStatus.Unknown(),
        };
}

public abstract record VideoStatus
{
    private VideoStatus() { }

    public sealed record Available : VideoStatus;

    public sealed record Unavailable(UnavailableReason Reason) : VideoStatus;

    public sealed record Unknown : VideoStatus;
}

public enum UnavailableReason
{
    /// <summary>oEmbed answered 404: taken down, or never existed.</summary>
    Removed,

    /// <summary>oEmbed answered 401 or 403: private, or embedding blocked.</summary>
    Private,
}

public sealed record UnavailableSource(
    string VideoId,
    string Song,
    UnavailableReason Reason,
    DateTimeOffset FirstSeen
);

public sealed record AvailabilityReport(
    IReadOnlyList<string> Downloadable,
    IReadOnlyList<UnavailableSource> Unavailable
);

/// <summary>Videos found unavailable, kept across restarts so they are not asked about again for a month.</summary>
public sealed class UnavailableSources
{
    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly Dictionary<string, UnavailableEntry> _entries;

    public UnavailableSources(CratesidePaths paths)
        : this(paths.UnavailableSourcesFile) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal UnavailableSources(string? path = null)
    {
        _path = path;
        _entries = new(
            path is not null
            && UnavailableSourcesJsonContext.Default.DictionaryStringUnavailableEntry.ReadFileOrDefault(
                path
            )
                is { } entries
                ? entries
                : [],
            StringComparer.Ordinal
        );
    }

    public IReadOnlyDictionary<string, UnavailableEntry> All()
    {
        lock (_lock)
        {
            return new Dictionary<string, UnavailableEntry>(_entries, StringComparer.Ordinal);
        }
    }

    /// <summary>Keeps the date it was first seen unavailable when already known.</summary>
    public void Mark(string videoId, UnavailableReason reason, DateTimeOffset checkedAt) =>
        Change(entries =>
            entries[videoId] = new UnavailableEntry(
                reason,
                entries.TryGetValue(videoId, out var known) ? known.FirstSeen : checkedAt,
                checkedAt
            )
        );

    public void Forget(string videoId) => Change(entries => entries.Remove(videoId));

    private void Change(Action<Dictionary<string, UnavailableEntry>> change)
    {
        lock (_lock)
        {
            change(_entries);

            if (_path is not null)
                UnavailableSourcesJsonContext.Default.DictionaryStringUnavailableEntry.WriteFile(
                    _path,
                    _entries
                );
        }
    }
}

public sealed record UnavailableEntry(
    UnavailableReason Reason,
    DateTimeOffset FirstSeen,
    DateTimeOffset Checked
);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(Dictionary<string, UnavailableEntry>))]
internal sealed partial class UnavailableSourcesJsonContext : JsonSerializerContext { }
