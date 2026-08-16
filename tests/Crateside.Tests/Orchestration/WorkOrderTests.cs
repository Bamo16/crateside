using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;

namespace Crateside.Tests.Orchestration;

public class WorkOrderTests : IDisposable
{
    private const string VideoId = "dQw4w9WgXcQ";

    private readonly string _ledgerPath = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n"),
        "runs.jsonl"
    );

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_ledgerPath)!;

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private static QueueItem Item(
        PremiumOutlook outlook = PremiumOutlook.Unknown,
        bool instrumentalRelease = false
    ) =>
        new()
        {
            PlaylistItemId = "PLI1",
            VideoId = VideoId,
            Title = "Yes - Owner of a Lonely Heart",
            PremiumOutlook = outlook,
            IsInstrumentalRelease = instrumentalRelease,
        };

    private KeyLedger Ledger(string? key, string confidence = "HIGH")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);

        var verdict = key is null
            ? $$"""{"key":null,"confidence":"{{confidence}}"}"""
            : $$"""{"key":"{{key}}","confidence":"{{confidence}}"}""";

        File.WriteAllText(
            _ledgerPath,
            $$"""
            {"timestamp":"2026-08-13T10:00:00Z","song":"yes - owner of a lonely heart","source_url":"https://music.youtube.com/watch?v={{VideoId}}","verdict":{{verdict}}}
            """
        );

        return KeyLedger.Read(_ledgerPath);
    }

    [Theory]
    [InlineData("A minor", "HIGH")]
    [InlineData("A minor", "MEDIUM")]
    public void A_key_the_user_would_tag_with_routes_to_key_identified(
        string key,
        string confidence
    )
    {
        var order = WorkOrder.Compose(Item(), Ledger(key, confidence));

        Assert.Equal(StemDestination.KeyIdentified, order.Destination);
    }

    [Theory]
    [InlineData("A minor", "LOW")]
    [InlineData(null, "WITHHELD")]
    public void Anything_short_of_a_taggable_key_routes_to_unresolved(
        string? key,
        string confidence
    )
    {
        var order = WorkOrder.Compose(Item(), Ledger(key, confidence));

        Assert.Equal(StemDestination.UnresolvedKey, order.Destination);
    }

    [Fact]
    public void A_track_with_no_verdict_yet_routes_to_unresolved_rather_than_nowhere()
    {
        // The bare stems root is never a valid destination for a track that went through this
        // workflow -- a stem there carries no signal about whether its key was resolved.
        var order = WorkOrder.Compose(Item(), KeyLedger.Read(_ledgerPath));

        Assert.Equal(StemDestination.UnresolvedKey, order.Destination);
    }

    [Fact]
    public void An_instrumental_release_asks_for_drums_and_keeps_the_source()
    {
        // There is nothing to separate out, so the drums are the only stem and the source track
        // is the sample -- which the separator has to place, since crateside directs output and
        // never moves files itself. Asking for vocal_full here is what made a vocal stem of a
        // track with no vocals.
        var order = WorkOrder.Compose(Item(instrumentalRelease: true), Ledger("A minor"));

        Assert.Empty(order.Recipe.Presets);
        Assert.True(order.Recipe.ExtractDrums);
        Assert.True(order.Recipe.KeepSource);
    }

    [Fact]
    public void The_instrumental_release_fact_travels_to_the_ledger_as_has_vocals()
    {
        // crateside is where the user is, so the statement is made here and passed through --
        // rather than read back from a ledger line that only exists after somebody was asked.
        Assert.False(
            WorkOrder.Compose(Item(instrumentalRelease: true), Ledger("A minor")).HasVocals
        );
    }

    [Fact]
    public void Saying_nothing_about_vocals_is_not_saying_it_has_them()
    {
        // Absent means nobody stated it, the same null semantics as verdict.mode. crateside's
        // policy for not-stated is to expect the usual pair, but the record must not claim the
        // user said so.
        Assert.Null(WorkOrder.Compose(Item(), Ledger("A minor")).HasVocals);
    }

    [Fact]
    public void An_art_track_gets_the_music_url()
    {
        var order = WorkOrder.Compose(Item(PremiumOutlook.ArtTrack), Ledger("A minor"));

        Assert.Equal($"https://music.youtube.com/watch?v={VideoId}", order.SourceUrl);
    }

    [Theory]
    [InlineData(PremiumOutlook.VideoUpload)]
    [InlineData(PremiumOutlook.Unknown)]
    public void Anything_else_gets_the_ordinary_watch_url(PremiumOutlook outlook)
    {
        var order = WorkOrder.Compose(Item(outlook), Ledger("A minor"));

        Assert.Equal($"https://www.youtube.com/watch?v={VideoId}", order.SourceUrl);
    }

    [Fact]
    public void A_video_upload_carries_the_advisory_that_a_better_url_may_exist()
    {
        var order = WorkOrder.Compose(Item(PremiumOutlook.VideoUpload), Ledger("A minor"));

        Assert.NotNull(order.Advisory);
    }

    [Fact]
    public void An_art_track_says_nothing_because_explaining_a_success_is_noise()
    {
        var order = WorkOrder.Compose(Item(PremiumOutlook.ArtTrack), Ledger("A minor"));

        Assert.Null(order.Advisory);
    }

    [Fact]
    public void The_standard_recipe_is_vocal_instrumental_and_drums()
    {
        var recipe = WorkOrder.Compose(Item(), Ledger("A minor")).Recipe;

        Assert.Equal(["vocal_full", "instrumental_full"], recipe.Presets);
        Assert.True(recipe.ExtractDrums);
        Assert.False(recipe.KeepSource);
        Assert.Equal("flac", recipe.Format);
    }

    [Fact]
    public void Each_destination_resolves_to_its_own_folder()
    {
        var paths = new CratesidePaths();

        Assert.EndsWith(
            Path.Combine("Stems", "Key Identified"),
            paths.DestinationFor(StemDestination.KeyIdentified)
        );
        Assert.EndsWith(
            Path.Combine("Stems", "Unresolved Key"),
            paths.DestinationFor(StemDestination.UnresolvedKey)
        );
        Assert.NotEqual(
            paths.DestinationFor(StemDestination.KeyIdentified),
            paths.DestinationFor(StemDestination.UnresolvedKey)
        );
    }
}
