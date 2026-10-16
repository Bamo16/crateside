using System.Text.Json;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class KeyLedgerTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n") + ".jsonl"
    );

    public KeyLedgerTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);

        GC.SuppressFinalize(this);
    }

    private KeyLedger ReadLines(params string[] lines)
    {
        File.WriteAllLines(_path, lines);
        return TestLedger.From(_path);
    }

    private static string Line(
        string timestamp,
        string song,
        string? url,
        string? key,
        string confidence = "HIGH"
    ) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp,
                song,
                artist = "A",
                title = "T",
                source_url = url,
                verdict = new { key, confidence },
                bpm = new
                {
                    value = 145,
                    source = "web",
                    beatdetect = 108.72,
                },
            }
        );

    [Fact]
    public void Reads_the_fields_the_board_needs()
    {
        var ledger = ReadLines(
            Line(
                "2026-07-27T01:48:04.008300+00:00",
                "kobaryo - kawaii",
                "https://music.youtube.com/watch?v=wqqARL9SDec",
                "D# minor",
                "MEDIUM"
            )
        );

        var run = Assert.Single(ledger.Runs);
        Assert.Equal("kobaryo - kawaii", run.Song);
        Assert.Equal("D# minor", run.Key);
        Assert.Equal(KeyConfidence.Medium, run.Confidence);
        Assert.Equal("wqqARL9SDec", run.SourceVideoId);
        Assert.Equal(145, run.Bpm);
        Assert.True(run.HasTaggableKey);
    }

    [Fact]
    public void The_latest_run_for_a_song_supersedes_the_earlier_one()
    {
        var ledger = ReadLines(
            Line(
                "2026-08-01T00:00:00+00:00",
                "marine - horny money world",
                "https://y.t/x",
                null,
                "WITHHELD"
            ),
            Line(
                "2026-08-12T20:52:12+00:00",
                "marine - horny money world",
                "https://www.youtube.com/watch?v=NSLYAT7sjAA",
                "C# minor",
                "MEDIUM"
            )
        );

        Assert.True(ledger.TryGetLatestBySong("marine - horny money world", out var run));
        Assert.Equal("C# minor", run.Key);
        Assert.Equal(1, ledger.SongCount);
    }

    [Fact]
    public void A_video_id_resolves_through_the_identity_to_the_newest_verdict()
    {
        // The supersede carries no URL, so only resolving to the identity first finds it.
        var ledger = ReadLines(
            Line(
                "2026-08-01T00:00:00+00:00",
                "a - b",
                "https://www.youtube.com/watch?v=NSLYAT7sjAA",
                null,
                "WITHHELD"
            ),
            Line("2026-08-12T00:00:00+00:00", "a - b", null, "C# minor", "MEDIUM")
        );

        Assert.True(ledger.TryGetLatestByVideoId("NSLYAT7sjAA", out var run));
        Assert.Equal("C# minor", run.Key);
        Assert.Equal(KeyConfidence.Medium, run.Confidence);
    }

    [Fact]
    public void A_line_carrying_no_verdict_is_refused_rather_than_taking_the_songs_key_away()
    {
        // Why a restatement carries the whole verdict (keytag ADR 0008).
        File.WriteAllLines(
            _path,
            [
                Line(
                    "2026-08-01T00:00:00+00:00",
                    "a - b",
                    "https://www.youtube.com/watch?v=NSLYAT7sjAA",
                    "C# minor",
                    "HIGH"
                ),
                JsonSerializer.Serialize(
                    new
                    {
                        timestamp = "2026-08-12T00:00:00+00:00",
                        song = "a - b",
                        source_url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                    }
                ),
            ]
        );
        using var store = TestLedger.Store(_path);

        Assert.True(store.Read().TryGetLatestByVideoId("NSLYAT7sjAA", out var original));
        Assert.Equal("C# minor", original.Key);
        Assert.False(store.Read().HasRunFor("dQw4w9WgXcQ"));
        Assert.Equal("2026-08-12T00:00:00+00:00", Assert.Single(store.ImportRefusals).Line);
    }

    /// <summary>A restatement shaped like a real <c>keytag-restate --dry-run</c>.</summary>
    private static string Restatement(
        string timestamp,
        string song,
        string url,
        string key,
        string restates,
        string confidence = "HIGH"
    ) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp,
                song,
                artist = "A",
                title = "T",
                verdict = new
                {
                    key,
                    mode = (string?)null,
                    confidence,
                },
                bpm = new
                {
                    value = 145,
                    source = "web",
                    beatdetect = 108.72,
                },
                has_vocals = (bool?)null,
                source_url = url,
                restates,
                input_path = (string?)null,
                input_kind = (string?)null,
                sources = (string?)null,
                tonic_check = (string?)null,
                reasoning = "Restates the run of "
                    + restates
                    + " under a new source URL. Same song, no detectors run, verdict copied unchanged.",
                machine = (string?)null,
            }
        );

    [Fact]
    public void A_restatement_resolves_the_new_video_id_to_the_same_verdict()
    {
        var ledger = ReadLines(
            Line("2026-09-01T00:00:00+00:00", "a - b", Watch("OLDoldOLDol"), "C# minor", "MEDIUM"),
            Restatement(
                "2026-09-09T00:00:00+00:00",
                "a - b",
                Watch("NEWnewNEWne"),
                "C# minor",
                "2026-09-01T00:00:00+00:00",
                "MEDIUM"
            )
        );

        Assert.True(ledger.TryGetLatestByVideoId("NEWnewNEWne", out var viaNew));
        Assert.Equal("C# minor", viaNew.Key);

        Assert.True(ledger.TryGetLatestByVideoId("OLDoldOLDol", out var viaOld));
        Assert.Equal(viaNew.Key, viaOld.Key);
        Assert.Equal(viaNew.Confidence, viaOld.Confidence);

        Assert.True(ledger.HasRunFor("NEWnewNEWne"));
        Assert.True(ledger.HasRunFor("OLDoldOLDol"));
    }

    [Fact]
    public void A_re_judgment_after_a_restatement_still_wins_through_both_ids()
    {
        var ledger = ReadLines(
            Line("2026-09-01T00:00:00+00:00", "a - b", Watch("OLDoldOLDol"), "C# minor", "MEDIUM"),
            Restatement(
                "2026-09-09T00:00:00+00:00",
                "a - b",
                Watch("NEWnewNEWne"),
                "C# minor",
                "2026-09-01T00:00:00+00:00",
                "MEDIUM"
            ),
            Line("2026-09-10T00:00:00+00:00", "a - b", Watch("NEWnewNEWne"), "F major")
        );

        foreach (var videoId in (string[])["OLDoldOLDol", "NEWnewNEWne"])
        {
            Assert.True(ledger.TryGetLatestByVideoId(videoId, out var run));
            Assert.Equal("F major", run.Key);
            Assert.Equal(KeyConfidence.High, run.Confidence);
        }
    }

    [Fact]
    public void A_restatement_leaves_the_song_count_alone()
    {
        var ledger = ReadLines(
            Line("2026-09-01T00:00:00+00:00", "a - b", Watch("OLDoldOLDol"), "C# minor", "MEDIUM"),
            Restatement(
                "2026-09-09T00:00:00+00:00",
                "a - b",
                Watch("NEWnewNEWne"),
                "C# minor",
                "2026-09-01T00:00:00+00:00",
                "MEDIUM"
            )
        );

        Assert.Equal(1, ledger.SongCount);
    }

    private static string Watch(string videoId) => $"https://music.youtube.com/watch?v={videoId}";

    [Fact]
    public void A_withheld_run_still_counts_as_having_been_processed()
    {
        var ledger = ReadLines(
            Line(
                "2026-08-01T00:00:00+00:00",
                "a - b",
                "https://www.youtube.com/watch?v=NSLYAT7sjAA",
                null,
                "WITHHELD"
            )
        );

        Assert.True(ledger.HasRunFor("NSLYAT7sjAA"));
        Assert.True(ledger.TryGetLatestByVideoId("NSLYAT7sjAA", out var run));
        Assert.Null(run.Key);
        Assert.False(run.HasTaggableKey);
    }

    [Theory]
    [InlineData("LOW")]
    [InlineData("WITHHELD")]
    public void Only_high_and_medium_verdicts_are_taggable(string confidence)
    {
        var ledger = ReadLines(
            Line("2026-08-01T00:00:00+00:00", "a - b", null, "C major", confidence)
        );

        Assert.False(Assert.Single(ledger.Runs).HasTaggableKey);
    }

    [Fact]
    public void A_confidence_the_ledger_does_not_know_is_refused()
    {
        File.WriteAllLines(
            _path,
            [Line("2026-08-01T00:00:00+00:00", "a - b", null, "C major", "SOMETHING_NEW")]
        );
        using var store = TestLedger.Store(_path);

        Assert.Empty(store.Read().Runs);
        Assert.StartsWith("confidence", Assert.Single(store.ImportRefusals).Problem);
    }

    [Fact]
    public void A_malformed_line_is_skipped_not_fatal()
    {
        var ledger = ReadLines(
            Line("2026-08-01T00:00:00+00:00", "a - b", null, "C major"),
            "",
            "{\"timestamp\": \"2026-08-02T00:00:00+00:00\", \"song\": \"truncated\"",
            Line("2026-08-03T00:00:00+00:00", "c - d", null, "D major")
        );

        Assert.Equal(2, ledger.Runs.Count);
    }

    [Fact]
    public void An_absent_ledger_reads_empty_rather_than_throwing()
    {
        var ledger = TestLedger.From(Path.Combine(Path.GetTempPath(), "no-such-ledger.jsonl"));

        Assert.Empty(ledger.Runs);
        Assert.False(ledger.HasRunFor("NSLYAT7sjAA"));
    }

    [Fact]
    public void An_unknown_video_id_resolves_to_nothing()
    {
        var ledger = ReadLines(
            Line(
                "2026-08-01T00:00:00+00:00",
                "a - b",
                "https://www.youtube.com/watch?v=NSLYAT7sjAA",
                "C major"
            )
        );

        Assert.False(ledger.TryGetLatestByVideoId("vfBJjufAG1E", out _));
    }
}
