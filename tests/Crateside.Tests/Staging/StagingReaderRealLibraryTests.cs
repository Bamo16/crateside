using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>Staging read against the real staging folder and the real ledger.</summary>
public sealed class StagingReaderRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private static bool Available => RealLibrary.HasStems(StagingRoot) && File.Exists(LedgerPath);

    private static IReadOnlyList<StagedSong> Staged() =>
        new StagingReader().Build(StagingRoot, KeyLedger.Read(LedgerPath));

    [Fact]
    public void Rolls_the_staged_stems_up_into_songs()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        var songs = Staged();

        Assert.NotEmpty(songs);
        Assert.Equal(
            Directory.GetFiles(StagingRoot, "*.flac").Length,
            songs.Sum(s => s.Stems.Count)
        );
        Assert.All(songs, song => Assert.NotEmpty(song.Stems));
        Assert.All(songs, song => Assert.NotEmpty(song.DisplayName));
    }

    [Fact]
    public void Every_staged_song_resolves_to_a_ledger_verdict()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        var songs = Staged();

        Assert.All(songs, song => Assert.NotNull(song.SourceVideoId));
        Assert.DoesNotContain(songs, s => s.AwaitingDetection);
    }

    [Fact]
    public void The_row_is_named_from_the_ledger_identity_not_the_filename()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        var song = Staged()
            .SingleOrDefault(s =>
                s.Stems.Any(stem => stem.FileName.StartsWith("Alan Gomez - Mission 15"))
            );

        Assert.SkipWhen(song is null, "The Mission 15 stems are no longer staged.");
        Assert.Contains("Luck Ra", song.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unfinished_songs_sort_above_finished_ones()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        var songs = Staged();
        var firstFinished = songs.ToList().FindIndex(s => s.IsFullyTagged);

        Assert.SkipWhen(firstFinished < 0, "Nothing in staging is fully tagged.");
        Assert.All(songs.Take(firstFinished), song => Assert.False(song.IsFullyTagged));
    }

    [Fact]
    public void Reports_per_stem_tagging_without_inventing_a_key()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        foreach (var song in Staged())
        {
            Assert.InRange(song.TaggedStemCount, 0, song.Stems.Count);
            Assert.All(
                song.Stems,
                stem =>
                {
                    Assert.Equal(stem.AbletonKey is not null, stem.IsKeyTagged);
                    Assert.False(stem.HasPartialKey && stem.IsKeyTagged);
                }
            );
        }
    }

    [Fact]
    public void No_row_carries_a_stem_that_is_not_on_disk()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        // The store keeps entries for files that have left (ADR 0003); none may be read back.
        Assert.All(
            Staged().SelectMany(song => song.Stems),
            stem => Assert.True(File.Exists(stem.Path), $"{stem.FileName} is not on disk")
        );
    }

    [Fact]
    public void An_absent_staging_folder_yields_an_empty_board()
    {
        var songs = new StagingReader().Build(
            Path.Combine(Path.GetTempPath(), "no-such-staging-root"),
            KeyLedger.Read(LedgerPath)
        );

        Assert.Empty(songs);
    }
}
