using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class ModeTextTests
{
    [Theory]
    [InlineData("A", DiatonicMode.Lydian, "A maj", "Lyd", "E major (12B)")]
    [InlineData("A", DiatonicMode.Mixolydian, "A maj", "Mixo", "D major (10B)")]
    [InlineData("A", DiatonicMode.Dorian, "A min", "Dor", "G major (9B)")]
    [InlineData("A", DiatonicMode.Phrygian, "A min", "Phr", "F major (7B)")]
    [InlineData("A", DiatonicMode.Locrian, "A min", "Loc", "B♭ major (6B)")]
    public void A_modal_verdict_gets_the_arrow_the_abbreviation_and_its_same_notes_key(
        string tonic,
        DiatonicMode mode,
        string tag,
        string abbreviation,
        string sameNotes
    )
    {
        var center = new TonalCenter(tonic, mode);

        Assert.Equal($"{tonic} {mode} → {tag}", ModeText.Arrow(center, false));
        Assert.Equal(abbreviation, ModeText.Abbreviation(center, false));
        Assert.Contains($"Same notes as {sameNotes}.", ModeText.Reason(center, false));
    }

    [Theory]
    [InlineData(DiatonicMode.Ionian)]
    [InlineData(DiatonicMode.Aeolian)]
    public void A_plain_verdict_adds_nothing_to_its_tag(DiatonicMode mode)
    {
        var center = new TonalCenter("A", mode);

        Assert.Null(ModeText.Lead(center, false));
        Assert.Null(ModeText.Arrow(center, false));
        Assert.Null(ModeText.Abbreviation(center, false));
        Assert.Null(ModeText.Reason(center, false));
    }

    [Fact]
    public void The_tag_spells_a_sharp_with_the_sharp_sign()
    {
        Assert.Equal("A♯ min", ModeText.Tag(new("A#", DiatonicMode.Dorian)));
        Assert.Equal("A♯ Dorian → A♯ min", ModeText.Arrow(new("A#", DiatonicMode.Dorian), false));
    }

    [Fact]
    public void A_mixed_verdict_is_tagged_by_its_harmony_and_says_so()
    {
        var center = new TonalCenter("A", DiatonicMode.Aeolian);

        Assert.Equal("A mixed → A min", ModeText.Arrow(center, true));
        Assert.Equal("maj/min", ModeText.Abbreviation(center, true));
        Assert.Equal(
            "Major melody over minor harmony; tagged by the harmony.",
            ModeText.Reason(center, true)
        );
        Assert.Equal(
            "Minor melody over major harmony; tagged by the harmony.",
            ModeText.Reason(new("A", DiatonicMode.Ionian), true)
        );
    }

    [Fact]
    public void A_source_that_named_a_mode_is_stated_in_full()
    {
        Assert.Equal("G Mixolydian", ModeText.Stated("G major", "mixolydian"));
        Assert.Equal("G major", ModeText.Stated("G major", null));
        Assert.Equal("G major", ModeText.Stated("G major", "ionian"));
    }

    [Fact]
    public void The_hover_names_the_other_centers_by_section()
    {
        var run = new LedgerRun
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Center = new("G", DiatonicMode.Mixolydian),
            Sections =
            [
                new("verse", new("G", DiatonicMode.Mixolydian)),
                new("chorus", new("E", DiatonicMode.Aeolian)),
                new("bridge", new("C", DiatonicMode.Lydian)),
            ],
        };

        var hover = ModeText.Hover(run);

        Assert.StartsWith("G Mixolydian → G maj", hover);
        Assert.EndsWith("Also E minor (chorus), C Lydian (bridge).", hover);
    }

    [Fact]
    public void A_plain_verdict_has_no_hover()
    {
        Assert.Null(
            ModeText.Hover(
                new LedgerRun
                {
                    Timestamp = DateTimeOffset.UnixEpoch,
                    Song = "a - b",
                    Center = new("A", DiatonicMode.Aeolian),
                }
            )
        );
    }

    private static readonly Section Verse = new("verse", new("G", DiatonicMode.Mixolydian));
    private static readonly Section Chorus = new("chorus", new("E", DiatonicMode.Aeolian));
    private static readonly Section Bridge = new("bridge", new("C", DiatonicMode.Lydian));

    [Fact]
    public void A_source_covering_every_section_covers_the_whole_song()
    {
        var coverage = ModeText.Coverage(
            ["verse", "chorus"],
            [Verse, Chorus],
            Verse.Center,
            "G major"
        );

        Assert.Equal("whole song", coverage.Covers);
        Assert.Equal("G major", coverage.JudgedAgainst);
        Assert.Null(coverage.Relation);
    }

    [Fact]
    public void A_source_covering_the_primary_section_agrees_with_it()
    {
        var coverage = ModeText.Coverage(
            ["verse"],
            [Verse, Chorus, Bridge],
            Verse.Center,
            "G major"
        );

        Assert.Equal("verse only", coverage.Covers);
        Assert.Equal("G major", coverage.JudgedAgainst);
        Assert.Equal("agrees with the verse", coverage.Relation);
    }

    [Fact]
    public void A_source_matching_another_section_is_judged_against_that_section_not_the_song()
    {
        var coverage = ModeText.Coverage(
            ["chorus"],
            [Verse, Chorus, Bridge],
            Verse.Center,
            "E minor"
        );

        Assert.Equal("chorus only", coverage.Covers);
        Assert.Equal("E minor", coverage.JudgedAgainst);
        Assert.Equal("matches the chorus, not the song", coverage.Relation);
    }

    [Fact]
    public void A_source_naming_another_key_than_its_section_disagrees_with_that_section()
    {
        var coverage = ModeText.Coverage(
            ["bridge"],
            [Verse, Chorus, Bridge],
            Verse.Center,
            "C minor"
        );

        Assert.Equal("disagrees with the bridge (C Lydian), not the song", coverage.Relation);
    }

    [Fact]
    public void A_source_with_no_sections_listed_is_judged_against_the_verdict()
    {
        var coverage = ModeText.Coverage(null, [], new("A", DiatonicMode.Aeolian), "A minor");

        Assert.Equal(new("whole song", "A minor", null), coverage);
    }
}

