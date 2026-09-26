using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Download;

/// <summary>
/// What an upload offers, asked with <c>--list-formats</c> before any audio is fetched (ADR 0019).
/// </summary>
public sealed class SourceProbe(string stemforge = "stemforge-cli")
{
    /// <summary>
    /// What the upload is and what it would arrive as. Never throws: a private, removed or
    /// unavailable video is an answer.
    /// </summary>
    public async Task<ProbedSource> ReadAsync(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await ExternalProcess.RunAsync(
                stemforge,
                ["download", url, "--list-formats", "--json"],
                workingDirectory: null,
                cancellationToken
            );
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            return ProbedSource.Unreadable($"could not run {stemforge}: {e.Message}");
        }

        // A non-zero exit means some or all inputs failed, which the JSON itself describes.
        return Parse(outcome.StandardOutput) switch
        {
            [{ Succeeded: true } row, ..] => new ProbedSource
            {
                Title = row.DisplayTitle ?? row.Title,
                BaseName = row.BaseName,
                Best =
                    row.Formats?.FirstOrDefault(format => format.IsAutoSelected)
                    ?? row.Formats?.FirstOrDefault(),
                FallsShort = row.PremiumShortfall is true,
            },
            [{ Error: { Length: > 0 } error }, ..] => ProbedSource.Unreadable(error),
            _ => ProbedSource.Unreadable("that upload could not be read"),
        };
    }

    private static IReadOnlyList<ProbeOutput>? Parse(string stdout)
    {
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<ProbeOutput>>(stdout[start..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>What an upload turned out to be, before anything was fetched.</summary>
public sealed record ProbedSource
{
    public string? Title { get; init; }

    /// <summary>The filename StemForge would give it, which a rename starts from.</summary>
    public string? BaseName { get; init; }

    /// <summary>The format a download would take, which is what the warning is about.</summary>
    public AudioFormat? Best { get; init; }

    /// <summary>No premium format was offered, so this is as good as the upload gets.</summary>
    public bool FallsShort { get; init; }

    public string? Problem { get; init; }

    public bool IsReadable => Problem is null;

    /// <summary>The quality, in the words the user needs to decide.</summary>
    public string Quality =>
        Best is { } format
            ? $"{format.Codec} at {format.BitrateKbps:N0} kbps"
            : "an unknown format";

    public static ProbedSource Unreadable(string problem) => new() { Problem = problem };
}

/// <summary>One format an upload offers.</summary>
public sealed record AudioFormat
{
    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    [JsonPropertyName("bitrateKbps")]
    public double BitrateKbps { get; init; }

    [JsonPropertyName("isPremium")]
    public bool IsPremium { get; init; }

    [JsonPropertyName("isAutoSelected")]
    public bool IsAutoSelected { get; init; }
}

/// <summary>One element of <c>download --list-formats --json</c>.</summary>
internal sealed record ProbeOutput
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("displayTitle")]
    public string? DisplayTitle { get; init; }

    [JsonPropertyName("baseName")]
    public string? BaseName { get; init; }

    [JsonPropertyName("premiumShortfall")]
    public bool? PremiumShortfall { get; init; }

    [JsonPropertyName("formats")]
    public IReadOnlyList<AudioFormat>? Formats { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
