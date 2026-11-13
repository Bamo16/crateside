using Crateside.Core.Library;

namespace Crateside.Tests.Library;

public sealed class SourceRetagTests : IDisposable
{
    private const string OldComment =
        "stemforge/0.1.0.0 | model: BuiltinPreset:vocal_full | date: 2026-05-29";

    private const string Url = "https://www.youtube.com/watch?v=aaaaaaaaaaa";

    private static readonly SourceMatch Song = new("Artist - Song", "aaaaaaaaaaa");

    private readonly string _library = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public SourceRetagTests() => Directory.CreateDirectory(_library);

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

    private static string? CommentOf(string path)
    {
        using var file = TagLib.File.Create(path);

        return file.Tag.Comment;
    }

    private static void Apply(IEnumerable<RetagFile> plan)
    {
        foreach (var file in plan.Where(file => file.Fate is RetagFate.Retag))
            Assert.True(SourceRetag.TryWrite(file, out _));
    }

    [Fact]
    public void Every_file_of_a_matched_song_gets_the_source_appended()
    {
        var vocal = Tagged("Artist - Song (Vocal - Full).flac", OldComment);
        var instrumental = Tagged("Artist - Song (Instrumental - Full).flac", OldComment);

        Apply(SourceRetag.Plan(_library, [Song]));

        Assert.Equal($"{OldComment} | source: {Url}", CommentOf(vocal));
        Assert.Equal($"{OldComment} | source: {Url}", CommentOf(instrumental));
    }

    [Fact]
    public void Running_it_twice_appends_the_source_once()
    {
        var path = Tagged("Artist - Song (Vocal - Full).flac", OldComment);

        Apply(SourceRetag.Plan(_library, [Song]));
        var second = SourceRetag.Plan(_library, [Song]);
        Apply(second);

        Assert.Equal(RetagFate.AlreadyTagged, Assert.Single(second).Fate);
        Assert.Equal($"{OldComment} | source: {Url}", CommentOf(path));
    }

    [Fact]
    public void A_file_that_already_names_another_source_is_left_alone()
    {
        var other = $"{OldComment} | source: https://www.youtube.com/watch?v=bbbbbbbbbbb";
        var path = Tagged("Artist - Song (Vocal - Full).flac", other);

        var plan = SourceRetag.Plan(_library, [Song]);
        Apply(plan);

        Assert.Equal(RetagFate.HasOtherSource, Assert.Single(plan).Fate);
        Assert.Equal(other, CommentOf(path));
    }

    [Fact]
    public void A_write_after_the_plan_still_refuses_a_file_that_gained_a_source()
    {
        var path = Tagged("Artist - Song (Vocal - Full).flac", OldComment);
        var plan = Assert.Single(SourceRetag.Plan(_library, [Song]));
        Apply([plan]);

        Assert.False(SourceRetag.TryWrite(plan, out var problem));
        Assert.NotNull(problem);
        Assert.Equal($"{OldComment} | source: {Url}", CommentOf(path));
    }

    [Fact]
    public void Other_songs_and_files_that_are_not_stems_are_not_touched()
    {
        var other = Tagged("Someone - Else (Vocal - Full).flac", OldComment);
        var download = Tagged("Artist - Song (for DJs).flac", "crateside/1.0 | date: 2026-06-01");
        var plain = Tagged("Artist - Song (Drums).flac", null);

        var plan = SourceRetag.Plan(_library, [Song]);

        Assert.Empty(plan);
        Assert.Equal(OldComment, CommentOf(other));
        Assert.Equal("crateside/1.0 | date: 2026-06-01", CommentOf(download));
        Assert.Null(CommentOf(plain));
    }

    [Fact]
    public void A_plan_alone_writes_nothing()
    {
        var path = Tagged("Artist - Song (Vocal - Full).flac", OldComment);

        var plan = Assert.Single(SourceRetag.Plan(_library, [Song]));

        Assert.Equal(RetagFate.Retag, plan.Fate);
        Assert.Equal(OldComment, CommentOf(path));
    }

    [Fact]
    public void The_list_reads_one_match_per_line_and_refuses_a_bad_id()
    {
        Assert.True(
            SourceRetag.TryParse("# note\n\naaaaaaaaaaa Artist - Song\r\n", out var matches, out _)
        );
        Assert.Equal(Song, Assert.Single(matches));

        Assert.False(SourceRetag.TryParse("nope Artist - Song", out _, out var problem));
        Assert.Contains("nope", problem);
    }
}
