using System.Text;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ledger;

public enum ProseKind
{
    Plain,
    Key,
    Chord,
    Value,
    Confidence,
}

public sealed record ProseSpan(string Text, ProseKind Kind = ProseKind.Plain);

public sealed record ProseParagraph(IReadOnlyList<ProseSpan> Spans)
{
    public override string ToString() => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// Typesets the judge's text at render time: strips Markdown, splits paragraphs, and spells keys,
/// chords and detectors one way. It changes only what it recognises with certainty (ADR 0064).
/// </summary>
public static partial class JudgeProse
{
    private const string Accidental = "[#b♯♭]";

    private const string NumeralShape =
        @"(?<![\w])(?:[#♭♯](?:VII|VI|IV|III|II|V|I|vii|vi|iv|iii|ii|v|i)|b(?:VII|VI|IV|III|II|V|I|vii|vi|iv|iii|ii|v))(?![\w])";

    private const string Function = @"(?:\s?\([^()\s]{1,6}\))?";

    private const string ChordShape = $$"""
        (?<Root>[A-G])(?<Accidental>\#\#|{{Accidental}})?
        (?<Quality>
            (?:maj|min|dim|aug|sus[24]?|add(?:9|11|13)|m)?
            (?:5|6|7|9|11|13)?
            (?:{{Accidental}}(?:5|9|11|13))*
            (?:\((?:[Mm]aj|[Mm]in|[Mm]|[iIvV]+)\.?\))?
        )
        (?:/(?<Bass>[A-G]{{Accidental}}?(?:m|maj|min)?))?
        """;

    /// <summary>The paragraphs of <paramref name="text"/>, each split into plain and styled spans.</summary>
    public static IReadOnlyList<ProseParagraph> Paragraphs(string? text) =>
        text is null
            ? []
            :
            [
                .. NewlineRun
                    .Split(StripMarkdown(text))
                    .Select(paragraph => paragraph.Trim())
                    .Where(paragraph => paragraph.Length > 0)
                    .Select(Typeset),
            ];

    /// <summary>One run of text typeset without paragraph splitting: a candidate's key, a window's why.</summary>
    public static ProseParagraph Inline(string? text) =>
        Typeset(StripMarkdown(text ?? string.Empty).Trim());

    /// <summary>A chord as the screen spells it: <c>G#m</c> becomes <c>G♯m</c>.</summary>
    public static string Chord(string chord) =>
        ChordToken.Match(chord) is { Success: true } token && token.Length == chord.Length
            ? SpellChord(token)
            : chord;

    private static ProseParagraph Typeset(string text)
    {
        List<ProseSpan> spans = [];
        var plain = new StringBuilder();
        var named = DetectorName.Replace(text, Detector);
        var cursor = 0;

        void Flush()
        {
            if (plain.Length is 0)
                return;

            spans.Add(new(plain.ToString()));
            plain.Clear();
        }

        foreach (var token in Tokens.Matches(named).Cast<Match>())
        {
            plain.Append(named, cursor, token.Index - cursor);
            cursor = token.Index + token.Length;

            var groups = token.Groups;

            if (groups["KeyNote"].Success)
            {
                Flush();
                spans.Add(
                    new(
                        $"{SpellNote(groups["KeyNote"].Value)} {SpellMode(groups["KeyMode"].Value)}",
                        ProseKind.Key
                    )
                );
            }
            else if (groups["Chain"].Success)
            {
                Flush();
                spans.AddRange(Chain(token.Value));
            }
            else if (groups["Value"].Success)
            {
                Flush();
                spans.Add(new(token.Value, ProseKind.Value));
            }
            else if (groups["Confidence"].Success)
            {
                Flush();
                spans.Add(new(token.Value, ProseKind.Confidence));
            }
            else if (groups["Octave"].Success)
            {
                Flush();
                spans.Add(new($"{SpellNote(token.Value[..^1])}{token.Value[^1]}", ProseKind.Chord));
            }
            else if (groups["Numeral"].Success)
                plain.Append(SpellAccidental(token.Value[0])).Append(token.Value[1..]);
            else if (ChordToken.Match(token.Value) is var chord && IsQualified(chord))
            {
                Flush();
                spans.Add(new(SpellChord(chord), ProseKind.Chord));
            }
            else
                plain.Append(token.Value);
        }

        plain.Append(named, cursor, named.Length - cursor);
        Flush();

        return new(spans);
    }

