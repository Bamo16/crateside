using System.Globalization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class VerdictRulesTests
{
    private static string Evidence(
        string? keydetect,
        string? madmom,
        string? essentia,
        double r = 0.9
    ) =>
        $$"""
            {
              "algorithms": {
                "ffmpeg_keydetect": {{Reading(keydetect, r)}},
                "madmom": {{Reading(madmom)}},
                "essentia_edma": {{Reading(essentia)}}
              },
              "chords": ["Am", "E7"]
            }
            """;

    private static string Reading(string? key, double? r = null) =>
        (key, r) switch
        {
            (null, _) => """{ "error": "no reading" }""",
            (_, { } value) =>
                $$"""{ "key": "{{key}}", "r": {{value.ToString(CultureInfo.InvariantCulture)}} }""",
            _ => $$"""{ "key": "{{key}}" }""",
        };

    private static SiteLookup Hooktheory(params string[] keys) =>
        new(
            "Hooktheory",
            LookupStatus.Found,
            [
                new FetchedPage
                {
                    Site = "Hooktheory",
                    Url = "https://www.hooktheory.com/theorytab/view/a/b",
                    Keys = [.. keys.Select(key => new PageKey(key, "Verse", null))],
                },
            ]
        );

    private static SiteLookup Musicnotes(string key, string? note = null) =>
        new(
            "Musicnotes",
            LookupStatus.Found,
            [
                new FetchedPage
                {
                    Site = "Musicnotes",
                    Url = "https://www.musicnotes.com/sheetmusic/a/b/MN1",
                    Note = note,
                    Keys = [new PageKey(key, "Original Published Key", null)],
                },
            ]
        );

    private static SiteLookup UltimateGuitar(string key) =>
        new(
            "Ultimate Guitar",
            LookupStatus.Found,
            [
                new FetchedPage
                {
                    Site = "Ultimate Guitar",
                    Url = "https://tabs.ultimate-guitar.com/tab/a/b-1",
                    Keys = [new PageKey(key, null, null)],
                },
            ]
        );

    private static SiteLookup NothingFound(string site) => new(site, LookupStatus.NotFound, []);

    private static SiteLookup Unreachable(string site) =>
        new(site, LookupStatus.Unreachable, []) { Error = "timed out" };

    private static Ruling? Rule(string evidence, params SiteLookup[] lookups) =>
        VerdictRules.TryRule(evidence, lookups, out var ruling) ? ruling : null;

    [Fact]
    public void Two_detectors_and_a_page_that_agree_rule_HIGH_on_the_page_backed_key()
    {
        var ruling = Rule(
            Evidence("D# minor", "D# minor", "F major"),
            Hooktheory("D# minor"),
            NothingFound("Musicnotes")
        );

        Assert.NotNull(ruling);
        Assert.Equal(KeyConfidence.High, ruling.Confidence);
        Assert.Equal(new TonalCenter("D#", DiatonicMode.Aeolian), ruling.Center);
        Assert.Equal(
            "keydetect and madmom name D# minor; Hooktheory's page agrees.",
            ruling.Reasoning
        );
        Assert.Equal(
            new RunCandidate("F major", "Essentia read F major."),
            Assert.Single(ruling.Candidates)
        );

        var source = Assert.Single(ruling.Sources);

        Assert.Equal(SourceKind.Authoritative, source.Kind);
        Assert.Equal("D# minor", source.StatesKey);
        Assert.Equal("https://www.hooktheory.com/theorytab/view/a/b", source.Url);
    }

    [Fact]
    public void Madmom_and_Essentia_without_keydetect_are_two_detectors_enough_for_a_page_backed_ruling()
    {
        var ruling = Rule(
            Evidence("C major", "A minor", "A minor"),
            Hooktheory("A minor"),
            Musicnotes("A Minor")
        );

        Assert.NotNull(ruling);
        Assert.Equal(KeyConfidence.High, ruling.Confidence);
        Assert.Equal(
            "madmom and Essentia name A minor; Hooktheory's and Musicnotes' pages agree.",
            ruling.Reasoning
        );
        Assert.Equal(2, ruling.Sources.Count);
    }

    [Fact]
    public void A_page_that_names_another_key_leaves_the_track_to_the_person()
    {
        Assert.Null(Rule(Evidence("A minor", "A minor", "A minor"), Hooktheory("C major")));
    }

    [Fact]
    public void Pages_that_disagree_with_each_other_leave_the_track_to_the_person()
    {
        Assert.Null(
            Rule(
                Evidence("A minor", "A minor", "A minor"),
                Hooktheory("A minor"),
                Musicnotes("C Major")
            )
        );
    }

    [Fact]
    public void A_page_agreeing_with_a_single_detector_is_not_enough()
    {
        Assert.Null(Rule(Evidence("A minor", "C major", "G major"), Hooktheory("A minor")));
    }

    [Fact]
    public void A_page_with_a_section_in_another_key_counts_as_naming_it()
    {
        Assert.Null(
            Rule(Evidence("A minor", "A minor", "A minor"), Hooktheory("A minor", "B minor"))
        );
    }

    [Fact]
    public void A_modal_page_key_is_compared_as_the_major_or_minor_it_collapses_to()
    {
        var ruling = Rule(Evidence("B minor", "B minor", "D major"), Hooktheory("B dorian"));

        Assert.NotNull(ruling);
        Assert.Equal(KeyConfidence.High, ruling.Confidence);
        Assert.Equal(new TonalCenter("B", DiatonicMode.Aeolian), ruling.Center);

        var source = Assert.Single(ruling.Sources);

        Assert.Equal("B minor", source.StatesKey);
        Assert.Equal("B dorian", source.States);
        Assert.Equal(DiatonicMode.Dorian, source.StatesMode);
    }

    [Fact]
    public void An_enharmonic_spelling_on_a_page_is_the_same_key_and_the_tonic_is_stored_sharp()
    {
        var ruling = Rule(Evidence("D# minor", "D# minor", "D# minor"), Hooktheory("Eb minor"));

        Assert.NotNull(ruling);
        Assert.Equal("D#", ruling.Center.Tonic);
        Assert.Equal(KeyConfidence.High, ruling.Confidence);
    }

    [Fact]
    public void A_musicnotes_user_arrangement_backs_a_ruling_as_corroborating_not_authoritative()
    {
        var arrangement = Rule(
            Evidence("E minor", "E minor", "G major"),
            Musicnotes("E Minor", "user arrangement by Someone, A")
        );
        var published = Rule(Evidence("E minor", "E minor", "G major"), Musicnotes("E Minor"));

        Assert.Equal(SourceKind.Corroborating, Assert.Single(arrangement!.Sources).Kind);
        Assert.Equal(SourceKind.Authoritative, Assert.Single(published!.Sources).Kind);
    }

    [Fact]
    public void Ultimate_Guitar_is_never_counted_either_way()
    {
        var dissenting = Rule(Evidence("A minor", "A minor", "A minor"), UltimateGuitar("C major"));
        var alone = Rule(Evidence("C major", "A minor", "A minor"), UltimateGuitar("A minor"));

        Assert.NotNull(dissenting);
        Assert.Equal(KeyConfidence.Medium, dissenting.Confidence);
        Assert.Null(alone);
    }

    [Fact]
    public void All_three_detectors_agreeing_with_no_page_rule_MEDIUM()
    {
        var ruling = Rule(
            Evidence("A minor", "A minor", "A minor", r: 0.5),
            NothingFound("Hooktheory"),
            NothingFound("Musicnotes"),
            UltimateGuitar("C major")
        );

        Assert.NotNull(ruling);
        Assert.Equal(KeyConfidence.Medium, ruling.Confidence);
        Assert.Equal(
            "keydetect, madmom and Essentia name A minor; no Hooktheory or Musicnotes page states a key.",
            ruling.Reasoning
        );
        Assert.Empty(ruling.Sources);
        Assert.Empty(ruling.Candidates);
    }

    [Fact]
    public void A_page_that_states_no_key_does_not_stand_in_for_one()
    {
        Assert.Equal(
            KeyConfidence.Medium,
            Rule(Evidence("A minor", "A minor", "A minor"), Hooktheory())!.Confidence
        );
    }

    [Fact]
    public void Keydetect_and_one_backer_rule_MEDIUM_from_r_of_exactly_point_eight()
    {
        var ruling = Rule(Evidence("A minor", "A minor", "C major", r: 0.80));

        Assert.NotNull(ruling);
        Assert.Equal(KeyConfidence.Medium, ruling.Confidence);
        Assert.Equal(
            "keydetect (r 0.80) and madmom name A minor; no Hooktheory or Musicnotes page states a key.",
            ruling.Reasoning
        );
        Assert.Equal(
            new RunCandidate("C major", "Essentia read C major."),
            Assert.Single(ruling.Candidates)
        );
    }

    [Fact]
    public void Keydetect_and_one_backer_just_under_point_eight_wait_for_the_person()
    {
        Assert.Null(Rule(Evidence("A minor", "A minor", "C major", r: 0.79)));
    }

    [Fact]
    public void Two_detectors_agreeing_without_keydetect_wait_for_the_person_when_no_page_backs_them()
    {
        Assert.Null(Rule(Evidence("C major", "A minor", "A minor", r: 0.95)));
    }

    [Fact]
    public void A_three_way_split_waits_for_the_person()
    {
        Assert.Null(
            Rule(Evidence("A minor", "C major", "D major", r: 0.95), Hooktheory("A minor"))
        );
    }

    [Fact]
    public void A_detector_that_read_nothing_cannot_make_three()
    {
        Assert.Null(Rule(Evidence("A minor", "A minor", null, r: 0.5)));
    }

    [Fact]
    public void A_counted_site_that_could_not_be_reached_stops_a_detectors_alone_ruling()
    {
        Assert.Null(
            Rule(
                Evidence("A minor", "A minor", "A minor"),
                Unreachable("Hooktheory"),
                NothingFound("Musicnotes")
            )
        );
    }

    [Fact]
    public void An_unreachable_Ultimate_Guitar_changes_nothing()
    {
        Assert.NotNull(
            Rule(
                Evidence("A minor", "A minor", "A minor"),
                NothingFound("Hooktheory"),
                NothingFound("Musicnotes"),
                Unreachable("Ultimate Guitar")
            )
        );
    }

    [Fact]
    public void A_lookup_that_was_never_made_leaves_the_detectors_to_rule_alone()
    {
        Assert.Equal(
            KeyConfidence.Medium,
            Rule(Evidence("A minor", "A minor", "A minor"))!.Confidence
        );
    }

    private static string WhyOpen(string evidence, params SiteLookup[] lookups) =>
        Assert.IsType<RulesOutcome.Open>(VerdictRules.Decide(evidence, lookups)).Reason;

    [Fact]
    public void A_split_between_detectors_and_a_page_says_who_named_which_key()
    {
        Assert.Equal(
            "C major from madmom and Essentia; A minor from keydetect and Hooktheory. Tier A needs two detectors and the pages on one key.",
            WhyOpen(Evidence("A minor", "C major", "C major"), Hooktheory("A minor"))
        );
    }

    [Fact]
    public void Without_a_page_the_reason_gives_the_bar_and_keydetects_r()
    {
        Assert.Equal(
            "A minor from keydetect and madmom; C major from Essentia. No Hooktheory or Musicnotes page states a key. Without one, all three detectors must name one key, or keydetect and one more with r of at least 0.80 (here 0.70).",
            WhyOpen(Evidence("A minor", "A minor", "C major", r: 0.7))
        );
    }

    [Fact]
    public void An_unreachable_counted_site_is_named_as_the_reason()
    {
        Assert.Equal(
            "Musicnotes could not be reached, and a page there may name another key, so nothing is ruled without it.",
            WhyOpen(Evidence("A minor", "A minor", "A minor"), Unreachable("Musicnotes"))
        );
    }

    [Fact]
    public void A_ruling_carries_no_reason()
    {
        Assert.IsType<RulesOutcome.Ruled>(
            VerdictRules.Decide(Evidence("A minor", "A minor", "A minor"), [])
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Evidence_that_holds_no_readings_rules_nothing(string? evidence)
    {
        Assert.False(VerdictRules.TryRule(evidence, [], out _));
    }

    [Fact]
    public void An_unconfirmed_candidate_never_rules_nor_blocks_a_ruling()
    {
        var candidate = new FetchedPage
        {
            Site = "Hooktheory",
            Url = "https://www.hooktheory.com/theorytab/view/sega/b",
            Keys = [new PageKey("C major", "Verse", null)],
        };

        var withCandidate = Rule(
            Evidence("A minor", "A minor", "A minor"),
            new SiteLookup("Hooktheory", LookupStatus.NotFound, []) { Candidates = [candidate] },
            NothingFound("Musicnotes")
        );

        Assert.Equal(KeyConfidence.Medium, withCandidate?.Confidence);
        Assert.Equal(new TonalCenter("A", DiatonicMode.Aeolian), withCandidate?.Center);
    }
}
