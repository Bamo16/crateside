using Crateside.Core.Library;
using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Moves a warped track's staged samples into the library with their sidecars, the same move the
/// maintainer makes in Live, so nothing else has to know it came from here (ADR 0109).
/// </summary>
public sealed class LibraryMove(
    CratesidePaths paths,
    IStagedFiles staged,
    ILibraryFiles library,
    WarpGuideKeeper guides
)
{
    public LibraryMoveOutcome Apply(IReadOnlyList<string> uploads)
    {
        var samples = Samples(uploads);

        if (paths.LibraryFolder is not { Length: > 0 } root)
            return new LibraryMoveOutcome.Refused("The library folder is not set.");

        if (samples is [])
            return new LibraryMoveOutcome.Refused("No sample of this track is in Staging.");

        if (samples.FirstOrDefault(sample => !sample.HasDefaultClip) is { } unwarped)
            return new LibraryMoveOutcome.Refused(
                $"{unwarped.FileName} has no saved warp yet, so the track is not ready to move."
            );

        var folder = Destination(uploads, root);
        var pairs = Pairs(samples, folder);

        if (
            pairs.FirstOrDefault(pair => File.Exists(pair.To) || File.Exists(pair.SidecarTo)) is
            { } taken
        )
            return new LibraryMoveOutcome.Refused(
                $"{Path.GetFileName(taken.To)} already exists in the library, so nothing was moved."
            );

        return Move(pairs);
    }

    /// <summary>Moves each file and its sidecar; the first one Windows refuses puts back what moved (#233).</summary>
    private LibraryMoveOutcome Move(List<Pair> pairs)
    {
        List<(string From, string To)> done = [];
        var steps = pairs.SelectMany(pair =>
            new[] { (pair.From, pair.To), (pair.SidecarFrom, pair.SidecarTo) }
        );

        foreach (var (from, to) in steps)
        {
            if (!File.Exists(from))
                continue;

            try
            {
                File.Move(from, to);
                done.Add((from, to));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return PutBack(done, Path.GetFileName(from), ex.Message);
            }
        }

        return new LibraryMoveOutcome.Moved(pairs.Count, guides.Keep());
    }

    private static LibraryMoveOutcome PutBack(
        List<(string From, string To)> done,
        string fileName,
        string error
    )
    {
        List<string> stranded = [];

        foreach (var (from, to) in Enumerable.Reverse(done))
        {
            try
            {
                File.Move(to, from);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stranded.Add($"{to} ({ex.Message})");
            }
        }

        return stranded is []
            ? new LibraryMoveOutcome.Held(fileName, error)
            : new LibraryMoveOutcome.Stranded(
                $"{fileName} stopped the move ({error}), and these could not go back to Staging: "
                    + string.Join("; ", stranded)
            );
    }

    private List<StagedFile> Samples(IReadOnlyList<string> uploads) =>
        [
            .. uploads
                .Distinct(StringComparer.Ordinal)
                .SelectMany(staged.Of)
                .Where(stem => !stem.IsWarpGuide)
                .DistinctBy(stem => stem.Path, StringComparer.OrdinalIgnoreCase),
        ];

    private string Destination(IReadOnlyList<string> uploads, string root) =>
        uploads
            .Distinct(StringComparer.Ordinal)
            .SelectMany(library.Of)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(directory => directory is { Length: > 0 })
        ?? root;

    private static List<Pair> Pairs(List<StagedFile> samples, string folder) =>
        [
            .. samples.Select(sample =>
            {
                var to = Path.Combine(folder, sample.FileName);

                return new Pair(
                    sample.Path,
                    to,
                    StemFileName.AnalysisFor(sample.Path),
                    StemFileName.AnalysisFor(to)
                );
            }),
        ];

    private sealed record Pair(string From, string To, string SidecarFrom, string SidecarTo);
}

public abstract record LibraryMoveOutcome
{
    private LibraryMoveOutcome() { }

    public sealed record Moved(int Samples, IReadOnlyList<GuideKeeping> Guides)
        : LibraryMoveOutcome;

    /// <summary>Windows refused to move a file, most likely open in a Live set, and nothing was left moved.</summary>
    public sealed record Held(string FileName, string Error) : LibraryMoveOutcome;

    public sealed record Refused(string Reason) : LibraryMoveOutcome;

    /// <summary>A move failed and some files could not be put back; the message says where they are.</summary>
    public sealed record Stranded(string Message) : LibraryMoveOutcome;
}
