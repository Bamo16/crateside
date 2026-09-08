using Crateside.Core;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// The stage that makes the stems root transient. Driven against real files, because every
/// decision it makes is about what is on disk.
/// </summary>
public class HandoffTests : IDisposable
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
            SampleStaging = Path.Combine(_directory, "Sample Staging"),
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

    private string Staged(string name) => Path.Combine(_paths.SampleStaging, name);

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
        // Warp markers live in a .asd beside the file and tags in Ableton's own store, so a name
        // already there means the user is part-way through the human steps -- or a re-separation
        // is landing on finished work. Neither is crateside's call to make.
        Directory.CreateDirectory(_paths.SampleStaging);
        File.WriteAllText(Staged("Yes - Owner (Drums).flac"), "already here");

        string[] stems =
        [
            Stem("Yes - Owner (Vocal - Full).flac"),
            Stem("Yes - Owner (Drums).flac"),
        ];

        var result = _runner.Run(stems);

        Assert.False(result.IsSuccess);
        Assert.Contains("already in staging", result.Error);

        // Nothing moved, rather than the half of it that did not clash.
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
        // The point of renaming the outputs rather than the source audio: a name stated while the
        // track was still queued has had nothing to land on until now.
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
        // Checked against where the files are going, not where they came from — or the rename
        // would be the one way to overwrite finished work.
        Directory.CreateDirectory(_paths.SampleStaging);
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
        // Only KeyIdentified is handed off: a LOW or WITHHELD verdict produces stems the user
        // cannot key-tag, and tagging is what staging is for. They are parked on purpose.
        Directory.CreateDirectory(_paths.StemsUnresolvedKey);

        var parked = Path.Combine(_paths.StemsUnresolvedKey, "Nobody - Unknown (Drums).flac");

        File.WriteAllText(parked, "parked");

        var selector = new HandoffSelector(
            new JobStore(Path.Combine(_directory, "jobs.jsonl")),
            _paths
        );

        // Nothing in the key-identified root, so nothing is picked -- the unresolved one is not
        // reachable from here at all.
        Assert.False(selector.TryPickNext(out _, out _));
        Assert.True(File.Exists(parked));
    }

    [Fact]
    public void A_file_crateside_did_not_make_is_left_alone()
    {
        // Grouping is by the video ID in the file's own provenance comment. A file with none
        // joins to nothing, so moving it would put somebody else's audio in the user's library.
        Stem("Something Else.flac");

        var selector = new HandoffSelector(
            new JobStore(Path.Combine(_directory, "jobs.jsonl")),
            _paths
        );

        Assert.False(selector.TryPickNext(out _, out _));
    }
}
