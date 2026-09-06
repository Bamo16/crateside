using System.Text;

namespace Crateside.Core.Ledger;

/// <summary>
/// Repairs ledger text that was written through a Windows-1252 misreading of its own UTF-8 bytes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The damage is in the file, not in the reading.</strong> Nineteen lines of
/// <c>runs.jsonl</c> hold the UTF-8 encoding of <c>â†’</c> where a <c>→</c> was meant: the arrow's
/// three bytes were decoded as Windows-1252 somewhere upstream and then written back out as UTF-8.
/// Every one of them predates <c>a40bb92</c>, which made crateside read child-process output as
/// UTF-8; nothing recorded since is affected. The ledger is keytag's and append-only, so the lines
/// stay as they are and this undoes them on the way to the screen.
/// </para>
/// <para>
/// <strong>Only text carrying the signature can change.</strong> The repair re-encodes to
/// Windows-1252 and decodes as UTF-8, and keeps the original unless both halves succeed exactly.
/// Pure ASCII is unchanged, because every byte is already valid UTF-8 for itself. A real
/// <c>café</c> fails, because <c>0xE9</c> alone is not valid UTF-8. Japanese fails at the first
/// step, because Windows-1252 cannot hold it. What survives both is the mojibake pattern.
/// </para>
/// </remarks>
public static class LedgerText
{
    /// <summary>
    /// The 32 code points Windows-1252 puts where Latin-1 has controls.
    /// </summary>
    /// <remarks>
    /// The whole of the difference between the two, written out because
    /// <see cref="Encoding.Latin1"/> is built in and Windows-1252 is not — reaching it needs the
    /// <c>System.Text.Encoding.CodePages</c> package, which is a dependency for a lookup table.
    /// <c>�</c> marks the five positions Windows-1252 leaves undefined.
    /// </remarks>
    private const string HighRange = "€�‚ƒ„…†‡" + "ˆ‰Š‹Œ�Ž�" + "�‘’“”•–—" + "˜™š›œ�žŸ";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    /// <summary>
    /// The text as it was meant to read, or the text unchanged when it was never damaged.
    /// </summary>
    public static string? Repaired(string? text)
    {
        if (text is not { Length: > 0 } || text.All(char.IsAscii))
            return text;

        if (!TryToWindows1252(text, out var bytes))
            return text;

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // The bytes are not UTF-8, so they were never a misread of it. Genuinely accented
            // text lands here, which is the point.
            return text;
        }
    }

    /// <summary>
    /// The bytes this text would have had as Windows-1252, or nothing when it has no such form.
    /// </summary>
    private static bool TryToWindows1252(string text, out byte[] bytes)
    {
        var buffer = new byte[text.Length];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // Latin-1's range maps to itself, apart from the block Windows-1252 reassigns.
            if (c < 0x80 || c is >= (char)0xA0 and <= (char)0xFF)
            {
                buffer[i] = (byte)c;
                continue;
            }

            // U+FFFD sits in the table for the five positions Windows-1252 leaves undefined, so a
            // real replacement character must not be allowed to match one of them.
            var high = c is '�' ? -1 : HighRange.IndexOf(c);

            if (high < 0)
            {
                bytes = [];
                return false;
            }

            buffer[i] = (byte)(0x80 + high);
        }

        bytes = buffer;
        return true;
    }
}
