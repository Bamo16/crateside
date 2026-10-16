using System.Text;
using System.Text.RegularExpressions;

namespace Crateside.Core.Ledger;

/// <summary>
/// The ledger's spelling rules, carried over from keytag's <c>ledger.py</c>: keys, modes, links
/// and text that survived its trip intact (docs/ledger.md).
/// </summary>
public static partial class LedgerVocabulary
{
    public static readonly IReadOnlyList<string> Confidences =
    [
        "HIGH",
        "MEDIUM",
        "LOW",
        "WITHHELD",
    ];

    private static readonly Encoding Cp1252;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Regex Mojibake;
    private static readonly Regex MojibakeRun;
    private static readonly Regex Residue;

    static LedgerVocabulary()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1252 = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback
        );

        // UTF-8 read as CP1252: a lead byte's rendering followed by a continuation byte's.
        Mojibake = new($"{Rendered(0xC2, 0xF5)}{Rendered(0x80, 0xC0)}");
        MojibakeRun = new(
            $"(?:{Rendered(0xC2, 0xE0)}{Rendered(0x80, 0xC0)}"
                + $"|{Rendered(0xE0, 0xF0)}{Rendered(0x80, 0xC0)}{{2}}"
                + $"|{Rendered(0xF0, 0xF5)}{Rendered(0x80, 0xC0)}{{3}})+"
        );
        // What a repair strands when the mojibake was lowercased first: a Latin-1 letter against a
        // character above U+00FF, which no real name has.
        Residue = new(
            $"{Rendered(0xC0, 0x100)}[^\\x00-\\xff]|[^\\x00-\\xff]{Rendered(0xC0, 0x100)}"
        );
    }

    /// <summary><c>Eb major</c> → <c>D# major</c>; null when the text does not start with a key.</summary>
    public static string? NormalizeKey(string? text)
    {
        if (
            text?.Replace('♯', '#').Replace('♭', 'b') is not { } plain
            || KeyAtStart.Match(plain) is not { Success: true } key
            || !TryNormalizeTonic(key.Groups["Tonic"].Value, out var sharp)
        )
            return null;

        return key.Groups["Quality"].Value.StartsWith("maj", StringComparison.OrdinalIgnoreCase)
            ? $"{sharp} major"
            : $"{sharp} minor";
    }

    /// <summary>A tonic in the ledger's spelling (<c>Bb</c> → <c>A#</c>), or false.</summary>
    public static bool TryNormalizeTonic(string? tonic, out string sharp)
    {
        sharp = string.Empty;

        if (tonic?.Trim().Replace('♯', '#').Replace('♭', 'b') is not { Length: 1 or 2 } plain)
            return false;

        var index = TonalCentre.Tonics.IndexOf(char.ToUpperInvariant(plain[0]).ToString());

        if (index < 0)
            return false;

        index = plain switch
        {
            [_] => index,
            [_, '#'] => index + 1,
            [_, 'b' or 'B'] => index + 11,
            _ => -1,
        };

        if (index < 0)
            return false;

        sharp = TonalCentre.Tonics[index % 12];

        return true;
    }

    /// <summary>
    /// The mode named in <paramref name="text"/>, bare (<c>dorian</c>) or with its tonic
    /// (<c>D Dorian</c>); <c>major</c> and <c>minor</c> are Ionian and Aeolian.
    /// </summary>
    public static DiatonicMode? ParseMode(string? text)
    {
        if (text is not { Length: > 0 })
            return null;

        var candidate = ModeNamed.Match(text) is { Success: true } named
            ? named.Groups["Mode"].Value
            : text.Trim();

        return candidate.ToLowerInvariant() switch
        {
            "major" => DiatonicMode.Ionian,
            "minor" => DiatonicMode.Aeolian,
            var name => Enum.TryParse<DiatonicMode>(name, ignoreCase: true, out var mode)
                ? mode
                : null,
        };
    }

    /// <summary><c>E Mixolydian throughout</c> → <c>E major</c>; null when no mode is named.</summary>
    public static string? CollapseModalStatement(string? text) =>
        text is not null
        && ModeNamed.Match(text) is { Success: true } named
        && ParseMode(named.Groups["Mode"].Value) is { } mode
            ? NormalizeKey(
                $"{named.Groups["Tonic"].Value} {(TonalCentre.IsMajor(mode) ? "major" : "minor")}"
            )
            : null;

    /// <summary>
    /// A link split from how it was obtained: <c>open.spotify.com/x (via web search)</c> →
    /// (<c>https://open.spotify.com/x</c>, <c>web search</c>); a description is all via.
    /// </summary>
    public static (string? Url, string? Via) SplitUrl(string? raw)
    {
        if (raw?.Trim() is not { Length: > 0 } text)
            return (null, null);

        string? via = null;

        if (TrailingNote.Match(text) is { Success: true } trailing)
        {
            via = trailing.Groups["Note"].Value.Trim();

            if (via.StartsWith("via ", StringComparison.OrdinalIgnoreCase))
                via = via[4..].Trim();

            text = text[..trailing.Index].Trim();
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var link) && link.Scheme is "http" or "https")
            return (text, via);

        if (!text.Contains(' ') && text.Contains('.'))
            return ($"https://{text}", via);

        return (null, string.Join(' ', new[] { text, via }.OfType<string>()));
    }

    /// <summary>The song identity: lowercased <c>artist - title</c>, whitespace collapsed, a stem suffix dropped.</summary>
    public static string SongIdentity(string artist, string title) =>
        $"{Collapsed(artist)} - {Collapsed(WithoutStemSuffix(title))}".ToLowerInvariant();

    /// <summary><c>Title (Instrumental)</c> → <c>Title</c>, as StemForge names a stem.</summary>
    public static string WithoutStemSuffix(string title)
    {
        string previous;

        do
        {
            previous = title;
            title = StemSuffix.Replace(title, string.Empty).Trim();
        } while (title != previous);

        return title;
    }

    /// <summary>Undoes a bad encoding hop: escaped bytes first, then UTF-8 read as CP1252, run by run.</summary>
    public static string RepairText(string text) => UndoDoubleEncode(UndoSurrogateEscape(text));

    /// <summary>Whether text still carries a bad encoding hop; asked after a repair, true means unrecoverable.</summary>
    public static bool IsDamaged(string text) =>
        Mojibake.IsMatch(text)
        || Residue.IsMatch(text)
        || text.Contains('�')
        || text.Any(char.IsSurrogate) && !IsWellFormed(text);

    private static string Collapsed(string text) => Whitespace.Replace(text, " ").Trim();

    private static string UndoDoubleEncode(string text) =>
        MojibakeRun.Replace(
            text,
            run =>
            {
                try
                {
                    return StrictUtf8.GetString(Cp1252.GetBytes(run.Value));
                }
                catch (Exception ex)
                    when (ex is EncoderFallbackException or DecoderFallbackException)
                {
                    return run.Value;
                }
            }
        );

    /// <summary>Lone surrogates U+DC80–U+DCFF back to the bytes they escaped, read as UTF-8.</summary>
    private static string UndoSurrogateEscape(string text)
    {
        if (IsWellFormed(text))
            return text;

        foreach (var others in new[] { StrictUtf8, Cp1252 })
        {
            try
            {
                var bytes = new List<byte>();

                for (var i = 0; i < text.Length; i++)
                {
                    if (
                        char.IsHighSurrogate(text[i])
                        && i + 1 < text.Length
                        && char.IsLowSurrogate(text[i + 1])
                    )
                    {
                        bytes.AddRange(others.GetBytes(text.Substring(i++, 2)));
                    }
                    else if (text[i] is >= '\uDC80' and <= '\uDCFF')
                        bytes.Add((byte)(text[i] - 0xDC00));
                    else
                        bytes.AddRange(others.GetBytes(text[i].ToString()));
                }

                return StrictUtf8.GetString([.. bytes]);
            }
            catch (Exception ex) when (ex is EncoderFallbackException or DecoderFallbackException)
            {
                continue;
            }
        }

        return text;
    }

    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (
                char.IsHighSurrogate(text[i])
                && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1])
            )
                i++;
            else if (char.IsSurrogate(text[i]))
                return false;
        }

        return true;
    }

    /// <summary>A character class of how CP1252 renders the bytes in [start, stop).</summary>
    private static string Rendered(int start, int stop)
    {
        var rendered = new StringBuilder();

        for (var b = start; b < stop; b++)
        {
            try
            {
                rendered.Append(Cp1252.GetString([(byte)b]));
            }
            catch (DecoderFallbackException)
            {
                // Undefined in CP1252, so no mojibake string can contain it.
            }
        }

        return $"[{Regex.Escape(rendered.ToString()).Replace("]", @"\]").Replace("-", @"\-")}]";
    }

    [GeneratedRegex(
        @"^\s*(?<Tonic>[A-G][#b]?)\s*(?<Quality>major|minor|maj|min)\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex KeyAtStart { get; }

    [GeneratedRegex(
        @"\b(?<Tonic>[A-G][#b♯♭]?)\s+(?<Mode>dorian|phrygian|lydian|mixolydian|locrian|aeolian|ionian)\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex ModeNamed { get; }

    [GeneratedRegex(@"\s*\((?<Note>[^()]*)\)\s*$")]
    private static partial Regex TrailingNote { get; }

    [GeneratedRegex(@"\s*\((instrumental|vocal|drums|bass)[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StemSuffix { get; }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace { get; }
}
