using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Giving a track a name of the user's own, and putting it on the files.
/// </summary>
/// <remarks>
/// The case is a name nobody would search for: the uploader called the video "99th Street" and
/// the library calls the same track "Starjunk 95 - 99th Street". What matters most is that one
/// path covers a track at any point in the pipeline — the files that exist are renamed now, and
/// the ones that do not are named at staging from the same stored instruction.
/// </remarks>
public class TrackRenameTests : IDisposable
{
    private const string Video = "aaaaaaaaaaa";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly AbletonFolderInfoWriter _writer = new();
    private readonly AbletonFolderInfoReader _reader = new();
    private readonly List<StagingStem> _staged = [];

    public TrackRenameTests()
    {
        _paths = new CratesidePaths { SampleStaging = Path.Combine(_root, "Sample Staging") };

        Directory.CreateDirectory(_paths.SampleStaging);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private QueueSettingsStore Settings() =>
        new(Options.Create(new QueueOptions()), Path.Combine(_root, "settings.json"));

    private TrackRename Renamer(QueueSettingsStore? settings = null) =>
        new(settings ?? Settings(), _ => _staged, _writer, _paths);

    /// <summary>A file in staging, as the board would report it.</summary>
    private string Stage(string name)
    {
        var path = Path.Combine(_paths.SampleStaging, name);

        File.WriteAllText(path, "stem");
        _staged.Add(new StagingStem { Path = path, FileName = name });

        return path;
    }

    private string Staged(string name) => Path.Combine(_paths.SampleStaging, name);

    private string? StoredName(QueueSettingsStore settings) =>
        settings.Read().Tracks.FirstOrDefault(track => track.VideoId == Video)?.Name;

    [Fact]
    public void The_preset_and_the_extension_survive_the_new_name()
    {
        // The whole rule: everything before the parenthetical is the name, and the parenthetical
        // is StemForge's and stays.
        Stage("99th Street (Vocal - Full).flac");
        Stage("99th Street (Instrumental - Full).flac");
        Stage("99th Street (Drums).flac");

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Equal(3, outcome.Renamed);
        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac")));
        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street (Instrumental - Full).flac")));
        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street (Drums).flac")));
    }

    [Fact]
    public void A_file_with_no_preset_keeps_only_its_extension()
    {
        // An instrumental release's kept source. StemForge tags it but writes no preset
        // descriptor, so there is no parenthetical to preserve.
        Stage("99th Street.flac");

        Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street.flac")));
    }

    [Fact]
    public void A_track_with_nothing_on_disk_records_the_name_anyway()
    {
        // The point of renaming the outputs rather than the source audio: before separation there
        // is nothing to rename, so the instruction stands until handoff reads it.
        var settings = Settings();

        var outcome = Renamer(settings).Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.Renamed);
        Assert.Equal("Starjunk 95 - 99th Street", StoredName(settings));
    }

    [Fact]
    public void The_ableton_entry_follows_the_file()
    {
        // Renaming a file out from under its entry is what makes a tagged file read as untagged,
        // which is the bug "+ folder info" in the button is about.
        Stage("99th Street (Vocal - Full).flac");

        _writer.Apply(
            _paths.SampleStaging,
            [
                new TagAssignment
                {
                    RelativePath = "99th Street (Vocal - Full).flac",
                    Keywords = ["Key|A", "Key|Minor", "Type|Vocal"],
                },
            ]
        );

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.Null(outcome.Note);

        // Moved rather than added: one entry, under the new name. Leaving the old one beside it
        // would grow the store by an entry per rename, for files that no longer exist.
        var entry = Assert.Single(_reader.Read(_paths.SampleStaging).Entries);

        Assert.Equal("Starjunk 95 - 99th Street (Vocal - Full).flac", entry.RelativePath);
        Assert.Equal("A minor", entry.Key);
        Assert.Equal("Vocal", entry.Type);
    }

    [Fact]
    public void Abletons_analysis_file_follows_the_sample()
    {
        // Warp markers live in a .asd named after the whole sample. Renaming the sample alone
        // orphans it and Ableton analyses the file again from scratch, losing the markers —
        // measured on the real folder, where two renames left three sidecars behind.
        var sample = Stage("99th Street (Vocal - Full).flac");

        File.WriteAllText(sample + ".asd", "warp markers");

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Note);

        var renamed = Staged("Starjunk 95 - 99th Street (Vocal - Full).flac");

