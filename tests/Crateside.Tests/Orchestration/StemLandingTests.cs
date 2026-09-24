using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>Where a run's stems belong once it has finished, which is not always where it was sent.</summary>
public class StemLandingTests : IDisposable
{
    private const string Video = "adjcmOELKIM";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public StemLandingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            StemsKeyIdentified = Path.Combine(_root, "identified"),
            StemsUnresolvedKey = Path.Combine(_root, "unresolved"),
            Ledger = Path.Combine(_root, "runs.jsonl"),
        };

    private KeyLedger Ledger(string? confidence)
    {
        File.WriteAllText(
            Paths.Ledger,
            confidence is null
                ? string.Empty
                : JsonSerializer.Serialize(
                    new
                    {
                        timestamp = "2026-09-16T07:10:21+00:00",
                        song = "high contrast, boy matthews - questions",
                        artist = "High Contrast",
                        title = "Questions",
                        source_url = $"https://music.youtube.com/watch?v={Video}",
                        verdict = new { key = "A# minor", confidence },
                    }
                )
        );

        return KeyLedger.Read(Paths.Ledger);
    }

    private IReadOnlyList<string> Parked(params string[] names)
    {
        var root = Paths.DestinationFor(StemDestination.UnresolvedKey);

        Directory.CreateDirectory(root);

        return
        [
            .. names.Select(name =>
            {
                var path = Path.Combine(root, name);

                File.WriteAllText(path, "audio");

                return path;
            }),
        ];
    }

    private static WorkOrder Order(StemDestination destination) =>
        new()
        {
            VideoId = Video,
            Title = "High Contrast, Boy Matthews - Questions",
            SourceUrl = $"https://music.youtube.com/watch?v={Video}",
            Destination = destination,
        };

    [Fact]
    public void A_verdict_that_landed_during_the_run_moves_its_stems_across()
    {
        var outputs = Parked("Questions (Vocal - Clean).flac", "Questions (Drums).flac");

        var moved = StemLanding.Reconcile(
            Paths,
            Ledger("MEDIUM"),
            Order(StemDestination.UnresolvedKey),
            outputs
        );

        Assert.Equal(2, moved.Count);
        Assert.All(moved, path => Assert.True(File.Exists(path)));
        Assert.All(outputs, path => Assert.False(File.Exists(path)));
        Assert.All(
            moved,
            path =>
                Assert.Equal(
                    Paths.DestinationFor(StemDestination.KeyIdentified),
                    Path.GetDirectoryName(path)
                )
        );
    }

    [Fact]
    public void A_run_with_no_taggable_verdict_stays_parked()
    {
        var outputs = Parked("Questions (Vocal - Clean).flac");

        Assert.Empty(
            StemLanding.Reconcile(
                Paths,
                Ledger("WITHHELD"),
                Order(StemDestination.UnresolvedKey),
                outputs
            )
        );
        Assert.All(outputs, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void A_run_the_ledger_has_never_heard_of_stays_parked()
    {
        var outputs = Parked("Questions (Vocal - Clean).flac");

        Assert.Empty(
            StemLanding.Reconcile(
                Paths,
                Ledger(null),
                Order(StemDestination.UnresolvedKey),
                outputs
            )
        );
        Assert.All(outputs, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void A_run_already_sent_to_the_key_identified_root_is_left_alone()
    {
        var outputs = Parked("Questions (Vocal - Clean).flac");

        Assert.Empty(
            StemLanding.Reconcile(
                Paths,
                Ledger("HIGH"),
                Order(StemDestination.KeyIdentified),
                outputs
            )
        );
    }
}
