using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Plans;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Edits;

public sealed class PlaylistUrlAddTests : IDisposable
{
    private const string Playlist = "https://www.youtube.com/playlist?list=PLabcdefghijklmnop";
    private const string Fresh = "aaaaaaaaaaa";
    private const string Held = "bbbbbbbbbbb";
    private const string Private = "ccccccccccc";
    private const string Poor = "ddddddddddd";

    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PLabcdefghijklmnop", true)]
    [InlineData("https://music.youtube.com/playlist?list=OLAK5uy_abcdefghijk&si=x", true)]
    [InlineData("https://www.youtube.com/watch?v=aaaaaaaaaaa&list=PLabcdefghijklmnop", false)]
    [InlineData("aaaaaaaaaaa", false)]
    [InlineData("https://example.com/playlist?list=PLabcdefghijklmnop", false)]
    [InlineData("https://www.youtube.com/playlist", false)]
    public void Only_a_playlist_page_counts_as_a_playlist_url(string input, bool expected) =>
        Assert.Equal(expected, PlaylistExpander.IsPlaylistUrl(input));

    [Fact]
    public async Task Every_new_video_in_a_playlist_is_added_and_the_rest_are_accounted_for()
    {
        var runner = new Scripted();
        var settings = Settings();
        var outcome = await Adder(runner, settings)
            .AddAsync(Playlist, [Row(Held)], ct: TestContext.Current.CancellationToken);

        Assert.Null(outcome.Problem);
        Assert.Equal([Fresh], outcome.Added.Select(entry => entry.VideoId));
        Assert.Equal(1, outcome.AlreadyHeld);
        Assert.Equal([Private, Poor], outcome.Skipped.Select(skipped => skipped.Entry.VideoId));
        Assert.Equal("private or removed", outcome.Skipped[0].Why);
        Assert.Contains("premium", outcome.Skipped[1].Why);
        Assert.True(settings.Read().Single(track => track.TrackKey == Fresh).AddedByUrl);
        Assert.DoesNotContain(settings.Read(), track => track.TrackKey is Held or Private or Poor);
        Assert.Equal("yt-dlp-from-stemforge", runner.Playlisted?.FileName);
        Assert.Contains("--flat-playlist", runner.Playlisted!.Arguments);
        Assert.StartsWith(
            "Added 1 track. 1 already held. 2 left out: [Private video] (private or removed); Cheap (",
            outcome.Summary
        );
    }

    [Fact]
    public async Task A_stemforge_without_the_tools_command_says_so_and_adds_nothing()
    {
        var runner = new Scripted { ToolsExit = 2 };
        var settings = Settings();
        var outcome = await Adder(runner, settings)
            .AddAsync(Playlist, [], ct: TestContext.Current.CancellationToken);

        Assert.Contains("tools command", outcome.Problem);
        Assert.Empty(settings.Read());
        Assert.Null(runner.Playlisted);
    }

    [Fact]
    public async Task A_playlist_yt_dlp_cannot_read_is_a_problem_with_its_own_words()
    {
        var runner = new Scripted { PlaylistExit = 1 };
        var outcome = await Adder(runner, Settings())
            .AddAsync(Playlist, [], ct: TestContext.Current.CancellationToken);

        Assert.Equal("ERROR: playlist does not exist", outcome.Problem);
    }

    private static TrackInstructionStore Settings() =>
        new(new LedgerStore(LedgerStore.InMemory, TimeProvider.System));

    private PlaylistUrlAdd Adder(Scripted runner, TrackInstructionStore settings)
    {
        Directory.CreateDirectory(_scratch);

        return new PlaylistUrlAdd(
            new PlaylistExpander(runner),
            new TrackAdd(
                TestLedger.Store(Path.Combine(_scratch, "runs.jsonl")),
                settings,
                new SourceProbe(runner),
                new ResolvedNameStore(Path.Combine(_scratch, "names.json"))
            )
        );
    }

    private static TrackRowFacts Row(string key) =>
        new()
        {
            TrackKey = key,
            Plan = StemPlan.Default,
            Uploads = [key],
        };

    /// <summary>Answers StemForge's tools and probe and yt-dlp's playlist listing from a script.</summary>
    private sealed class Scripted : IProcessRunner
    {
        public int ToolsExit { get; init; }

        public int PlaylistExit { get; init; }

        public ProcessRequest? Playlisted { get; private set; }

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (request is { FileName: "stemforge-cli", Arguments: ["tools", ..] })
                return Task.FromResult(
                    Outcome(
                        ToolsExit,
                        """[{"kind":"ffmpeg","path":"f.exe","installed":true},{"kind":"yt-dlp","path":"yt-dlp-from-stemforge","installed":true}]"""
                    )
                );

            if (request.FileName is "yt-dlp-from-stemforge")
            {
                Playlisted = request;

                return Task.FromResult(
                    PlaylistExit is 0
                        ? Outcome(
                            0,
                            $$"""
                            {"entries":[
                              {"id":"{{Fresh}}","title":"Fresh"},
                              {"id":"{{Held}}","title":"Held"},
                              {"id":"{{Private}}","title":"[Private video]"},
                              {"id":"{{Poor}}","title":"Cheap"},
                              {"id":"{{Fresh}}","title":"Fresh again"}
                            ]}
                            """
                        )
                        : Outcome(PlaylistExit, string.Empty, "ERROR: playlist does not exist")
                );
            }

            var shortfall = request.Arguments[1].Contains(Poor);

            return Task.FromResult(
                Outcome(
                    0,
                    $$"""[{"succeeded":true,"title":"T","displayTitle":"Artist - T","baseName":"Artist - T","premiumShortfall":{{(shortfall ? "true" : "false")}}}]"""
                )
            );
        }

        private static ProcessOutcome Outcome(int exit, string stdout, string stderr = "") =>
            new()
            {
                ExitCode = exit,
                StandardOutput = stdout,
                StandardError = stderr,
            };
    }
}
