using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class PauseStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string StorePath => Path.Combine(_directory, "paused.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Nothing_is_paused_until_the_user_says_so() =>
        Assert.Equal(new PausedStages(), new PauseStore(StorePath).Read());

    [Fact]
    public void A_pause_survives_a_restart_and_the_other_stage_keeps_running()
    {
        new PauseStore(StorePath).RememberJudging(paused: true);

        var restarted = new PauseStore(StorePath).Read();

        Assert.True(restarted.Judging);
        Assert.False(restarted.Separation);
    }

    [Fact]
    public void Resuming_is_remembered_too()
    {
        var store = new PauseStore(StorePath);

        store.RememberSeparation(paused: true);
        store.RememberSeparation(paused: false);

        Assert.False(new PauseStore(StorePath).Read().Separation);
    }

    [Fact]
    public void Judging_the_backfill_in_idle_windows_survives_a_restart_without_pausing_anything()
    {
        new PauseStore(StorePath).RememberBackfill(on: true);

        var restarted = new PauseStore(StorePath).Read();

        Assert.True(restarted.Backfill);
        Assert.False(restarted.Judging);
    }

    [Fact]
    public void An_unreadable_file_reads_as_nothing_paused()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "{ not json");

        Assert.Equal(new PausedStages(), new PauseStore(StorePath).Read());
    }
}
