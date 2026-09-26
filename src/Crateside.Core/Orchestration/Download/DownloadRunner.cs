using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Download;

/// <summary>
/// Fetches one source through <c>stemforge-cli download</c>, which writes the provenance every
/// reader joins on (ADR 0005 §4).
/// </summary>
public sealed class DownloadRunner(string stemforge = "stemforge-cli")
{
    /// <param name="into">
    /// Where the file lands: the working set for a detection source, the stems output for an
    /// added one.
    /// </param>
    public async Task<DownloadResult> RunAsync(
        string url,
        string into,
        string format = "flac",
        CancellationToken ct = default
    )
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await ExternalProcess.RunAsync(
                stemforge,
                ["download", url, "--json", "--format", format, "--output", into],
                workingDirectory: null,
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return DownloadResult.Failed($"could not run {stemforge}: {ex.Message}");
        }

        if (!outcome.Succeeded)
            return DownloadResult.Failed(
                ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"stemforge-cli download exited with {outcome.ExitCode}"
            );

        // A zero exit still has to be checked: the CLI reports per-input failures inside its
        // JSON rather than by failing the whole process.
        return Parse(outcome.StandardOutput) switch
        {
            null => DownloadResult.Failed(
                "stemforge-cli download reported success but produced no readable JSON"
            ),
            [{ Succeeded: true, Path: { Length: > 0 } path } row, ..] => DownloadResult.Succeeded(
                path,
                new Acquisition
                {
                    PremiumStatus = Acquisition.StatusFrom(row.PremiumStatus),
                    IsPremium = row.IsPremium is true,
                }
            ),
            [{ Error: { Length: > 0 } error }, ..] => DownloadResult.Failed(error),
            _ => DownloadResult.Failed("stemforge-cli download failed without saying why"),
        };
    }

    private static IReadOnlyList<DownloadOutput>? Parse(string stdout)
    {
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<DownloadOutput>>(stdout[start..]);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>How one download turned out.</summary>
public sealed record DownloadResult
{
    public required bool IsSuccess { get; init; }

    /// <summary>Where the audio landed. Null unless it succeeded.</summary>
    public string? Path { get; init; }

    /// <summary>What the download said about its own quality. Null unless it succeeded.</summary>
    public Acquisition? Acquisition { get; init; }

    public string? Error { get; init; }

    public static DownloadResult Succeeded(string path, Acquisition acquisition) =>
        new()
        {
            IsSuccess = true,
            Path = path,
            Acquisition = acquisition,
        };

    public static DownloadResult Failed(string error) => new() { IsSuccess = false, Error = error };
}

/// <summary>One element of <c>stemforge-cli download</c>'s <c>--json</c> array.</summary>
internal sealed record DownloadOutput
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("isPremium")]
    public bool? IsPremium { get; init; }

    [JsonPropertyName("premiumStatus")]
    public string? PremiumStatus { get; init; }
}
