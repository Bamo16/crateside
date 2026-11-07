using System.Text.Json;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Tracks;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Tracks;

/// <summary>A folder of per-song stores, and a ledger that judges <see cref="Video"/> as <see cref="Song"/>.</summary>
internal sealed class SongKeyFixture : IDisposable
{
    public const string Video = "hYt5NSjWKds";
    public const string Song = "taylor swift - style";

    public static readonly DateTimeOffset At = new(2026, 10, 30, 21, 38, 0, TimeSpan.Zero);

    public SongKeyFixture() => Directory.CreateDirectory(Folder);

    public string Folder { get; } =
        Path.Combine(Path.GetTempPath(), "crateside-tests", Guid.NewGuid().ToString("n"));

    /// <summary>Where the per-song stores keep their records, so a new store over it reads as after a restart.</summary>
    public LedgerStore Decisions { get; } = new(LedgerStore.InMemory, TimeProvider.System);

    /// <summary>A reader over the stores given, else fresh instances over the same ledger, as after a restart.</summary>
    public TrackAnnotationReader Reader(GenreStore? genres = null) =>
        new(
            new ResolvedNameStore(Path.Combine(Folder, "names.json")),
            new WorkingSet(Path.Combine(Folder, "working")),
            new DeletionLog(Path.Combine(Folder, "deletions.jsonl")),
            genres ?? new GenreStore(Decisions),
            new FamiliarityStore(Decisions),
            new TrackMarkStore(Decisions)
        );

    /// <summary>The ledger lines in which <see cref="Video"/> is judged as <see cref="Song"/>.</summary>
    public string JudgedLines() =>
        Lines(
            "judged.jsonl",
            JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-10-30T22:07:31+00:00",
                    song = Song,
                    artist = "Taylor Swift",
                    title = "Style",
                    source_url = $"https://music.youtube.com/watch?v={Video}",
                    verdict = new { key = "D major", confidence = "HIGH" },
                    bpm = new { value = 95.0 },
                }
            )
        );

    public KeyLedger Judged() => TestLedger.From(JudgedLines());

    public KeyLedger Unjudged() => TestLedger.From(Lines("unjudged.jsonl", string.Empty));

    public void Dispose()
    {
        Decisions.Dispose();

        if (Directory.Exists(Folder))
            Directory.Delete(Folder, recursive: true);
    }

    private string Lines(string name, string text)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, text);

        return path;
    }
}
