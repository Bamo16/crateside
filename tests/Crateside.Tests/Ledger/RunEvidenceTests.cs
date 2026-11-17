using System.Text.Json;

namespace Crateside.Tests.Ledger;

public sealed class RunEvidenceTests : IDisposable
{
    private const string Song = "a - b";

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n") + ".jsonl"
    );

    public RunEvidenceTests() => Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_restatement_does_not_become_the_evidence_for_its_song()
    {
        // A restatement carries no machine layer at all (ADR 0055).
        File.WriteAllLines(_path, [Run("2026-09-01T00:00:00+00:00"), Restatement()]);

        var evidence = TestLedger.EvidenceFor(_path, Song);

        Assert.NotNull(evidence);
        Assert.Equal("the argument", evidence.Reasoning);
        Assert.NotEmpty(evidence.Chords);
    }

    [Fact]
    public void A_real_re_judgment_still_supersedes()
    {
        File.WriteAllLines(
            _path,
            [
                Run("2026-09-01T00:00:00+00:00"),
                Restatement(),
                Run("2026-09-10T00:00:00+00:00", "the newer argument"),
            ]
        );

        Assert.Equal("the newer argument", TestLedger.EvidenceFor(_path, Song)?.Reasoning);
    }

    [Fact]
    public void A_runs_chord_starts_are_read_beside_its_chords()
    {
        File.WriteAllLines(_path, [Run("2026-09-01T00:00:00+00:00", chordStarts: [0.0, 2.5])]);

        var run = TestLedger.EvidenceFor(_path, Song);

        Assert.Equal(["Cm", "Ab"], run?.Chords);
        Assert.Equal([0.0, 2.5], run?.ChordStarts);
    }

    [Fact]
    public void A_run_recorded_before_chord_starts_were_kept_has_none() =>
        Assert.Empty(
            TestLedger.EvidenceFor(WriteRun("2026-09-01T00:00:00+00:00"), Song)?.ChordStarts ?? [1]
        );

    private string WriteRun(string timestamp)
    {
        File.WriteAllLines(_path, [Run(timestamp)]);

        return _path;
    }

    private static string Run(
        string timestamp,
        string reasoning = "the argument",
        double[]? chordStarts = null
    ) =>
        JsonSerializer.Serialize(
            new
            {
                timestamp,
                song = Song,
                artist = "A",
                title = "T",
                source_url = "https://music.youtube.com/watch?v=OLDoldOLDol",
                verdict = new { key = "C minor", confidence = "HIGH" },
                reasoning,
                machine = new
                {
                    chords = new[] { "Cm", "Ab" },
                    chord_starts = chordStarts,
                    key_events = new[] { new { t = 0.0, key = "C minor" } },
                },
            }
        );

    private static string Restatement() =>
        JsonSerializer.Serialize(
            new
            {
                timestamp = "2026-09-09T00:00:00+00:00",
                song = Song,
                artist = "A",
                title = "T",
                source_url = "https://music.youtube.com/watch?v=NEWnewNEWne",
                verdict = new { key = "C minor", confidence = "HIGH" },
                restates = "2026-09-01T00:00:00+00:00",
                reasoning = "Restates the run of 2026-09-01. Verdict copied unchanged.",
                machine = (object?)null,
            }
        );
}
