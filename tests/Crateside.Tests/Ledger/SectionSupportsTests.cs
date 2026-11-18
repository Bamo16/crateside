using System.Text.Json.Nodes;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Pairing;

namespace Crateside.Tests.Ledger;

public sealed class SectionSupportsTests
{
    private static readonly TonalCenter Home = new("E", DiatonicMode.Ionian);
    private static readonly TonalCenter Away = new("F#", DiatonicMode.Ionian);

    private static readonly Section Verse = new("Verse", Home, 0);
    private static readonly Section Outro = new("Outro", Away, 120);

    private static RunRecord Run(
        IReadOnlyList<Section> sections,
        IReadOnlyList<RunWindow>? windows = null,
        IReadOnlyList<RunSource>? sources = null,
        string? evidence = null,
        KeyConfidence confidence = KeyConfidence.Medium
    ) =>
        new()
        {
            RecordedAt = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Confidence = confidence,
            Center = Home,
            Sections = sections,
            Windows = windows ?? [],
            Sources = sources ?? [],
            Evidence = evidence,
        };

    private static string Evidence(
        string? whole = null,
        params (double Start, double End, string?[] Keys)[] windows
    ) =>
        new JsonObject
        {
            ["algorithms"] = Algorithms(whole is null ? [null, null, null] : [whole, whole, whole]),
            ["windows"] = new JsonArray([
                .. windows.Select(window =>
                    (JsonNode)
                        new JsonObject
                        {
                            ["start"] = window.Start,
                            ["end"] = window.End,
                            ["evidence"] = new JsonObject
                            {
                                ["algorithms"] = Algorithms(window.Keys),
                            },
                        }
                ),
            ]),
        }.ToJsonString();

    private static string Lifted(params (double At, int Shift)[] lifts)
    {
        var evidence = JsonNode.Parse(Evidence("E major"))!.AsObject();

        evidence["key_lifts"] = new JsonArray([
            .. lifts.Select(lift =>
                (JsonNode)
                    new JsonObject
                    {
                        ["at"] = lift.At,
                        ["length"] = 30,
                        ["shift"] = lift.Shift,
                        ["strength"] = 0.9,
                        ["fit"] = 0.2,
                    }
            ),
        ]);

        return evidence.ToJsonString();
    }

    private static JsonObject Algorithms(string?[] keys) =>
        new()
        {
            ["ffmpeg_keydetect"] = new JsonObject { ["key"] = keys[0] },
            ["madmom"] = new JsonObject { ["key"] = keys[1] },
            ["essentia_edma"] = new JsonObject { ["key"] = keys[2] },
        };

    private static SectionSupport? SupportOf(RunRecord run, string label) =>
        SectionSupports.Classify(run).Single(section => section.Label == label).Support;

