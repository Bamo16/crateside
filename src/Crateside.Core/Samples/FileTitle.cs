using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Crateside.Core.Samples;

/// <summary>
/// A port of StemForge's <c>FileTitle</c> (StemForge.Core/FileTitle.cs), so a name built here is the
/// name StemForge would build: the same 195/40 limits and the same hash.
/// </summary>
public static class FileTitle
{
    public const int MaxComponentLength = 255;

    // The longest suffix, " (Instrumental - Balanced) (99).flac.asd", is 40; the rest is headroom.
    public const int SuffixReserve = 60;

    public const int MaxLength = MaxComponentLength - SuffixReserve;

    private const int MinLength = 48;

    // The longest full path Win32 accepts without long paths: MAX_PATH less its terminating NUL.
    private const int MaxPath = 259;

    // An artist list is cut before the song title, but keeps at least this much to stay readable.
    private const int ArtistFloor = 40;

    private const string Separator = " - ";

    public static bool LongPathsEnabled { get; } = DetectLongPaths();

    public static int MaxLengthIn(string directory, bool longPathsEnabled) =>
        longPathsEnabled
            ? MaxLength
            : Math.Clamp(
                MaxPath
                    - Path.GetFullPath(directory is { Length: > 0 } ? directory : ".").Length
                    - 1
                    - SuffixReserve,
                MinLength,
                MaxLength
            );

    /// <summary>
    /// The title unchanged when it fits, otherwise cut at a grapheme boundary (artist list first),
    /// marked with an ellipsis and ended with a <c>[hash]</c> of the whole title. The brackets keep
    /// the cut name clear of <see cref="StemFileName"/>'s preset parentheses.
    /// </summary>
    public static string Fit(string title, int maxLength = MaxLength)
    {
        maxLength = Math.Max(maxLength, MinLength);
        if (title.Length <= maxLength)
            return title;

        var tag = $" [{Hash(title)}]";
        var room = maxLength - tag.Length;

        if (
            title.Split(Separator, 2) is [{ Length: > 0 } artist, { Length: > 0 } song]
            && Math.Min(artist.Length, ArtistFloor) + Separator.Length < room
        )
        {
            var forArtist = room - Separator.Length - song.Length;
            var cutArtist = Shorten(
                artist,
                Math.Max(forArtist, Math.Min(artist.Length, ArtistFloor))
            );
            var cutSong = Shorten(song, room - Separator.Length - cutArtist.Length);

            return $"{cutArtist}{Separator}{cutSong}{tag}";
        }

        return $"{Shorten(title, room)}{tag}";
    }

    private static string Shorten(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{Head(text, maxLength - 1)}…";

    // The longest run of whole graphemes within maxLength, without the dangling punctuation.
    private static string Head(string text, int maxLength)
    {
        var end = 0;
        while (end < text.Length)
        {
            var next = end + StringInfo.GetNextTextElementLength(text, end);
            if (next > maxLength)
                break;

            end = next;
        }

        return text[..end].TrimEnd(' ', ',', '-', '(', '[', '&', '/');
    }

    private static string Hash(string title) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title)))[..6];

    private static bool DetectLongPaths()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        try
        {
            return Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem",
                "LongPathsEnabled",
                0
            )
                is 1;
        }
        catch (Exception ex) when (ex is SecurityException or IOException)
        {
            return false;
        }
    }
}
