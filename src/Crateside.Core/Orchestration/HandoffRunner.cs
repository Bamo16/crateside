using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Hands one track's finished stems to the human half, by moving them into
/// <c>Sample Staging</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The stems root is transient and this is what makes it so.</strong> keytag's
/// <c>CONTEXT.md</c> defines <c>~/Music/Stems</c> as "a track fresh out of separation" and
/// <c>Sample Staging</c> as the holding area between the automated and the human halves. Without
/// this stage a separated track never crosses that line: it is not <strong>fully processed</strong>,
/// so it never leaves the queue, its working set is never discarded, and the Queue view shows it
/// as awaiting stems it already has.
/// </para>
/// <para>
/// <strong>Only <see cref="StemDestination.KeyIdentified"/> is handed off.</strong> A LOW or
/// WITHHELD verdict produces stems the user cannot key-tag, and staging is where tagging happens
/// — so they stay in <c>Unresolved Key</c> rather than filling the holding area with work that
/// cannot be finished. The consequence is deliberate and worth stating: nothing currently moves
/// them on, so they are parked until a verdict changes.
/// </para>
/// </remarks>
public sealed class HandoffRunner(CratesidePaths paths)
{
    /// <summary>Moves one track's stems into staging.</summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing is overwritten, and a collision fails the whole track before anything
    /// moves.</strong> A name already in staging means either the user is part-way through the
    /// human steps on it — warp markers live in a <c>.asd</c> beside the file, and tags in
    /// Ableton's own store — or a re-separation is arriving on top of finished work. Neither is
    /// crateside's call. Checking every destination first is what keeps a refusal from leaving
    /// half a track behind.
    /// </para>
    /// <para>
    /// Move rather than copy, because the stems root is defined as transient and two copies of a
    /// stem is exactly the ambiguity <strong>fully processed</strong> exists to resolve.
    /// </para>
    /// </remarks>
    /// <param name="name">
    /// What the user calls this track, or null if they have not said. This is the only point a
    /// rename given before there were files reaches them, which is why every rename lands here
    /// rather than on the source audio — see <see cref="TrackRename"/>.
    /// </param>
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
            // Staging lives on an external drive that Ableton may have files open on, so this
            // is an ordinary, retryable failure rather than anything wrong with the track.
            return HandoffResult.Failed(e.Message);
        }
    }

    /// <summary>Where one stem lands: under the user's name for the track, or under its own.</summary>
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

    /// <summary>How many stems reached staging.</summary>
    public int Moved { get; init; }

    public string? Error { get; init; }

    public static HandoffResult Succeeded(int moved) => new() { IsSuccess = true, Moved = moved };

    public static HandoffResult Failed(string error) => new() { IsSuccess = false, Error = error };
}

/// <summary>
/// Decides which track's stems to hand off next, by reading the stems root rather than the queue.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reality, not a claim.</strong> A file sitting in the key-identified stems root is a
/// stem that has been separated and not yet handed off — which is the entry condition and the
/// exit condition at once, so a run interrupted half way is simply picked up again. That is the
/// same idiom the verdict stage uses against the ledger.
/// </para>
/// <para>
/// It needs no queue, and deliberately does not read one: moving files crateside already made is
/// no business of a YouTube credential, and a lapsed one must not stop the pipeline finishing
/// what it started.
/// </para>
/// </remarks>
public sealed class HandoffSelector(
    JobStore jobs,
    CratesidePaths paths,
    int maxStrikes = HandoffSelector.DefaultMaxStrikes
)
{
    public const int DefaultMaxStrikes = 3;

    /// <summary>The next track whose stems are waiting to be handed over.</summary>
    /// <remarks>
    /// <paramref name="stems"/> is grouped by <strong>source video ID</strong>, read from each
    /// file's own provenance, so a file crateside did not make is left alone rather than moved
    /// somewhere it does not belong.
    /// </remarks>
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

    /// <remarks>
    /// <c>retrySucceeded</c> is true: a successful handoff leaves nothing behind, so stems back
    /// in this folder are new ones from a re-separation, and they want moving just as much.
    /// </remarks>
    private bool IsEligible(string videoId) =>
        StageEligibility.Allows(jobs, videoId, JobStage.Handoff, maxStrikes, retrySucceeded: true);
}
