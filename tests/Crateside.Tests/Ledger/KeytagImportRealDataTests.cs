using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

/// <summary>The import against keytag's real ledger, read here independently of the importer.</summary>
public sealed class KeytagImportRealDataTests
{
    private const string LedgerPath = @"V:\Repos\keytag\results\runs.jsonl";

    /// <summary>Dropped by the import: re-recorded unchanged under the song's own identity.</summary>
    private const string SplitIdentity =
        "houshou marine, maron (iosys), d.watt (iosys) - a horny money world (legendary night) -- segment reanalysis";

    private static readonly Lazy<IReadOnlyList<JsonElement>> Lines = new(() =>
        [
            .. File.ReadLines(LedgerPath)
                .Where(line => line.Trim().Length > 0)
                .Select(line => JsonDocument.Parse(line).RootElement)
                .Where(line => line.GetProperty("song").GetString() != SplitIdentity),
        ]
    );

    private static LedgerStore Imported()
    {
        if (!File.Exists(LedgerPath))
            Assert.Skip("keytag's ledger is not on this machine");

        return TestLedger.Store(LedgerPath);
    }

    [Fact]
    public void Every_line_of_the_history_is_taken()
    {
        using var store = Imported();

        Assert.Equal(Lines.Value.Count, store.Read().Runs.Count);
        Assert.Empty(store.ImportRefusals);
    }

    [Fact]
    public void Every_songs_latest_verdict_comes_out_the_same()
    {
        using var store = Imported();
        var ledger = store.Read();
        var expected = Lines
            .Value.GroupBy(line => line.GetProperty("song").GetString()!)
            .Select(song =>
                song.MaxBy(line => DateTimeOffset.Parse(line.GetProperty("timestamp").GetString()!))
            )
            .ToList();

        Assert.Equal(expected.Count, ledger.SongCount);
        Assert.All(
            expected,
            line =>
            {
                var song = line.GetProperty("song").GetString()!;
                var verdict = line.GetProperty("verdict");

                Assert.True(ledger.TryGetLatestBySong(song, out var run), song);
                Assert.Equal(verdict.GetProperty("key").GetString(), run.Key);
                Assert.Equal(
                    verdict.GetProperty("confidence").GetString(),
                    run.Confidence.ToString().ToUpperInvariant()
                );
                Assert.Equal(ExpectedMode(verdict), run.Mode);
            }
        );
    }

    [Fact]
    public void Every_video_id_resolves_to_the_same_song()
    {
        using var store = Imported();
        var ledger = store.Read();
        var expected = Lines
            .Value.Select(line =>
                (
                    VideoId: YouTubeUrl.TryGetVideoId(
                        line.GetProperty("source_url").GetString(),
                        out var videoId
                    )
                        ? videoId
                        : null,
                    At: DateTimeOffset.Parse(line.GetProperty("timestamp").GetString()!),
                    Song: line.GetProperty("song").GetString()!
                )
            )
            .Where(line => line.VideoId is not null)
            .GroupBy(line => line.VideoId!)
            .ToDictionary(upload => upload.Key, upload => upload.MaxBy(line => line.At).Song);

        Assert.NotEmpty(expected);
        Assert.All(expected, pair => Assert.Equal(pair.Value, ledger.SongFor(pair.Key)));
    }

    [Fact]
    public void A_restatement_carries_its_runs_verdict_and_no_evidence_of_its_own()
    {
        using var store = Imported();
        var restated = store.Read().Runs.Where(run => run.Restates is not null).ToList();

        Assert.Equal(
            Lines.Value.Count(line =>
                line.TryGetProperty("restates", out var pointer)
                && pointer.ValueKind is JsonValueKind.String
            ),
            restated.Count
        );
        Assert.All(
            restated,
            run =>
                Assert.Contains(
                    store.Read().Runs,
                    original =>
                        original.Restates is null
                        && original.Timestamp == run.Restates
                        && original.Key == run.Key
                )
        );
    }

    [Fact]
    public void The_split_identitys_windows_survive_on_the_correctly_named_run()
    {
        using var store = Imported();

        var evidence = store.EvidenceFor(
            SplitIdentity.Replace(" -- segment reanalysis", string.Empty)
        );

        Assert.NotNull(evidence);
        Assert.Equal(new RunWindow(53.498, 79.98, null, null), Assert.Single(evidence.Windows));
        Assert.False(store.Read().TryGetLatestBySong(SplitIdentity, out _));
    }

    private static DiatonicMode? ExpectedMode(JsonElement verdict) =>
        (verdict.GetProperty("key").GetString(), verdict.GetProperty("mode").GetString()) switch
        {
            (null, _) => null,
            (_, { } mode) => LedgerVocabulary.ParseMode(mode),
            ({ } key, null) => key.EndsWith("major", StringComparison.Ordinal)
                ? DiatonicMode.Ionian
                : DiatonicMode.Aeolian,
        };
}
