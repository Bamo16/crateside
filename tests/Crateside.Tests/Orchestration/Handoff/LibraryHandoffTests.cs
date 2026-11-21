using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Warps;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>A re-cut's new stems go straight to the library only when nothing is left to warp (#157).</summary>
public sealed class LibraryHandoffTests : IDisposable
{
    private const string Video = "abcdefghijk";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CratesidePaths _paths;
    private readonly LibraryHandoff _handoff;

    public LibraryHandoffTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
            DataRoot = Path.Combine(_root, "data"),
        };
        _handoff = new LibraryHandoff(_paths, new WrittenWarps());
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(Path.Combine(_paths.LibraryFolder, "Song"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void New_stems_go_to_the_folder_of_the_library_stems_when_all_are_warped_and_one_is_lent()
    {
        var library = Stage(
            Path.Combine(_paths.LibraryFolder, "Song"),
            "Vocal - Full",
            warped: true
        );

        var folder = _handoff.FolderFor(Video, Lent(), isOwnSource: true);

        Assert.Equal(Path.GetDirectoryName(library), folder);
    }

    [Fact]
    public void New_stems_go_to_Staging_when_a_stem_of_the_track_is_still_staged()
    {
        Stage(Path.Combine(_paths.LibraryFolder, "Song"), "Vocal - Full", warped: true);
        Stage(_paths.StagingFolder, "Instrumental - Full", warped: false);

        Assert.Null(_handoff.FolderFor(Video, Lent(), isOwnSource: true));
    }

    [Fact]
    public void New_stems_go_to_Staging_when_a_library_stem_was_never_warped_by_the_user()
    {
        Stage(Path.Combine(_paths.LibraryFolder, "Song"), "Vocal - Full", warped: false);

        Assert.Null(_handoff.FolderFor(Video, Lent(), isOwnSource: true));
    }

    [Fact]
    public void New_stems_go_to_Staging_when_the_warp_cannot_be_lent()
    {
        Stage(Path.Combine(_paths.LibraryFolder, "Song"), "Vocal - Full", warped: true);

        Assert.Null(_handoff.FolderFor(Video, Starter(), isOwnSource: true));
    }

    [Fact]
    public void A_source_added_to_the_track_goes_to_Staging()
    {
        Stage(Path.Combine(_paths.LibraryFolder, "Song"), "Vocal - Full", warped: true);

        Assert.Null(_handoff.FolderFor(Video, Lent(), isOwnSource: false));
    }

    [Fact]
    public void A_track_with_nothing_in_the_library_goes_to_Staging()
    {
        Assert.Null(_handoff.FolderFor(Video, Lent(), isOwnSource: true));
    }

    [Fact]
    public void The_runner_moves_stems_into_the_folder_it_is_given()
    {
        var source = Path.Combine(_root, "Song (Vocal - Full).flac");
        File.WriteAllText(source, "audio");
        var into = Path.Combine(_paths.LibraryFolder, "Song");

        var result = new HandoffRunner(_paths).Run([source], into: into);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(into, "Song (Vocal - Full).flac")));
        Assert.False(File.Exists(Path.Combine(_paths.StagingFolder, "Song (Vocal - Full).flac")));
    }

    private static Dictionary<string, PreparedSidecar> Lent() =>
        new() { ["new.flac"] = Prepared(from: "lender.flac", warped: true) };

    private static Dictionary<string, PreparedSidecar> Starter() =>
        new() { ["new.flac"] = Prepared(from: null, warped: true) };

    private static PreparedSidecar Prepared(string? from, bool warped) =>
        new(
            "new.flac",
            new AnalysisFile
            {
                Audio = AudioAnalysis.FromSamples(new float[4], 2, [], 1),
                Warp = warped ? new Warp { Markers = [new(0.1, 0), new(0.4, 1)] } : new Warp(),
            },
            from,
            null,
            null
        );

    private static string Stage(string folder, string preset, bool warped)
    {
        var path = Path.Combine(folder, $"Song ({preset}).flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Comment =
                $"stemforge/0.4.0.0 | preset: {preset} | source: https://www.youtube.com/watch?v={Video}";
            file.Save();
        }

        if (warped)
            new AnalysisFile
            {
                Audio = AudioAnalysis.FromSamples(new float[4], 2, [], new FileInfo(path).Length),
                Warp = new Warp { Markers = [new(0.1, 0), new(0.4, 1)] },
                Clip = new Clip
                {
                    LoopStart = 0,
                    LoopEnd = 1,
                    LoopOn = false,
                },
                IsDefaultClipSaved = true,
            }.Write($"{path}.asd");

        return path;
    }
}
