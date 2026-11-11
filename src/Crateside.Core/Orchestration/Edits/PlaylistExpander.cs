using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>
/// Lists a public or unlisted YouTube playlist with the yt-dlp StemForge runs, so a playlist URL
/// needs no API key (#162). Listing downloads nothing; adding the videos goes through
/// <see cref="TrackAdd"/>.
/// </summary>
public sealed class PlaylistExpander(IProcessRunner processes, string stemforge = "stemforge-cli")
{
    private static readonly string[] YouTubeHosts =
    [
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com",
    ];

    /// <summary>Whether the input is a YouTube playlist page, not a video that happens to be in one.</summary>
    public static bool IsPlaylistUrl(string? input) =>
        input?.Trim() is { Length: > 0 } trimmed
        && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && YouTubeHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
        && uri.AbsolutePath.TrimEnd('/').Equals("/playlist", StringComparison.OrdinalIgnoreCase)
        && PlaylistReference.TryParse(trimmed, out _);

    public async Task<PlaylistListing> ListAsync(string url, CancellationToken ct = default)
    {
        var (ytdlp, problem) = await YtdlpPathAsync(ct);

        if (ytdlp is null)
            return new PlaylistListing.Unreadable(problem ?? "yt-dlp could not be found");

        ProcessOutcome outcome;

        try
        {
            outcome = await processes.RunAsync(
                new ProcessRequest(ytdlp, ["--flat-playlist", "-J", "--no-warnings", url.Trim()]),
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return new PlaylistListing.Unreadable($"could not run yt-dlp: {ex.Message}");
        }

        if (!outcome.Succeeded)
            return new PlaylistListing.Unreadable(
                outcome.LastLine ?? $"yt-dlp exited with {outcome.ExitCode}"
            );

        return ReadEntries(outcome.StandardOutput) switch
        {
            null => new PlaylistListing.Unreadable("yt-dlp did not return a playlist"),
            var entries => new PlaylistListing.Listed(entries),
        };
    }

    /// <summary>The path to yt-dlp as <c>stemforge-cli tools --json</c> reports it, or why it cannot.</summary>
    private async Task<(string? Path, string? Problem)> YtdlpPathAsync(CancellationToken ct)
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await processes.RunAsync(
                new ProcessRequest(stemforge, ["tools", "--json"]),
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return (null, $"could not run {stemforge}: {ex.Message}");
        }

        if (!outcome.Succeeded)
            return (
                null,
                "this stemforge-cli cannot say where yt-dlp is; update StemForge to a build with the tools command"
            );

        try
        {
            return JsonSerializer
                .Deserialize(
                    outcome.StandardOutput,
                    PlaylistJsonContext.Default.ListYtdlpToolReport
                )
                ?.FirstOrDefault(tool => tool.Kind is "yt-dlp") switch
            {
                { Installed: true, Path: { Length: > 0 } path } => (path, null),
                { } => (null, "StemForge has not installed yt-dlp yet; run a download once"),
                null => (null, "stemforge-cli tools does not list yt-dlp"),
            };
        }
        catch (JsonException)
        {
            return (null, "stemforge-cli tools --json was not readable");
        }
    }

    private static List<PlaylistEntry>? ReadEntries(string stdout)
    {
        try
        {
            return
                JsonSerializer.Deserialize(stdout, PlaylistJsonContext.Default.YtdlpPlaylist)
                    is { Entries: { } entries }
                ?
                [
                    .. entries
                        .Where(entry => YouTubeUrl.TryGetVideoId(entry.Id, out _))
                        .DistinctBy(entry => entry.Id, StringComparer.Ordinal)
                        .Select(entry => new PlaylistEntry(entry.Id!, entry.Title ?? entry.Id!)),
                ]
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What listing a playlist found.</summary>
public abstract record PlaylistListing
{
    private PlaylistListing() { }

    public sealed record Listed(IReadOnlyList<PlaylistEntry> Entries) : PlaylistListing;

    public sealed record Unreadable(string Problem) : PlaylistListing;
}

public sealed record PlaylistEntry(string VideoId, string Title)
{
    /// <summary>YouTube lists a removed or private video by a placeholder title and no way to fetch it.</summary>
    public bool IsUnavailable =>
        Title is "[Private video]" or "[Deleted video]" or "[Unavailable video]";
}

internal sealed record YtdlpPlaylist
{
    public IReadOnlyList<YtdlpPlaylistEntry>? Entries { get; init; }
}

internal sealed record YtdlpPlaylistEntry
{
    public string? Id { get; init; }

    public string? Title { get; init; }
}

internal sealed record YtdlpToolReport
{
    public string Kind { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public bool Installed { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(YtdlpPlaylist))]
[JsonSerializable(typeof(List<YtdlpToolReport>))]
internal sealed partial class PlaylistJsonContext : JsonSerializerContext { }
