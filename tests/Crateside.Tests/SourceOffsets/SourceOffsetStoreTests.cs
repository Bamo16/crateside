using Crateside.Core.SourceOffsets;

namespace Crateside.Tests.SourceOffsets;

public sealed class SourceOffsetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public SourceOffsetStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_measurement_survives_a_restart_and_stands_while_both_files_are_unchanged()
    {
        var (mix, source) = (Touch("mix.flac"), Touch("source.flac"));
        var file = Path.Combine(_root, "source-offsets.json");
        var record = new SourceOffsetRecord(
            "track",
            FileStamp.Of(mix)!,
            FileStamp.Of(source)!,
            new OffsetReading
            {
                Verdict = OffsetVerdict.Aligned,
                SampleRate = 44_100,
                WindowSeconds = 5,
                Offset = 19_470,
                Windows = [new(0, 19_470, 21.5, WindowState.Voting)],
            },
            new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)
        );

        new SourceOffsetStore(file).Save("upload", record);
        var back = new SourceOffsetStore(file).Of("upload");

        Assert.NotNull(back);
        Assert.Equal(19_470, back.Reading.Offset);
        Assert.Equal(OffsetVerdict.Aligned, back.Reading.Verdict);
        Assert.Equal(record.Source, back.Source);
        Assert.True(back.Stands);
        Assert.Null(new SourceOffsetStore(file).Of("another"));
    }

    [Fact]
    public void A_measurement_stops_standing_when_the_added_source_is_replaced()
    {
        var (mix, source) = (Touch("mix.flac"), Touch("source.flac"));
        var record = new SourceOffsetRecord(
            "track",
            FileStamp.Of(mix)!,
            FileStamp.Of(source)!,
            new OffsetReading
            {
                Verdict = OffsetVerdict.Aligned,
                SampleRate = 44_100,
                WindowSeconds = 5,
            },
            DateTimeOffset.UnixEpoch
        );

        File.AppendAllText(source, "more");

        Assert.False(record.Stands);
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, "audio");

        return path;
    }
}
