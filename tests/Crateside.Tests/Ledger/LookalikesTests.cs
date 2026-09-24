using System.Text.Json;
using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

/// <summary>The two measured cases of #41, and the near-misses that must not match.</summary>
public class LookalikesTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private KeyLedger Ledger(params string[] songs)
    {
        var path = Path.Combine(_directory, "runs.jsonl");

        Directory.CreateDirectory(_directory);
        File.WriteAllLines(
            path,
            songs.Select(
                (song, i) =>
                    JsonSerializer.Serialize(
                        new
                        {
                            timestamp = $"2026-08-{10 + i:00}T00:00:00+00:00",
                            song,
                            source_url = $"https://music.youtube.com/watch?v=video{i:00000}",
                            verdict = new { key = "C major", confidence = "HIGH" },
                        }
                    )
            )
        );

        return KeyLedger.Read(path);
    }

    [Theory]
    [InlineData("Moby - South Side", "moby - south side (feat. gwen stefani)")]
    [InlineData("Moby - South Side (feat. Gwen Stefani)", "moby - south side")]
    [InlineData("nimino - Better (feat. Manta)", "nimino, manta - better")]
    [InlineData("nimino, Manta - Better", "nimino - better (feat. manta)")]
    [InlineData("Moby - South Side (Official Video)", "moby - south side")]
    [InlineData("Beyoncé - Halo", "beyonce - halo")]
    [InlineData("Ludacris ft. Shawnna - Stand Up", "ludacris - stand up")]
    public void A_name_that_differs_only_in_its_credits_is_found(string name, string known)
    {
        Assert.True(Lookalikes.TryFind(name, Ledger(known), out var song));
        Assert.Equal(known, song.Song);
    }

    [Theory]
    [InlineData("Moby - Porcelain", "moby - south side")]
    [InlineData("Ace of Base - Venus", "bananarama - venus")]
    [InlineData("South Side", "moby - south side")]
    [InlineData("ドゥーマー - 東京真中", "tokyo manaka - doomer")]
    [InlineData(null, "moby - south side")]
    public void A_different_song_is_not(string? name, string known) =>
        Assert.False(Lookalikes.TryFind(name, Ledger(known), out _));
}
