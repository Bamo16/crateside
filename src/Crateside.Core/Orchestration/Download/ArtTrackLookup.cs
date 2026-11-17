using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Download;

/// <summary>
/// Asks <c>stemforge-cli art-track</c> for the song upload that matches a video, and remembers the
/// answer so a page view never reaches YouTube twice (ADR 0104).
/// </summary>
public sealed class ArtTrackLookup(
    IProcessRunner processes,
    CratesidePaths paths,
    TimeProvider time,
    string stemforge = "stemforge-cli"
)
{
    /// <summary>How long "no song found" stands before the next look.</summary>
    public static readonly TimeSpan NothingFoundFor = TimeSpan.FromDays(14);

    private readonly string _path = paths.ArtTracksFile;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private Dictionary<string, ArtTrackAnswer>? _answers;

    /// <summary>
    /// The suggestion for <paramref name="videoId"/>, null when there is none or StemForge could
    /// not say; only a real answer is kept, so a failure is tried again next time.
    /// </summary>
    public async Task<ArtTrackSuggestion?> FindAsync(string videoId, CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct);

        try
        {
            var answers = _answers ??=
                ArtTrackJsonContext.Default.DictionaryStringArtTrackAnswer.ReadFileOrDefault(_path)
                ?? [];

            if (
                answers.TryGetValue(videoId, out var kept)
                && (
                    kept.Suggestion is not null
                    || time.GetUtcNow() - kept.CheckedAt < NothingFoundFor
                )
            )
                return kept.Suggestion;

            if (await AskAsync(videoId, ct) is not { } output)
                return null;

            var suggestion = output is { Found: true, VideoId: { } id, Title: { } title }
                ? new ArtTrackSuggestion(
                    id,
                    title,
                    output.Artist ?? string.Empty,
                    output.LengthSeconds ?? 0,
                    output.LengthDifferenceSeconds ?? 0
                )
                : null;

            answers[videoId] = new ArtTrackAnswer(suggestion, time.GetUtcNow());
            Save(answers);

            return suggestion;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<ArtTrackOutput?> AskAsync(string videoId, CancellationToken ct)
    {
        ProcessOutcome outcome;

        try
        {
            // The link, not the id: an id may start with "-", which the command reads as an option.
            outcome = await processes.RunAsync(
                new ProcessRequest(
                    stemforge,
                    ["art-track", $"https://music.youtube.com/watch?v={videoId}"]
                )
                {
                    Timeout = ProcessRequest.QueryTimeout,
                },
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        if (!outcome.Succeeded)
            return null;

        try
        {
            return JsonSerializer.Deserialize(
                outcome.StandardOutput,
                StemForgeJsonContext.Default.ArtTrackOutput
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save(Dictionary<string, ArtTrackAnswer> answers)
    {
        try
        {
            ArtTrackJsonContext.Default.DictionaryStringArtTrackAnswer.WriteFile(_path, answers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory; a restart asks again.
        }
    }
}

/// <summary>The song upload that matches a video, and how far its length is from the video's.</summary>
public sealed record ArtTrackSuggestion(
    string VideoId,
    string Title,
    string Artist,
    int LengthSeconds,
    int LengthDifferenceSeconds
)
{
    public string Url => $"https://music.youtube.com/watch?v={VideoId}";
}

/// <summary>What one look found, and when, so a "nothing" can expire.</summary>
public sealed record ArtTrackAnswer(ArtTrackSuggestion? Suggestion, DateTimeOffset CheckedAt);

/// <summary>What <c>stemforge-cli art-track</c> prints.</summary>
public sealed record ArtTrackOutput(
    bool Found,
    string? VideoId,
    string? Title,
    string? Artist,
    int? LengthSeconds,
    int? LengthDifferenceSeconds
);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(Dictionary<string, ArtTrackAnswer>))]
internal sealed partial class ArtTrackJsonContext : JsonSerializerContext { }
