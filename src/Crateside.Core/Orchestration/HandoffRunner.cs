using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Hands one track's finished stems to the human half, by moving them into
/// <c>Sample Staging</c>.
/// </summary>
public sealed class HandoffRunner(CratesidePaths paths)
{
    /// <param name="superseded">
    /// Staged files of an upload this track has been swapped away from, cleared to make room.
    /// </param>
    /// <remarks>
    /// The superseded files go <em>before</em> the move: a replacement usually carries the same
    /// filename, and the stems are still in the separator's output until the move runs (ADR 0014).
    /// </remarks>
    public HandoffResult Run(
        IReadOnlyList<string> stems,
        string? name = null,
        IReadOnlyList<string>? superseded = null
    )
    {
        if (stems.Count is 0)
            return HandoffResult.Failed("no stems to hand off");

        var cleared = Clear(superseded ?? []);

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

            return HandoffResult.Succeeded(moves.Count, cleared);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Staging lives on an external drive Ableton may hold files open on.
            return HandoffResult.Failed(e.Message);
        }
    }

    /// <remarks>
    /// A file that will not go is left alone rather than failing the handoff: the clash check
    /// below is what decides whether that mattered, and it says so in the words the user needs.
    /// </remarks>
    private static int Clear(IReadOnlyList<string> superseded)
    {
        var cleared = 0;

        foreach (var path in superseded)
        {
            try
            {
                // Removal is silent about a path that is not there, which would make the
                // count say files went that never existed.
                if (!File.Exists(path))
                    continue;

                RecycleBin.Remove(path);
                cleared++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return cleared;
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

    /// <summary>Superseded staged files removed to make room for these (ADR 0014).</summary>
    public int Cleared { get; init; }

    public string? Error { get; init; }

    public static HandoffResult Succeeded(int moved, int cleared = 0) =>
        new()
        {
            IsSuccess = true,
            Moved = moved,
            Cleared = cleared,
        };

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
        SeparationOwesNothingMore(videoId)
        && StageEligibility.Allows(
            jobs,
            videoId,
            JobStage.Handoff,
            maxStrikes,
            retrySucceeded: true
        );

    /// <summary>
    /// Whether what is on disk for this track is all there is going to be.
    /// </summary>
    /// <remarks>
    /// Only <see cref="JobStatus.Failed"/> blocks, and deliberately: a copied source has no
    /// separation job at all, and a recorded <see cref="JobStatus.Running"/> may be an adopted run
    /// nobody releases (ADR 0025).
    /// </remarks>
    private bool SeparationOwesNothingMore(string videoId) =>
        !jobs.TryGet(videoId, JobStage.Separation, out var separation)
        || separation is not { Status: JobStatus.Failed };
}
