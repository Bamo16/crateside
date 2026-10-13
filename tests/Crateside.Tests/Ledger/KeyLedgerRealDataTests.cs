using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Samples;

namespace Crateside.Tests.Ledger;

/// <summary>A stem on disk joins to a ledger verdict, against the real ledger and library.</summary>
public sealed class KeyLedgerRealDataTests
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
        Assert.True(ledger.SongCount <= ledger.Runs.Count);
    }

    [Fact]
    public void A_modal_verdict_reads_as_a_collapse_of_the_tagged_key()
    {
        Assert.SkipUnless(File.Exists(LedgerPath), $"{LedgerPath} not present.");

        var modal = KeyLedger.Read(LedgerPath).Runs.Where(r => r.IsCollapsed).ToList();

        // A mode must never contradict the tag it collapses to.
        Assert.NotEmpty(modal);
        Assert.All(modal, r => Assert.NotNull(r.ModalKey));
        Assert.All(modal, r => Assert.Equal(r.Key!.Split(' ')[0], r.ModalKey!.Split(' ')[0]));
    }

    [Fact]
    public void Nearly_every_run_carries_an_extractable_video_id()
    {
        Assert.SkipUnless(File.Exists(LedgerPath), $"{LedgerPath} not present.");

        var runs = KeyLedger.Read(LedgerPath).Runs;
        var withId = runs.Count(r => r.SourceVideoId is not null);

        // Measured at 124/125 at design; a run from a local file has no URL.
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

        var unresolved = Directory
            .GetFiles(StagingRoot, "*.flac")
            .Select(path => (Name: Path.GetFileName(path), Tags: StemTagReader.Read(path)))
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

        // Its ledger identity is "alan gomez, luck ra - luck ra | mission 15": only the video ID
        // in the tag can find it.
        var stem = new[] { StagingRoot, new CratesidePaths().LibraryFolder }
            .Select(r => Path.Combine(r, "Alan Gomez - Mission 15 (Vocal - Full).flac"))
            .FirstOrDefault(File.Exists);

        Assert.SkipWhen(stem is null, "The Mission 15 stem is no longer in the library.");

        var tags = StemTagReader.Read(stem);
        Assert.NotNull(tags?.SourceVideoId);

        Assert.True(
            KeyLedger.Read(LedgerPath).TryGetLatestByVideoId(tags.SourceVideoId, out var run)
        );
        Assert.DoesNotContain("mission 15", Path.GetFileNameWithoutExtension(stem));
        Assert.Contains("mission 15", run.Song, StringComparison.OrdinalIgnoreCase);
    }
}
