using System.Text.Json;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public class KeyLedgerTests : IDisposable
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
        return KeyLedger.Read(_path);
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
        // The supersede carries no URL at all. Going straight from video ID to "the run that
        // mentioned it" would return the stale WITHHELD; resolving to the identity first is
        // what makes the newer verdict win.
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
    public void A_withheld_run_still_counts_as_having_been_processed()
    {
        // The removal predicate is "a verdict was recorded", not "a key was produced".
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
    [InlineData("SOMETHING_NEW")]
    public void Only_high_and_medium_verdicts_are_taggable(string confidence)
    {
        var ledger = ReadLines(
            Line("2026-08-01T00:00:00+00:00", "a - b", null, "C major", confidence)
        );

        Assert.False(Assert.Single(ledger.Runs).HasTaggableKey);
    }

    [Fact]
    public void A_malformed_line_is_skipped_not_fatal()
    {
        // The file is append-only and may be caught mid-write.
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
        var ledger = KeyLedger.Read(Path.Combine(Path.GetTempPath(), "no-such-ledger.jsonl"));

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
