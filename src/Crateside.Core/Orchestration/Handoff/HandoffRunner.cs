using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Samples;

namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Hands one track's finished stems to the human half, by moving them into
/// <c>Sample Staging</c>.
/// </summary>
public sealed class HandoffRunner(CratesidePaths paths, DeletionLog? deletions = null)
{
    private readonly DeletionLog _deletions = deletions ?? DeletionLog.None;

    /// <param name="superseded">
    /// Staged files of an upload this track has been swapped away from, cleared <em>before</em>
    /// the move to make room for same-named replacements (ADR 0014).
    /// </param>
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

    /// <summary>A file that will not go is left for the clash check to judge, not a failure here.</summary>
    private int Clear(IReadOnlyList<string> superseded)
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

                _deletions.Recycle(path, "superseded by the new upload");
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
