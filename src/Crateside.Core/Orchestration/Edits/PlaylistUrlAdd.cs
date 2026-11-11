using Crateside.Core.Tracks;
using Humanizer;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>Adds every video of a playlist URL as if each had been pasted on its own, with no YouTube API key (#162).</summary>
public sealed class PlaylistUrlAdd(PlaylistExpander expander, TrackAdd adding)
{
    /// <param name="progress">Called with how many entries are done and how many there are.</param>
    public async Task<PlaylistAddOutcome> AddAsync(
        string url,
        IReadOnlyList<TrackRowFacts> known,
        Action<int, int>? progress = null,
        CancellationToken ct = default
    )
    {
        var listing = await expander.ListAsync(url, ct);

        if (listing is not PlaylistListing.Listed(var entries))
            return new PlaylistAddOutcome
            {
                Problem = listing is PlaylistListing.Unreadable(var why)
                    ? why
                    : "the playlist could not be read",
            };

        List<PlaylistEntry> added = [];
        List<SkippedEntry> skipped = [];
        var alreadyHeld = 0;

        foreach (var (entry, i) in entries.Select((entry, i) => (entry, i)))
        {
            progress?.Invoke(i, entries.Count);

            if (entry.IsUnavailable)
            {
                skipped.Add(new SkippedEntry(entry, "private or removed"));
            }
            else if (adding.PlanFor(entry.VideoId, known).Refusal is not null)
            {
                alreadyHeld++;
            }
            else
            {
                var outcome = await adding.ApplyAsync(entry.VideoId, known, ct: ct);

                if (outcome.Succeeded)
                    added.Add(entry with { Title = outcome.Title ?? entry.Title });
                else
                    skipped.Add(
                        new SkippedEntry(
                            entry,
                            outcome.Problem
                                ?? $"offers no premium format, best is {outcome.Shortfall?.Quality}"
                        )
                    );
            }
        }

        progress?.Invoke(entries.Count, entries.Count);

        return new PlaylistAddOutcome
        {
            Added = added,
            AlreadyHeld = alreadyHeld,
            Skipped = skipped,
        };
    }
}

public sealed record SkippedEntry(PlaylistEntry Entry, string Why);

public sealed record PlaylistAddOutcome
{
    /// <summary>Why nothing was read from the playlist, when that is so.</summary>
    public string? Problem { get; init; }

    public IReadOnlyList<PlaylistEntry> Added { get; init; } = [];

    /// <summary>Videos crateside already had, which are left as they are.</summary>
    public int AlreadyHeld { get; init; }

    public IReadOnlyList<SkippedEntry> Skipped { get; init; } = [];

    public string Summary =>
        Problem
        ?? string.Join(
            ". ",
            new[]
            {
                $"Added {"track".ToQuantity(Added.Count)}",
                AlreadyHeld > 0 ? $"{AlreadyHeld} already held" : null,
                Skipped.Count > 0
                    ? $"{Skipped.Count} left out: {string.Join("; ", Skipped.Take(5).Select(skipped => $"{skipped.Entry.Title} ({skipped.Why})"))}{(Skipped.Count > 5 ? "; …" : null)}"
                    : null,
            }.OfType<string>()
        );
}