    [Fact]
    public void A_second_center_nothing_independent_places_stays_a_hypothesis()
    {
        var run = Run([Verse, Outro], evidence: Evidence("E major"));

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(run, "Outro"));
        Assert.Null(SupportOf(run, "Verse"));
    }

    [Fact]
    public void A_human_source_naming_the_section_confirms_it()
    {
        var run = Run(
            [Verse, Outro],
            sources:
            [
                new()
                {
                    Kind = SourceKind.Authoritative,
                    StatesKey = "F# major",
                    Sections = ["outro"],
                },
            ]
        );

        Assert.Equal(SectionSupport.HumanSource, SupportOf(run, "Outro"));
    }

    [Fact]
    public void A_source_that_does_not_name_the_section_or_is_no_human_leaves_it_a_hypothesis()
    {
        var run = Run(
            [Verse, Outro],
            sources:
            [
                new() { Kind = SourceKind.Authoritative, StatesKey = "F# major" },
                new()
                {
                    Kind = SourceKind.Algorithmic,
                    StatesKey = "F# major",
                    Sections = ["Outro"],
                },
            ]
        );

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(run, "Outro"));
    }

    [Fact]
    public void A_window_the_maintainer_gave_over_the_section_confirms_it()
    {
        var run = Run(
            [Verse, Outro],
            windows: [new RunWindow(125, 150, "Outro", WindowAsker.User)],
            evidence: Evidence(null, (125, 150, ["E major", null, null]))
        );

        Assert.Equal(SectionSupport.Asked, SupportOf(run, "Outro"));
    }

    [Fact]
    public void A_window_the_judge_asked_for_does_not_count_as_the_maintainers()
    {
        var run = Run(
            [Verse, Outro],
            windows: [new RunWindow(125, 150, "Outro", WindowAsker.Judge)],
            evidence: Evidence(null, (125, 150, [null, null, null]))
        );

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(run, "Outro"));
    }

    [Fact]
    public void Two_detectors_reading_the_center_in_a_window_there_no_longer_place_it_alone()
    {
        var run = Run(
            [Verse, Outro],
            windows:
            [
                new RunWindow(10, 40, null, WindowAsker.Judge),
                new RunWindow(125, 150, null, WindowAsker.Judge),
            ],
            evidence: Evidence(
                null,
                (10, 40, ["E major", "E major", null]),
                (125, 150, ["F# major", null, "F# major"])
            )
        );

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(run, "Outro"));
    }

    [Theory]
    [InlineData(124, 2)]
    [InlineData(113, 2)]
    public void A_lift_by_the_step_into_the_center_where_the_section_starts_confirms_it(
        double at,
        int shift
    )
    {
        var run = Run([Verse, Outro], evidence: Lifted((at, shift)));

        Assert.Equal(SectionSupport.KeyLift, SupportOf(run, "Outro"));
    }

    [Theory]
    [InlineData(124, 1)]
    [InlineData(110, 2)]
    [InlineData(60, 2)]
    public void A_lift_by_another_step_or_away_from_the_start_leaves_it_a_hypothesis(
        double at,
        int shift
    )
    {
        var run = Run([Verse, Outro], evidence: Lifted((at, shift)));

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(run, "Outro"));
    }

    [Fact]
    public void A_lift_only_counts_before_the_next_section_starts()
    {
        var bridge = new Section("Bridge", Away, 60);
        var run = Run([Verse, bridge, Outro with { Center = Home }], evidence: Lifted((150, 2)));

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(run, "Bridge"));
    }

    [Fact]
    public void A_lift_by_the_step_between_collections_confirms_a_modal_section()
    {
        // E Aeolian has G major's notes and B Dorian A major's: the notes move up 2, the tonic 7.
        var minor = new TonalCenter("E", DiatonicMode.Aeolian);
        var dorian = new TonalCenter("B", DiatonicMode.Dorian);
        var run = Run(
            [new Section("Verse", minor, 0), new Section("Outro", dorian, 120)],
            evidence: Lifted((121, 2))
        ) with
        {
            Center = minor,
        };

        Assert.Equal(SectionSupport.KeyLift, SupportOf(run, "Outro"));
    }

    [Fact]
    public void A_lift_from_another_sections_center_confirms_it()
    {
        var up = new TonalCenter("G#", DiatonicMode.Ionian);
        var run = Run([Verse, Outro, new Section("Coda", up, 200)], evidence: Lifted((200, 2)));

        Assert.Equal(SectionSupport.KeyLift, SupportOf(run, "Coda"));
    }

    [Fact]
    public void Evidence_never_scanned_for_lifts_or_a_section_without_a_start_stays_a_hypothesis()
    {
        var unscanned = Run([Verse, Outro], evidence: Evidence("E major"));
        var unplaced = Run([Verse, Outro with { Start = null }], evidence: Lifted((124, 2)));

        Assert.Equal(SectionSupport.Hypothesis, SupportOf(unscanned, "Outro"));
        Assert.Equal(SectionSupport.Hypothesis, SupportOf(unplaced, "Outro"));
    }

    [Fact]
    public void An_unconfirmed_section_cannot_lift_a_verdict_the_evidence_would_not_carry()
    {
        var run = Run([Verse, Outro], evidence: Evidence());

        Assert.True(RecordTimeCheck.LeansOnHypothesis(run, EarWeight.Hint));
    }

    [Fact]
    public void A_confirmed_section_can_lift_a_verdict_and_so_can_evidence_for_the_primary_alone()
    {
        var confirmed = Run(
            [Verse, Outro],
            sources:
            [
                new()
                {
                    Kind = SourceKind.Authoritative,
                    StatesKey = "F# major",
                    Sections = ["Outro"],
                },
            ]
        );
        var backed = Run([Verse, Outro], evidence: Evidence("E major"));

        Assert.False(RecordTimeCheck.LeansOnHypothesis(confirmed, EarWeight.Hint));
        Assert.False(RecordTimeCheck.LeansOnHypothesis(backed, EarWeight.Hint));
    }

    [Fact]
    public void A_primary_only_an_unbacked_whole_file_read_holds_is_refused_once_windows_were_judged()
    {
        var windows = new[] { new RunWindow(10, 40, null, WindowAsker.User) };
        var unbacked = Run(
            [],
            windows,
            evidence: Evidence(null, (10, 40, ["C# minor", "A# minor", null]))
        ) with
        {
            Center = new TonalCenter("G#", DiatonicMode.Aeolian),
        };
        var read = unbacked with { Center = new TonalCenter("C#", DiatonicMode.Aeolian) };

        Assert.NotNull(RecordTimeCheck.PrimaryOutsideWindows(unbacked, EarWeight.Hint));
        Assert.Null(RecordTimeCheck.PrimaryOutsideWindows(read, EarWeight.Hint));
        Assert.Null(
            RecordTimeCheck.PrimaryOutsideWindows(unbacked with { Windows = [] }, EarWeight.Hint)
        );
    }

    [Fact]
    public void A_stored_verdict_reads_back_with_its_sections_classified_and_loses_the_caveat_when_unconfirmed()
    {
        using var store = new LedgerStore(LedgerStore.InMemory, TimeProvider.System);
        var run = Run([Verse, Outro], evidence: Evidence("E major")) with
        {
            SourceUrl = "https://music.youtube.com/watch?v=NSLYAT7sjAA",
            Song = "a - b",
        };

        Assert.IsType<RecordOutcome.Recorded>(store.Record(run));

        Assert.True(store.Read().TryGetLatestByTrackKey("NSLYAT7sjAA", out var latest));
        Assert.Equal(
            [null, SectionSupport.Hypothesis],
            latest.Sections.Select(section => section.Support)
        );
        Assert.Empty(Core.Ableton.BrowserCollections.TrackMarksFor(latest));
    }

    [Fact]
    public void Relations_between_keys_are_computed_from_their_notes()
    {
        var lines = KeyRelations
            .Lines([
                MusicKey.FromString("G# minor")!,
                MusicKey.FromString("A# minor")!,
                MusicKey.FromString("B major")!,
            ])
            .ToList();

        Assert.Contains(lines, line => line.Contains("G# minor and B major: relative"));
        Assert.DoesNotContain(lines, line => line.Contains("G# minor and A# minor: relative"));
        Assert.Contains(lines, line => line.Contains("5 notes in common"));
    }
}
