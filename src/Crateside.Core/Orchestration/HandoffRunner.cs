using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Hands one track's finished stems to the human half, by moving them into
/// <c>Sample Staging</c>.
/// </summary>
public sealed class HandoffRunner(CratesidePaths paths)
{
    /// <param name="name">What the user calls this track, or null if they have not said.</param>
    public HandoffResult Run(IReadOnlyList<string> stems, string? name = null)
    {
        if (stems.Count is 0)
            return HandoffResult.Failed("no stems to hand off");

        var moves = stems.Select(s => (From: s, To: Destination(s, name))).ToList();

        if (moves.FirstOrDefault(m => File.Exists(m.To)) is { From: not null } clash)
            return HandoffResult.Failed(
                $"{Path.GetFileName(clash.To)} is already in staging; nothing was moved"
            );

        try
        {
            Directory.CreateDirectory(paths.SampleStaging);

            foreach (var (from, to) in moves)
                File.Move(from, to);

            return HandoffResult.Succeeded(moves.Count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Staging lives on an external drive Ableton may hold files open on.
            return HandoffResult.Failed(e.Message);
        }
    }

    private string Destination(string stem, string? name) =>
        Path.Combine(
            paths.SampleStaging,
            Path.GetFileName(StemFileName.IsUsable(name) ? StemFileName.Renamed(stem, name) : stem)
        );
}

/// <summary>How one handoff turned out.</summary>
public sealed record HandoffResult
{
    public required bool IsSuccess { get; init; }

    public int Moved { get; init; }

    public string? Error { get; init; }

    public static HandoffResult Succeeded(int moved) => new() { IsSuccess = true, Moved = moved };

    public static HandoffResult Failed(string error) => new() { IsSuccess = false, Error = error };
}

/// <summary>
/// Decides which track's stems to hand off next, by reading the stems root rather than the queue.
/// </summary>
public sealed class HandoffSelector(
    JobStore jobs,
    CratesidePaths paths,
    int maxStrikes = HandoffSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 3;

    public bool TryPickNext(out string videoId, out IReadOnlyList<string> stems)
    {
        videoId = null!;
        stems = [];

        if (!Directory.Exists(paths.StemsKeyIdentified))
            return false;

        var byVideoId = Directory
            .EnumerateFiles(paths.StemsKeyIdentified)
            .Select(StemTagReader.Read)
            .OfType<StemTags>()
            .Where(t => t.SourceVideoId is { Length: > 0 })
            .GroupBy(t => t.SourceVideoId!, StringComparer.Ordinal)
            .FirstOrDefault(g => IsEligible(g.Key));

        if (byVideoId is null)
            return false;

        videoId = byVideoId.Key;
        stems = [.. byVideoId.Select(t => t.Path)];

        return true;
    }

    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Handoff, maxStrikes, retrySucceeded: true);
}
