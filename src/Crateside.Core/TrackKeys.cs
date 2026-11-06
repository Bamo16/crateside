using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Crateside.Core;

/// <summary>
/// The one place that knows the shapes of a track's work key: a bare video ID, <c>&lt;source&gt;~&lt;id&gt;</c>,
/// and the <c>track:&lt;key&gt;</c> form that carries one in provenance (ADR 0077).
/// </summary>
public static partial class TrackKeys
{
    private const string ProvenancePrefix = "track:";
    private const string FileSource = "file";
    private const int FileHashHexLength = 16;

    public static bool TryParse(string? input, [NotNullWhen(true)] out string? key)
    {
        var trimmed = input?.Trim();
        var bare = trimmed?.StartsWith(ProvenancePrefix, StringComparison.Ordinal) is true
            ? trimmed[ProvenancePrefix.Length..]
            : trimmed;

        key = bare switch
        {
            { Length: > 0 } when SourcedKeyPattern.IsMatch(bare) => bare,
            _ when YouTubeUrl.TryGetVideoId(bare, out var videoId) => videoId,
            _ => null,
        };

        return key is not null;
    }

    public static bool IsProvenance(string? input) =>
        input?.StartsWith(ProvenancePrefix, StringComparison.Ordinal) is true
        && TryParse(input, out _);

    public static bool IsVideoId(string? key) => key is { Length: > 0 } && !key.Contains('~');

    public static string ProvenanceOf(string key) => $"{ProvenancePrefix}{key}";

    public static string ForSite(string extractorKey, string id) =>
        $"{UnsafeCharacters.Replace(extractorKey, string.Empty).ToLowerInvariant()}~{UnsafeCharacters.Replace(id, "-")}";

    public static string ForFileContent(Stream content) =>
        $"{FileSource}~{Convert.ToHexStringLower(SHA256.HashData(content))[..FileHashHexLength]}";

    [GeneratedRegex(@"^[a-z0-9]+~[A-Za-z0-9_.\-]+$")]
    private static partial Regex SourcedKeyPattern { get; }

    [GeneratedRegex(@"[^A-Za-z0-9_.\-]")]
    private static partial Regex UnsafeCharacters { get; }
}
