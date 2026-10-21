using Crateside.Core;
using Crateside.Core.Watching;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Watching;

public sealed class FileChangesTests
{
    private static readonly TimeSpan Timer = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task A_burst_of_changes_wakes_the_listener_once_two_seconds_after_the_last()
    {
        var changes = new FileChanges(_time);
        var wait = changes.Listen().WaitAsync(Timer, CancellationToken.None);

        changes.Notify();
        _time.Advance(TimeSpan.FromSeconds(1.5));
        changes.Notify();
        _time.Advance(TimeSpan.FromSeconds(1.5));

        Assert.False(wait.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(0.5));
        await wait;

        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_change_while_the_listener_was_busy_is_not_missed()
    {
        var changes = new FileChanges(_time);
        var listener = changes.Listen();

        changes.Notify();
        _time.Advance(FileChanges.Quiet);

        await listener.WaitAsync(Timer, CancellationToken.None);
    }

    [Fact]
    public async Task A_change_wakes_the_listener_once_and_the_next_wait_runs_out_its_timer()
    {
        var changes = new FileChanges(_time);
        var listener = changes.Listen();
        changes.Notify();
        _time.Advance(FileChanges.Quiet);
        await listener.WaitAsync(Timer, CancellationToken.None);

        var wait = listener.WaitAsync(Timer, CancellationToken.None);
        _time.Advance(Timer - TimeSpan.FromSeconds(1));

        Assert.False(wait.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));
        await wait;
    }

    [Fact]
    public async Task A_deliberate_change_wakes_the_listener_without_waiting_out_the_quiet()
    {
        var changes = new FileChanges(_time);
        var wait = changes.Listen().WaitAsync(Timer, CancellationToken.None);

        changes.NotifyNow();

        await wait;

        Assert.True(wait.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Every_listener_is_woken_by_the_same_change()
    {
        var changes = new FileChanges(_time);
        var first = changes.Listen().WaitAsync(Timer, CancellationToken.None);
        var second = changes.Listen().WaitAsync(Timer, CancellationToken.None);

        changes.Notify();
        _time.Advance(FileChanges.Quiet);

        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task A_cancelled_wait_throws()
    {
        using var cancel = new CancellationTokenSource();
        var wait = new FileChanges(_time).Listen().WaitAsync(Timer, cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}

public sealed class FileWatchersTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _staging;
    private readonly string _library;

    public FileWatchersTests()
    {
        _staging = Path.Combine(_root, "Staging");
        _library = Path.Combine(_root, "Library");
        Directory.CreateDirectory(_staging);
        Directory.CreateDirectory(Path.Combine(_library, "Artist"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("Staging", "Song (Drums).flac")]
    [InlineData("Staging", "Song (Drums).flac.asd")]
    [InlineData("Library", "Artist/Song (Drums).flac")]
    public async Task Saving_a_file_wakes_the_listener_within_seconds(string root, string file)
    {
        var (changes, listener) = Listening(out var watchers);
        using var _ = watchers;

        File.WriteAllText(Path.Combine(root is "Staging" ? _staging : _library, file), "x");

        await listener.WaitAsync(TimeSpan.FromMinutes(10), Within(15));
    }

    [Fact]
    public async Task A_file_in_a_folder_of_Staging_is_not_watched()
    {
        var (_, listener) = Listening(out var watchers);
        using var _ = watchers;
        Directory.CreateDirectory(Path.Combine(_staging, "Sub"));
        await listener.WaitAsync(TimeSpan.FromMinutes(10), Within(15));

        File.WriteAllText(Path.Combine(_staging, "Sub", "Song.flac"), "x");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            listener.WaitAsync(TimeSpan.FromMinutes(10), Within(5))
        );
    }

    [Fact]
    public async Task Something_that_is_not_audio_or_a_sidecar_wakes_nothing()
    {
        var (_, listener) = Listening(out var watchers);
        using var _ = watchers;

        File.WriteAllText(Path.Combine(_staging, "notes.txt"), "x");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            listener.WaitAsync(TimeSpan.FromMinutes(10), Within(5))
        );
    }

    private (FileChanges, FileChanges.Listener) Listening(out FileWatchers watchers)
    {
        var changes = new FileChanges(TimeProvider.System);
        watchers = new FileWatchers(
            new CratesidePaths { StagingFolder = _staging, LibraryFolder = _library },
            changes
        );

        return (changes, changes.Listen());
    }

    private static CancellationToken Within(int seconds) =>
        new CancellationTokenSource(TimeSpan.FromSeconds(seconds)).Token;
}
