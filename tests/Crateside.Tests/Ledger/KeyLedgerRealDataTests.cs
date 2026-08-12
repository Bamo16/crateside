using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Tests.Ledger;

/// <summary>
/// The whole point of the design is that a stem on disk joins to a ledger verdict. This
/// asserts that end-to-end against the real ledger and the real staging folder.
/// </summary>
public class KeyLedgerRealDataTests
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";

    [Fact]
    public void Reads_the_real_ledger()
    {
        Assert.SkipUnless(File.Exists(LedgerPath), $"{LedgerPath} not present.");

        var ledger = KeyLedger.Read(LedgerPath);

        Assert.NotEmpty(ledger.Runs);
        Assert.All(ledger.Runs, r => Assert.NotEmpty(r.Song));
        // A supersede files under an existing identity, so there are fewer songs than runs.
        Assert.True(ledger.SongCount <= ledger.Runs.Count);
    }

    [Fact]
    public void Nearly_every_run_carries_an_extractable_video_id()
    {
        Assert.SkipUnless(File.Exists(LedgerPath), $"{LedgerPath} not present.");

        var runs = KeyLedger.Read(LedgerPath).Runs;
        var withId = runs.Count(r => r.SourceVideoId is not null);

        // The design was measured at 124/125. Asserting a ratio rather than a count keeps
        // this meaningful as the ledger grows; a run recorded from a local file legitimately
        // has no URL.
        Assert.True(
            withId >= runs.Count - 2,
            $"only {withId} of {runs.Count} runs carry a video ID"
        );
    }

    [Fact]
    public void Every_staged_stem_joins_to_a_ledger_verdict()
    {
        Assert.SkipUnless(
            File.Exists(LedgerPath) && Directory.Exists(StagingRoot),
            "Ledger and staging root must both be present."
        );

        var ledger = KeyLedger.Read(LedgerPath);
        var reader = new StemTagReader();

        var unresolved = Directory
            .GetFiles(StagingRoot, "*.flac")
            .Select(path => (Name: Path.GetFileName(path), Tags: reader.Read(path)))
            .Where(s =>
                s.Tags?.SourceVideoId is null
                || !ledger.TryGetLatestByVideoId(s.Tags.SourceVideoId, out _)
            )
            .Select(s => s.Name)
            .ToArray();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void The_join_survives_a_renamed_file()
    {
        Assert.SkipUnless(
            File.Exists(LedgerPath) && Directory.Exists(StagingRoot),
            "Ledger and staging root must both be present."
        );

        // The file is named "Alan Gomez - Mission 15"; its ledger identity is
        // "alan gomez, luck ra - luck ra | mission 15". The filename would never have found
        // it — the video ID in the tag is what does.
        var stem = Path.Combine(StagingRoot, "Alan Gomez - Mission 15 (Vocal - Full).flac");
        Assert.SkipUnless(File.Exists(stem), "The Mission 15 stem is no longer staged.");

        var tags = new StemTagReader().Read(stem);
        Assert.NotNull(tags?.SourceVideoId);

        Assert.True(
            KeyLedger.Read(LedgerPath).TryGetLatestByVideoId(tags.SourceVideoId, out var run)
        );
        Assert.DoesNotContain("mission 15", Path.GetFileNameWithoutExtension(stem));
        Assert.Contains("mission 15", run.Song, StringComparison.OrdinalIgnoreCase);
    }
}
