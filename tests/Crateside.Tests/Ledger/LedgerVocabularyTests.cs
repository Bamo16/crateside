using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class LedgerVocabularyTests
{
    [Theory]
    [InlineData("Eb major", "D# major")]
    [InlineData("Bb minor", "A# minor")]
    [InlineData("f♯ min", "F# minor")]
    [InlineData("A♭ Maj", "G# major")]
    [InlineData("c major", "C major")]
    [InlineData("Cb major", "B major")]
    [InlineData("Fb minor", "E minor")]
    [InlineData("B# major", "C major")]
    [InlineData("  G minor, mostly", "G minor")]
    public void A_key_at_the_start_is_spelled_with_sharps_and_a_full_quality(
        string text,
        string expected
    )
    {
        Assert.Equal(expected, LedgerVocabulary.NormalizeKey(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H major")]
    [InlineData("E Mixolydian")]
    [InlineData("the key is C major")]
    [InlineData("C")]
    public void Text_that_does_not_start_with_a_key_has_no_key(string? text)
    {
        Assert.Null(LedgerVocabulary.NormalizeKey(text));
    }

    [Theory]
    [InlineData("Bb", "A#")]
    [InlineData(" bb ", "A#")]
    [InlineData("BB", "A#")]
    [InlineData("A♭", "G#")]
    [InlineData("E#", "F")]
    [InlineData("c#", "C#")]
    [InlineData("G", "G")]
    public void A_tonic_takes_the_ledgers_sharp_spelling(string tonic, string expected)
    {
        Assert.True(LedgerVocabulary.TryNormalizeTonic(tonic, out var sharp));
        Assert.Equal(expected, sharp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("C##")]
    [InlineData("Cx")]
    public void A_tonic_that_is_not_one_is_refused(string? tonic)
    {
        Assert.False(LedgerVocabulary.TryNormalizeTonic(tonic, out var sharp));
        Assert.Equal(string.Empty, sharp);
    }

    [Theory]
    [InlineData("dorian", DiatonicMode.Dorian)]
    [InlineData("D Dorian", DiatonicMode.Dorian)]
    [InlineData("E Mixolydian throughout", DiatonicMode.Mixolydian)]
    [InlineData("major", DiatonicMode.Ionian)]
    [InlineData("Minor", DiatonicMode.Aeolian)]
    [InlineData("LOCRIAN", DiatonicMode.Locrian)]
    [InlineData("phrygian", DiatonicMode.Phrygian)]
    public void A_mode_is_read_bare_or_with_its_tonic(string text, DiatonicMode expected)
    {
        Assert.Equal(expected, LedgerVocabulary.ParseMode(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("A minor")]
    [InlineData("Dorian,Lydian")]
    public void Text_naming_no_mode_has_none(string? text)
    {
        Assert.Null(LedgerVocabulary.ParseMode(text));
    }

    [Theory]
    [InlineData("E Mixolydian throughout", "E major")]
    [InlineData("E Dorian", "E minor")]
    [InlineData("Bb Lydian", "A# major")]
    [InlineData("a phrygian", "A minor")]
    public void A_modal_statement_collapses_to_the_tag_keeping_its_tonic(
        string text,
        string expected
    )
    {
        Assert.Equal(expected, LedgerVocabulary.CollapseModalStatement(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("C major")]
    [InlineData("Phrygian")]
    public void A_statement_naming_no_tonic_with_a_mode_collapses_to_nothing(string? text)
    {
        Assert.Null(LedgerVocabulary.CollapseModalStatement(text));
    }

    [Fact]
    public void A_modal_center_is_found_with_its_tonic_spelled_in_sharps()
    {
        Assert.Equal(
            new TonalCenter("D#", DiatonicMode.Mixolydian),
            LedgerVocabulary.ModalCenterOf("Eb Mixolydian")
        );
        Assert.Equal(
            new TonalCenter("D", DiatonicMode.Dorian),
            LedgerVocabulary.ModalCenterOf("D dorian")
        );
    }

    [Theory]
    [InlineData("A minor")]
    [InlineData("C ionian")]
    [InlineData("Dorian")]
    [InlineData(null)]
    public void A_plain_mode_or_a_missing_tonic_is_not_a_modal_center(string? text)
    {
        Assert.Null(LedgerVocabulary.ModalCenterOf(text));
    }

    [Theory]
    [InlineData("open.spotify.com/x (via web search)", "https://open.spotify.com/x", "web search")]
    [InlineData("https://a.com/x", "https://a.com/x", null)]
    [InlineData("https://a.com/x (Spotify)", "https://a.com/x", "Spotify")]
    [InlineData("  www.example.com  ", "https://www.example.com", null)]
    [InlineData("just some words", null, "just some words")]
    [InlineData("ftp://host/file", null, "ftp://host/file")]
    [InlineData(null, null, null)]
    [InlineData("   ", null, null)]
    public void A_link_is_split_from_how_it_was_obtained(string? raw, string? url, string? via)
    {
        Assert.Equal((url, via), LedgerVocabulary.SplitUrl(raw));
    }

    [Theory]
    [InlineData("The  Band", "Song (Instrumental)", "the band - song")]
    [InlineData("A", "Song (Vocal - Full) ", "a - song")]
    [InlineData("A", "Song (Drums) (Vocal)", "a - song")]
    [InlineData("A", "Song (Live)", "a - song (live)")]
    [InlineData("A", "Song (Vocal) Remix", "a - song (vocal) remix")]
    public void A_song_is_identified_by_its_lowercased_names_without_a_stem_suffix(
        string artist,
        string title,
        string expected
    )
    {
        Assert.Equal(expected, LedgerVocabulary.SongIdentity(artist, title));
    }

    [Fact]
    public void Utf8_read_as_cp1252_is_repaired_run_by_run_and_clean_text_is_left_alone()
    {
        Assert.Equal("Beyoncé – Halo", LedgerVocabulary.RepairText("BeyoncÃ© â€“ Halo"));
        Assert.Equal("Beyoncé – Halo", LedgerVocabulary.RepairText("Beyoncé – Halo"));
    }

    [Fact]
    public void Surrogate_escaped_bytes_are_read_back_as_utf8()
    {
        Assert.Equal("é", LedgerVocabulary.RepairText("\uDCC3\uDCA9"));
    }

    [Theory]
    [InlineData("BeyoncÃ©", true)]
    [InlineData("bad �", true)]
    [InlineData("lone \uD800 high", true)]
    [InlineData("Beyoncé", false)]
    [InlineData("Plain", false)]
    public void Text_still_carrying_a_bad_hop_is_damaged(string text, bool damaged)
    {
        Assert.Equal(damaged, LedgerVocabulary.IsDamaged(text));
    }
}
