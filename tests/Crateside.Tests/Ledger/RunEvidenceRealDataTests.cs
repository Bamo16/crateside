using System.Text.Json;
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

    /// <summary>
    /// crateside stopped repairing text on the way out once keytag swept the file
    /// (its ADR 0007), so this is the whole of its defence against that regressing.
    /// </summary>
    /// <remarks>
    /// Walks every string rather than a list of fields. keytag's repair script and its own check
    /// each enumerated fields, agreed with one another, and missed 97 damaged strings between
    /// them for six days — a list here would be the third one to drift.
    /// </remarks>
    [Fact]
    public void No_string_anywhere_in_the_ledger_is_mis_encoded()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        foreach (var line in File.ReadLines(LedgerPath).Where(l => l.Trim().Length > 0))
        {
            using var document = JsonDocument.Parse(line);

            foreach (var text in Strings(document.RootElement))
                Assert.DoesNotContain(text, IsMojibake);
        }
    }

    private static IEnumerable<string> Strings(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Strings(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
            JsonValueKind.String => [element.GetString()!],
            _ => [],
        };

    /// <summary>
    /// A CP1252 lead byte read back as UTF-8, or a character lost outright — what text looks
    /// like after crossing a console pipe in the wrong codepage.
    /// </summary>
    private static bool IsMojibake(char c) => c is 'Â' or 'Ã' or 'â' or '�';

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
                // Chords may legitimately be empty, and that is information rather than a gap:
                // a track the recogniser commits nothing on leaves the tonic check with only
                // the flicker to argue from, which is exactly when a verdict should not claim
                // positive confirmation. Held for 125 of 126 runs until an underground techno
                // single produced none, so the old assertion was an observation about the
                // corpus rather than a property of the runner.
                Assert.NotNull(e.Chords);
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
    public void Every_source_carries_a_kind_from_the_closed_set()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        Assert.NotEmpty(sources);
        // The whole point of the closed vocabulary: nothing falls through. This is the assertion
        // the old `tier` field could never have passed — it held over thirty distinct values.
        Assert.All(sources, s => Assert.NotEqual(SourceKind.Unknown, s.Kind));
        Assert.All(sources, s => Assert.NotEqual(SourceKind.Unclassified, s.Kind));
        Assert.Contains(sources, s => s.Kind is SourceKind.Authoritative);
        Assert.Contains(sources, s => s.Kind is SourceKind.Algorithmic);
    }

    [Fact]
    public void A_sources_stance_on_the_verdict_is_computed_not_read()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        // The single most useful thing the sources list can show, and for most of this record's
        // life it could only be had by reading every line of prose. Both stances are real: the
        // ledger contains sources that back the verdict and sources that argue with it.
        Assert.Contains(sources, s => s.Stance is SourceStance.Agrees);
        Assert.Contains(sources, s => s.Stance is SourceStance.Dissents);
    }

    [Fact]
    public void A_source_with_no_page_still_says_where_it_came_from()
    {
        Assert.SkipUnless(Available, "The keytag ledger must be present.");

        // The user photographed sheet music: no host to classify by, and among the strongest
        // evidence in the record. The old schema could only record that as a URL that wasn't one.
        Assert.Contains(
            All().SelectMany(e => e.Sources),
            s => s.Url is null && s.Via is { Length: > 0 }
        );
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
            s => s.Kind is SourceKind.ChordRecogniser
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

                // Proportional to the octave, not absolute. An absolute 0.05 is 2.5% at 2x and
                // 10% at 1/2x, so it called Delta Heavy - White Flag's 6.2% miss (92.38 against
                // 174, where half-time is 87) an octave on the way down while it would have
                // called the same error a conflict on the way up.
                //
                // Deliberately tighter than BpmConflicts' own 0.05 in log2 (~3.5% here). This
                // catches octaves being flagged at all; pinning the exact boundary from live
                // data is what made it wrong.
                bool NearlyAt(double octave) => Math.Abs(ratio - octave) / octave < 0.02;

                Assert.False(
                    NearlyAt(2) || NearlyAt(0.5),
                    $"{e.Song}: {ratio:F3} is a tempo octave and should not be flagged."
                );
            }
        );
    }
}
