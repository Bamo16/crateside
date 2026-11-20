using Crateside.Core;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Staging;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Edits;

public sealed class LibraryMoveTests : IDisposable
{
    private const string Key = "abcdefghijk";
    private const string Upload = $"https://www.youtube.com/watch?v={Key}";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly List<StagedFile> _staged = [];
    private readonly LibraryMove _move;

    public LibraryMoveTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
            KeptWarpGuides = Path.Combine(_root, "Drums"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(_paths.LibraryFolder);

        _move = new LibraryMove(
            _paths,
            new ListedStagedFiles(() => _staged),
            new LibraryFiles(_paths),
            new WarpGuideKeeper(
                _paths,
                new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero))
            )
        );
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_warped_tracks_samples_land_in_the_library_with_their_sidecars()
    {
        var vocal = Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);
        var instrumental = Stage("Song (Instrumental - Full).flac", "Instrumental - Full", [2]);

        var outcome = Assert.IsType<LibraryMoveOutcome.Moved>(_move.Apply([Key]));

        Assert.Equal(2, outcome.Samples);

        foreach (var (staged, bytes) in new[] { (vocal, (byte)1), (instrumental, (byte)2) })
        {
            var to = Path.Combine(_paths.LibraryFolder, Path.GetFileName(staged));
            Assert.False(File.Exists(staged));
            Assert.False(File.Exists($"{staged}.asd"));
            Assert.True(File.Exists(to));
            Assert.Equal([bytes], File.ReadAllBytes($"{to}.asd"));
        }
    }

    [Fact]
    public void Samples_go_beside_the_files_the_track_already_has_in_the_library()
    {
        var folder = Path.Combine(_paths.LibraryFolder, "Mashups");
        Directory.CreateDirectory(folder);
        Tagged(Path.Combine(folder, "Song (Instrumental - Full).flac"), "Instrumental - Full");
        Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);

        _move.Apply([Key]);

        Assert.True(File.Exists(Path.Combine(folder, "Song (Vocal - Full).flac")));
    }

    [Fact]
    public void The_drums_guide_goes_to_the_drums_folder_and_never_into_the_library()
    {
        Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);
        var guide = Stage("Song (Drums).flac", "Drums - htdemucs_ft", [3], warped: false);

        var outcome = Assert.IsType<LibraryMoveOutcome.Moved>(_move.Apply([Key]));

        Assert.Equal(1, outcome.Samples);
        Assert.False(File.Exists(guide));
        Assert.False(File.Exists(Path.Combine(_paths.LibraryFolder, "Song (Drums).flac")));
        Assert.Equal(
            [3],
            File.ReadAllBytes(Path.Combine(_paths.KeptWarpGuides, "Song (Drums).flac.asd"))
        );
    }

    [Fact]
    public void A_file_held_open_moves_nothing_and_is_named()
    {
        var vocal = Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);
        var instrumental = Stage("Song (Instrumental - Full).flac", "Instrumental - Full", [2]);

        using (new FileStream(instrumental, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var held = Assert.IsType<LibraryMoveOutcome.Held>(_move.Apply([Key]));

            Assert.Equal("Song (Instrumental - Full).flac", held.FileName);
        }

        Assert.True(File.Exists(vocal));
        Assert.True(File.Exists($"{vocal}.asd"));
        Assert.True(File.Exists(instrumental));
        Assert.Empty(Directory.GetFiles(_paths.LibraryFolder));
    }

    [Fact]
    public void A_held_sidecar_moves_nothing_too()
    {
        var vocal = Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);

        using (new FileStream($"{vocal}.asd", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var held = Assert.IsType<LibraryMoveOutcome.Held>(_move.Apply([Key]));

            Assert.Equal("Song (Vocal - Full).flac.asd", held.FileName);
        }

        Assert.True(File.Exists(vocal));
        Assert.Empty(Directory.GetFiles(_paths.LibraryFolder));
    }

    [Fact]
    public void A_name_already_in_the_library_stops_the_move_with_nothing_changed()
    {
        var vocal = Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);
        var instrumental = Stage("Song (Instrumental - Full).flac", "Instrumental - Full", [2]);
        var taken = Path.Combine(_paths.LibraryFolder, "Song (Instrumental - Full).flac");
        File.WriteAllBytes(taken, [9]);

        Assert.IsType<LibraryMoveOutcome.Refused>(_move.Apply([Key]));

        Assert.True(File.Exists(vocal));
        Assert.True(File.Exists(instrumental));
        Assert.Equal([9], File.ReadAllBytes(taken));
    }

    [Fact]
    public void A_track_with_an_unwarped_sample_is_not_moved()
    {
        var vocal = Stage("Song (Vocal - Full).flac", "Vocal - Full", [1]);
        Stage("Song (Instrumental - Full).flac", "Instrumental - Full", [2], warped: false);

        Assert.IsType<LibraryMoveOutcome.Refused>(_move.Apply([Key]));

        Assert.True(File.Exists(vocal));
        Assert.Empty(Directory.GetFiles(_paths.LibraryFolder));
    }

    private string Stage(string name, string preset, byte[] sidecar, bool warped = true)
    {
        var path = Tagged(Path.Combine(_paths.StagingFolder, name), preset);
        File.WriteAllBytes($"{path}.asd", sidecar);
        _staged.Add(
            new StagedFile
            {
                Path = path,
                FileName = name,
                Preset = preset,
                HasDefaultClip = warped,
            }
        );

        return path;
    }

    private static string Tagged(string path, string preset)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: {preset} | source: {Upload}";
        file.Save();

        return path;
    }
}
