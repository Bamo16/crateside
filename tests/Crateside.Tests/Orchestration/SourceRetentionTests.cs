using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration;

public sealed class SourceRetentionTests : IDisposable
{
    private static readonly DateTimeOffset Left = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(7, 6, false)]
    [InlineData(7, 7, true)]
    public void A_number_of_days_lapses_that_long_after_the_track_left(
        int days,
        int elapsed,
        bool lapsed
    ) =>
        Assert.Equal(
            lapsed,
            new SourceRetention { Days = days }.HasLapsed(Left, Left.AddDays(elapsed))
        );

    [Fact]
    public void Forever_never_lapses() =>
        Assert.False(new SourceRetention { Forever = true }.HasLapsed(Left, Left.AddYears(100)));

    [Fact]
    public void The_default_is_zero_days_so_nothing_changes_until_the_user_chooses() =>
        Assert.True(
            new SourceRetentionStore(Path.Combine(_directory, "r.json")).Read().KeepsNothing
        );

    [Fact]
    public void The_choice_survives_a_restart()
    {
        var path = Path.Combine(_directory, "r.json");
        new SourceRetentionStore(path).Remember(new SourceRetention { Days = 45 });

        Assert.Equal(45, new SourceRetentionStore(path).Read().Days);

        new SourceRetentionStore(path).Remember(new SourceRetention { Forever = true });

        Assert.True(new SourceRetentionStore(path).Read().Forever);
    }

    [Fact]
    public void A_negative_number_of_days_is_stored_as_zero()
    {
        var path = Path.Combine(_directory, "r.json");
        new SourceRetentionStore(path).Remember(new SourceRetention { Days = -3 });

        Assert.Equal(0, new SourceRetentionStore(path).Read().Days);
    }

    [Fact]
    public async Task A_failed_backfill_download_leaves_no_folder_behind_to_be_mistaken_for_a_source()
    {
        var set = new WorkingSet(Path.Combine(_directory, "working"));
        var backfill = new SourceBackfill(
            TestLedger.Store(),
            set,
            new DownloadRunner(Path.Combine(_directory, "no-such-stemforge.exe")),
            TimeProvider.System,
            new CratesidePaths
            {
                StagingFolder = Path.Combine(_directory, "Staging"),
                LibraryFolder = Path.Combine(_directory, "Library"),
            }
        );

        var results = await backfill.RunAsync(
            ["dQw4w9WgXcQ"],
            ct: TestContext.Current.CancellationToken
        );

        Assert.NotNull(Assert.Single(results).Error);
        Assert.Empty(set.VideoIds);
    }

    [Fact]
    public void The_index_lists_a_held_source_and_is_rewritten_only_when_it_changes()
    {
        var set = new WorkingSet(Path.Combine(_directory, "working"));
        File.WriteAllText(Path.Combine(set.Prepare("dQw4w9WgXcQ"), "Song.flac"), "audio");
        var path = Path.Combine(_directory, "source-index.json");
        var index = new SourceIndex(set, path);
        var ledger = TestLedger.From(string.Empty);
        var processed = ProcessedTracks.Scan(ledger, [Path.Combine(_directory, "nowhere")]);

        index.Refresh(processed, ledger);
        var written = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, written.AddHours(-1));
        index.Refresh(processed, ledger);

        var json = File.ReadAllText(path);
        Assert.Contains("\"videoId\": \"dQw4w9WgXcQ\"", json);
        Assert.Contains("\"location\": \"WorkingSet\"", json);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(path));
    }
}
