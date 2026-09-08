using System.Text;

namespace Crateside.Core.Ledger;

/// <summary>
/// Repairs ledger text that was written through a Windows-1252 misreading of its own UTF-8 bytes.
/// </summary>
/// <remarks>
/// The ledger is append-only, so damaged lines stay as they are; this undoes the mojibake on the
/// way to the screen. Only text that round-trips exactly through Windows-1252 → UTF-8 is
/// changed — pure ASCII, real accented text, and non-Latin text all fail that round-trip and
/// pass through unchanged.
/// </remarks>
public static class LedgerText
{
    /// <summary>
    /// The 32 code points Windows-1252 puts where Latin-1 has controls. <c>�</c> marks the five
    /// positions Windows-1252 leaves undefined.
    /// </summary>
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

            // A real U+FFFD must not match the undefined slots in the table.
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
