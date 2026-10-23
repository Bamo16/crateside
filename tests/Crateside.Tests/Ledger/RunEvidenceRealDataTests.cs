using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class RunEvidenceRealDataTests
{
    /// <summary>The current three (ADR 0055), the retired bake-off's three, and those with madmom and edma backfilled (#139).</summary>
    private static readonly string[] DetectorSets =
    [
        "Essentia edma, ffmpeg keydetect, madmom",
        "CREMA, ffmpeg keydetect, librosa (K-S)",
        "CREMA, Essentia edma, ffmpeg keydetect, librosa (K-S), madmom",
    ];

    private static bool Available => TestLedger.HasReal;

    private static readonly Lazy<IReadOnlyList<RunEvidence>> Evidence = new(() =>
        [.. TestLedger.LatestEvidenceReal().Values]
    );

    private static IReadOnlyList<RunEvidence> All() => Evidence.Value;

    [Fact]
    public void Every_song_on_the_board_can_be_expanded()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        var ledger = TestLedger.FromReal();
        var latest = TestLedger.LatestEvidenceReal();

        Assert.All(
            ledger.Runs.Select(r => r.Song).Distinct(StringComparer.OrdinalIgnoreCase),
            song => Assert.True(latest.ContainsKey(song), song)
        );
    }

    [Fact]
    public void The_reasoning_is_always_there_because_it_is_the_actual_argument()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        Assert.All(All(), e => Assert.False(string.IsNullOrWhiteSpace(e.Reasoning)));
    }

    /// <summary>Walks every string rather than a list of fields, which can drift.</summary>
    [Fact]
    public void No_string_anywhere_in_the_ledger_is_mis_encoded()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        foreach (
            var line in File.ReadLines(
                    Path.ChangeExtension(new CratesidePaths().LedgerDatabase, ".jsonl")
                )
                .Where(l => l.Trim().Length > 0)
        )
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
        Assert.SkipUnless(Available, "The ledger must be present.");

        Assert.All(
            All(),
            e =>
            {
                // A run recorded without its evidence file holds no machine layer.
                if (e.Algorithms.Count is 0)
                    return;

                Assert.Contains(
                    string.Join(
                        ", ",
                        e.Algorithms.Select(result => result.Name).Order(StringComparer.Ordinal)
                    ),
                    DetectorSets
                );
                Assert.Equal(3, e.Agreement.Count);
                Assert.All(
                    e.Agreement,
                    pair =>
                        Assert.Contains(
                            pair.Status,
                            (string[])["full", "relative", "none", "single"]
                        )
                );
                // Chords may legitimately be empty.
                Assert.NotNull(e.Chords);
                Assert.NotEmpty(e.KeyEvents);
            }
        );
    }

    [Fact]
    public void A_run_with_no_human_source_is_an_ordinary_state()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        var all = All();

        Assert.Contains(all, e => e.Sources.Count == 0);
        Assert.Contains(all, e => e.Sources.Count > 0);
    }

    [Fact]
    public void Every_source_carries_a_kind_from_the_closed_set()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.NotEqual(SourceKind.Unknown, s.Kind));
        Assert.All(sources, s => Assert.NotEqual(SourceKind.Unclassified, s.Kind));
        Assert.Contains(sources, s => s.Kind is SourceKind.Authoritative);
        Assert.Contains(sources, s => s.Kind is SourceKind.Algorithmic);
    }

    [Fact]
    public void A_sources_stance_on_the_verdict_is_computed_not_read()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        Assert.Contains(sources, s => s.Stance is SourceStance.Agrees);
        Assert.Contains(sources, s => s.Stance is SourceStance.Dissents);
    }

    [Fact]
    public void A_source_with_no_page_still_says_where_it_came_from()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        Assert.Contains(
            All().SelectMany(e => e.Sources),
            s => s.Url is null && s.Via is { Length: > 0 }
        );
    }

    [Fact]
    public void A_recorded_url_that_is_not_a_link_is_not_offered_as_one()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        var sources = All().SelectMany(e => e.Sources).ToList();

        // Real entries include "user-provided image", which has no page to link to.
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
        Assert.SkipUnless(Available, "The ledger must be present.");

        Assert.Contains(
            All().SelectMany(e => e.Sources),
            s => s.Kind is SourceKind.ChordRecogniser
        );
    }

    [Fact]
    public void Full_agreement_is_reported_as_agreement()
    {
        Assert.SkipUnless(Available, "The ledger must be present.");

        var pairs = All().SelectMany(e => e.Agreement).ToList();

        Assert.Contains(pairs, p => p.Agrees);
        Assert.All(pairs.Where(p => p.Agrees), p => Assert.NotNull(p.Recommended));
        Assert.All(pairs.Where(p => p.Status == "none"), p => Assert.False(p.Agrees));
    }
}
