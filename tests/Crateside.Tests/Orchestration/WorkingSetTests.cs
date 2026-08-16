using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class WorkingSetTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private WorkingSet Set => field ??= new WorkingSet(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Write(string fileName, string content = "x")
    {
        var path = Path.Combine(Set.Prepare(VideoId), fileName);

        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void Nothing_is_held_for_a_track_that_has_not_been_worked()
    {
        Assert.False(Set.HasEvidence(VideoId));
        Assert.False(Set.TryGetSource(VideoId, out _));
        Assert.Empty(Set.VideoIds);
    }

    [Fact]
    public void Evidence_is_found_where_it_was_put()
    {
        var written = Write(WorkingSet.EvidenceFileName, """{"input":"x"}""");

        Assert.True(Set.HasEvidence(VideoId));
        Assert.Equal(written, Set.EvidencePathFor(VideoId));
    }

    [Fact]
    public void The_source_is_found_by_elimination_because_the_downloader_names_it()
    {
        // stemforge-cli names the file from the release metadata, and that name is worth keeping
        // -- the separator reads the same tags to name the stems. So it is found by not being
        // one of ours rather than by a name we chose.
        var audio = Write("Yes - Owner of a Lonely Heart.flac");
        Write(WorkingSet.EvidenceFileName);

        Assert.True(Set.TryGetSource(VideoId, out var found));
        Assert.Equal(audio, found);
    }

    [Fact]
    public void A_segment_rerun_sitting_beside_the_evidence_does_not_look_like_audio()
    {
        var audio = Write("track.flac");
        Write(WorkingSet.EvidenceFileName);
        Write("evidence.62-98.json");

        Assert.True(Set.TryGetSource(VideoId, out var found));
        Assert.Equal(audio, found);
    }

    [Fact]
    public void Two_audio_files_report_none_rather_than_guessing()
    {
        // Not a case to reconcile -- it means the folder is not what we think it is. Reporting
        // nothing makes the caller re-acquire, which is cheap and always right; picking one
        // could separate a track from the wrong audio, which is neither.
        Write("first.flac");
        Write("second.flac");

        Assert.False(Set.TryGetSource(VideoId, out _));
    }

    [Fact]
    public void Discarding_takes_the_whole_working_set()
    {
        Write("track.flac");
        Write(WorkingSet.EvidenceFileName);

        Set.Discard(VideoId);

        Assert.False(Set.HasEvidence(VideoId));
        Assert.False(Set.TryGetSource(VideoId, out _));
        Assert.Empty(Set.VideoIds);
    }

    [Fact]
    public void Discarding_something_that_was_never_there_is_not_an_error() =>
        Set.Discard("never-worked");

    [Fact]
    public void The_size_is_reportable_because_the_retention_window_runs_for_weeks()
    {
        Write("track.flac", new string('x', 500));
        Write(WorkingSet.EvidenceFileName, new string('y', 100));

        Assert.Equal(600, Set.TotalBytes);
    }

    [Fact]
    public void Every_track_being_held_is_listed()
    {
        Set.Prepare(VideoId);
        Set.Prepare("aaaaaaaaaaa");

        Assert.Equal(["aaaaaaaaaaa", VideoId], Set.VideoIds.Order());
    }
}
