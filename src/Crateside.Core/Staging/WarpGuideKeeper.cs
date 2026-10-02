using Crateside.Core.Samples;

namespace Crateside.Core.Staging;

/// <summary>
/// Moves a warp guide and its sidecar out of staging, out of Ableton's sight, once the rest of
/// its track is in Mashup Samples (#87, ADR 0046).
/// </summary>
public sealed class WarpGuideKeeper(CratesidePaths paths, TimeProvider time)
{
    public IReadOnlyList<GuideKeeping> Keep()
    {
        if (!Directory.Exists(paths.SampleStaging))
            return [];

        var staged = Cuts(paths.SampleStaging);
        var filed = Cuts(paths.MashupSamples)
            .Select(cut => cut.VideoId)
            .ToHashSet(StringComparer.Ordinal);
        List<GuideKeeping> kept = [];

        foreach (var guide in staged.Where(cut => cut.IsGuide))
        {
            if (
                !filed.Contains(guide.VideoId)
                || staged.Any(cut => !cut.IsGuide && cut.VideoId == guide.VideoId)
            )
                continue;

            kept.Add(Move(guide.Path));
        }

        return kept;
    }

    private GuideKeeping Move(string guide)
    {
        Directory.CreateDirectory(paths.KeptWarpGuides);
        var destination = FreeName(Path.Combine(paths.KeptWarpGuides, Path.GetFileName(guide)));
        var sidecar = StemFileName.AnalysisFor(guide);

        try
        {
            File.Move(guide, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new GuideKeeping.Failed(guide, ex.Message);
        }

        try
        {
            if (File.Exists(sidecar))
                File.Move(sidecar, StemFileName.AnalysisFor(destination));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept apart, the sidecar would describe a file that is no longer beside it.
            File.Move(destination, guide);

            return new GuideKeeping.Failed(guide, ex.Message);
        }

        return new GuideKeeping.Kept(guide, destination);
    }

    /// <summary>The name itself, or one dated today, so an earlier snapshot is never overwritten.</summary>
    private string FreeName(string path)
    {
        if (IsFree(path))
            return path;

        var date = time.GetLocalNow().ToString("yyyy-MM-dd");
        var stem = Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path)
        );
        var extension = Path.GetExtension(path);
        var candidate = $"{stem} ({date}){extension}";

        for (var i = 2; !IsFree(candidate); i++)
            candidate = $"{stem} ({date}, {i}){extension}";

        return candidate;
    }

    private static bool IsFree(string path) =>
        !File.Exists(path) && !File.Exists(StemFileName.AnalysisFor(path));

    private static List<Cut> Cuts(string root) =>
        Directory.Exists(root)
            ?
            [
                .. Directory
                    .EnumerateFiles(root)
                    .Where(SampleFiles.IsAudio)
                    .Select(path => (Path: path, StemTagReader.Read(path)?.Provenance))
                    .Where(file => file.Provenance is { IsAdopted: false, SourceVideoId: not null })
                    .Select(file => new Cut(
                        file.Path,
                        file.Provenance!.SourceVideoId!,
                        StagedFile.IsWarpGuidePreset(
                            file.Provenance.Preset ?? StemFileName.PresetOf(file.Path)
                        )
                    )),
            ]
            : [];

    private sealed record Cut(string Path, string VideoId, bool IsGuide);
}

public abstract record GuideKeeping
{
    private GuideKeeping() { }

    public sealed record Kept(string From, string To) : GuideKeeping;

    /// <summary>The guide is locked, most likely open in Ableton, and is tried again next time.</summary>
    public sealed record Failed(string Guide, string Error) : GuideKeeping;
}
