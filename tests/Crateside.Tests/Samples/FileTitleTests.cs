using Crateside.Core.Samples;

namespace Crateside.Tests.Samples;

public sealed class FileTitleTests
{
    // names.json's baseName for HbhqeBUuvwE (Futari no Zenopittan), 221 characters.
    private const string Futari =
        "Ai Maeda, Mariko Suzuki, Rumi Shishido, Kotoba no Puzzle Mojipittan, Bandai Namco Game Music, "
        + "Hiroyuki Goto (Bandai Namco Entertainment Inc.), Satoru Kosaki (Bandai Namco Entertainment Inc.)"
        + " - ふたりのぜのぴったん(『ゼノサーガフリークス』)より収録";

    private static readonly string Folder = Path.Combine("P:", "Staging");

    public static TheoryData<string, string> Suffixes =>
        new()
        {
            { "Instrumental - Balanced", "99" },
            { "Vocal - Full", string.Empty },
            { "Drums", string.Empty },
        };

    [Theory]
    [MemberData(nameof(Suffixes))]
    public void Every_file_of_a_long_name_fits_a_file_name_with_its_analysis_file(
        string preset,
        string number
    )
    {
        var renamed = StemFileName.Renamed(
            Path.Combine(
                Folder,
                number is { Length: > 0 }
                    ? $"99th Street ({preset}) ({number}).flac"
                    : $"99th Street ({preset}).flac"
            ),
            Futari
        );
        var name = Path.GetFileName(StemFileName.AnalysisFor(renamed));

        Assert.True(name.Length <= FileTitle.MaxComponentLength, $"{name.Length}: {name}");
        Assert.Equal(Folder, Path.GetDirectoryName(renamed));
        Assert.Equal(number is { Length: > 0 } ? number : preset, StemFileName.PresetOf(renamed));
    }

    [Fact]
    public void A_cut_name_keeps_its_preset_and_the_artist_is_still_before_the_first_separator()
    {
        var renamed = StemFileName.Renamed(
            Path.Combine(Folder, "99th Street (Vocal - Full).flac"),
            Futari
        );
        var song = StemFileName.WithoutPreset(renamed);

        Assert.Equal("Vocal - Full", StemFileName.PresetOf(renamed));
        Assert.Equal(song, FileTitle.Fit(Futari, song.Length));
        Assert.StartsWith("Ai Maeda, Mariko Suzuki", song, StringComparison.Ordinal);
        Assert.Matches(@" \[[0-9a-f]{6}\]$", song);
    }

    [Fact]
    public void Renaming_a_cut_name_to_its_own_title_changes_nothing()
    {
        var renamed = StemFileName.Renamed(Path.Combine(Folder, "x (Drums).flac"), Futari);

        Assert.Equal(renamed, StemFileName.Renamed(renamed, StemFileName.WithoutPreset(renamed)));
    }

    [Fact]
    public void A_name_at_the_limit_is_kept_and_one_past_it_is_cut()
    {
        var kept = "Artist - " + new string('a', FileTitle.MaxLength - "Artist - ".Length);
        var cut = kept + "a";

        Assert.Equal(kept, FileTitle.Fit(kept));
        Assert.Equal(FileTitle.MaxLength, FileTitle.Fit(cut).Length);
        Assert.Matches(@" \[[0-9a-f]{6}\]$", FileTitle.Fit(cut));
    }

    [Fact]
    public void A_cut_is_the_same_every_time_and_differs_between_titles()
    {
        var other = Futari + "x";

        Assert.Equal(FileTitle.Fit(Futari), FileTitle.Fit(Futari));
        Assert.NotEqual(FileTitle.Fit(Futari), FileTitle.Fit(other));
    }

    [Fact]
    public void A_label_is_added_to_a_name_that_still_fits_with_it()
    {
        var path = Path.Combine(Folder, Futari + ".flac");
        var labeled = StemFileName.Labeled(path, "Instrumental - Balanced");
        var name = Path.GetFileName(StemFileName.AnalysisFor(labeled));

        Assert.True(name.Length <= FileTitle.MaxComponentLength, name);
        Assert.Equal("Instrumental - Balanced", StemFileName.PresetOf(labeled));
        Assert.Equal(
            Path.Combine(Folder, "Yes - No (Live).flac"),
            StemFileName.Labeled(Path.Combine(Folder, "Yes - No.flac"), "Live")
        );
    }

    [Fact]
    public void Without_long_paths_a_deep_folder_leaves_less_room()
    {
        var deep = Path.Combine("P:", new string('d', 120));

        Assert.True(FileTitle.MaxLengthIn(deep, longPathsEnabled: false) < FileTitle.MaxLength);
        Assert.Equal(FileTitle.MaxLength, FileTitle.MaxLengthIn(deep, longPathsEnabled: true));
    }
}
