using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

/// <summary>
/// Read against the real ledger, because the point of this type is what agents have actually
/// written — and what they wrote is messier than the schema suggests.
/// </summary>
public class RunEvidenceRealDataTests
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private static bool Available => File.Exists(LedgerPath);

    private static IReadOnlyList<RunEvidence> All()
    {
        var ledger = KeyLedger.Read(LedgerPath);

        return
        [
            .. ledger
                .Runs.Select(r => r.Song)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(song => RunEvidence.ReadLatestFor(LedgerPath, song))
                .OfType<RunEvidence>(),
        ];
    }

    [Fact]
    public void Every_song_on_the_board_can_be_expanded()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        var ledger = KeyLedger.Read(LedgerPath);

        // A detail view that silently fails for some songs is worse than none.
        Assert.All(
            ledger.Runs.Select(r => r.Song).Distinct(StringComparer.OrdinalIgnoreCase),
            song => Assert.NotNull(RunEvidence.ReadLatestFor(LedgerPath, song))
        );
    }

    [Fact]
    public void The_reasoning_is_always_there_because_it_is_the_actual_argument()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        Assert.All(All(), e => Assert.False(string.IsNullOrWhiteSpace(e.Reasoning)));
    }

    [Fact]
    public void The_machine_layer_survives_the_round_trip()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        Assert.All(
            All(),
            e =>
            {
                // Three algorithms and three pairings, on every run ever recorded.
                Assert.Equal(3, e.Algorithms.Count);
                Assert.Equal(3, e.Agreement.Count);
                // The status vocabulary is closed; anything else means the runner changed.
                Assert.All(
                    e.Agreement,
                    pair =>
                        Assert.Contains(
                            pair.Status,
                            (string[])["full", "relative", "none", "single"]
                        )
                );
                Assert.NotEmpty(e.Chords);
                Assert.NotEmpty(e.KeyEvents);
            }
        );
    }

    [Fact]
    public void A_run_with_no_human_source_is_an_ordinary_state()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        // About a third have none — whole catalogs have no tier-1/2 coverage, which is a fact
        // about the catalog and not a defect. The view must render it as such.
        var all = All();

        Assert.Contains(all, e => e.Sources.Count == 0);
        Assert.Contains(all, e => e.Sources.Count > 0);
    }

    [Fact]
    public void A_source_is_classified_by_its_host_not_by_what_the_agent_typed()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        Assert.NotEmpty(sources);
        // The recorded tier carries three vocabularies plus free prose, so nothing may switch
        // on it — but anything that resolves to a real link still gets a usable classification.
        Assert.All(
            sources.Where(s => s.Link is not null),
            s => Assert.NotEqual(SourceTier.Unknown, s.Tier)
        );
        Assert.Contains(sources, s => s.Tier is SourceTier.Authoritative);
        Assert.Contains(sources, s => s.Tier is SourceTier.Algorithmic);
    }

    [Fact]
    public void A_recorded_url_that_is_not_a_link_is_not_offered_as_one()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        // Real entries include "user-provided image" — the user photographed sheet music, and
        // there is no page to visit. Rendering that as an anchor would be a dead link.
        Assert.All(
            sources.Where(s => s.Link is null),
            s => Assert.True(s.Url is null || s.Url.Contains(' ') || !s.Url.Contains('.'))
        );

        // And a real link wearing a trailing note is still a link.
        Assert.All(
            sources.Where(s => s.Link is not null),
            s => Assert.Contains(s.Link!.Scheme, (string[])["http", "https"])
        );
    }

    [Fact]
    public void The_chord_recognisers_in_the_ledger_are_identified_as_such()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        // Chordify and ChordU are present in the real data, and their key field may never be
        // the basis of a verdict. The view has to be able to say so.
        Assert.Contains(
            All().SelectMany(e => e.Sources),
            s => s.Tier is SourceTier.ChordRecogniser
        );
    }

    [Fact]
    public void Full_agreement_is_reported_as_agreement()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        // Guards a real bug: the shape was guessed as {agree, relation} when it is
        // {status, recommended, note}, so every pair rendered as a disagreement while the
        // algorithms plainly agreed.
        var pairs = All().SelectMany(e => e.Agreement).ToList();

        Assert.Contains(pairs, p => p.Agrees);
        Assert.All(pairs.Where(p => p.Agrees), p => Assert.NotNull(p.Recommended));
        Assert.All(pairs.Where(p => p.Status == "none"), p => Assert.False(p.Agrees));
    }

    [Fact]
    public void A_bpm_conflict_means_something_other_than_a_tempo_octave()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        // Half or double time is just a choice of octave and is not a conflict; anything else
        // means one of the two figures is simply wrong.
        Assert.All(
            All().Where(e => e.BpmConflicts),
            e =>
            {
                Assert.NotNull(e.Bpm);
                Assert.NotNull(e.BeatdetectBpm);
                var ratio = e.BeatdetectBpm!.Value / e.Bpm!.Value;
                Assert.False(
                    Math.Abs(ratio - 2) < 0.05 || Math.Abs(ratio - 0.5) < 0.05,
                    $"{e.Song}: {ratio:F3} is a tempo octave and should not be flagged."
                );
            }
        );
    }
}
