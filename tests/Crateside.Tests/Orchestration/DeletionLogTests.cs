using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class DeletionLogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public DeletionLogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Sample(string name, string videoId)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.3.1.0 | preset: Vocal - Full | source: https://www.youtube.com/watch?v={videoId}";
        file.Save();

        return path;
    }

    [Fact]
    public void A_recycled_sample_is_recorded_with_its_upload_and_why_and_survives_a_restart()
    {
        var path = Sample("Song (Vocal - Full).flac", "aaaaaaaaaaa");

        new DeletionLog(Path.Combine(_root, "deletions.jsonl")).Recycle(
            path,
            "superseded by a swap"
        );

        Assert.False(File.Exists(path));

        var deletion = Assert.Single(new DeletionLog(Path.Combine(_root, "deletions.jsonl")).All());
        Assert.Equal("aaaaaaaaaaa", deletion.VideoId);
        Assert.Equal("Song (Vocal - Full).flac", deletion.FileName);
        Assert.Equal("superseded by a swap", deletion.Reason);
    }

    [Fact]
    public void A_file_already_gone_records_nothing()
    {
        var log = new DeletionLog(Path.Combine(_root, "deletions.jsonl"));

        log.Recycle(Path.Combine(_root, "absent.flac"), "anything");

        Assert.Empty(log.All());
    }

    [Fact]
    public void Nowhere_still_recycles()
    {
        var path = Sample("Song (Vocal - Full).flac", "aaaaaaaaaaa");

        DeletionLog.None.Recycle(path, "anything");

        Assert.False(File.Exists(path));
        Assert.Empty(DeletionLog.None.All());
    }
}
