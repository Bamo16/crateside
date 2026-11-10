namespace Crateside.Core.Ledger;

/// <summary>
/// What a run holds about one section of the track: the windows that settled it, and the sources
/// that said something about it. The judge's reasoning is one paragraph for the whole track and is
/// not split by section (#180).
/// </summary>
/// <param name="Windows">The windows the run placed as this section, with what the detectors read in each.</param>
/// <param name="Naming">The sources that named this section among those they covered.</param>
/// <param name="WholeSong">The sources that covered the whole song, which this section is part of.</param>
public sealed record SectionEvidence(
    Section Section,
    IReadOnlyList<WindowReading> Windows,
    IReadOnlyList<EvidenceSource> Naming,
    IReadOnlyList<EvidenceSource> WholeSong
)
{
    /// <summary>The section of <paramref name="run"/> called <paramref name="label"/>, or null when it has none.</summary>
    public static SectionEvidence? Of(RunEvidence run, string label) =>
        run.Sections.FirstOrDefault(section =>
            section.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
        )
            is { } found
            ? new SectionEvidence(
                found,
                [
                    .. run.Readings.Where(reading =>
                        reading.Section?.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
                            is true
                    ),
                ],
                [.. run.Sources.Where(source => Names(source, found))],
                [.. run.Sources.Where(source => source.Sections is not { Count: > 0 })]
            )
            : null;

    private static bool Names(EvidenceSource source, Section section) =>
        source.Sections?.Contains(section.Label, StringComparer.OrdinalIgnoreCase) is true;
}
