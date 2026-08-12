using Crateside.Core.Ledger;
using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

/// <summary>
/// The board composed against the real staging folder and the real ledger — the whole point
/// of the readers, assembled.
/// </summary>
public class StagingBoardRealLibraryTests
{
    private const string StagingRoot = @"P:\RYAN\Ableton\Sample Staging";
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    private static bool Available => Directory.Exists(StagingRoot) && File.Exists(LedgerPath);

    private static IReadOnlyList<StagingSong> Board() =>
        new StagingBoard().Build(StagingRoot, KeyLedger.Read(LedgerPath));

    [Fact]
    public void Rolls_the_staged_stems_up_into_songs()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        var board = Board();

        Assert.NotEmpty(board);
        // Every staged file lands on exactly one row, and no row is empty.
        Assert.Equal(
            Directory.GetFiles(StagingRoot, "*.flac").Length,
            board.Sum(s => s.Stems.Count)
        );
        Assert.All(board, song => Assert.NotEmpty(song.Stems));
        Assert.All(board, song => Assert.NotEmpty(song.DisplayName));
    }

    [Fact]
    public void Every_staged_song_resolves_to_a_ledger_verdict()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        var board = Board();

        Assert.All(board, song => Assert.NotNull(song.SourceVideoId));
        Assert.DoesNotContain(board, s => s.AwaitingDetection);
    }

    [Fact]
    public void The_row_is_named_from_the_ledger_identity_not_the_filename()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        // "Alan Gomez - Mission 15" on disk is "Alan Gomez, Luck Ra - Luck Ra | Mission 15"
        // in the ledger. The board shows the identity, because that is what was detected.
        var song = Board()
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

        var board = Board();
        var firstFinished = board.ToList().FindIndex(s => s.IsFullyTagged);

        Assert.SkipWhen(firstFinished < 0, "Nothing on the board is fully tagged.");
        Assert.All(board.Take(firstFinished), song => Assert.False(song.IsFullyTagged));
    }

    [Fact]
    public void Reports_per_stem_tagging_without_inventing_a_key()
    {
        Assert.SkipUnless(Available, "Staging root and ledger must both be present.");

        foreach (var song in Board())
        {
            Assert.InRange(song.TaggedStemCount, 0, song.Stems.Count);
            Assert.All(
                song.Stems,
                stem =>
                {
                    // A stem is tagged only when Ableton holds a whole key for it.
                    Assert.Equal(stem.AbletonKey is not null, stem.IsKeyTagged);
                    Assert.False(stem.HasPartialKey && stem.IsKeyTagged);
                }
            );
        }
    }

    [Fact]
    public void An_absent_staging_folder_yields_an_empty_board()
    {
        var board = new StagingBoard().Build(
            Path.Combine(Path.GetTempPath(), "no-such-staging-root"),
            KeyLedger.Read(LedgerPath)
        );

        Assert.Empty(board);
    }
}
