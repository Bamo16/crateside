using System.Text.Json;
using static Crateside.Core.Orchestration.Verdict.HooktheoryPageReader;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>Names a Hookpad score's chords by the key in force at each beat, never by the page's numerals (keydetect-harness ADR 0002).</summary>
internal static class HookpadChord
{
    private static readonly Dictionary<string, int[]> Modes = new()
    {
        ["major"] = [0, 2, 4, 5, 7, 9, 11],
        ["dorian"] = [0, 2, 3, 5, 7, 9, 10],
        ["phrygian"] = [0, 1, 3, 5, 7, 8, 10],
        ["lydian"] = [0, 2, 4, 6, 7, 9, 11],
        ["mixolydian"] = [0, 2, 4, 5, 7, 9, 10],
        ["minor"] = [0, 2, 3, 5, 7, 8, 10],
        ["locrian"] = [0, 1, 3, 5, 6, 8, 10],
        ["harmonicMinor"] = [0, 2, 3, 5, 7, 8, 11],
        ["phrygianDominant"] = [0, 1, 4, 5, 7, 8, 10],
    };

    private static readonly string[] Sharps =
    [
        "C",
        "C#",
        "D",
        "D#",
        "E",
        "F",
        "F#",
        "G",
        "G#",
        "A",
        "A#",
        "B",
    ];

    private static readonly string[] Flats =
    [
        "C",
        "Db",
        "D",
        "Eb",
        "E",
        "F",
        "Gb",
        "G",
        "Ab",
        "A",
        "Bb",
        "B",
    ];

    // Parent major keys written with sharps; every other key is spelled with flats.
    private static readonly int[] SharpKeys = [7, 2, 9, 4, 11, 6];

    private static readonly Dictionary<int, string> Degrees = new()
    {
        [0] = "1",
        [1] = "b2",
        [2] = "2",
        [3] = "b3",
        [4] = "3",
        [5] = "4",
        [6] = "b5",
        [7] = "5",
        [8] = "#5",
        [9] = "6",
        [10] = "b7",
        [11] = "7",
        [13] = "b9",
        [14] = "9",
        [15] = "#9",
        [17] = "11",
        [18] = "#11",
        [20] = "b13",
        [21] = "13",
    };

    private static readonly Dictionary<string, string> Shorthand = new()
    {
        ["0,4,7"] = "maj",
        ["0,3,7"] = "min",
        ["0,3,6"] = "dim",
        ["0,4,8"] = "aug",
        ["0,4,7,11"] = "maj7",
        ["0,3,7,10"] = "min7",
        ["0,4,7,10"] = "7",
        ["0,3,6,9"] = "dim7",
        ["0,3,6,10"] = "hdim7",
        ["0,3,7,11"] = "minmaj7",
        ["0,4,7,9"] = "maj6",
        ["0,3,7,9"] = "min6",
        ["0,5,7"] = "sus4",
        ["0,2,7"] = "sus2",
        ["0,4,7,10,14"] = "9",
        ["0,4,7,11,14"] = "maj9",
        ["0,3,7,10,14"] = "min9",
    };

    private static readonly ScoreKeyDto DefaultKey = new(1, "C", "major");

    /// <summary>Every chord of a section that is not a rest, with its Harte label (<c>C:maj7/3</c>); a chord the score cannot name is left out.</summary>
    public static IReadOnlyList<TabChord> Read(ScoreDto score)
    {
        IReadOnlyList<ScoreKeyDto> keys = score.Keys is [_, ..] given ? given : [DefaultKey];

        return
        [
            .. (score.Chords ?? [])
                .Where(chord => chord.IsRest is not true)
                .Select(chord => (chord.Beat, Label: Label(chord, KeyAt(keys, chord.Beat))))
                .Where(chord => chord.Label is not null)
                .Select(chord => new TabChord(chord.Beat, chord.Label!)),
        ];
    }

    /// <summary>The last key change at or before the beat, else the score's first key.</summary>
    public static ScoreKeyDto KeyAt(IReadOnlyList<ScoreKeyDto> keys, double beat)
    {
        var current = keys[0];

        foreach (var key in keys.OrderBy(key => key.Beat))
        {
            if (key.Beat <= beat)
                current = key;
        }

        return current;
    }

    private static string? Label(ScoreChordDto chord, ScoreKeyDto key)
    {
        if (Tones(chord, key) is not var (root, stack, bass) || Names(key) is not { } names)
            return null;

        var intervals = stack.Distinct().Order().ToList();
        var quality =
            Shorthand.GetValueOrDefault(string.Join(',', intervals))
            ?? $"({(string.Join(',', intervals.Where(interval => interval != 0).Select(Degree)) is { Length: > 0 } degrees ? degrees : "1")})";
        var label = $"{names[root]}:{quality}";

        return bass == 0 ? label : $"{label}/{Degree(bass % 12)}";
    }

