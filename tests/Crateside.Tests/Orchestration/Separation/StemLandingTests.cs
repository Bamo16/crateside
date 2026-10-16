using System.Text.Json;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Separation;

/// <summary>Where a run's stems belong once it has finished, which is not always where it was sent.</summary>
public sealed class StemLandingTests : IDisposable
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

    private string LedgerFile => Path.Combine(_root, "runs.jsonl");

    private CratesidePaths Paths =>
        field ??= new CratesidePaths
        {
            StemsKeyIdentified = Path.Combine(_root, "identified"),
            StemsUnresolvedKey = Path.Combine(_root, "unresolved"),
            StagingFolder = Path.Combine(_root, "staging"),
        };

    private KeyLedger Ledger(string? confidence)
    {
        File.WriteAllText(
            LedgerFile,
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

        return TestLedger.From(LedgerFile);
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

    private JobStore Jobs => field ??= new JobStore(Path.Combine(_root, "jobs.jsonl"));

    /// <summary>A kept source copied out before its verdict, as Darude - Sandstorm was on 2026-09-25.</summary>
    private string ParkedSource()
    {
        var root = Paths.DestinationFor(StemDestination.UnresolvedKey);
        var path = Path.Combine(root, "High Contrast, Boy Matthews - Questions.flac");

        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment =
            $"stemforge/0.3.1.0 | date: 2026-09-25 | source: https://www.youtube.com/watch?v={Video}";
        file.Save();

        return path;
    }

    [Fact]
    public void A_parked_file_whose_song_has_since_been_judged_is_released()
    {
        var parked = ParkedSource();

        File.WriteAllText(parked + ".asd", "analysis");

        Assert.Equal(1, StemLanding.ReleaseParked(Paths, Ledger("HIGH"), Jobs));

        var landed = Path.Combine(
            Paths.DestinationFor(StemDestination.KeyIdentified),
            Path.GetFileName(parked)
        );

        Assert.True(File.Exists(landed));
        Assert.True(File.Exists(landed + ".asd"));
        Assert.False(File.Exists(parked));
    }

    [Fact]
    public void A_parked_file_whose_verdict_is_still_withheld_stays()
    {
        var parked = ParkedSource();

        Assert.Equal(0, StemLanding.ReleaseParked(Paths, Ledger("WITHHELD"), Jobs));
        Assert.True(File.Exists(parked));
    }

    [Fact]
    public void A_separation_still_writing_its_outputs_is_left_to_land_them_itself()
    {
        var parked = ParkedSource();

        Jobs.Record(Video, JobStage.Separation, JobStatus.Running);

        Assert.Equal(0, StemLanding.ReleaseParked(Paths, Ledger("HIGH"), Jobs));
        Assert.True(File.Exists(parked));
    }

    [Fact]
    public void A_parked_copy_of_a_file_already_staged_is_recycled_rather_than_released()
    {
        // Logistics - Over and Out, 2026-09-25: released onto a staged twin, the handoff struck out.
        var parked = ParkedSource();
        var staged = Path.Combine(Paths.StagingFolder, Path.GetFileName(parked));

        Directory.CreateDirectory(Paths.StagingFolder);
        File.Copy(parked, staged);

        Assert.Equal(1, StemLanding.ReleaseParked(Paths, Ledger("HIGH"), Jobs));
        Assert.False(File.Exists(parked));
        Assert.False(
            File.Exists(
                Path.Combine(
                    Paths.DestinationFor(StemDestination.KeyIdentified),
                    Path.GetFileName(parked)
                )
            )
        );
        Assert.True(File.Exists(staged));
    }

    [Fact]
    public void A_parked_file_whose_staged_namesake_differs_stays_parked()
    {
        var parked = ParkedSource();

        Directory.CreateDirectory(Paths.StagingFolder);
        File.WriteAllText(Path.Combine(Paths.StagingFolder, Path.GetFileName(parked)), "other");

        Assert.Equal(0, StemLanding.ReleaseParked(Paths, Ledger("HIGH"), Jobs));
        Assert.True(File.Exists(parked));
    }
}
