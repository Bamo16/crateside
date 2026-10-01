using Crateside.Core;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.WorkLists;

/// <summary>A playlist that cannot be read means no new arrivals, not no queue (ADR 0021).</summary>
public sealed class UnreadablePlaylistTests : IDisposable
{
    private const string Known = "RzQRxV2XqFU";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public UnreadablePlaylistTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string PathIn(string name) => Path.Combine(_directory, name);

    /// <summary>A service whose reader has no credential, so every read refuses.</summary>
    private QueueService Unreadable(IntakeStore intake)
    {
        var options = Options.Create(new PlaylistOptions());
        var authorizer = new PlaylistAuthorizer(
            options,
            new ProtectedTokenStore(
                DataProtectionProvider.Create("crateside-tests"),
                PathIn("tokens")
            ),
            new PlaylistWriteAccess()
        );

        return new QueueService(
            Options.Create(
                new CratesidePaths
                {
                    SampleStaging = PathIn("staging"),
                    MashupSamples = PathIn("library"),
                    StemsKeyIdentified = PathIn("identified"),
                    StemsUnresolvedKey = PathIn("unresolved"),
                    Ledger = PathIn("runs.jsonl"),
                }
            ),
            new QueueSettingsStore(options, PathIn("queue.json")),
            new PlaylistReader(options),
            authorizer,
            new PlaylistRemover(authorizer),
            new BumpedTrack(),
            intake
        );
    }

    [Fact]
    public async Task The_scan_still_yields_what_crateside_has_taken_on()
    {
        var intake = new IntakeStore(PathIn("intake.json"));

        intake.Record([
            new QueueItem
            {
                PlaylistItemId = "PLI-known",
                VideoId = Known,
                Title = "Eminem - Medicine Ball",
            },
        ]);

        var view = await Unreadable(intake).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Known, Assert.Single(view.Items).VideoId);
    }

    [Fact]
    public async Task The_refusal_is_reported_rather_than_thrown()
    {
        var service = Unreadable(new IntakeStore(PathIn("intake.json")));

        await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(service.PlaylistProblem);
    }

    [Fact]
    public async Task A_scan_that_could_not_read_reports_no_problem_once_it_can()
    {
        var service = Unreadable(new IntakeStore(PathIn("intake.json")));

        var scan = await service.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(service.PlaylistProblem, scan.PlaylistProblem);
        Assert.NotNull(scan.PlaylistProblem);
    }
}
