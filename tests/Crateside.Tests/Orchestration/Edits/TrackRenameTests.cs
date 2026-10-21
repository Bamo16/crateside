using AbleKit.Sets;
using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Playlist;
using Crateside.Core.Staging;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Edits;

/// <summary>Giving a track a name of the user's own, and putting it on the files.</summary>
public sealed class TrackRenameTests : IDisposable
{
    private const string Video = "aaaaaaaaaaa";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly FolderInfoWriter _writer = new();
    private readonly FolderInfoReader _reader = new();
    private readonly List<StagedFile> _staged = [];
    private readonly List<string> _library = [];
    private readonly FakeSets _sets = new();

    public TrackRenameTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };

        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(_paths.LibraryFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private TrackInstructionStore Settings() =>
        new(
            new QueueFile(
                Options.Create(new PlaylistOptions()),
                Path.Combine(_root, "settings.json")
            )
        );

    private TrackRename Renamer(TrackInstructionStore? settings = null) =>
        new(
            settings ?? Settings(),
            new ListedStagedFiles(() => _staged),
            _writer,
            _paths,
            _ => _library,
            _sets
        );

    private string InLibrary(string name)
    {
        var path = Path.Combine(_paths.LibraryFolder, name);

        File.WriteAllText(path, "stem");
        _library.Add(path);

        return path;
    }

    private string Stage(string name)
    {
        var path = Path.Combine(_paths.StagingFolder, name);

        File.WriteAllText(path, "stem");
        _staged.Add(new StagedFile { Path = path, FileName = name });

        return path;
    }

    private string Staged(string name) => Path.Combine(_paths.StagingFolder, name);

    private static string? StoredName(TrackInstructionStore settings) =>
        settings.Read().FirstOrDefault(track => track.VideoId == Video)?.Name;

    [Fact]
    public void Renaming_a_sourceless_song_renames_its_files_and_stores_no_instruction()
    {
        var vocal = InLibrary("Old (Vocal - Full).flac");
        InLibrary("Old (Instrumental - Full).flac");
        var settings = Settings();

        var outcome = Renamer(settings).Apply(SourcelessSongs.KeyNamed("Old"), "New");

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(Path.Combine(_paths.LibraryFolder, "New (Vocal - Full).flac")));
        Assert.False(File.Exists(vocal));
        Assert.Empty(settings.Read());
    }

    [Fact]
    public void The_preset_and_the_extension_survive_the_new_name()
    {
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
        // A kept source, which has no parenthetical to preserve.
        Stage("99th Street.flac");

        Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(File.Exists(Staged("Starjunk 95 - 99th Street.flac")));
    }

    [Fact]
    public void A_track_with_nothing_on_disk_records_the_name_anyway()
    {
        var settings = Settings();

        var outcome = Renamer(settings).Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.Renamed);
        Assert.Equal("Starjunk 95 - 99th Street", StoredName(settings));
    }

    [Fact]
    public void The_ableton_entry_follows_the_file()
    {
        Stage("99th Street (Vocal - Full).flac");

        _writer.Apply(
            _paths.StagingFolder,
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

        var entry = Assert.Single(_reader.Read(_paths.StagingFolder).Entries);

        Assert.Equal("Starjunk 95 - 99th Street (Vocal - Full).flac", entry.RelativePath);
        Assert.Equal("A minor", entry.Key);
        Assert.Equal("Vocal", entry.Type);
    }

    [Fact]
    public void A_file_in_the_library_is_renamed_with_its_markers_and_its_tags()
    {
        var sample = InLibrary("99th Street (Vocal - Full).flac");
        File.WriteAllText($"{sample}.asd", "markers");

        _writer.Apply(
            _paths.LibraryFolder,
            [
                new TagAssignment
                {
                    RelativePath = "99th Street (Vocal - Full).flac",
                    Keywords = ["Key|A", "Key|Minor"],
                },
            ]
        );

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        var renamed = Path.Combine(
            _paths.LibraryFolder,
            "Starjunk 95 - 99th Street (Vocal - Full).flac"
        );
        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(renamed));
        Assert.Equal("markers", File.ReadAllText($"{renamed}.asd"));

        var entry = Assert.Single(_reader.Read(_paths.LibraryFolder).Entries);
        Assert.Equal("Starjunk 95 - 99th Street (Vocal - Full).flac", entry.RelativePath);
        Assert.Equal("A minor", entry.Key);
    }

    [Fact]
    public void Staged_and_library_files_are_renamed_together()
    {
        Stage("99th Street (Instrumental - Full).flac");
        InLibrary("99th Street (Vocal - Full).flac");

        Assert.Equal(2, Renamer().Apply(Video, "Starjunk 95 - 99th Street").Renamed);
    }

    [Fact]
    public void The_plan_names_the_sets_that_use_the_files_before_anything_moves()
    {
        var sample = InLibrary("99th Street (Vocal - Full).flac");
        _sets.Users = ["Mashup One"];

        var plan = Renamer().PlanFor(Video, "Starjunk 95 - 99th Street");

        Assert.Equal(["Mashup One"], plan.Sets);
        Assert.Equal([sample], Assert.Single(_sets.Lookups));
        Assert.True(File.Exists(sample));
    }

    [Fact]
    public void The_sets_are_relinked_to_the_new_names()
    {
        var sample = InLibrary("99th Street (Vocal - Full).flac");
        _sets.Users = ["Mashup One"];

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.Null(outcome.Note);
        Assert.Equal(
            new SampleMove(
                sample,
                Path.Combine(_paths.LibraryFolder, "Starjunk 95 - 99th Street (Vocal - Full).flac")
            ),
            Assert.Single(Assert.Single(_sets.Relinks))
        );
    }

    [Fact]
    public void A_set_open_in_Ableton_that_uses_the_files_stops_the_rename()
    {
        var sample = InLibrary("99th Street (Vocal - Full).flac");
        _sets.Users = ["Mashup One"];
        _sets.Open = new OpenSet(IsRunning: true, "Mashup One");

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.Contains("Mashup One is open in Ableton", outcome.Problem);
        Assert.True(File.Exists(sample));
        Assert.Empty(_sets.Relinks);
    }

    [Fact]
    public void A_set_open_in_Ableton_that_does_not_use_the_files_is_no_obstacle()
    {
        InLibrary("99th Street (Vocal - Full).flac");
        _sets.Users = ["Mashup One"];
        _sets.Open = new OpenSet(IsRunning: true, "Something Else");

        Assert.True(Renamer().Apply(Video, "Starjunk 95 - 99th Street").Succeeded);
    }

    [Fact]
    public void Ableton_running_without_saying_which_set_is_open_stops_the_rename()
    {
        InLibrary("99th Street (Vocal - Full).flac");
        _sets.Users = ["Mashup One"];
        _sets.Open = OpenSet.Unknown;

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.Contains("does not say which set is open", outcome.Problem);
    }

    [Fact]
    public void A_file_no_set_uses_renames_whatever_Ableton_shows()
    {
        InLibrary("99th Street (Vocal - Full).flac");
        _sets.Open = OpenSet.Unknown;

        Assert.True(Renamer().Apply(Video, "Starjunk 95 - 99th Street").Succeeded);
    }

    [Fact]
    public void A_set_that_could_not_be_relinked_is_named_in_the_note()
    {
        InLibrary("99th Street (Vocal - Full).flac");
        _sets.Users = ["Mashup One"];
        _sets.Failing = ["Mashup One"];

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Equal("Mashup One could not be updated; relink it in Ableton.", outcome.Note);
    }

    [Fact]
    public void A_name_already_in_the_library_is_refused()
    {
        InLibrary("99th Street (Vocal - Full).flac");
        File.WriteAllText(
            Path.Combine(_paths.LibraryFolder, "Taken (Vocal - Full).flac"),
            "theirs"
        );

        var outcome = Renamer().Apply(Video, "Taken");

        Assert.Contains("already in Library", outcome.Problem);
    }

    [Fact]
    public void Abletons_analysis_file_follows_the_sample()
    {
        // Warp markers live in a .asd named after the whole sample.
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
        Stage("99th Street (Vocal - Full).flac");

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.Note);
        Assert.False(File.Exists(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac.asd")));
    }

    [Fact]
    public void An_analysis_file_already_under_the_new_name_stops_the_rename()
    {
        // Somebody else's warp markers.
        var sample = Stage("99th Street (Vocal - Full).flac");

        File.WriteAllText(
            Staged("Starjunk 95 - 99th Street (Vocal - Full).flac.asd"),
            "somebody else's markers"
        );

        var outcome = Renamer().Apply(Video, "Starjunk 95 - 99th Street");

        Assert.False(outcome.Succeeded);
        Assert.Contains("already in Staging", outcome.Problem);
        Assert.True(File.Exists(sample));
        Assert.Equal(
            "somebody else's markers",
            File.ReadAllText(Staged("Starjunk 95 - 99th Street (Vocal - Full).flac.asd"))
        );
    }

    [Fact]
    public void Markers_that_could_not_follow_are_reported_rather_than_undone()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Only Windows refuses a file another program holds."
        );

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
        Stage("99th Street (Vocal - Full).flac");

        _writer.Apply(
            _paths.StagingFolder,
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

        var info = _reader.Read(_paths.StagingFolder);

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

        _staged.Clear();
        _staged.Add(
            new StagedFile
            {
                Path = Staged("Starjunk 95 - 99th Street (Vocal - Full).flac"),
                FileName = "Starjunk 95 - 99th Street (Vocal - Full).flac",
            }
        );

        var outcome = renamer.Revert(Video, "99th Street");

        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(original));

        Assert.Null(StoredName(settings));
    }

    [Fact]
    public void A_file_already_carrying_the_name_is_not_work()
    {
        Stage("Starjunk 95 - 99th Street (Vocal - Full).flac");
        Stage("99th Street (Drums).flac");

        var plan = Renamer().PlanFor(Video, "Starjunk 95 - 99th Street");

        Assert.True(plan.IsPossible);
        Assert.Equal(["99th Street (Drums).flac"], plan.Renaming.Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Starjunk 95 / 99th Street")]
    [InlineData("Starjunk 95: 99th Street")]
    public void A_name_no_file_can_carry_is_refused(string name)
    {
        Assert.SkipWhen(
            !OperatingSystem.IsWindows() && name.IndexOfAny([':', '?']) >= 0,
            "Only Windows forbids ':' and '?' in a file name."
        );

        Stage("99th Street (Vocal - Full).flac");

        var plan = Renamer().PlanFor(Video, name);

        Assert.False(plan.IsPossible);
        Assert.Contains("cannot be blank", plan.Refusal);
    }

    [Fact]
    public void A_name_that_is_already_in_staging_is_refused_before_anything_moves()
    {
        var mine = Stage("99th Street (Vocal - Full).flac");
        var theirs = Path.Combine(_paths.StagingFolder, "Taken (Vocal - Full).flac");

        File.WriteAllText(theirs, "someone else");

        var settings = Settings();
        var outcome = Renamer(settings).Apply(Video, "Taken");

        Assert.False(outcome.Succeeded);
        Assert.Contains("already in Staging", outcome.Problem);
        Assert.True(File.Exists(mine));
        Assert.Equal("someone else", File.ReadAllText(theirs));
        Assert.Null(StoredName(settings));
    }

    [Fact]
    public void Two_files_that_would_collide_are_refused()
    {
        // Only reachable with no presets to tell them apart.
        Stage("99th Street.flac");
        Stage("99th Street take two.flac");

        var plan = Renamer().PlanFor(Video, "Starjunk 95 - 99th Street");

        Assert.False(plan.IsPossible);
        Assert.Contains("same name", plan.Refusal);
    }

    [Fact]
    public void A_file_it_cannot_rename_keeps_the_name_that_was_stated()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Only Windows refuses a file another program holds."
        );

        // Nearly always a file Ableton has open.
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

internal sealed class FakeSets : ISetLinks
{
    public IReadOnlyList<string> Users { get; set; } = [];
    public OpenSet Open { get; set; } = OpenSet.NotRunning;
    public IReadOnlyList<string> Failing { get; set; } = [];
    public List<IReadOnlyCollection<string>> Lookups { get; } = [];
    public List<IReadOnlyList<SampleMove>> Relinks { get; } = [];

    public IReadOnlyList<string> Using(IReadOnlyCollection<string> samples)
    {
        Lookups.Add(samples);

        return Users;
    }

    public OpenSet ReadOpenSet() => Open;

    public SetRelink Relink(IReadOnlyList<SampleMove> moves)
    {
        Relinks.Add(moves);

        return new SetRelink([.. Users.Except(Failing)], Failing);
    }
}
