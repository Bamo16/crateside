using Crateside.Core.SourceOffsets;
using Crateside.Core.Warps;

namespace Crateside.Tests.SourceOffsets;

public sealed class SourceOffsetPassTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly SourceOffsetStore _store = new();
    private readonly CountingDecoder _decoder = new();

    public SourceOffsetPassTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_moved_source_keeps_its_measurement_and_its_new_path_is_stored()
    {
        var (mix, source) = (Touch("mix", "mix.flac"), Touch("staging", "source.flac"));
        Remember(mix, source);
        var moved = Move(source, "library");

        var measured = await Pass()
            .SettleAsync(Pair(mix, moved), TestContext.Current.CancellationToken);

        Assert.False(measured);
        Assert.Equal(0, _decoder.Calls);
        Assert.Equal(moved, _store.Of("added")!.Source.Path);
        Assert.True(_store.Of("added")!.IsUsable);
    }

    [Fact]
    public async Task A_moved_mix_keeps_its_measurement_and_its_new_path_is_stored()
    {
        var (mix, source) = (Touch("working", "mix.flac"), Touch("library", "source.flac"));
        Remember(mix, source);
        var moved = Move(mix, "elsewhere");

        await Pass().SettleAsync(Pair(moved, source), TestContext.Current.CancellationToken);

        Assert.Equal(0, _decoder.Calls);
        Assert.Equal(moved, _store.Of("added")!.Mix.Path);
    }

    [Fact]
    public async Task A_source_whose_content_changed_is_measured_again_after_a_move()
    {
        var (mix, source) = (Touch("mix", "mix.flac"), Touch("staging", "source.flac"));
        Remember(mix, source);
        var moved = Move(source, "library");
        File.AppendAllText(moved, "more");

        await Pass().SettleAsync(Pair(mix, moved), TestContext.Current.CancellationToken);

        Assert.True(_decoder.Calls > 0);
    }

    [Fact]
    public void A_moved_source_is_usable_at_its_new_path_before_any_pass_runs()
    {
        var (mix, source) = (Touch("mix", "mix.flac"), Touch("staging", "source.flac"));
        Remember(mix, source);
        var moved = Move(source, "library");

        Assert.True(_store.Of("added")!.IsUsableAt(moved));
    }

    [Fact]
    public void A_changed_source_is_not_usable_at_its_new_path()
    {
        var (mix, source) = (Touch("mix", "mix.flac"), Touch("staging", "source.flac"));
        Remember(mix, source);
        var moved = Move(source, "library");
        File.AppendAllText(moved, "more");

        Assert.False(_store.Of("added")!.IsUsableAt(moved));
    }

    [Fact]
    public void A_second_move_of_the_same_source_is_new_work()
    {
        var staging = Pair("mix.flac", Path.Combine("staging", "source.flac"));
        var library = Pair("mix.flac", Path.Combine("library", "source.flac"));

        Assert.NotEqual(staging.WorkKey, library.WorkKey);
        Assert.Equal(
            staging.WorkKey,
            Pair("mix.flac", Path.Combine("staging", "source.flac")).WorkKey
        );
    }

    private SourceOffsetPass Pass() => new(_decoder, _store, TimeProvider.System);

    private static SourceOffsetPair Pair(string mix, string source) =>
        new("main", "added", "Official Instrumental", mix, source);

    private void Remember(string mix, string source) =>
        _store.Save(
            "added",
            new SourceOffsetRecord(
                "main",
                FileStamp.Of(mix)!,
                FileStamp.Of(source)!,
                new OffsetReading
                {
                    Verdict = OffsetVerdict.Aligned,
                    SampleRate = 44_100,
                    WindowSeconds = 5,
                    Offset = 100,
                },
                DateTimeOffset.UnixEpoch
            )
        );

    private string Touch(string folder, string name)
    {
        var path = Path.Combine(_root, folder, name);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "audio");

        return path;
    }

    private string Move(string path, string folder)
    {
        var target = Path.Combine(_root, folder, Path.GetFileName(path));

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target);

        return target;
    }

    private sealed class CountingDecoder : IAudioDecoder
    {
        public int Calls { get; private set; }

        public Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default)
        {
            Calls++;

            return Task.FromResult<DecodedAudio?>(null);
        }
    }
}
