using Crateside.Core;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Loop;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Download;

public sealed class ArtTrackLookupTests : IDisposable
{
    private const string Found =
        """{"found":true,"videoId":"LcTV5ceBb_I","title":"STREETPOP (feat. RedDash)","artist":"Wävė","lengthSeconds":200,"lengthDifferenceSeconds":-1}""";

    private const string Nothing = """{"found":false}""";

    private readonly string _directory = Directory.CreateTempSubdirectory("art-track-").FullName;
    private readonly FakeTimeProvider _time = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ArtTrackLookup Lookup(CannedRunner runner) =>
        new(
            runner,
            new CratesidePaths { ArtTracksFile = Path.Combine(_directory, "art-tracks.json") },
            _time
        );

    [Fact]
    public async Task A_found_song_is_returned_and_asked_for_by_link()
    {
        var runner = new CannedRunner(0, Found);

        var suggestion = await Lookup(runner)
            .FindAsync("-pj24dQYGRc", TestContext.Current.CancellationToken);

        Assert.Equal(
            new ArtTrackSuggestion("LcTV5ceBb_I", "STREETPOP (feat. RedDash)", "Wävė", 200, -1),
            suggestion
        );
        Assert.Equal(
            ["art-track", "https://music.youtube.com/watch?v=-pj24dQYGRc"],
            runner.Requests.Single().Arguments
        );
    }

    [Fact]
    public async Task A_found_song_is_asked_for_once_even_across_a_restart()
    {
        var runner = new CannedRunner(0, Found);

        await Lookup(runner).FindAsync("-pj24dQYGRc", TestContext.Current.CancellationToken);
        var again = await Lookup(runner)
            .FindAsync("-pj24dQYGRc", TestContext.Current.CancellationToken);

        Assert.Equal("LcTV5ceBb_I", again?.VideoId);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Nothing_found_is_kept_for_two_weeks_then_asked_again()
    {
        var runner = new CannedRunner(0, Nothing);
        var lookup = Lookup(runner);

        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));
        _time.Advance(ArtTrackLookup.NothingFoundFor - TimeSpan.FromHours(1));
        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));
        Assert.Single(runner.Requests);

        _time.Advance(TimeSpan.FromHours(2));
        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));
        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task A_failed_run_is_not_kept_so_the_next_view_tries_again()
    {
        var runner = new CannedRunner(1, string.Empty);
        var lookup = Lookup(runner);

        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));
        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));

        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task Output_that_is_not_json_is_not_kept()
    {
        var runner = new CannedRunner(0, "Error: Unknown command 'art-track'.");
        var lookup = Lookup(runner);

        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));
        Assert.Null(await lookup.FindAsync("aaaaaaaaaaa", TestContext.Current.CancellationToken));

        Assert.Equal(2, runner.Requests.Count);
    }

    private sealed class CannedRunner(int exitCode, string output) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = exitCode,
                    StandardOutput = output,
                    StandardError = string.Empty,
                }
            );
        }
    }
}
