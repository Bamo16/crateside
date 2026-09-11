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

    [Fact]
    public void How_the_download_went_survives_a_restart()
    {
        Set.SaveAcquisition(
            VideoId,
            new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder, IsPremium = false }
        );

        var read = new WorkingSet(_root).AcquisitionFor(VideoId);

        Assert.Equal(PremiumStatus.NoPremiumLadder, read?.PremiumStatus);
        Assert.True(read?.Stops);
    }

    [Fact]
    public void A_track_nothing_recorded_for_reads_as_no_finding_rather_than_a_shortfall()
    {
        // Every working set acquired before crateside read these fields is in this state, and
        // none of them should suddenly park.
        Set.Prepare(VideoId);

        Assert.Null(Set.AcquisitionFor(VideoId));
        Assert.Empty(Set.Acquisitions());
    }

    [Fact]
    public void An_unreadable_record_reads_as_none_rather_than_throwing()
    {
        Write(WorkingSet.AcquisitionFileName, "{ this is not json");

        Assert.Null(Set.AcquisitionFor(VideoId));
    }

    [Fact]
    public void The_record_is_not_mistaken_for_the_downloaded_audio()
    {
        // TryGetSource finds the audio by elimination, so anything else written into the folder
        // has to stay out of its way.
        var audio = Write("Yes - Owner of a Lonely Heart.flac");

        Set.SaveAcquisition(VideoId, new Acquisition { PremiumStatus = PremiumStatus.Premium });

        Assert.True(Set.TryGetSource(VideoId, out var found));
        Assert.Equal(audio, found);
    }

    [Fact]
    public void Accepting_leaves_the_measurement_alone_and_only_stops_it_stopping()
    {
        Set.SaveAcquisition(
            VideoId,
            new Acquisition { PremiumStatus = PremiumStatus.SourceHasNoPremiumAudio }
        );

        Assert.True(Set.AcceptAcquisition(VideoId));

        var read = Set.AcquisitionFor(VideoId);

        Assert.False(read?.Stops);
        Assert.True(read?.IsAccepted);
        Assert.Equal(PremiumStatus.SourceHasNoPremiumAudio, read?.PremiumStatus);
    }

    [Fact]
    public void There_is_nothing_to_accept_for_a_track_holding_no_download() =>
        Assert.False(Set.AcceptAcquisition(VideoId));

    [Fact]
    public void Throwing_away_the_download_throws_away_the_finding_with_it()
    {
        // The point of keeping it here: a swap re-acquires, and the answer is measured again
        // rather than inherited from the upload being replaced (ADR 0016).
        Set.SaveAcquisition(
            VideoId,
            new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder }
        );

        Set.Discard(VideoId);

        Assert.Null(Set.AcquisitionFor(VideoId));
    }
}
