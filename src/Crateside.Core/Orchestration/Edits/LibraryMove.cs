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

        if (FirstHeld(pairs) is { } held)
            return new LibraryMoveOutcome.Held(held);

        return Move(pairs);
    }

    private LibraryMoveOutcome Move(List<Pair> pairs)
    {
        List<(string From, string To)> done = [];

        foreach (var pair in pairs)
        {
            try
            {
                File.Move(pair.From, pair.To);
                done.Add((pair.From, pair.To));

                if (File.Exists(pair.SidecarFrom))
                {
                    File.Move(pair.SidecarFrom, pair.SidecarTo);
                    done.Add((pair.SidecarFrom, pair.SidecarTo));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return PutBack(done, Path.GetFileName(pair.From), ex.Message);
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
            ? new LibraryMoveOutcome.Held(fileName)
            : new LibraryMoveOutcome.Stranded(
                $"{fileName} stopped the move ({error}), and these could not go back to Staging: "
                    + string.Join("; ", stranded)
            );
    }

    private static string? FirstHeld(List<Pair> pairs)
    {
        foreach (var path in pairs.SelectMany(pair => new[] { pair.From, pair.SidecarFrom }))
        {
            if (!File.Exists(path))
                continue;

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Path.GetFileName(path);
            }
        }

        return null;
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

    /// <summary>A file is open elsewhere, most likely in a Live set, and nothing was left moved.</summary>
    public sealed record Held(string FileName) : LibraryMoveOutcome;

    public sealed record Refused(string Reason) : LibraryMoveOutcome;

    /// <summary>A move failed and some files could not be put back; the message says where they are.</summary>
    public sealed record Stranded(string Message) : LibraryMoveOutcome;
}
