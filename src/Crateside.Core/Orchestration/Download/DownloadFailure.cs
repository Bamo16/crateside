using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Orchestration.Download;

/// <summary>Reads yt-dlp's error for an upload YouTube will never serve, however often it is asked (#63).</summary>
public static class DownloadFailure
{
    // First match wins: the specific reasons are often prefixed with "Video unavailable".
    private static readonly (string Says, string Reason)[] Permanent =
    [
        ("on copyright grounds", "blocked on copyright"),
        ("available in your country", "blocked in this country"),
        ("available from your location", "blocked in this country"),
        ("Private video", "upload private"),
        ("has been removed", "upload removed"),
        ("has been terminated", "upload removed"),
        ("Video unavailable", "upload unavailable"),
        ("This video is not available", "upload unavailable"),
        ("This video is unavailable", "upload unavailable"),
    ];

    public static bool IsPermanent(string? error, [NotNullWhen(true)] out string? reason)
    {
        reason = error is null
            ? null
            : Permanent
                .Where(entry => error.Contains(entry.Says, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Reason)
                .FirstOrDefault();

        return reason is not null;
    }
}
