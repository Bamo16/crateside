using Crateside.Core;
using Crateside.Core.Library;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Library;

public sealed class SourcelessSongsTests : IDisposable
{
    private readonly string _library = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public SourcelessSongsTests() => Directory.CreateDirectory(_library);

    public void Dispose()
    {
        if (Directory.Exists(_library))
            Directory.Delete(_library, recursive: true);
    }

    private string Tagged(string name, string? comment)
    {
        var path = Path.Combine(_library, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = comment;
        file.Save();

        return path;
    }

    private const string OldComment =
        "stemforge/0.1.0.0 | model: BuiltinPreset:vocal_full | date: 2026-05-29";

    [Fact]
    public void A_stemforge_file_with_no_source_is_found_under_its_name_without_the_preset()
    {
        Tagged("Song (Instrumental - Full).flac", OldComment);
        Tagged("Song (Vocal - Full).flac", OldComment);

        var found = SourcelessSongs.Scan(_library);

        var song = Assert.Single(found);
        Assert.Equal("name:Song", song.Key);
        Assert.Equal(2, song.Value.Count);
    }

    [Fact]
    public void Files_with_a_source_or_no_provenance_are_not_sourceless()
    {
        Tagged(
            "Sourced (Instrumental - Full).flac",
            $"{OldComment} | source: https://www.youtube.com/watch?v=aaaaaaaaaaa"
        );
        Tagged("Hand made.flac", null);

        Assert.Empty(SourcelessSongs.Scan(_library));
    }

    [Fact]
    public void A_sourceless_key_can_never_be_a_video_id()
    {
        Assert.True(SourcelessSongs.IsKey(SourcelessSongs.KeyOf("Song (Vocal - Full).flac")));
        Assert.False(SourcelessSongs.IsKey("dQw4w9WgXcQ"));
        Assert.False(SourcelessSongs.IsKey("-_-_-_-_-_-"));
    }

    [Fact]
    public void A_sourceless_song_gets_a_library_only_row_that_cannot_be_queued()
    {
        Tagged("Song (Instrumental - Full).flac", OldComment);

        var row = Assert.Single(
            TracksView
                .Compose(
                    EmptyView(),
                    TestLedger.From(Path.Combine(_library, "none.jsonl")),
                    [],
                    SourcelessSongs.Scan(_library),
                    _library,
                    new JobStore(new CratesidePaths()),
                    new Dictionary<string, TrackInstruction>()
                )
                .Rows
        );

        Assert.Equal("name:Song", row.VideoId);
        Assert.Equal("Song", row.Title);
        Assert.True(row.IsLibraryOnly);
        Assert.False(row.HasUpload);
        Assert.Empty(row.Facts.Uploads);
        Assert.False(row.Facts is { IsLibraryOnly: true, HasVerdict: false, HasUpload: true });
        Assert.Equal(TrackState.Done, row.State);
    }

    private static WorkListsView EmptyView() =>
        WorkListsView.Compose(
            [],
            new HashSet<string>(),
            new HashSet<string>(),
            new HashSet<string>(),
            new Dictionary<string, TrackInstruction>()
        );

    [Fact]
    public void Every_sourceless_song_in_the_real_library_gets_a_row()
    {
        var paths = RealPaths.Create();

        Assert.SkipUnless(Directory.Exists(paths.LibraryFolder), "no library on this machine");

        var sourceless = SourcelessSongs.Scan(paths.LibraryFolder);

        Assert.SkipWhen(sourceless.Count is 0, "no sourceless songs in this library");

        var rows = TracksView
            .Compose(
                EmptyView(),
                TestLedger.FromReal(),
                [],
                sourceless,
                paths.LibraryFolder,
                new JobStore(new CratesidePaths()),
                new Dictionary<string, TrackInstruction>()
            )
            .Rows;

        Assert.Equal(
            [.. sourceless.Keys.Order()],
            [.. rows.Where(row => !row.HasUpload).Select(row => row.VideoId).Order()]
        );
    }
}
