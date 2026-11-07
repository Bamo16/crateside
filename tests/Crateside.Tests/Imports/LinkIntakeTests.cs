using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Imports;

public sealed class LinkIntakeTests : IDisposable
{
    private readonly IntakeWorld _world = new();

    public void Dispose() => _world.Dispose();

    /// <summary>A stemforge-cli that writes a tagged FLAC into --output and says what the source was.</summary>
    private sealed class FakeDownload(
        IntakeWorld world,
        Func<string, object?> reports,
        string? failure = null
    ) : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Calls.Add(request.Arguments);

            if (failure is not null)
                return Task.FromResult(
                    new ProcessOutcome
                    {
                        ExitCode = 1,
                        StandardOutput = string.Empty,
                        StandardError = failure,
                    }
                );

            var output = request.Arguments[request.Arguments.ToList().IndexOf("--output") + 1];
            var url = request.Arguments[1];
            var file = world.TaggedFlac(output, "downloaded.flac", seed: url);

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 0,
                    StandardOutput = JsonSerializer.Serialize(
                        new[] { reports(file) },
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    ),
                    StandardError = string.Empty,
                }
            );
        }
    }

    private LinkIntake Links(FakeDownload download) =>
        new(
            new DownloadRunner(download),
            _world.Files,
            _world.Intake,
            new WorkingSet(_world.Paths),
            _world.Paths
        );

    private static Dictionary<string, object?> Source(
        string key,
        string? id,
        bool generic,
        string? uploader = null
    ) =>
        new()
        {
            ["extractorKey"] = key,
            ["id"] = id,
            ["isGeneric"] = generic,
            ["uploader"] = uploader,
        };

    private static Func<string, object?> Reply(Dictionary<string, object?> source) =>
        path =>
        {
            var row = new Dictionary<string, object?>
            {
                ["succeeded"] = true,
                ["path"] = path,
                ["premiumStatus"] = "NotApplicable",
            };

            foreach (var (name, value) in source)
                row[name] = value;

            return row;
        };

    [Fact]
    public async Task A_direct_link_becomes_a_file_keyed_track_in_the_imports()
    {
        var download = new FakeDownload(
            _world,
            Reply(Source("Generic", "tone.flac", generic: true))
        );

        var outcome = await Links(download)
            .TakeInAsync("http://127.0.0.1/tone.flac", ct: TestContext.Current.CancellationToken);

        var taken = Assert.IsType<IntakeOutcome.TakenIn>(outcome);

        Assert.StartsWith("file~", taken.TrackKey);
        Assert.Equal("Be For U - GRADUATION", taken.Name);
        Assert.True(_world.Imports.TryGetFile(taken.TrackKey, out _));
        Assert.Equal("http://127.0.0.1/tone.flac", _world.Intake.Read().Single().PageUrl);
        Assert.Equal("download", download.Calls.Single()[0]);
    }

    [Fact]
    public async Task A_site_track_is_keyed_by_its_extractor_and_id_and_stays_in_the_working_set()
    {
        var download = new FakeDownload(
            _world,
            Reply(Source("Soundcloud", "242136202", generic: false, "djfoo"))
        );

        var outcome = await Links(download)
            .TakeInAsync(
                "https://soundcloud.com/djfoo/track",
                ct: TestContext.Current.CancellationToken
            );

        var taken = Assert.IsType<IntakeOutcome.TakenIn>(outcome);

        Assert.Equal("soundcloud~242136202", taken.TrackKey);
        Assert.True(new WorkingSet(_world.Paths).TryGetSource(taken.TrackKey, out var held));
        Assert.StartsWith(_world.Paths.WorkingFolder, held);
        Assert.False(_world.Imports.TryGetFile(taken.TrackKey, out _));
        Assert.Equal("https://soundcloud.com/djfoo/track", _world.Intake.Read().Single().PageUrl);
    }

    [Fact]
    public async Task A_link_already_taken_in_is_not_downloaded_again()
    {
        var download = new FakeDownload(_world, Reply(Source("Soundcloud", "1", generic: false)));
        var links = Links(download);

        await links.TakeInAsync(
            "https://soundcloud.com/a/b",
            ct: TestContext.Current.CancellationToken
        );
        var again = await links.TakeInAsync(
            "https://soundcloud.com/a/b",
            ct: TestContext.Current.CancellationToken
        );

        Assert.IsType<IntakeOutcome.AlreadyHeld>(again);
        Assert.Single(download.Calls);
    }

    [Fact]
    public async Task A_failed_download_is_refused_with_its_reason_and_leaves_nothing_behind()
    {
        var outcome = await Links(new FakeDownload(_world, Reply([]), failure: "HTTP Error 404"))
            .TakeInAsync(
                "https://example.com/gone.flac",
                ct: TestContext.Current.CancellationToken
            );

        Assert.Equal("HTTP Error 404", Assert.IsType<IntakeOutcome.Refused>(outcome).Reason);
        Assert.Empty(_world.Intake.Read());
        Assert.Empty(
            Directory.EnumerateDirectories(Path.Combine(_world.Paths.ImportsFolder, ".incoming"))
        );
    }

    [Fact]
    public async Task An_old_stemforge_that_does_not_say_what_the_source_is_is_named_as_too_old()
    {
        var outcome = await Links(new FakeDownload(_world, path => new { succeeded = true, path }))
            .TakeInAsync("https://example.com/a.flac", ct: TestContext.Current.CancellationToken);

        Assert.Equal(
            LinkIntake.StemForgeTooOld,
            Assert.IsType<IntakeOutcome.Refused>(outcome).Reason
        );
        Assert.Empty(_world.Intake.Read());
    }
}
