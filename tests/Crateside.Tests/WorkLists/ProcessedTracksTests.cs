using System.Text.Json;
using Crateside.Core.Ledger;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.WorkLists;

public sealed class ProcessedTracksTests : IDisposable
{
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

    /// <summary>Copies a real staged stem and rewrites its provenance.</summary>
    private string? StemFor(string trackKey, string name)
    {
        // Any staged FLAC: a named one skips itself once the user finishes that track.
        var source =
            RealPaths.Create().StagingFolder is var staging && Directory.Exists(staging)
                ? Directory
                    .EnumerateFiles(staging, "*.flac")
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
            file.Tag.Comment = string.Format(RealStemComment, trackKey);
            file.Save();
        }

        return path;
    }

    private KeyLedger LedgerWith(params string[] trackKeys)
    {
        File.WriteAllLines(
            _ledgerPath,
            trackKeys.Select(
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

        return TestLedger.From(_ledgerPath);
    }

    [Fact]
    public void A_verdict_and_a_stem_together_are_fully_processed()
    {
        const string TrackKey = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(TrackKey, "done.flac") is null, "Needs a real stem to copy.");

        var processed = ProcessedTracks.Scan(LedgerWith(TrackKey), [_root]);

        Assert.True(processed.IsFullyProcessed(TrackKey));
        Assert.Single(processed.StemsFor(TrackKey));
        Assert.Empty(processed.VerdictWithoutStems);
    }

    [Fact]
    public void A_stem_remembers_its_preset_and_the_root_it_was_found_in()
    {
        const string TrackKey = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(TrackKey, "kept.flac") is null, "Needs a real stem to copy.");

        var stem = Assert.Single(
            ProcessedTracks.Scan(LedgerWith(TrackKey), [_root]).StemsFor(TrackKey)
        );

        Assert.Equal("Vocal - Full", stem.Preset);
        Assert.Equal(_root, stem.Root);
    }

    [Fact]
    public void A_verdict_with_no_stem_is_not_processed()
    {
        // The run recorded a key and then died during separation. It must stay queued.
        const string TrackKey = "NSLYAT7sjAA";

        var processed = ProcessedTracks.Scan(LedgerWith(TrackKey), [_root]);

        Assert.False(processed.IsFullyProcessed(TrackKey));
        Assert.Contains(TrackKey, processed.VerdictWithoutStems);
    }

    [Fact]
    public void A_stem_with_no_verdict_is_not_processed()
    {
        const string TrackKey = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(TrackKey, "orphan.flac") is null, "Needs a real stem to copy.");

        var processed = ProcessedTracks.Scan(LedgerWith(), [_root]);

        Assert.False(processed.IsFullyProcessed(TrackKey));
        Assert.Empty(processed.FullyProcessed);
    }

    [Fact]
    public void Deleted_stems_leave_a_track_queued_rather_than_processed()
    {
        // Deleted stems and a separation that never finished are indistinguishable.
        const string TrackKey = "vfBJjufAG1E";
        var stem = StemFor(TrackKey, "deleted.flac");
        Assert.SkipWhen(stem is null, "Needs a real stem to copy.");

        var ledger = LedgerWith(TrackKey);
        Assert.True(ProcessedTracks.Scan(ledger, [_root]).IsFullyProcessed(TrackKey));

        File.Delete(stem);

        Assert.False(ProcessedTracks.Scan(ledger, [_root]).IsFullyProcessed(TrackKey));
    }

    [Fact]
    public void Stems_are_found_in_any_root_so_moving_them_out_of_staging_keeps_them_processed()
    {
        const string TrackKey = "vfBJjufAG1E";
        var stem = StemFor(TrackKey, "moved.flac");
        Assert.SkipWhen(stem is null, "Needs a real stem to copy.");

        var destination = Path.Combine(_root, "mashup-samples");
        Directory.CreateDirectory(destination);
        File.Move(stem, Path.Combine(destination, Path.GetFileName(stem)));

        var processed = ProcessedTracks.Scan(LedgerWith(TrackKey), [_root]);

        Assert.True(processed.IsFullyProcessed(TrackKey));
    }

    [Fact]
    public void The_scan_is_idempotent()
    {
        const string TrackKey = "vfBJjufAG1E";
        Assert.SkipWhen(StemFor(TrackKey, "idempotent.flac") is null, "Needs a real stem.");

        var ledger = LedgerWith(TrackKey);

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
