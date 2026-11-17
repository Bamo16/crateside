using Crateside.Core.Orchestration;
using Crateside.Core.Samples;

namespace Crateside.Core.Tuning;

/// <summary>One track's samples, and the audio its tuning is read from.</summary>
/// <param name="ReadFrom">The instrumental stem, else the source; null when neither is on disk.</param>
public sealed record TuningTrack(
    string TrackKey,
    string Song,
    IReadOnlyList<string> Samples,
    string? ReadFrom,
    TimeSpan? Duration
);

/// <summary>The tracks in Staging and the library, each with the samples cut from it (ADR 0101).</summary>
public sealed class TuningTracks(CratesidePaths paths, SourceIndex sources)
{
    private const string InstrumentalPreset = "Instrumental - ";

    public IReadOnlyList<TuningTrack> Find() =>
        [
            .. Samples()
                .Select(path => (Path: path, Tags: StemTagReader.Read(path)))
                .Where(sample => sample.Tags is { SourceTrackKey: not null })
                .GroupBy(sample => sample.Tags!.SourceTrackKey!, StringComparer.Ordinal)
                .Select(group => Track(group.Key, [.. group])),
        ];

    private TuningTrack Track(string trackKey, List<(string Path, StemTags? Tags)> samples)
    {
        var readFrom =
            samples
                .Select(sample => sample.Path)
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path =>
                    StemTagReader.PresetOf(path) is { } preset
                    && preset.StartsWith(InstrumentalPreset, StringComparison.Ordinal)
                )
            ?? sources.PathOf(trackKey);
        var named = samples.Select(sample => sample.Tags).OfType<StemTags>().First();

        return new TuningTrack(
            trackKey,
            named is { Artist: { Length: > 0 } artist, Title: { Length: > 0 } title }
                ? $"{artist} – {title}"
                : named.Title ?? trackKey,
            [.. samples.Select(sample => sample.Path)],
            readFrom,
            readFrom is null ? null : StemTagReader.Read(readFrom)?.Duration
        );
    }

    private IEnumerable<string> Samples() =>
        Audio(paths.StagingFolder, SearchOption.TopDirectoryOnly)
            .Concat(Audio(paths.LibraryFolder, SearchOption.AllDirectories));

    private static IEnumerable<string> Audio(string root, SearchOption depth) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", depth).Where(SampleFiles.IsAudio)
            : [];
}
