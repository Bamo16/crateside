using System.Text.Json;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Pairing;

namespace Crateside.Core.Ledger;

/// <summary>Which sections in a center other than the primary something independent of the judge places (ADR 0095).</summary>
internal static class SectionSupports
{
    private const double Slack = 1;
    private const int BackersNeeded = 2;

    private static readonly string[] Detectors = ["ffmpeg keydetect", "madmom", "Essentia edma"];

    /// <summary>The run's sections, each one outside the primary center marked with what supports it.</summary>
    public static IReadOnlyList<Section> Classify(RunRecord run) =>
        run is { Center: { } center, Sections: { Count: > 0 } sections }
        && sections.Any(section => MusicKey.Of(section.Center) != MusicKey.Of(center))
            ? Classify(run, MusicKey.Of(center), Readings(run))
            : run.Sections;

    /// <summary>Whether two of keydetect, madmom and Essentia name the key in these readings.</summary>
    public static bool Backed(IReadOnlyList<AlgorithmResult> algorithms, MusicKey key) =>
        Detectors.Count(name =>
            algorithms.FirstOrDefault(result => result.Name == name) is { Key: { } read }
            && RecordTimeCheck.KeyOf(read) == key
        ) >= BackersNeeded;

    private static List<Section> Classify(
        RunRecord run,
        MusicKey primary,
        IReadOnlyList<WindowReading> readings
    ) =>
        [
            .. run.Sections.Select(section =>
                MusicKey.Of(section.Center) is var key && key == primary
                    ? section
                    : section with
                    {
                        Support = SupportOf(run, section, key, primary, readings),
                    }
            ),
        ];

    private static SectionSupport SupportOf(
        RunRecord run,
        Section section,
        MusicKey key,
        MusicKey primary,
        IReadOnlyList<WindowReading> readings
    )
    {
        var inside = readings.Where(reading => IsInside(run, reading, section)).ToList();

        if (run.Sources.Any(source => StatesIt(source, section, key)))
            return SectionSupport.HumanSource;

        if (inside.Any(reading => reading.Window.AskedBy is WindowAsker.User))
            return SectionSupport.Asked;

        return
            inside.Any(reading => Backed(reading.Algorithms, key))
            && readings.Any(reading =>
                !IsInside(run, reading, section) && Backed(reading.Algorithms, primary)
            )
            ? SectionSupport.Detectors
            : SectionSupport.Hypothesis;
    }

    private static bool StatesIt(RunSource source, Section section, MusicKey key) =>
        source is { Kind: SourceKind.Authoritative or SourceKind.Corroborating }
        && RecordTimeCheck.KeyOf(source.StatesKey) == key
        && source.Sections?.Contains(section.Label, StringComparer.OrdinalIgnoreCase) is true;

    // A window belongs to the section it is named for, or lies between the section's start and the next one's.
    private static bool IsInside(RunRecord run, WindowReading reading, Section section) =>
        reading.Section == section
        || (
            section.Start is { } start
            && reading.Window.Start >= start - Slack
            && reading.Window.End <= EndOf(run, section) + Slack
        );

    private static double EndOf(RunRecord run, Section section) =>
        run.Sections.Where(other => other.Start > section.Start).Select(other => other.Start).Min()
        ?? double.MaxValue;

    public static IReadOnlyList<WindowReading> Readings(RunRecord run) =>
        run is { Windows.Count: > 0, Evidence: { } json } && Machine(json) is { } machine
            ? WindowReading.Of(run, machine.Windows ?? [])
            : WindowReading.Of(run, []);

    private static MachineDto? Machine(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, RunEvidenceJsonContext.Default.MachineDto);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
