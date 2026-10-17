using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Crateside.Core.Ledger;

namespace Crateside.Core.Pairing;

/// <summary>
/// One of the 24 keys, positioned on both the chromatic scale and the circle of fifths.
/// </summary>
public sealed partial class MusicKey : IEquatable<MusicKey>, IComparable<MusicKey>
{
    private static readonly string[] SharpNames =
    [
        "C",
        "C♯",
        "D",
        "D♯",
        "E",
        "F",
        "F♯",
        "G",
        "G♯",
        "A",
        "A♯",
        "B",
    ];

    private static readonly string[] FlatNames =
    [
        "C",
        "D♭",
        "D",
        "E♭",
        "E",
        "F",
        "G♭",
        "G",
        "A♭",
        "A",
        "B♭",
        "B",
    ];

    private MusicKey(int circlePos, int chromatic, Mode mode)
    {
        CirclePos = circlePos;
        Chromatic = chromatic;
        Mode = mode;

        var code = (CirclePos + 8) % 12;
        CamelotCode = $"{(code is 0 ? 12 : code)}{(Mode is Mode.Major ? "B" : "A")}";
    }

    /// <summary>Position on the circle of fifths, 0–11.</summary>
    public int CirclePos { get; }

    /// <summary>Position on the chromatic scale, 0–11 with C at 0.</summary>
    public int Chromatic { get; }

    public Mode Mode { get; }

    /// <summary>The harmonic-mixing notation — <c>8B</c>, <c>5A</c>.</summary>
    public string CamelotCode { get; }

    /// <summary>Sharp-spelled with a Unicode ♯ — <c>G♯ Major</c>.</summary>
    public string Name => SharpName;

    public string SharpName => $"{SharpNames[Chromatic]} {Mode}";

    public string FlatName => $"{FlatNames[Chromatic]} {Mode}";

    /// <summary>
    /// The ledger's spelling, which crateside joins on: ASCII accidental, lowercase mode —
    /// <c>G# major</c>.
    /// </summary>
    public string LedgerName =>
        $"{SharpNames[Chromatic].Replace('♯', '#')} {Mode.ToString().ToLowerInvariant()}";

    /// <summary>All 24 keys, majors by circle position then minors.</summary>
    public static readonly ImmutableArray<MusicKey> AllKeys =
    [
        // Major keys (B) — primary ring
        new(0, 0, Mode.Major), // 8B  - C
        new(1, 7, Mode.Major), // 9B  - G
        new(2, 2, Mode.Major), // 10B - D
        new(3, 9, Mode.Major), // 11B - A
        new(4, 4, Mode.Major), // 12B - E
        new(5, 11, Mode.Major), // 1B  - B
        new(6, 6, Mode.Major), // 2B  - F♯
        new(7, 1, Mode.Major), // 3B  - C♯
        new(8, 8, Mode.Major), // 4B  - G♯
        new(9, 3, Mode.Major), // 5B  - D♯
        new(10, 10, Mode.Major), // 6B  - A♯
        new(11, 5, Mode.Major), // 7B  - F
        // Minor keys (A) — aligned with their major relatives
        new(0, 9, Mode.Minor), // 8A  - Am
        new(1, 4, Mode.Minor), // 9A  - Em
        new(2, 11, Mode.Minor), // 10A - Bm
        new(3, 6, Mode.Minor), // 11A - F♯m
        new(4, 1, Mode.Minor), // 12A - C♯m
        new(5, 8, Mode.Minor), // 1A  - G♯m
        new(6, 3, Mode.Minor), // 2A  - D♯m
        new(7, 10, Mode.Minor), // 3A  - A♯m
        new(8, 5, Mode.Minor), // 4A  - Fm
        new(9, 0, Mode.Minor), // 5A  - Cm
        new(10, 7, Mode.Minor), // 6A  - Gm
        new(11, 2, Mode.Minor), // 7A  - Dm
    ];

