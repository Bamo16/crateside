using System.Diagnostics;
using Crateside.Core.Pairing;

namespace Crateside.Core.Ledger;

/// <summary>The words that show a verdict's mode beside its tag (design handoff 17).</summary>
public static class ModeText
{
    /// <summary>The tag as the screen spells it: <c>A♯ min</c>.</summary>
    public static string Tag(TonalCentre centre) =>
        $"{Tonic(centre)} {(TonalCentre.IsMajor(centre.Mode) ? "maj" : "min")}";

    /// <summary>The arrow's left half (<c>A Dorian</c>, <c>A mixed</c>), or null where the tag says it all.</summary>
    public static string? Lead(TonalCentre centre, bool mixed) =>
        (centre, mixed) switch
        {
            (_, true) => $"{Tonic(centre)} mixed",
            ({ IsModal: true }, _) => $"{Tonic(centre)} {centre.Mode}",
            _ => null,
        };

    /// <summary><c>A Dorian → A min</c>, or null where the verdict is plain.</summary>
    public static string? Arrow(TonalCentre centre, bool mixed) =>
        Lead(centre, mixed) is { } lead ? $"{lead} → {Tag(centre)}" : null;

    /// <summary>The abbreviation that sits after the tag on the Tracks row, or null where the verdict is plain.</summary>
    public static string? Abbreviation(TonalCentre centre, bool mixed) =>
        (centre.Mode, mixed) switch
        {
            (_, true) => "maj/min",
            (DiatonicMode.Dorian, _) => "Dor",
            (DiatonicMode.Phrygian, _) => "Phr",
            (DiatonicMode.Lydian, _) => "Lyd",
            (DiatonicMode.Mixolydian, _) => "Mixo",
            (DiatonicMode.Locrian, _) => "Loc",
            _ => null,
        };

    /// <summary>Why the tag says what it does, or null where the verdict is plain.</summary>
    public static string? Reason(TonalCentre centre, bool mixed) =>
        (centre, mixed) switch
        {
            (_, true) when TonalCentre.IsMajor(centre.Mode) =>
                "Minor melody over major harmony; tagged by the harmony.",
            (_, true) => "Major melody over minor harmony; tagged by the harmony.",
            ({ IsModal: true }, _) =>
                $"{centre.Mode} is a {(TonalCentre.IsMajor(centre.Mode) ? "major" : "minor")} mode, so it is tagged by its tonic. Same notes as {SameNotes(centre)}.",
            _ => null,
        };

    /// <summary>The major key with the same notes, named with its Camelot code: <c>G major (9B)</c>.</summary>
    public static string SameNotes(TonalCentre centre) =>
        MusicKey.FromString(centre.Collapsed) is { } tag
        && MusicKey.AllKeys.FirstOrDefault(key =>
            key.Mode is Mode.Major
            && key.Chromatic == (tag.Chromatic + ToParentMajor(centre.Mode)) % 12
        )
            is { } parent
            ? $"{parent.ShortName} major ({parent.CamelotCode})"
            : centre.Name;

    /// <summary>A source's stated key in full: <c>G Mixolydian</c> where it named a mode, else the key as it came.</summary>
    public static string? Stated(string? statesKey, string? statesMode) =>
        (statesKey?.Split(' '), Enum.TryParse<DiatonicMode>(statesMode, true, out var mode)) switch
        {
            ([var tonic, _], true) when new TonalCentre(tonic, mode) is { IsModal: true } centre =>
                $"{Tonic(centre)} {mode}",
            _ => statesKey,
        };

    /// <summary>
    /// What a source covered and how its key relates to it. A source that covered only some
    /// sections is judged against those, so it never reads as a dissent from the song.
    /// </summary>
    public static SourceCoverage Coverage(
        IReadOnlyList<string>? covered,
        IReadOnlyList<Section> sections,
        TonalCentre? verdict,
        string? statesKey
    )
    {
        var theirs = covered is { Count: > 0 }
            ? sections
                .Where(section => covered.Contains(section.Label, StringComparer.OrdinalIgnoreCase))
                .ToList()
            : [];

        if (theirs.Count is 0 || theirs.Count == sections.Count)
            return new("whole song", verdict?.Collapsed, null);

        var only = $"{string.Join(" and ", theirs.Select(section => section.Label))} only";
        var against = theirs.Select(section => section.Centre.Collapsed).Distinct().ToList();

        if (against is not [var key])
            return new(only, verdict?.Collapsed, null);

        var label = theirs[0].Label;
        var inTheirs = theirs[0].Centre;
        var relation = (
            statesKey is { Length: > 0 },
            inTheirs == verdict,
            key.Equals(statesKey, StringComparison.OrdinalIgnoreCase)
        ) switch
        {
            (false, _, _) => null,
            (_, true, true) => $"agrees with the {label}",
            (_, false, true) => $"matches the {label}, not the song",
            _ => $"disagrees with the {label} ({Named(inTheirs)}), not the song",
        };

        return new(only, key, relation);
    }

    /// <summary>A centre by name, spelled for the screen: <c>A♯ Dorian</c>, <c>E minor</c>.</summary>
    public static string Named(TonalCentre centre) =>
        $"{Tonic(centre)} {(centre.IsModal ? centre.Mode : centre.Collapsed.Split(' ')[1])}";

    /// <summary>
    /// What a hover on the Tracks row's key says: the arrow, the reason and the other centres.
    /// Null where there is nothing to add to the tag.
    /// </summary>
    public static string? Hover(LedgerRun run)
    {
        if (run.Centre is not { } centre)
            return null;

        var others = run
            .Sections.Where(section => section.Centre != centre)
            .Select(section => $"{Named(section.Centre)} ({section.Label})")
            .ToList();

        string?[] parts =
        [
            Arrow(centre, run.Mixed),
            Reason(centre, run.Mixed),
            others.Count > 0 ? $"Also {string.Join(", ", others)}." : null,
        ];

        return string.Join(' ', parts.OfType<string>()) is { Length: > 0 } hover ? hover : null;
    }

    private static string Tonic(TonalCentre centre) => centre.Tonic.Replace('#', '♯');

    // Semitones from a mode's tonic up to the tonic of the major scale it shares notes with.
    private static int ToParentMajor(DiatonicMode mode) =>
        mode switch
        {
            DiatonicMode.Ionian => 0,
            DiatonicMode.Dorian => 10,
            DiatonicMode.Phrygian => 8,
            DiatonicMode.Lydian => 7,
            DiatonicMode.Mixolydian => 5,
            DiatonicMode.Aeolian => 3,
            DiatonicMode.Locrian => 1,
            _ => throw new UnreachableException(),
        };
}

/// <param name="Covers">What the source covered: <c>whole song</c>, <c>chorus only</c>.</param>
/// <param name="JudgedAgainst">The key its stance is judged against: the verdict's, or the covered section's.</param>
/// <param name="Relation">How its key relates to what it covered, where that is not the whole song.</param>
public sealed record SourceCoverage(string Covers, string? JudgedAgainst, string? Relation);
