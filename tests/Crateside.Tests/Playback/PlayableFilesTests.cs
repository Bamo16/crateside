using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Plans;
using Crateside.Core.Playback;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Playback;

public sealed class PlayableFilesTests : IDisposable
{
    private const string TrackKey = "dQw4w9WgXcQ";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string Staging => Path.Combine(_root, "Staging");
    private string Library => Path.Combine(_root, "Library");
    private string Working => Path.Combine(_root, "working");

    public PlayableFilesTests()
    {
        Directory.CreateDirectory(Staging);
        Directory.CreateDirectory(Library);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private PlayableFiles Files =>
        new(
            new SourceIndex(
                new WorkingSet(new CratesidePaths { WorkingFolder = Working }),
                Path.Combine(_root, "source-index.json")
            ),
            new CratesidePaths { StagingFolder = Staging, LibraryFolder = Library }
        );

    private string Touch(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, [1]);

        return path;
    }

    private static TrackRow Row(params TrackSample[] samples) =>
        new()
        {
            TrackKey = TrackKey,
            Title = "Artist - Song",
            Samples = samples,
        };

    private static TrackSample Stem(string path, string label, StemKind kind = StemKind.Preset) =>
        new()
        {
            Path = path,
            Label = label,
            Kind = kind,
        };

    [Fact]
    public void The_mix_comes_first_and_every_stem_on_disk_follows_by_its_label()
    {
        Touch(Path.Combine(Working, TrackKey), "source.flac");
        var vocal = Touch(Staging, "Song (Vocal - Full).flac");
        var drums = Touch(Library, "Song (Drums).flac");

        var files = Files.Of(Row(Stem(vocal, "Vocal - Full"), Stem(drums, "Drums")));

        Assert.Equal(
            ["mix", "Song (Vocal - Full).flac", "Song (Drums).flac"],
            files.Select(file => file.Id)
        );
        Assert.Equal(["Mix", "Vocal - Full", "Drums"], files.Select(file => file.Name));
    }

    [Fact]
    public void A_stem_with_no_file_on_disk_is_not_offered()
    {
        var gone = Path.Combine(Staging, "Song (Vocal - Full).flac");

        Assert.Empty(Files.Of(Row(Stem(gone, "Vocal - Full"))));
    }

    [Fact]
    public void A_stem_the_library_now_holds_is_found_by_its_name_whatever_folder_the_row_says()
    {
        var stale = Path.Combine(Staging, "Song (Vocal - Full).flac");
        var moved = Touch(Library, "Song (Vocal - Full).flac");

        var found = Files.Find(Row(Stem(stale, "Vocal - Full")), "Song (Vocal - Full).flac");

        Assert.Equal(moved, found?.Path);
    }

    [Fact]
    public void The_kept_source_is_the_mix_and_not_one_of_the_stems()
    {
        var kept = Touch(Library, "Song (Source).flac");

        var files = Files.Of(Row(Stem(kept, "source", StemKind.Source)));

        Assert.Equal(["mix"], files.Select(file => file.Id));
        Assert.Equal(kept, files[0].Path);
    }

    [Fact]
    public void An_id_that_is_not_a_file_of_the_track_finds_nothing()
    {
        var vocal = Touch(Staging, "Song (Vocal - Full).flac");
        Touch(Staging, "Other.flac");

        Assert.Null(Files.Find(Row(Stem(vocal, "Vocal - Full")), "Other.flac"));
    }
}
