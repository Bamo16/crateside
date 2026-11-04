using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.WorkLists;

/// <summary>
/// Which source video IDs are fully processed: a ledger verdict and a stem carrying that
/// video ID on disk. A WITHHELD verdict counts: re-queueing would give the same answer.
/// </summary>
public sealed class ProcessedTracks
{
    private ProcessedTracks(
        Dictionary<string, List<StemOnDisk>> stemsByTrackKey,
        IReadOnlySet<string> fullyProcessed,
        IReadOnlySet<string> verdictWithoutStems
    )
    {
        StemsByTrackKey = stemsByTrackKey.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<StemOnDisk>)entry.Value,
            StringComparer.Ordinal
        );
        FullyProcessed = fullyProcessed;
        VerdictWithoutStems = verdictWithoutStems;
    }

    /// <summary>Every file the scan found, by the video ID it came from.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>> StemsByTrackKey { get; }

    /// <summary>Video IDs that satisfy both halves of the test.</summary>
    public IReadOnlySet<string> FullyProcessed { get; }

    /// <summary>
    /// Video IDs with a verdict but no stem anywhere on disk — a separation that never
    /// finished, or stems the user deleted. These deliberately stay queued.
    /// </summary>
    public IReadOnlySet<string> VerdictWithoutStems { get; }

    /// <summary>Where the stems for a video ID live. Empty when none are on disk.</summary>
    public IReadOnlyList<StemOnDisk> StemsFor(string trackKey) =>
        StemsByTrackKey.GetValueOrDefault(trackKey) ?? [];

    public bool IsFullyProcessed(string trackKey) => FullyProcessed.Contains(trackKey);

    /// <summary>
    /// When a file arrived, or null where the filesystem will not say — which Windows reports as
    /// 1601-01-01.
    /// </summary>
    private static DateTimeOffset? CreationTimeOf(FileInfo file)
    {
        try
        {
            var created = file.CreationTimeUtc;

            return created.Year > 1601 ? new DateTimeOffset(created, TimeSpan.Zero) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scans every stem root for provenance and pairs it against the ledger.
    /// </summary>
    /// <param name="stemRoots">
    /// Every folder a finished stem might live in — staging <em>and</em> the destination it
    /// gets moved to. A track whose stems have left staging is still processed.
    /// </param>
    public static ProcessedTracks Scan(KeyLedger ledger, IEnumerable<string> stemRoots)
    {
        var stemsByTrackKey = new Dictionary<string, List<StemOnDisk>>(StringComparer.Ordinal);

        foreach (var root in stemRoots.Where(Directory.Exists))
        {
            foreach (
                var file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories)
            )
            {
                if (!SampleFiles.IsAudio(file.Name))
                    continue;

                if (StemTagReader.Read(file)?.Provenance is not { SourceTrackKey: { } trackKey } p)
                    continue;

                if (!stemsByTrackKey.TryGetValue(trackKey, out var found))
                    stemsByTrackKey[trackKey] = found = [];

                found.Add(
                    new StemOnDisk
                    {
                        Path = file.FullName,
                        Root = root,
                        Preset = p.Preset,
                        IsCut = p.Model is not null,
                        Label = p.Label,
                        IsAdopted = p.IsAdopted,
                        IsPremium = p.IsPremium,
                        SourceTrackKey = trackKey,
                        CreatedAt = CreationTimeOf(file),
                    }
                );
            }
        }

        var withVerdict = ledger
            .Runs.Select(run => run.SourceTrackKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return new ProcessedTracks(
            stemsByTrackKey,
            withVerdict.Where(stemsByTrackKey.ContainsKey).ToHashSet(StringComparer.Ordinal),
            withVerdict
                .Where(id => !stemsByTrackKey.ContainsKey(id))
                .ToHashSet(StringComparer.Ordinal)
        );
    }
}

/// <summary>One file carrying a track's provenance, and where the scan found it.</summary>
public sealed record StemOnDisk
{
    public required string Path { get; init; }

    /// <summary>
    /// The stem root it was found under, which is the completion signal (ADR 0002).
    /// </summary>
    public required string Root { get; init; }

    /// <summary>
    /// The StemForge preset — <c>Vocal - Full</c>, <c>Drums</c>. Null for a verbatim source
    /// download, which is how a kept source reads.
    /// </summary>
    public string? Preset { get; init; }

    /// <summary>A verbatim copy of the download, which is how a kept source reads.</summary>
    public bool IsVerbatimSource => Preset is null && !IsCut && !IsAdopted;

    /// <summary>An early stem names the models that cut it where a later one names a preset.</summary>
    public bool IsCut { get; init; }

    /// <summary>What the user calls an adopted sample. Null for anything crateside made.</summary>
    public string? Label { get; init; }

    /// <summary>crateside took this file into the track rather than making it (ADR 0022).</summary>
    public bool IsAdopted { get; init; }

    /// <summary>Whether it was downloaded as premium audio. Null when the file does not say.</summary>
    public bool? IsPremium { get; init; }

    /// <summary>
    /// The upload this file was cut or downloaded from — what tells a separation source from an
    /// added one (ADR 0019).
    /// </summary>
    [JsonPropertyName("sourceVideoId")]
    public string? SourceTrackKey { get; init; }

    /// <summary>
    /// When the file appeared in <see cref="Root"/>, which is when it arrived rather than when it
    /// was made (ADR 0008).
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// A staged file as the scanner would have found it, so one classifier answers for both
    /// readers.
    /// </summary>
    public static StemOnDisk Of(StagedFile stem, string? sourceTrackKey = null) =>
        new()
        {
            Path = stem.Path,
            Root = System.IO.Path.GetDirectoryName(stem.Path) ?? string.Empty,
            Preset = stem.Preset,
            IsPremium = stem.IsPremium,
            SourceTrackKey = sourceTrackKey,
        };
}