    /// <summary>
    /// Normalizes a note name to its sharp spelling — flats become their enharmonic sharp,
    /// so <c>Eb</c> and <c>D#</c> are the same key.
    /// </summary>
    public static string GetCanonicalNoteName(string input) =>
        input switch
        {
            { Length: 0 } => input,

            ['B' or 'b', '♭' or 'b', .. var rest] => $"A♯{rest}",
            ['D' or 'd', '♭' or 'b', .. var rest] => $"C♯{rest}",
            ['E' or 'e', '♭' or 'b', .. var rest] => $"D♯{rest}",
            ['G' or 'g', '♭' or 'b', .. var rest] => $"F♯{rest}",
            ['A' or 'a', '♭' or 'b', .. var rest] => $"G♯{rest}",

            [var note, '#' or '♯', .. var rest] => $"{char.ToUpperInvariant(note)}♯{rest}",

            [var note, .. var rest] => $"{char.ToUpperInvariant(note)}{rest}",
        };

    /// <summary>
    /// Parses a key from any usual spelling or a Camelot code, or returns null rather than guessing.
    /// </summary>
    public static MusicKey? FromString(string? input)
    {
        if (
            string.IsNullOrWhiteSpace(input)
            || KeyPattern.Match(input.Trim()) is not { Success: true, Groups: var groups }
        )
        {
            return null;
        }

        if (groups["CamelotCode"].Success)
        {
            return AllKeys.FirstOrDefault(k =>
                k.CamelotCode.Equals(
                    groups["CamelotCode"].Value,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        if (groups["Key"].Success)
        {
            var canonicalNote = GetCanonicalNoteName(groups["Note"].Value);

            var mode = groups["Mode"] switch
            {
                { Success: true, Value: "m" } => Mode.Minor,
                { Success: true, Value: "M" } => Mode.Major,
                { Success: true, Value: var m } when m.ToLowerInvariant() is "min" or "minor" =>
                    Mode.Minor,
                _ => Mode.Major,
            };

            return AllKeys.FirstOrDefault(k =>
                k.Name.Equals($"{canonicalNote} {mode}", StringComparison.OrdinalIgnoreCase)
            );
        }

        return null;
    }

    /// <summary>A centre's tag as a key: its collapse.</summary>
    public static MusicKey Of(TonalCentre centre) =>
        AllKeys.Single(key =>
            key.Chromatic == centre.Chromatic
            && key.Mode == (TonalCentre.IsMajor(centre.Mode) ? Mode.Major : Mode.Minor)
        );

    /// <summary>The key as a centre on Ionian or Aeolian.</summary>
    public static TonalCentre CentreOf(MusicKey key) =>
        new(
            TonalCentre.Tonics[key.Chromatic],
            key.Mode is Mode.Major ? DiatonicMode.Ionian : DiatonicMode.Aeolian
        );

    /// <summary>The key <paramref name="semitones"/> away in the same mode, up or down.</summary>
    public MusicKey Transposed(int semitones) =>
        AllKeys.Single(key =>
            key.Mode == Mode && key.Chromatic == ((Chromatic + semitones) % 12 + 12) % 12
        );

    /// <summary>The same notes in the other mode: same circle position, other letter.</summary>
    public MusicKey Relative =>
        AllKeys.Single(key => key.Mode != Mode && key.CirclePos == CirclePos);

    /// <summary>The note alone, as the Camelot wheel labels it: majors flat, minors sharp with an m.</summary>
    public string ShortName =>
        Mode is Mode.Major ? FlatNames[Chromatic] : $"{SharpNames[Chromatic]}m";

    public bool Equals(MusicKey? other) =>
        other is not null && Chromatic == other.Chromatic && Mode == other.Mode;

    public override bool Equals(object? obj) => Equals(obj as MusicKey);

    public override int GetHashCode() => HashCode.Combine(Chromatic, Mode);

    public int CompareTo(MusicKey? other)
    {
        if (other is null)
            return 1;

        var byChromatic = Chromatic.CompareTo(other.Chromatic);
        return byChromatic != 0 ? byChromatic : Mode.CompareTo(other.Mode);
    }

    public override string ToString() => Name;

    [GeneratedRegex(
        @"
        ^
            (
                (?<CamelotCode>(1[0-2]|[1-9])[AB])
                |
                (?<Key>
                    (?<Note>[A-Ga-g][#♯b♭♮]?)
                    \s*
                    (?<Mode>
                        (?i:major|minor|maj|min)
                        |
                        (M|m)
                    )?
                )
            )
        $
        ",
        RegexOptions.ExplicitCapture | RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex KeyPattern { get; }
}
