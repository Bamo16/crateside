using AbleKit.Analysis;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class ClipColorsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public ClipColorsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Drums_are_one_color_whatever_the_song()
    {
        Assert.Equal(
            ClipColor.At(1, 2),
            ClipColors.For(Stem("One (Drums).flac", "Drums", "aaaaaaaaaaa"))
        );
        Assert.Equal(
            ClipColor.At(1, 2),
            ClipColors.For(Stem("Two (Drums).flac", "Drums", "bbbbbbbbbbb"))
        );
    }

    [Fact]
    public void A_songs_vocal_and_instrumental_take_their_own_families()
    {
        var vocal = ClipColors.For(Stem("Song (Vocal - Full).flac", "Vocal - Full", "abcdefghijk"));
        var instrumental = ClipColors.For(
            Stem("Song (Instrumental - Full).flac", "Instrumental - Full", "abcdefghijk")
        );

        Assert.Contains(vocal!.Value.Index, (int[])[3, 4, 5, 17, 18, 31, 32, 33]);
        Assert.Contains(instrumental!.Value.Index, (int[])[7, 8, 10, 11, 20, 21, 22, 24]);
    }

    [Fact]
    public void A_songs_shade_is_the_same_on_every_read_and_for_every_cut()
    {
        var full = Stem("Song (Vocal - Full).flac", "Vocal - Full", "abcdefghijk");
        var clean = Stem("Song (Vocal - Clean).flac", "Vocal - Clean", "abcdefghijk");

        Assert.Equal(ClipColors.For(full), ClipColors.For(clean));
        Assert.Equal(ClipColors.For(full), ClipColors.For(full));
    }

    [Fact]
    public void A_kept_source_is_colored_as_the_instrumental_it_is_used_as()
    {
        var source = Stem("Song.flac", preset: null, "abcdefghijk");
        var instrumental = Stem(
            "Song (Instrumental - Full).flac",
            "Instrumental - Full",
            "abcdefghijk"
        );

        Assert.Equal(ClipColors.For(instrumental), ClipColors.For(source));
    }

    [Fact]
    public void A_kept_source_named_with_a_parenthetical_is_still_an_instrumental()
    {
        var source = Stem("Theme (1998 - 2006).flac", preset: null, "abcdefghijk");

        Assert.Contains(ClipColors.For(source)!.Value.Index, (int[])[7, 8, 10, 11, 20, 21, 22, 24]);
    }

    private string Stem(string name, string? preset, string videoId)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = preset is null
            ? $"stemforge/0.4.0.0 | source: https://www.youtube.com/watch?v={videoId}"
            : $"stemforge/0.4.0.0 | preset: {preset} | source: https://www.youtube.com/watch?v={videoId}";
        file.Save();

        return path;
    }
}
