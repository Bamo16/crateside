using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.Queue;

namespace Crateside.Tests.Queue;

/// <summary>
/// The removal predicate gates an irreversible action — deleting from the user's playlist —
/// so it is tested for what it must <em>refuse</em> as much as what it accepts.
/// </summary>
public class ProcessedTracksTests : IDisposable
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    private const string RealStemComment =
        "stemforge/0.2.1.1 | preset: Vocal - Full | date: 2026-08-05 | "
        + "source: https://www.youtube.com/watch?v={0} | codec: mp4a.40.2";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _ledgerPath;

    public ProcessedTracksTests()
    {
        Directory.CreateDirectory(_root);
        _ledgerPath = Path.Combine(_root, "runs.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Copies a real staged stem and rewrites its provenance, so the scan is exercised against
    /// an actual FLAC that TagLibSharp must parse — not a stub reader.
    /// </summary>
    private string? StemFor(string videoId, string name)
    {
        // Any staged FLAC will do, and naming one was a mistake: staging is the folder whose
        // entire purpose is that things leave it, so a fixture pinned to a filename skips itself
        // the first time the user finishes that track. Five tests here went quiet at once when
        // "Alan Gomez - Mission 15 (Vocal - Full).flac" was moved to Mashup Samples.
        var source = Directory.Exists(StagingRoot)
            ? Directory
                .EnumerateFiles(StagingRoot, "*.flac")
                .Order(StringComparer.Ordinal)
                .FirstOrDefault()
            : null;

        if (source is null)
            return null;

        var stemsDir = Path.Combine(_root, "stems");
        Directory.CreateDirectory(stemsDir);

        var path = Path.Combine(stemsDir, name);
        File.Copy(source, path, overwrite: true);

        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Comment = string.Format(RealStemComment, videoId);
            file.Save();
        }

        return path;
    }

    private KeyLedger LedgerWith(params string[] videoIds)
    {
        File.WriteAllLines(
            _ledgerPath,
            videoIds.Select(
                (id, i) =>
                    JsonSerializer.Serialize(
                        new
                        {
                            timestamp = $"2026-08-0{i + 1}T00:00:00+00:00",
                            song = $"song {i}",
                            source_url = $"https://www.youtube.com/watch?v={id}",
                            verdict = new { key = "C major", confidence = "HIGH" },
                        }
                    )
            )
        );

        return KeyLedger.Read(_ledgerPath);
    }

    [Fact]
    public void A_verdict_and_a_stem_together_are_fully_processed()
    {
        const string VideoId = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(VideoId, "done.flac") is null, "Needs a real stem to copy.");

        var processed = ProcessedTracks.Scan(LedgerWith(VideoId), [_root]);

        Assert.True(processed.IsFullyProcessed(VideoId));
        Assert.Single(processed.StemsFor(VideoId));
        Assert.Empty(processed.VerdictWithoutStems);
    }

    [Fact]
    public void A_verdict_with_no_stem_is_not_processed()
    {
        // The run recorded a key and then died during separation. It must stay queued.
        const string VideoId = "NSLYAT7sjAA";

        var processed = ProcessedTracks.Scan(LedgerWith(VideoId), [_root]);

        Assert.False(processed.IsFullyProcessed(VideoId));
        Assert.Contains(VideoId, processed.VerdictWithoutStems);
    }

    [Fact]
    public void A_stem_with_no_verdict_is_not_processed()
    {
        const string VideoId = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(VideoId, "orphan.flac") is null, "Needs a real stem to copy.");

        var processed = ProcessedTracks.Scan(LedgerWith(), [_root]);

        Assert.False(processed.IsFullyProcessed(VideoId));
        Assert.Empty(processed.FullyProcessed);
    }

    [Fact]
    public void Deleted_stems_leave_a_track_queued_rather_than_processed()
    {
        // The open question, pinned as behaviour: deleting stems is indistinguishable from a
        // separation that never finished, and both correctly fail the test.
        const string VideoId = "vfBJjufAG1E";
        var stem = StemFor(VideoId, "deleted.flac");
        Assert.SkipWhen(stem is null, "Needs a real stem to copy.");

        var ledger = LedgerWith(VideoId);
        Assert.True(ProcessedTracks.Scan(ledger, [_root]).IsFullyProcessed(VideoId));

        File.Delete(stem);

        Assert.False(ProcessedTracks.Scan(ledger, [_root]).IsFullyProcessed(VideoId));
    }

    [Fact]
    public void Stems_are_found_in_any_root_so_moving_them_out_of_staging_keeps_them_processed()
    {
        const string VideoId = "vfBJjufAG1E";
        var stem = StemFor(VideoId, "moved.flac");
        Assert.SkipWhen(stem is null, "Needs a real stem to copy.");

        var destination = Path.Combine(_root, "mashup-samples");
        Directory.CreateDirectory(destination);
        File.Move(stem, Path.Combine(destination, Path.GetFileName(stem)));

        var processed = ProcessedTracks.Scan(LedgerWith(VideoId), [_root]);

        Assert.True(processed.IsFullyProcessed(VideoId));
    }

    [Fact]
    public void The_scan_is_idempotent()
    {
        const string VideoId = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(VideoId, "idempotent.flac") is null, "Needs a real stem.");

        var ledger = LedgerWith(VideoId);

        Assert.Equal(
            ProcessedTracks.Scan(ledger, [_root]).FullyProcessed,
            ProcessedTracks.Scan(ledger, [_root]).FullyProcessed
        );
    }

    [Fact]
    public void A_missing_root_is_skipped_rather_than_throwing()
    {
        var processed = ProcessedTracks.Scan(
            LedgerWith("vfBJjufAG1E"),
            [Path.Combine(_root, "does-not-exist")]
        );

        Assert.Empty(processed.FullyProcessed);
    }
}
