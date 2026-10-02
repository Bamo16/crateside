using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class WarpSiblingsTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public WarpSiblingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void An_instrumental_karaoke_or_kept_source_cut_from_the_same_upload_lends_its_warp()
    {
        var target = Stem("Song (Vocal - Full).flac", "Vocal - Full");
        Stem("Song (Instrumental - Clean).flac", "Instrumental - Clean");
        Stem("Song (Karaoke).flac", "Karaoke");
        Stem("Song.flac", preset: null);

        var authorities = WarpSiblings.Scan([_root]).AuthoritiesFor(WarpSiblings.Read(target)!);

        Assert.Equal(
            ["Song (Instrumental - Clean).flac", "Song (Karaoke).flac", "Song.flac"],
            authorities.Select(Path.GetFileName).Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void A_vocal_a_drums_stem_and_another_upload_never_lend_a_warp()
    {
        var target = Stem("Song (Instrumental - Full).flac", "Instrumental - Full");
        Stem("Song (Vocal - Full).flac", "Vocal - Full");
        Stem("Song (Drums).flac", "Drums - htdemucs_ft");
        Stem(
            "Song (Instrumental - Clean).flac",
            "Instrumental - Clean",
            "https://www.youtube.com/watch?v=zyxwvutsrqp"
        );

        Assert.Empty(WarpSiblings.Scan([_root]).AuthoritiesFor(WarpSiblings.Read(target)!));
    }

    [Fact]
    public void A_cut_of_the_same_kind_is_one_whose_preset_starts_with_the_same_word()
    {
        var target = Stem("Song (Vocal - Lead).flac", "Vocal - Lead");
        Stem("Song (Vocal - Full).flac", "Vocal - Full");
        Stem("Song (Instrumental - Full).flac", "Instrumental - Full");
        Stem(
            "Song (Vocal - Clean).flac",
            "Vocal - Clean",
            "https://www.youtube.com/watch?v=zyxwvutsrqp"
        );

        Assert.Equal(
            ["Song (Vocal - Full).flac"],
            WarpSiblings
                .Scan([_root])
                .SameKindAs(WarpSiblings.Read(target)!)
                .Select(Path.GetFileName)
        );
    }

    [Fact]
    public void An_adopted_file_neither_lends_a_warp_nor_takes_one()
    {
        var adopted = Stem("Song (Instrumental - Mix).flac", preset: null, adopted: true);
        Stem("Song (Instrumental - Full).flac", "Instrumental - Full");

        Assert.Null(WarpSiblings.Read(adopted));
    }

    [Fact]
    public void A_kept_source_whose_title_has_a_parenthetical_is_not_trusted()
    {
        var target = Stem("Ruhe (feat. Boys Noize) (Vocal - Full).flac", "Vocal - Full");
        Stem("Ruhe (feat. Boys Noize).flac", preset: null);

        Assert.Empty(WarpSiblings.Scan([_root]).AuthoritiesFor(WarpSiblings.Read(target)!));
    }

    private string Stem(string name, string? preset, string source = Upload, bool adopted = false)
    {
        var path = Path.Combine(_root, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = string.Join(
            " | ",
            new[]
            {
                adopted ? "crateside/0.1.0" : "stemforge/0.4.0.0",
                preset is null ? null : $"preset: {preset}",
                $"source: {source}",
            }.OfType<string>()
        );
        file.Save();

        return path;
    }
}
