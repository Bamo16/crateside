using System.Globalization;
using Crateside.Core;
using Crateside.Core.Familiarity;
using Crateside.Core.Ledger;
using Microsoft.Data.Sqlite;

namespace Crateside.Tests.Familiarity;

/// <summary>
/// The sources as they answer today, slow because they are asked politely; run with
/// <c>CRATESIDE_NETWORK_TESTS=1</c>. <c>CRATESIDE_LIVE_OUT</c> names a file to write each song's reading to.
/// </summary>
public sealed class FamiliarityLiveTests
{
    private static FamiliarityLookup Sources()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("CRATESIDE_NETWORK_TESTS") is "1",
            "CRATESIDE_NETWORK_TESTS is not set"
        );

        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(CratesideHttp.UserAgent);

        return new FamiliarityLookup(new FixedHttpClientFactory(client), TimeProvider.System);
    }

    private static async Task<(string Value, FamiliarityReading Reading)> ReadAsync(
        FamiliarityLookup lookup,
        string artist,
        string title,
        StreamWriter? report
    )
    {
        var ct = TestContext.Current.CancellationToken;
        FamiliarityReading? read = null;

        // MusicBrainz answers 503 to a client that has been asking faster than it likes.
        for (var attempt = 1; read is null && attempt <= 5; attempt++)
        {
            read = await lookup.ReadAsync(artist, title, ct);

            if (read is null)
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
        }

        var reading =
            read
            ?? throw new InvalidOperationException(
                $"A source could not be reached for {artist} - {title}"
            );
        var value = FamiliarityVocabulary.FromReach(reading.Listeners, reading.Pageviews);

        if (report is not null)
        {
            await report.WriteLineAsync(
                string.Join(
                    '	',
                    artist,
                    title,
                    reading.Listeners?.ToString(CultureInfo.InvariantCulture),
                    reading.Pageviews?.ToString(CultureInfo.InvariantCulture),
                    reading.WikidataItem,
                    value
                )
            );
            await report.FlushAsync(ct);
        }

        return (value, reading);
    }

    private static StreamWriter? Report(string suffix) =>
        Environment.GetEnvironmentVariable("CRATESIDE_LIVE_OUT") is { Length: > 0 } path
            ? new StreamWriter($"{path}.{suffix}.tsv")
            : null;

    [Fact]
    public async Task The_labelled_songs_read_as_the_note_measured_them_give_or_take_the_lookups_moving()
    {
        var sources = Sources();
        using var report = Report("labelled");
        var reads = new List<(bool Known, string Value)>();

        foreach (var song in LabelledSong.All)
        {
            var (value, _) = await ReadAsync(sources, song.Artist, song.Title, report);

            reads.Add((song.Known, value));
        }

        var spread = FamiliarityNoteTests.Spread(reads);

        Assert.InRange(spread.GetValueOrDefault((true, FamiliarityVocabulary.Mainstream)), 12, 18);
        Assert.InRange(spread.GetValueOrDefault((false, FamiliarityVocabulary.Obscure)), 24, 30);
    }

    [Fact]
    public async Task Every_song_in_a_copy_of_the_ledger_reads_a_value_and_the_spread_is_reported()
    {
        var sources = Sources();
        var real = new CratesidePaths().LedgerDatabase;

        Assert.SkipUnless(File.Exists(real), "Needs this machine's ledger.");

        var folder = Path.Combine(
            Path.GetTempPath(),
            "crateside-tests",
            Guid.NewGuid().ToString("n")
        );

        Directory.CreateDirectory(folder);

        try
        {
            var copy = Path.Combine(folder, "ledger.db");

            File.Copy(real, copy);

            var limit = int.TryParse(
                Environment.GetEnvironmentVariable("CRATESIDE_LIVE_LIMIT"),
                out var cap
            )
                ? cap
                : int.MaxValue;

            LedgerRun[] songs;

            using (var ledger = new LedgerStore(copy, TimeProvider.System))
            {
                songs =
                [
                    .. ledger
                        .Read()
                        .Runs.Where(run => run is { Artist.Length: > 0, Title.Length: > 0 })
                        .GroupBy(run => run.Song, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.MaxBy(run => run.Timestamp)!)
                        .Take(limit),
                ];
            }

            using var report = Report("ledger");
            var values = new List<string>();

            foreach (var song in songs)
            {
                var (value, _) = await ReadAsync(sources, song.Artist!, song.Title!, report);

                values.Add(value);
            }

            Assert.NotEmpty(values);

            TestContext.Current.SendDiagnosticMessage(
                $"{values.Count} songs: "
                    + string.Join(
                        ", ",
                        FamiliarityVocabulary.All.Select(one =>
                            $"{one} {values.Count(value => value == one)}"
                        )
                    )
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            Directory.Delete(folder, recursive: true);
        }
    }
}
