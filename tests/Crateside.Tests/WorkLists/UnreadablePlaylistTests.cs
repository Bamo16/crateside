using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

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

    private WorkListService Unreadable(IntakeStore intake, out CachedPlaylist playlist) =>
        UnreadableWorkLists.In(_directory, intake, out playlist);

    [Fact]
    public async Task The_scan_still_yields_what_crateside_has_taken_on()
    {
        var intake = new IntakeStore(PathIn("intake.json"));

        intake.Record([
            new WorkItem
            {
                PlaylistItemId = "PLI-known",
                TrackKey = Known,
                Title = "Eminem - Medicine Ball",
            },
        ]);

        var view = await Unreadable(intake, out _).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Known, Assert.Single(view.Items).TrackKey);
    }

    [Fact]
    public async Task The_refusal_is_reported_rather_than_thrown()
    {
        var service = Unreadable(new IntakeStore(PathIn("intake.json")), out var playlist);

        await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(playlist.Problem);
    }

    [Fact]
    public async Task A_scan_that_could_not_read_reports_no_problem_once_it_can()
    {
        var service = Unreadable(new IntakeStore(PathIn("intake.json")), out var playlist);

        var scan = await service.ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(playlist.Problem, scan.PlaylistProblem);
        Assert.NotNull(scan.PlaylistProblem);
    }
}
