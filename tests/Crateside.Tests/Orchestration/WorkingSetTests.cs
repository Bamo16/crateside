using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;

namespace Crateside.Tests.Orchestration;

public sealed class WorkingSetTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";

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
        var path = Path.Combine(Set.Prepare(TrackKey), fileName);

        File.WriteAllText(path, content);

        return path;
    }

    [Fact]
    public void Nothing_is_held_for_a_track_that_has_not_been_worked()
    {
        Assert.False(Set.HasEvidence(TrackKey));
        Assert.False(Set.TryGetSource(TrackKey, out _));
        Assert.Empty(Set.TrackKeys);
    }

    [Fact]
    public void Evidence_is_found_where_it_was_put()
    {
        var written = Write(WorkingSet.EvidenceFileName, """{"input":"x"}""");

        Assert.True(Set.HasEvidence(TrackKey));
        Assert.Equal(written, Set.EvidencePathFor(TrackKey));
    }

    [Fact]
    public void The_source_is_found_by_elimination_because_the_downloader_names_it()
    {
        // Found by not being one of ours, rather than by a name we chose.
        var audio = Write("Yes - Owner of a Lonely Heart.flac");
        Write(WorkingSet.EvidenceFileName);

        Assert.True(Set.TryGetSource(TrackKey, out var found));
        Assert.Equal(audio, found);
    }

    [Fact]
    public void A_segment_rerun_sitting_beside_the_evidence_does_not_look_like_audio()
    {
        var audio = Write("track.flac");
        Write(WorkingSet.EvidenceFileName);
        Write("evidence.62-98.json");

        Assert.True(Set.TryGetSource(TrackKey, out var found));
        Assert.Equal(audio, found);
    }

    [Fact]
    public void Two_audio_files_report_none_rather_than_guessing()
    {
        Write("first.flac");
        Write("second.flac");

        Assert.False(Set.TryGetSource(TrackKey, out _));
    }

    [Fact]
    public void Discarding_takes_the_whole_working_set()
    {
        Write("track.flac");
        Write(WorkingSet.EvidenceFileName);

        Set.Discard(TrackKey);

        Assert.False(Set.HasEvidence(TrackKey));
        Assert.False(Set.TryGetSource(TrackKey, out _));
        Assert.Empty(Set.TrackKeys);
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
        Set.Prepare(TrackKey);
        Set.Prepare("aaaaaaaaaaa");

        Assert.Equal(["aaaaaaaaaaa", TrackKey], Set.TrackKeys.Order());
    }

    [Fact]
    public void How_the_download_went_survives_a_restart()
    {
        Set.SaveAcquisition(
            TrackKey,
            new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder, IsPremium = false }
        );

        var read = new WorkingSet(_root).AcquisitionFor(TrackKey);

        Assert.Equal(PremiumStatus.NoPremiumLadder, read?.PremiumStatus);
        Assert.True(read?.Stops);
    }

    [Fact]
    public void A_track_nothing_recorded_for_reads_as_no_finding_rather_than_a_shortfall()
    {
        Set.Prepare(TrackKey);

        Assert.Null(Set.AcquisitionFor(TrackKey));
        Assert.Empty(Set.Acquisitions());
    }

    [Fact]
    public void An_unreadable_record_reads_as_none_rather_than_throwing()
    {
        Write(WorkingSet.AcquisitionFileName, "{ this is not json");

        Assert.Null(Set.AcquisitionFor(TrackKey));
    }

    [Fact]
    public void The_record_is_not_mistaken_for_the_downloaded_audio()
    {
        var audio = Write("Yes - Owner of a Lonely Heart.flac");

        Set.SaveAcquisition(TrackKey, new Acquisition { PremiumStatus = PremiumStatus.Premium });

        Assert.True(Set.TryGetSource(TrackKey, out var found));
        Assert.Equal(audio, found);
    }

    [Fact]
    public void Accepting_leaves_the_measurement_alone_and_only_stops_it_stopping()
    {
        Set.SaveAcquisition(
            TrackKey,
            new Acquisition { PremiumStatus = PremiumStatus.SourceHasNoPremiumAudio }
        );

        Assert.True(Set.AcceptAcquisition(TrackKey));

        var read = Set.AcquisitionFor(TrackKey);

        Assert.False(read?.Stops);
        Assert.True(read?.IsAccepted);
        Assert.Equal(PremiumStatus.SourceHasNoPremiumAudio, read?.PremiumStatus);
    }

    [Fact]
    public void There_is_nothing_to_accept_for_a_track_holding_no_download() =>
        Assert.False(Set.AcceptAcquisition(TrackKey));

    [Fact]
    public void Throwing_away_the_download_throws_away_the_finding_with_it()
    {
        Set.SaveAcquisition(
            TrackKey,
            new Acquisition { PremiumStatus = PremiumStatus.NoPremiumLadder }
        );

        Set.Discard(TrackKey);

        Assert.Null(Set.AcquisitionFor(TrackKey));
    }
}
