using Crateside.Core;
using Crateside.Core.Art;

namespace Crateside.Tests.Art;

public sealed class CoverArtTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly CountingRunner _runner = new();
    private readonly CoverArt _art;

    public CoverArtTests()
    {
        Assert.SkipUnless(FfmpegAudio.IsAvailable, "No ffmpeg on PATH.");
        Directory.CreateDirectory(_root);

        _art = new CoverArt(_runner, new CratesidePaths { ArtFolder = Path.Combine(_root, "art") });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Audio(string name) => Path.Combine(_root, name);

    [Fact]
    public async Task The_small_cover_is_the_embedded_picture_cropped_square_and_the_full_one_is_untouched()
    {
        var file = Audio("a.flac");
        await FfmpegAudio.MakeAsync(file, 320, 180);

        var small = await _art.FindAsync("abc123", [file], CoverSize.Small, Ct);
        var full = await _art.FindAsync("abc123", [file], CoverSize.Full, Ct);

        Assert.Equal((160, 160), await FfmpegAudio.SizeOfAsync(small!.Path));
        Assert.Equal((320, 180), await FfmpegAudio.SizeOfAsync(full!.Path));
        Assert.Equal("image/jpeg", small.ContentType);
        Assert.NotEqual(small.ETag, full.ETag);
    }

    [Fact]
    public async Task A_kept_cover_is_served_without_running_ffmpeg_again()
    {
        var file = Audio("a.flac");
        await FfmpegAudio.MakeAsync(file, 200, 200);

        await _art.FindAsync("abc123", [file], CoverSize.Small, Ct);
        var runs = _runner.Runs;
        var again = await _art.FindAsync("abc123", [file], CoverSize.Full, Ct);

        Assert.NotNull(again);
        Assert.Equal(runs, _runner.Runs);
    }

    [Fact]
    public async Task A_cover_survives_the_file_moving_but_not_changing()
    {
        var file = Audio("a.flac");
        await FfmpegAudio.MakeAsync(file, 200, 200);
        var first = await _art.FindAsync("abc123", [file], CoverSize.Small, Ct);
        var runs = _runner.Runs;
        var moved = Audio("b.flac");
        File.Move(file, moved);

        var afterMove = await _art.FindAsync("abc123", [moved], CoverSize.Small, Ct);

        Assert.Equal(first!.ETag, afterMove!.ETag);
        Assert.Equal(runs, _runner.Runs);

        await FfmpegAudio.MakeAsync(moved, 400, 400, "blue");

        var afterChange = await _art.FindAsync("abc123", [moved], CoverSize.Full, Ct);

        Assert.Equal((400, 400), await FfmpegAudio.SizeOfAsync(afterChange!.Path));
    }

    [Fact]
    public async Task A_file_with_no_picture_has_no_cover_and_is_not_probed_again()
    {
        var file = Audio("plain.flac");
        await FfmpegAudio.MakeAsync(file);

        Assert.Null(await _art.FindAsync("abc123", [file], CoverSize.Small, Ct));

        var runs = _runner.Runs;

        Assert.Null(await _art.FindAsync("abc123", [file], CoverSize.Small, Ct));
        Assert.Equal(runs, _runner.Runs);
    }

    [Fact]
    public async Task A_file_without_a_picture_is_passed_over_for_the_next_that_has_one()
    {
        var plain = Audio("plain.flac");
        var covered = Audio("covered.flac");
        await FfmpegAudio.MakeAsync(plain);
        await FfmpegAudio.MakeAsync(covered, 300, 300);

        var cover = await _art.FindAsync("abc123", [plain, covered], CoverSize.Full, Ct);

        Assert.Equal((300, 300), await FfmpegAudio.SizeOfAsync(cover!.Path));
    }

    [Fact]
    public async Task A_track_that_gains_a_file_with_a_picture_gets_its_cover_after_having_none()
    {
        var plain = Audio("plain.flac");
        var covered = Audio("covered.flac");
        await FfmpegAudio.MakeAsync(plain);
        await FfmpegAudio.MakeAsync(covered, 300, 300);

        Assert.Null(await _art.FindAsync("abc123", [plain], CoverSize.Small, Ct));
        Assert.NotNull(await _art.FindAsync("abc123", [plain, covered], CoverSize.Small, Ct));
    }

    [Fact]
    public async Task Video_IDs_that_differ_only_in_case_keep_separate_covers()
    {
        var red = Audio("red.flac");
        var blue = Audio("blue.flac");
        await FfmpegAudio.MakeAsync(red, 100, 100, "red");
        await FfmpegAudio.MakeAsync(blue, 300, 300, "blue");

        await _art.FindAsync("AbC", [red], CoverSize.Full, Ct);
        var lower = await _art.FindAsync("abc", [blue], CoverSize.Full, Ct);
        var upper = await _art.FindAsync("AbC", [red], CoverSize.Full, Ct);

        Assert.Equal((300, 300), await FfmpegAudio.SizeOfAsync(lower!.Path));
        Assert.Equal((100, 100), await FfmpegAudio.SizeOfAsync(upper!.Path));
    }

    [Fact]
    public async Task A_track_with_no_file_on_disk_has_no_cover()
    {
        Assert.Null(await _art.FindAsync("abc123", [Audio("gone.flac")], CoverSize.Small, Ct));
    }
}
