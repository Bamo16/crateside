using System.Diagnostics;
using System.Text.Json;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Pairing;

namespace Crateside.Core.Ledger;

/// <summary>Which sections in a center other than the primary something independent of the judge places (ADR 0095, ADR 0100).</summary>
internal static class SectionSupports
{
    private const double Slack = 1;
    private const int BackersNeeded = 2;

    /// <summary>How far before a section's start a lift may begin and still be its own: report 15's TAPI・TAPI lift starts 7 s early.</summary>
    private const double LiftLead = 8;

    private static readonly string[] Detectors = ["ffmpeg keydetect", "madmom", "Essentia edma"];

    /// <summary>The run's sections, each one outside the primary center marked with what supports it.</summary>
    public static IReadOnlyList<Section> Classify(RunRecord run) =>
        run is { Center: { } center, Sections: { Count: > 0 } sections }
        && sections.Any(section => MusicKey.Of(section.Center) != MusicKey.Of(center))
            ? Classify(run, MusicKey.Of(center), Machine(run))
            : run.Sections;

    /// <summary>Whether two of keydetect, madmom and Essentia name the key in these readings.</summary>
    public static bool Backed(IReadOnlyList<AlgorithmResult> algorithms, MusicKey key) =>
        Detectors.Count(name =>
            algorithms.FirstOrDefault(result => result.Name == name) is { Key: { } read }
            && RecordTimeCheck.KeyOf(read) == key
        ) >= BackersNeeded;

    private static List<Section> Classify(RunRecord run, MusicKey primary, MachineDto? machine)
    {
        var readings = WindowReading.Of(run, Windows(run, machine));
        IReadOnlyList<MachineDto.KeyLiftDto> lifts = machine?.KeyLifts ?? [];

        return
        [
            .. run.Sections.Select(section =>
                MusicKey.Of(section.Center) is var key && key == primary
                    ? section
                    : section with
                    {
                        Support = SupportOf(run, section, key, readings, lifts),
                    }
            ),
        ];
    }

    private static SectionSupport SupportOf(
        RunRecord run,
        Section section,
        MusicKey key,
        IReadOnlyList<WindowReading> readings,
        IReadOnlyList<MachineDto.KeyLiftDto> lifts
    )
    {
        if (run.Sources.Any(source => StatesIt(source, section, key)))
            return SectionSupport.HumanSource;

        if (
            readings.Any(reading =>
                IsInside(run, reading, section) && reading.Window.AskedBy is WindowAsker.User
            )
        )
            return SectionSupport.Asked;

        return Lifted(run, section, lifts) ? SectionSupport.KeyLift : SectionSupport.Hypothesis;
    }

    // keydetect's transposed repeat, with the two-key fit agreeing, begins where the section does and moves
    // by the step from another center to this one, by tonic or by collection (ADR 0100).
    private static bool Lifted(
        RunRecord run,
        Section section,
        IReadOnlyList<MachineDto.KeyLiftDto> lifts
    )
    {
        if (section.Start is not { } start || run.Center is not { } primary)
            return false;

        var shifts = run
            .Sections.Select(other => other.Center)
            .Append(primary)
            .Where(other => MusicKey.Of(other) != MusicKey.Of(section.Center))
            .SelectMany(other =>
                new[]
                {
                    Step(other.Chromatic, section.Center.Chromatic),
                    Step(Collection(other), Collection(section.Center)),
                }
            )
            .Where(shift => shift is not 0)
            .ToHashSet();
        var end = EndOf(run, section);

        return lifts.Any(lift =>
            lift.At >= start - LiftLead && lift.At < end && shifts.Contains(lift.Shift)
        );
    }

    private static int Step(int from, int to) => ((to - from) % 12 + 12) % 12;

    // The major scale a mode's notes come from: A Aeolian and C Ionian share C's.
    private static int Collection(TonalCenter center) =>
        center.Chromatic
        + center.Mode switch
        {
            DiatonicMode.Ionian => 0,
            DiatonicMode.Dorian => -2,
            DiatonicMode.Phrygian => -4,
            DiatonicMode.Lydian => -5,
            DiatonicMode.Mixolydian => -7,
            DiatonicMode.Aeolian => -9,
            DiatonicMode.Locrian => -11,
            _ => throw new UnreachableException(),
        };

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
        run.Sections.Where(other => other.Start > section.Start).Min(other => other.Start)
        ?? double.MaxValue;

    public static IReadOnlyList<WindowReading> Readings(RunRecord run) =>
        WindowReading.Of(run, Windows(run, run.Windows.Count > 0 ? Machine(run) : null));

    private static IReadOnlyList<MachineDto.WindowDto> Windows(
        RunRecord run,
        MachineDto? machine
    ) => run.Windows.Count > 0 ? machine?.Windows ?? [] : [];

    private static MachineDto? Machine(RunRecord run)
    {
        if (run.Evidence is not { } json)
            return null;

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
