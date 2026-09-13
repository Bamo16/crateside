using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Driven against real child processes rather than mocks, for the same reason
/// <see cref="SeparationRunnerTests"/> is: the argument lists, the stdout plumbing and the
/// exit-code handling are the parts most likely to be wrong, and a fake exercises none of them.
/// </summary>
public class DownloadRunnerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public DownloadRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Script(string body)
    {
        var path = Path.Combine(_directory, "fake-stemforge.cmd");

        File.WriteAllText(path, $"@echo off{Environment.NewLine}{body}");

        return path;
    }

    private DownloadRunner Runner(string stdout, int exitCode = 0) =>
        new(
            Script(
                $"""
                echo {stdout}
                exit /b {exitCode}
                """
            )
        );

    private static string Says(string path, string? premiumStatus = null, bool isPremium = false) =>
        premiumStatus is null
            ? $$"""[{"succeeded":true,"path":"{{path.Replace(@"\", @"\\")}}"}]"""
            : $$"""
                [{"succeeded":true,"path":"{{path.Replace(@"\", @"\\")}}","isPremium":{{(
                isPremium ? "true" : "false"
            )}},"premiumStatus":"{{premiumStatus}}"}]
                """;

    private Task<DownloadResult> RunAsync(string stdout, int exitCode = 0) =>
        Runner(stdout, exitCode)
            .RunAsync(
                "https://example/watch",
                _directory,
                "flac",
                TestContext.Current.CancellationToken
            );

    [Fact]
    public async Task A_finished_download_reports_where_the_audio_landed()
    {
        var result = await RunAsync(Says(@"C:\working\track.flac"));

        Assert.True(result.IsSuccess);
        Assert.Equal(@"C:\working\track.flac", result.Path);
    }

    [Fact]
    public async Task A_failed_download_says_what_the_downloader_said()
    {
        var result = await RunAsync("ERROR: video unavailable", exitCode: 1);

        Assert.False(result.IsSuccess);
        Assert.Contains("video unavailable", result.Error);
    }

    [Fact]
    public async Task A_per_input_failure_is_read_out_of_the_json_despite_a_clean_exit()
    {
        // The CLI reports per-input failures inside its JSON rather than by failing the process,
        // so a zero exit is not an answer on its own.
        var result = await RunAsync("""[{"succeeded":false,"error":"ffmpeg not found"}]""");

        Assert.False(result.IsSuccess);
        Assert.Contains("ffmpeg not found", result.Error);
    }

    [Fact]
    public async Task Unreadable_output_is_a_failure_rather_than_a_guess()
    {
        var result = await RunAsync("not json at all");

        Assert.False(result.IsSuccess);
        Assert.Contains("no readable JSON", result.Error);
    }

    [Fact]
    public async Task What_the_download_got_is_kept_instead_of_being_parsed_past()
    {
        // These fields arrive on every download and were once discarded, which is what left
        // crateside predicting audio quality it was already being told (ADR 0016).
        var result = await RunAsync(Says(@"C:\t.flac", "NoPremiumLadder", isPremium: false));

        Assert.Equal(PremiumStatus.NoPremiumLadder, result.Acquisition?.PremiumStatus);
        Assert.False(result.Acquisition?.IsPremium);
        Assert.True(result.Acquisition?.Stops);
    }

    [Fact]
    public async Task A_premium_download_is_recorded_too_and_stops_nothing()
    {
        var result = await RunAsync(Says(@"C:\t.flac", "Premium", isPremium: true));

        Assert.Equal(PremiumStatus.Premium, result.Acquisition?.PremiumStatus);
        Assert.True(result.Acquisition?.IsPremium);
        Assert.False(result.Acquisition?.Stops);
    }

    [Fact]
    public async Task A_download_that_said_nothing_about_premium_records_no_finding()
    {
        // A StemForge too old to report the fields must not read as a shortfall.
        var result = await RunAsync(Says(@"C:\t.flac"));

        Assert.Equal(PremiumStatus.Unreported, result.Acquisition?.PremiumStatus);
        Assert.False(result.Acquisition?.Stops);
    }

    [Fact]
    public async Task A_downloader_that_is_not_there_is_an_answer_rather_than_a_throw()
    {
        var result = await new DownloadRunner("stemforge-cli-that-does-not-exist").RunAsync(
            "https://example/watch",
            _directory,
            "flac",
            TestContext.Current.CancellationToken
        );

        Assert.False(result.IsSuccess);
        Assert.Contains("could not run", result.Error);
    }
}