    private static IEnumerable<ProseSpan> Chain(string text)
    {
        var cursor = 0;

        foreach (var chord in ChordToken.Matches(text).Cast<Match>())
        {
            if (chord.Index > cursor)
                yield return new(Numerals(text[cursor..chord.Index]));

            yield return new(SpellChord(chord), ProseKind.Chord);

            cursor = chord.Index + chord.Length;
        }

        if (cursor < text.Length)
            yield return new(Numerals(text[cursor..]));
    }

    private static string Numerals(string text) =>
        NumeralToken.Replace(text, match => SpellAccidental(match.Value[0]) + match.Value[1..]);

    private static string StripMarkdown(string text) =>
        Emphasis.Replace(text.Replace("`", string.Empty, StringComparison.Ordinal), "${Words}");

    private static string Detector(Match name) =>
        name.Groups["Keydetect"].Success ? "keydetect" : "Essentia";

    private static bool IsQualified(Match chord) =>
        chord.Groups["Accidental"].Success
        || chord.Groups["Quality"].Length > 0
        || chord.Groups["Bass"].Value.AsSpan().ContainsAny("#b♯♭");

    private static string SpellChord(Match chord) =>
        string.Concat(
            chord.Groups["Root"].Value,
            SpellAccidentals(chord.Groups["Accidental"].Value),
            ExtensionAccidental.Replace(
                chord.Groups["Quality"].Value,
                match => SpellAccidental(match.Value[0])
            ),
            chord.Groups["Bass"].Success ? $"/{SpellNote(chord.Groups["Bass"].Value)}" : null
        );

    private static string SpellNote(string note) =>
        string.Concat(note[0], SpellAccidentals(note[1..]));

    private static string SpellAccidentals(string accidentals) =>
        string.Concat(accidentals.Select(SpellAccidental));

    private static string SpellAccidental(char accidental) =>
        accidental switch
        {
            '#' => "♯",
            'b' => "♭",
            _ => accidental.ToString(),
        };

    private static string SpellMode(string mode) =>
        mode.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries) switch
        {
            [.., "maj" or "major"] words => string.Join(' ', [.. words[..^1], "major"]),
            [.., "min" or "minor"] words => string.Join(' ', [.. words[..^1], "minor"]),
            [var name] => $"{char.ToUpperInvariant(name[0])}{name[1..]}",
            var words => string.Join(' ', words),
        };

    [GeneratedRegex(@"(?:\r?\n)+")]
    private static partial Regex NewlineRun { get; }

    [GeneratedRegex(
        @"
        (?<![\w*])(?<Marks>\*{1,2})(?=\S)(?<Words>.+?)(?<=\S)\k<Marks>(?![\w*])
        |
        (?<![\w])(?<Marks>_{1,2})(?=[^\s_])(?<Words>.+?)(?<=[^\s_])\k<Marks>(?!\w)
        ",
        RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex Emphasis { get; }

    [GeneratedRegex(
        @"
        (?<![\w])
        (
            (?<Keydetect>ffmpeg(?:[_\s]|['’]s\s)keydetect|keydetect)
            |
            Essentia[_\s]edma|Essentia\s?\(edma\)|edma
        )
        (?![\w])
        ",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex DetectorName { get; }

    [GeneratedRegex(
        $$"""
            (?<Chain>(?<![\w#♯♭]){{ChordShape}}{{Function}}(?:\s*[→–]\s*{{ChordShape}}{{Function}})+(?![\w#♯♭]))
            |
            (?<![\w#♯♭])
            (?<KeyNote>[A-G]{{Accidental}}?)\s+
            (?<KeyMode>(?:(?i:natural|harmonic|melodic)\s+)?(?i:major|minor|maj|min|ionian|dorian|phrygian|lydian|mixolydian|aeolian|locrian))
            (?![\w])
            |
            (?<Value>(?<![\w])[rR]\s?(?:[=<>≈~≥≤]\s?)?\d*\.\d+(?:\s?[–-]\s?\d*\.\d+)?)
            |
            (?<Confidence>\b(?:HIGH|MEDIUM|LOW)\b)
            |
            (?<Numeral>{{NumeralShape}})
            |
            (?<Octave>(?<![\w#♯♭])[A-G]{{Accidental}}\d(?![\w#♯♭]))
            |
            (?<![\w#♯♭]){{ChordShape}}(?![\w#♯♭])
            """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture
    )]
    private static partial Regex Tokens { get; }

    [GeneratedRegex(
        $$"""{{ChordShape}}""",
        RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture
    )]
    private static partial Regex ChordToken { get; }

    [GeneratedRegex(NumeralShape)]
    private static partial Regex NumeralToken { get; }

    [GeneratedRegex(@"[#b](?=\d)")]
    private static partial Regex ExtensionAccidental { get; }
}