        Assert.Equal("warp markers", File.ReadAllText(renamed + ".asd"));
        Assert.False(File.Exists(sample + ".asd"));
    }

    [Fact]
    public void A_sample_nobody_has_analysed_carries_nothing_extra()
    {
        // Most of them. Ableton writes a .asd for having looked at the file, so its absence
        // means nobody has.
        Stage("99th Street (Vocal - Full).flac");

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Note);
        Assert.False(File.Exists(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac.asd")));
    }

    [Fact]
    public void An_analysis_file_already_under_the_new_name_stops_the_rename()
    {
        // Somebody else's warp markers. Overwriting them would be the one way a rename destroys
        // work that cannot be redone from anything crateside holds.
        var sample = Stage("99th Street (Vocal - Full).flac");

        File.WriteAllText(
            Staged("Starjunk 95 - 99th Street (Vocal - Full).flac.asd"),
            "somebody else's markers"
        );

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.False(outcome.Succeeded);
        Assert.Contains("already in Sample Staging", outcome.Problem);
        Assert.True(File.Exists(sample));
        Assert.Equal(
            "somebody else's markers",
            File.ReadAllText(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac.asd"))
        );
    }

    [Fact]
    public void Markers_that_could_not_follow_are_reported_rather_than_undone()
    {
        // The sample is renamed either way: markers can be placed again, and a half-renamed
        // track cannot be read. So this is a note, not a failure.
        var sample = Stage("99th Street (Vocal - Full).flac");

        File.WriteAllText(sample + ".asd", "warp markers");

        using var held = File.Open(sample + ".asd", FileMode.Open, FileAccess.Read, FileShare.None);

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, outcome.Renamed);
        Assert.Contains("warp markers", outcome.Note);
        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac")));
    }

    [Fact]
    public void Entries_for_other_files_are_left_alone()
    {
        // The store is shared with a running Ableton and mostly holds the user's own work.
        Stage("99th Street (Vocal - Full).flac");

        _writer.Apply(
            _paths.SampleStaging,
            [
                new TagAssignment
                {
                    RelativePath = "99th Street (Vocal - Full).flac",
                    Keywords = ["Key|A", "Key|Minor"],
                },
                new TagAssignment
                {
                    RelativePath = "Somebody Else - Their Track.flac",
                    Keywords = ["Key|C", "Key|Major"],
                },
            ]
        );

        Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        var info = _reader.Read(_paths.SampleStaging);

        Assert.True(info.TryGet(Staged("Somebody Else - Their Track.flac"), out var theirs));
        Assert.Equal("C major", theirs.Key);
    }

    [Fact]
    public void Reverting_drops_the_name_and_puts_the_files_back()
    {
        var settings = Settings();
        var renamer = Renamer(settings);

        var original = Stage("99th Street (Vocal - Full).flac");

        renamer.Apply(Video, "Starjunk 95 - 99th Street");

        // The board would report the renamed file on its next read; this stands in for that.
        _staged.Clear();
        _staged.Add(
            new StagingStem
            {
                Path = Staged("Starjunk 95 - 99th Street (Vocal - Full).flac"),
                FileName = "Starjunk 95 - 99th Street (Vocal - Full).flac",
            }
        );

        var outcome = renamer.Revert(Video, "99th Street");

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(original));

        // Dropped rather than written empty: absence is what "not stated" means, and an entry
        // saying nothing would be a permanent line in the file.
        Assert.Null(StoredName(settings));
    }

    [Fact]
    public void A_file_already_carrying_the_name_is_not_work()
    {
        // What makes pressing Rename again finish an interrupted one rather than start over.
        Stage("Starjunk 95 - 99th Street (Vocal - Full).flac");
        Stage("99th Street (Drums).flac");

        var plan = Renamer().PlanFor(Video, "Starjunk 95 - 99th Street");

        Assert.True(plan.IsPossible);
        Assert.Equal(["99th Street (Drums).flac"], plan.Renaming.Select(stem => stem.FileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Starjunk 95 / 99th Street")]
    [InlineData("Starjunk 95: 99th Street")]
    public void A_name_no_file_can_carry_is_refused(string name)
    {
        Stage("99th Street (Vocal - Full).flac");

        var plan = Renamer().PlanFor(Video, name);

        Assert.False(plan.IsPossible);
        Assert.Contains("cannot be blank", plan.Refusal);
    }

    [Fact]
    public void A_name_that_is_already_in_staging_is_refused_before_anything_moves()
    {
        // Someone else's file, or the same track under a name it half carries already. Either way
        // nothing is overwritten, and the refusal comes before the first move rather than after
        // it — which is the difference between a track under one name and a track under two.
        var mine = Stage("99th Street (Vocal - Full).flac");
        var theirs = Path.Combine(_paths.SampleStaging, "Taken (Vocal - Full).flac");

        File.WriteAllText(theirs, "someone else");

        var settings = Settings();
        var outcome = Renamer(settings).Apply(Video, "Taken");

        Assert.False(outcome.Succeeded);
        Assert.Contains("already in Sample Staging", outcome.Problem);
        Assert.True(File.Exists(mine));
        Assert.Equal("someone else", File.ReadAllText(theirs));
        Assert.Null(StoredName(settings));
    }

    [Fact]
    public void Two_files_that_would_collide_are_refused()
    {
        // Only reachable with no presets to tell them apart, which is a track whose provenance
        // was lost. Better refused than silently reduced to one file.
        Stage("99th Street.flac");
        Stage("99th Street take two.flac");

        var plan = Renamer().PlanFor(Video, "Starjunk 95 - 99th Street");

        Assert.False(plan.IsPossible);
        Assert.Contains("same name", plan.Refusal);
    }

    [Fact]
    public void A_file_it_cannot_rename_keeps_the_name_that_was_stated()
    {
        // Nearly always a file Ableton has open. The name is the user's statement and is true
        // whether or not every file caught up; pressing again finishes the ones that did not.
        Stage("99th Street (Vocal - Full).flac");

        var held = Stage("99th Street (Drums).flac");
        var settings = Settings();

        using (File.Open(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = Renamer(settings).Apply(Video, "Starjunk 95 - 99th Street");

            Assert.False(outcome.Succeeded);
            Assert.Contains("open in Ableton", outcome.Problem);
            Assert.Equal(1, outcome.Renamed);
        }

        Assert.Equal("Starjunk 95 - 99th Street", StoredName(settings));
        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac")));
        Assert.True(File.Exists(Staged("99th Street (Drums).flac")));
    }
}
