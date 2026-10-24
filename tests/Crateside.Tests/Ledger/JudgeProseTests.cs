using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class JudgeProseTests
{
    private static string Marked(string text) =>
        string.Join(
            "\n\n",
            JudgeProse
                .Paragraphs(text)
                .Select(paragraph =>
                    string.Concat(
                        paragraph.Spans.Select(span =>
                            span.Kind switch
                            {
                                ProseKind.Plain => span.Text,
                                ProseKind.Key => $"<k>{span.Text}</k>",
                                ProseKind.Chord => $"<c>{span.Text}</c>",
                                ProseKind.Value => $"<v>{span.Text}</v>",
                                _ => $"<f>{span.Text}</f>",
                            }
                        )
                    )
                )
        );

    [Fact]
    public void Markdown_markers_go_and_the_words_stay() =>
        Assert.Equal(
            "acceptable is a good read, to be sure, and bold too.",
            Marked("`acceptable` is a *good* read, *to* be sure, and __bold__ too.")
        );

    [Fact]
    public void An_asterisk_inside_a_name_or_a_sum_is_not_emphasis() =>
        Assert.Equal(
            "TANO*C and Ujico*, 95.91*2=191.8",
            Marked("TANO*C and Ujico*, 95.91*2=191.8")
        );

    [Fact]
    public void An_underscore_inside_an_identifier_stays() =>
        Assert.Equal("librosa_ks and key_alt", Marked("librosa_ks and key_alt"));

    [Fact]
    public void Any_run_of_newlines_ends_a_paragraph() =>
        Assert.Equal(
            ["One.", "Two.", "Three."],
            JudgeProse
                .Paragraphs("One.\nTwo.\r\n\r\n\nThree.\n")
                .Select(paragraph => paragraph.ToString())
        );

    [Theory]
    [InlineData("in E maj the", "in <k>E major</k> the")]
    [InlineData("G# minor", "<k>G♯ minor</k>")]
    [InlineData("A Dorian here", "<k>A Dorian</k> here")]
    [InlineData("Bb min and C# mixolydian", "<k>B♭ minor</k> and <k>C♯ Mixolydian</k>")]
    [InlineData("D# natural minor", "<k>D♯ natural minor</k>")]
    public void A_note_and_a_mode_word_become_a_key_in_full(string text, string expected) =>
        Assert.Equal(expected, Marked(text));

    [Theory]
    [InlineData("the G#m chord", "the <c>G♯m</c> chord")]
    [InlineData("a B7 and F#maj7", "a <c>B7</c> and <c>F♯maj7</c>")]
    [InlineData("Am/D over Em7b5", "<c>Am/D</c> over <c>Em7♭5</c>")]
    [InlineData("D#/Eb enharmonic", "<c>D♯/E♭</c> enharmonic")]
    [InlineData("G#3 median", "<c>G♯3</c> median")]
    [InlineData("Cm/Bm/F#/C list", "<c>Cm/Bm</c>/<c>F♯/C</c> list")]
    public void A_chord_with_a_quality_or_an_accidental_is_recognised_anywhere(
        string text,
        string expected
    ) => Assert.Equal(expected, Marked(text));

    [Theory]
    [InlineData("Chords B, E and G are diatonic")]
    [InlineData("Be sure to Add Dim and Bed")]
    [InlineData("so A is the tonic")]
    public void A_bare_letter_outside_a_progression_stays_plain(string text) =>
        Assert.Equal(text, Marked(text));

    [Theory]
    [InlineData("B → E", "<c>B</c> → <c>E</c>")]
    [InlineData("G–D–Em–C loop", "<c>G</c>–<c>D</c>–<c>Em</c>–<c>C</c> loop")]
    [InlineData("B (V) → E (I)", "<c>B</c> (V) → <c>E</c> (I)")]
    [InlineData("B7 → Em (i)", "<c>B7</c> → <c>Em</c> (i)")]
    public void A_bare_letter_next_to_an_arrow_is_a_chord(string text, string expected) =>
        Assert.Equal(expected, Marked(text));

    [Theory]
    [InlineData("a bVII chord and the bIII", "a ♭VII chord and the ♭III")]
    [InlineData("#iv and ♭VI", "♯iv and ♭VI")]
    [InlineData("V to I, never bi-weekly", "V to I, never bi-weekly")]
    [InlineData("a progression → (bVII)", "a progression → (♭VII)")]
    public void A_numeral_the_judge_wrote_is_typeset_and_none_is_added(
        string text,
        string expected
    ) => Assert.Equal(expected, Marked(text));

    [Theory]
    [InlineData("ffmpeg keydetect says", "keydetect says")]
    [InlineData("KeyDetect and ffmpeg_keydetect", "keydetect and keydetect")]
    [InlineData("librosa_ks+ffmpeg_keydetect agreed", "librosa_ks+keydetect agreed")]
    [InlineData("Essentia edma and edma's dissent", "Essentia and Essentia's dissent")]
    [InlineData("essentia_edma and Essentia (edma)", "Essentia and Essentia")]
    public void Detector_names_are_canonical(string text, string expected) =>
        Assert.Equal(expected, Marked(text));

    [Fact]
    public void R_values_are_values_and_confidence_words_are_set_apart() =>
        Assert.Equal(
            "keydetect names <k>E major</k> (<v>r 0.837</v>), so <f>MEDIUM</f> not <f>HIGH</f>; <v>r=0.4-0.58</v>.",
            Marked("keydetect names E major (r 0.837), so MEDIUM not HIGH; r=0.4-0.58.")
        );

    [Fact]
    public void A_word_that_only_looks_like_a_note_is_left_alone() =>
        Assert.Equal("A bad Bbird beat the Abacus odd", Marked("A bad Bbird beat the Abacus odd"));

    [Fact]
    public void Starry_Eyed_Surprise_reads_in_full_keys_and_canonical_names() =>
        Assert.Equal(
            "keydetect names <k>E major</k> (<v>r 0.837</v>) and both backers, madmom (0.98) and Essentia (0.81), name <k>E major</k> too. The loop <c>G♯m</c>–<c>E</c> with a B turnaround could also be read as <c>G♯m</c> aeolian (i–VI–III) or <k>B major</k> (vi–IV–I), but the <c>B</c>→<c>E</c> motion favours <k>E major</k>.",
            Marked(
                "keydetect names E major (r 0.837) and both backers, madmom (0.98) and essentia edma (0.81), name E major too. The loop G#m–E with a B turnaround could also be read as G#m aeolian (i–VI–III) or B major (vi–IV–I), but the B→E motion favours E major."
            )
        );

    [Fact]
    public void Takyon_reads_without_losing_a_word() =>
        Assert.Equal(
            "The 45-85s window's sustained <c>D♯/D♯m</c> drone doesn't fit <k>B minor</k>'s scale (B natural/harmonic minor has D, not <c>D♯</c>), so <c>F♯</c>(maj. with <c>A♯</c> leading tone)-to-<c>Bm</c> is the cadence.",
            Marked(
                "The 45-85s window's sustained D#/D#m drone doesn't fit B minor's scale (B natural/harmonic minor has D, not D#), so F#(maj. with A# leading tone)-to-Bm is the cadence."
            )
        );

    [Theory]
    [InlineData("backing F# minor", "backing F♯ minor")]
    [InlineData("G#", "G♯")]
    [InlineData("D#m", "D♯m")]
    [InlineData("Bb major", "B♭ major")]
    [InlineData("Chorus", "Chorus")]
    public void A_label_or_a_flicker_key_is_spelled_with_sharps_and_flats(
        string written,
        string spelled
    ) => Assert.Equal(spelled, JudgeProse.Inline(written).ToString());

    [Fact]
    public void A_missing_text_has_no_paragraphs() => Assert.Empty(JudgeProse.Paragraphs(null));
}