    private static string Degree(int interval) =>
        Degrees.GetValueOrDefault(interval) ?? $"{interval}";

    /// <summary>The root's pitch class, the intervals above it in stacking order, and the bass's interval.</summary>
    private static (int Root, List<int> Stack, int Bass)? Tones(
        ScoreChordDto chord,
        ScoreKeyDto key
    )
    {
        var size = chord.Type switch
        {
            5 => 3,
            7 => 4,
            9 => 5,
            11 => 6,
            13 => 7,
            _ => 0,
        };
        var degree = chord.Root - 1;

        if (size is 0 || degree is < 0 or > 6 || Scale(chord, key) is not { Length: 7 } scale)
            return null;

        var tonic = PitchClass(key.Tonic);

        if (chord.Applied is >= 1 and <= 7 and var applied)
        {
            // The chord is a degree of its target's major scale.
            tonic += scale[degree];
            (scale, degree) = (Modes["major"], applied - 1);
        }

        List<int> stack = [];

        for (var i = 0; i < size; i++)
        {
            var index = degree + 2 * i;
            stack.Add(scale[index % 7] + 12 * (index / 7) - scale[degree]);
        }

        if (chord.Applied is 7 && size >= 4)
            stack[3] = 9; // Hookpad's applied vii7 is fully diminished

        var root = Modulo(tonic + scale[degree]);

        if (chord.Substitutions?.Contains("tri") is true)
            root = Modulo(root + 6);

        foreach (var suspension in chord.Suspensions ?? [])
        {
            if (suspension is 2 or 4)
                stack[1] = suspension is 2 ? 2 : 5;
        }

        foreach (var alteration in chord.Alterations ?? [])
        {
            if (alteration is "b5" or "#5")
                stack[2] += alteration is "b5" ? -1 : 1;
        }

        if (chord.Inversion is { } inversion && (inversion < 0 || inversion >= size))
            return null;

        var bass = stack[chord.Inversion ?? 0];
        var omitted = (chord.Omits ?? [])
            .Select(omit =>
                omit switch
                {
                    3 => 1,
                    5 => 2,
                    _ => -1,
                }
            )
            .ToHashSet();

        stack = [.. stack.Where((_, index) => !omitted.Contains(index))];

        foreach (var add in chord.Adds ?? [])
        {
            if (
                add switch
                {
                    9 => 14,
                    4 => 5,
                    6 => 9,
                    _ => (int?)null,
                } is
                { } added
            )
                stack.Add(added);
        }

        return (root, stack, bass);
    }

    /// <summary>The scale the chord's degree counts in: a <c>borrowed</c> mode name or seven semitone offsets, else the key's.</summary>
    private static int[]? Scale(ScoreChordDto chord, ScoreKeyDto key) =>
        chord.Borrowed switch
        {
            { ValueKind: JsonValueKind.Array } offsets
                when offsets
                    .EnumerateArray()
                    .All(offset => offset.ValueKind is JsonValueKind.Number) =>
            [
                .. offsets.EnumerateArray().Select(offset => offset.GetInt32()),
            ],
            { ValueKind: JsonValueKind.String } mode
                when mode.GetString() is { Length: > 0 } name => Modes.GetValueOrDefault(name),
            _ => Modes.GetValueOrDefault(key.Scale),
        };

    /// <summary>Pitch names as the key's signature spells them: its parent major key's, or the relative major's for harmonic minor and its kin.</summary>
    private static string[]? Names(ScoreKeyDto key)
    {
        if (key.Tonic.Contains('#', StringComparison.Ordinal))
            return Sharps;

        if (key.Tonic.Contains('b', StringComparison.Ordinal))
            return Flats;

        if (Modes.GetValueOrDefault(key.Scale) is not { } mode)
            return null;

        var tonic = PitchClass(key.Tonic);
        var notes = mode.Select(step => Modulo(tonic + step)).ToHashSet();
        var parent =
            Enumerable
                .Range(0, 12)
                .Where(major =>
                    notes.SetEquals(Modes["major"].Select(step => Modulo(major + step)))
                )
                .Select(major => (int?)major)
                .FirstOrDefault()
            ?? Modulo(tonic + 3);

        return SharpKeys.Contains(parent) ? Sharps : Flats;
    }

    private static int PitchClass(string name)
    {
        var pitch = char.ToUpperInvariant(name[0]) switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => 0,
        };

        foreach (var accidental in name[1..])
        {
            pitch += accidental switch
            {
                '#' => 1,
                'b' => -1,
                _ => 0,
            };
        }

        return Modulo(pitch);
    }

    private static int Modulo(int pitch) => ((pitch % 12) + 12) % 12;
}
