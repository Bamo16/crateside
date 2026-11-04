using System.Text.Json;
using Crateside.Core.Ledger;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What recording a verdict does besides the run itself, whoever decided it.</summary>
internal static class VerdictRecording
{
    /// <summary>
    /// A source swapped before its verdict is recorded under the stated upload, so the row's own
    /// ID is linked to that song, as the swap does for a judged track (#88).
    /// </summary>
    public static void LinkSwappedSource(LedgerStore store, WorkOrder order)
    {
        if (
            YouTubeUrl.TryGetVideoId(order.SourceUrl, out var source)
            && !source.Equals(order.VideoId, StringComparison.Ordinal)
        )
            store.Restate(source, $"https://music.youtube.com/watch?v={order.VideoId}");
    }

    /// <summary>What the lookup found, as the ledger keeps it; null when it found nothing to keep.</summary>
    public static string? Serialized(IReadOnlyList<SiteLookup> lookups) =>
        lookups is []
            ? null
            : JsonSerializer.Serialize(
                lookups,
                SiteLookupJsonContext.Default.IReadOnlyListSiteLookup
            );
}
