using System.Text.Json;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Orchestration.Download;

/// <summary>
/// Fetches one source through <c>stemforge-cli download</c>, which writes the provenance every
/// reader joins on (ADR 0005 §4).
/// </summary>
public sealed class DownloadRunner(IProcessRunner processes, string stemforge = "stemforge-cli")
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
            outcome = await processes.RunAsync(
                new ProcessRequest(
                    stemforge,
                    ["download", url, "--json", "--format", format, "--output", into]
                ),
                ct
            );
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return DownloadResult.Failed($"could not run {stemforge}: {ex.Message}");
        }

        if (!outcome.Succeeded)
            return DownloadResult.Failed(
                outcome.LastLine ?? $"stemforge-cli download exited with {outcome.ExitCode}"
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
            ) with
            {
                Summary = Summarize(path, row),
                Source = row.ExtractorKey is null
                    ? null
                    : new DownloadedSource
                    {
                        ExtractorKey = row.ExtractorKey,
                        Id = row.Id,
                        IsGeneric = row.IsGeneric is true,
                        Uploader = row.Uploader,
                        UploadDate = row.UploadDate,
                        ReleaseYear = row.ReleaseYear,
                        Album = row.Album,
                        AlbumArtist = row.AlbumArtist,
                        TrackNumber = row.TrackNumber,
                    },
            },
            [{ Error: { Length: > 0 } error }, ..] => DownloadResult.Failed(error),
            _ => DownloadResult.Failed("stemforge-cli download failed without saying why"),
        };
    }

    private static string Summarize(string path, DownloadOutput row) =>
        string.Join(
            ' ',
            new[]
            {
                "downloaded",
                Path.GetExtension(path).TrimStart('.'),
                row.BitrateKbps is { } kbps ? $"{kbps:0} kbps" : null,
                row.FormatId is { Length: > 0 } format ? $"(format {format})" : null,
            }.Where(part => part is { Length: > 0 })
        );

    private static List<DownloadOutput>? Parse(string stdout)
    {
        var start = stdout.IndexOf('[');

        if (start < 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize(
                stdout[start..],
                StemForgeJsonContext.Default.ListDownloadOutput
            );
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

    /// <summary>What was fetched, in a line for the activity feed (#170). Null unless it succeeded.</summary>
    public string? Summary { get; init; }

    /// <summary>What identifies the source; null when StemForge is too old to say.</summary>
    public DownloadedSource? Source { get; init; }

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
    public bool Succeeded { get; init; }

    public string? Path { get; init; }

    public string? Error { get; init; }

    public bool? IsPremium { get; init; }

    public string? PremiumStatus { get; init; }

    public string? FormatId { get; init; }

    public double? BitrateKbps { get; init; }

    public string? ExtractorKey { get; init; }

    public string? Id { get; init; }

    public bool? IsGeneric { get; init; }

    public string? Uploader { get; init; }

    public string? UploadDate { get; init; }

    public int? ReleaseYear { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public int? TrackNumber { get; init; }
}

/// <summary>What <c>stemforge-cli download --json</c> says the source was.</summary>
public sealed record DownloadedSource
{
    /// <summary>yt-dlp's extractor key, such as <c>Soundcloud</c> or <c>Generic</c>.</summary>
    public required string ExtractorKey { get; init; }

    public string? Id { get; init; }

    /// <summary>A direct link to a media file, which yt-dlp reads with its generic extractor.</summary>
    public bool IsGeneric { get; init; }

    public string? Uploader { get; init; }

    public string? UploadDate { get; init; }

    public int? ReleaseYear { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public int? TrackNumber { get; init; }
}