public sealed class SourceCoverageTests
{
    [Fact]
    public void A_source_covering_one_section_is_not_a_dissent_from_the_song()
    {
        using var store = TestLedger.Store();
        var recorded = store.Record(
            new RunRecord
            {
                RecordedAt = DateTimeOffset.UnixEpoch,
                Song = "a - b",
                SourceUrl = "https://music.youtube.com/watch?v=abcdefghijk",
                Confidence = KeyConfidence.High,
                Center = new("G", DiatonicMode.Mixolydian),
                Sections =
                [
                    new("verse", new("G", DiatonicMode.Mixolydian)),
                    new("chorus", new("E", DiatonicMode.Aeolian)),
                ],
                Sources =
                [
                    new RunSource
                    {
                        Kind = SourceKind.Authoritative,
                        StatesKey = "E minor",
                        Sections = ["chorus"],
                    },
                ],
            }
        );

        Assert.IsType<RecordOutcome.Recorded>(recorded);

        var source = Assert.Single(store.EvidenceFor("a - b")!.Sources);

        Assert.Equal(SourceStance.Agrees, source.Stance);
        Assert.Equal("chorus only", source.Covers);
        Assert.Equal("matches the chorus, not the song", source.Relation);
    }

    [Theory]
    [InlineData("E minor", null, null)]
    [InlineData("E minor", null, "Key of E minor.")]
    [InlineData("F# minor", null, "F♯ min")]
    [InlineData("G major", "Mixolydian", "G Mixolydian")]
    public void A_statement_that_only_repeats_the_key_adds_nothing(
        string statesKey,
        string? statesMode,
        string? states
    )
    {
        Assert.Null(ModeText.Elaboration(statesKey, statesMode, states));
    }

    [Theory]
    [InlineData("E minor", "E minor and G minor (sections)")]
    [InlineData("E minor", "Em verses → Gm choruses with D major pivot")]
    public void A_statement_that_says_more_than_the_key_is_kept(string statesKey, string states) =>
        Assert.Equal(states, ModeText.Elaboration(statesKey, null, states));
}
