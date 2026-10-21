using Crateside.Core;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration.Handoff;

public sealed class HandoffTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly HandoffRunner _runner;

    public HandoffTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_directory, "Staging"),
            StemsKeyIdentified = Path.Combine(_directory, "Key Identified"),
            StemsUnresolvedKey = Path.Combine(_directory, "Unresolved Key"),
        };
        _runner = new HandoffRunner(_paths);

        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(_paths.StemsKeyIdentified);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Stem(string name)
    {
        var path = Path.Combine(_paths.StemsKeyIdentified, name);

        File.WriteAllText(path, name);

        return path;
    }

    private string Staged(string name) => Path.Combine(_paths.StagingFolder, name);

    /// <summary>A stem in the cut root carrying a given video ID in its StemForge provenance.</summary>
    private void Provenanced(string fileName, string videoId)
    {
        var path = Path.Combine(_paths.StemsKeyIdentified, fileName);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"),
            path,
            overwrite: true
        );

        using var file = TagLib.File.Create(path);

        file.Tag.Comment =
            "stemforge/0.2.1.1 | preset: Vocal - Full | date: 2026-09-23 | "
            + $"source: https://www.youtube.com/watch?v={videoId}";
        file.Save();
    }

    [Fact]
    public void Stems_reach_staging()
    {
        string[] stems =
        [
            Stem("Yes - Owner (Vocal - Full).flac"),
            Stem("Yes - Owner (Drums).flac"),
        ];

        var result = _runner.Run(stems);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Moved);
        Assert.All(stems, s => Assert.False(File.Exists(s)));
        Assert.All(stems, s => Assert.True(File.Exists(Staged(Path.GetFileName(s)))));
    }

    [Fact]
    public void A_name_already_in_staging_stops_the_whole_track()
    {
        Directory.CreateDirectory(_paths.StagingFolder);
        File.WriteAllText(Staged("Yes - Owner (Drums).flac"), "already here");

        string[] stems =
        [
            Stem("Yes - Owner (Vocal - Full).flac"),
            Stem("Yes - Owner (Drums).flac"),
        ];

        var result = _runner.Run(stems);

        Assert.False(result.IsSuccess);
        Assert.Contains("already in staging", result.Error);

        Assert.All(stems, s => Assert.True(File.Exists(s)));
        Assert.Equal("already here", File.ReadAllText(Staged("Yes - Owner (Drums).flac")));
    }

    [Fact]
    public void Handing_off_nothing_is_a_failure_rather_than_a_quiet_success()
    {
        Assert.False(_runner.Run([]).IsSuccess);
    }

    [Fact]
    public void A_name_given_before_there_were_files_reaches_them_here()
    {
        string[] stems =
        [
            Stem("Yes - Owner (Vocal - Full).flac"),
            Stem("Yes - Owner (Drums).flac"),
        ];

        var result = _runner.Run(stems, "Somebody Else - A Better Name");

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Staged("Somebody Else - A Better Name (Vocal - Full).flac")));
        Assert.True(File.Exists(Staged("Somebody Else - A Better Name (Drums).flac")));
    }

    [Fact]
    public void A_clash_under_the_new_name_stops_the_track_just_the_same()
    {
        // Checked against where the files are going, not where they came from.
        Directory.CreateDirectory(_paths.StagingFolder);
        File.WriteAllText(Staged("Renamed (Drums).flac"), "already here");

        var result = _runner.Run([Stem("Yes - Owner (Drums).flac")], "Renamed");

        Assert.False(result.IsSuccess);
        Assert.Contains("already in staging", result.Error);
        Assert.Equal("already here", File.ReadAllText(Staged("Renamed (Drums).flac")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("no / slashes")]
    public void A_track_with_no_usable_name_keeps_the_one_the_separator_gave_it(string? name)
    {
        _runner.Run([Stem("Yes - Owner (Vocal - Full).flac")], name);

        Assert.True(File.Exists(Staged("Yes - Owner (Vocal - Full).flac")));
    }

    [Fact]
    public void Unresolved_key_stems_are_left_where_they_are()
    {
        Directory.CreateDirectory(_paths.StemsUnresolvedKey);

        var parked = Path.Combine(_paths.StemsUnresolvedKey, "Nobody - Unknown (Drums).flac");

        File.WriteAllText(parked, "parked");

        var selector = Selector(new JobStore(Path.Combine(_directory, "jobs.jsonl")));

        Assert.False(selector.TryPickNext(out _, out _));
        Assert.True(File.Exists(parked));
    }

    [Fact]
    public void A_failed_separations_partial_output_is_not_handed_off()
    {
        var jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));

        Provenanced("Moe Shop - HARDCODED (Vocal - Full).flac", "_DiMKyZwaA0");

        jobs.Record(
            "_DiMKyZwaA0",
            JobStage.Separation,
            JobStatus.Failed,
            error: "separation process 25220 is gone without recording a result"
        );

        Assert.False(Selector(jobs).TryPickNext(out _, out _));
    }

    [Fact]
    public void A_separation_that_succeeded_is_handed_off()
    {
        var jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));

        Provenanced("Moe Shop - HARDCODED (Vocal - Full).flac", "_DiMKyZwaA0");

        jobs.Record("_DiMKyZwaA0", JobStage.Separation, JobStatus.Succeeded);

        Assert.True(Selector(jobs).TryPickNext(out var videoId, out _));
        Assert.Equal("_DiMKyZwaA0", videoId);
    }

    [Fact]
    public void A_copied_source_has_no_separation_job_and_is_still_handed_off()
    {
        var jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));

        Provenanced("Moe Shop - HARDCODED (Original).flac", "_DiMKyZwaA0");

        Assert.True(Selector(jobs).TryPickNext(out var videoId, out _));
        Assert.Equal("_DiMKyZwaA0", videoId);
    }

    [Fact]
    public void A_file_crateside_did_not_make_is_left_alone()
    {
        // No provenance, so it joins to nothing.
        Stem("Something Else.flac");

        var selector = Selector(new JobStore(Path.Combine(_directory, "jobs.jsonl")));

        Assert.False(selector.TryPickNext(out _, out _));
    }

    [Fact]
    public void A_separation_is_not_handed_off_until_it_is_settled_even_once_its_process_is_gone()
    {
        var jobs = new JobStore(Path.Combine(_directory, "jobs.jsonl"));
        var started = new DateTimeOffset(2026, 9, 25, 22, 46, 22, TimeSpan.Zero);

        // The process has ended, but nothing has yet counted what it wrote (Creep, 2026-09-29).
        Provenanced("T & Sugah - TumDaDaDum (Vocal - Full).flac", "vaQClQzY2fk");
        jobs.Record("vaQClQzY2fk", JobStage.Separation, JobStatus.Running, 33224, started);

        var selector = Selector(jobs);

        Assert.False(selector.TryPickNext(out _, out _));

        jobs.Record("vaQClQzY2fk", JobStage.Separation, JobStatus.Succeeded);

        Assert.True(selector.TryPickNext(out var videoId, out _));
        Assert.Equal("vaQClQzY2fk", videoId);
    }

    private HandoffSelector Selector(JobStore jobs) => new(jobs, _paths);
}
