using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Tracks;

namespace Crateside.Tests.Tracks;

/// <summary>
/// What the expanded row shows: the plan while there is still a plan, and the sentence beside it.
/// </summary>
/// <remarks>
/// Here rather than in the page for the reason <see cref="TrackRow.Next"/> and
/// <see cref="TrackRow.ChipLabel"/> are: it is copy derived from the same reading, and copy that
/// can be edited apart from what it describes drifts away from it.
/// </remarks>
public class TrackExpansionTests
{
    private static TrackRow Row() =>
        new()
        {
            VideoId = "aaaaaaaaaaa",
            PlaylistItemId = "PLI-a",
            Title = "Rob Dougan - Furious Angels",
        };

    private static LedgerRun Judged(string? key, KeyConfidence confidence) =>
        new()
        {
            Timestamp = DateTimeOffset.Parse("2026-08-01T00:00:00+00:00"),
            Song = "rob dougan - furious angels",
            Key = key,
            Confidence = confidence,
        };

    [Fact]
    public void A_track_nothing_has_separated_yet_can_still_change_its_plan()
    {
        var row = Row();

        Assert.True(row.CanChangePlan);
        Assert.Equal(SeparationRecipe.Standard, row.Recipe);
    }

    [Theory]
    [InlineData(JobStage.Separation)]
    [InlineData(JobStage.Handoff)]
    [InlineData(JobStage.Tagging)]
    public void A_track_the_gpu_has_already_started_cannot(JobStage stage) =>
        Assert.False((Row() with { RunningStage = stage }).CanChangePlan);

    [Fact]
    public void A_parked_track_cannot()
    {
        // Parked is separated: its stems are in the unresolved-key root rather than in staging,
        // so nothing about it shows up in Samples — and a plan is not a plan once stems exist.
        Assert.False((Row() with { ParkedFor = ParkReason.NoKey }).CanChangePlan);
    }

    [Fact]
    public void A_track_whose_files_are_on_disk_cannot()
    {
        var staged = Row() with { Samples = [new TrackSample { Label = "Vocal - Full" }] };

        Assert.False(staged.CanChangePlan);
        Assert.False((Row() with { HasWarpGuide = true }).CanChangePlan);
    }

    [Fact]
    public void An_instrumental_release_plans_no_vocal_stem_and_keeps_the_source()
    {
        var row = Row() with { IsInstrumentalRelease = true };

        Assert.Equal(SeparationRecipe.InstrumentalRelease, row.Recipe);
        Assert.Empty(row.Recipe.Presets);
        Assert.True(row.Recipe.KeepSource);
        Assert.True(row.Recipe.ExtractDrums);
    }

    [Fact]
    public void An_unjudged_track_says_so_and_claims_nothing_else()
    {
        Assert.Equal("Never judged.", Row().Detail);
    }

    [Fact]
    public void A_judged_track_says_the_key_and_how_sure_it_was()
    {
        var row = Row() with { Verdict = Judged("C minor", KeyConfidence.High) };

        Assert.Equal("Judged C minor, high confidence.", row.Detail);
    }

    [Theory]
    [InlineData(KeyConfidence.Low, "low")]
    [InlineData(KeyConfidence.Withheld, "withheld")]
    public void A_verdict_too_weak_to_tag_with_says_why_there_is_no_key(
        KeyConfidence confidence,
        string spoken
    )
    {
        // The Key column is a dash for these, which says nothing about whether anyone looked. The
        // sentence is where the difference between "not judged" and "judged, and the answer was
        // no" gets said.
        var row = Row() with
        {
            Verdict = Judged("C minor", confidence),
        };

        Assert.Equal(
            $"Judged, and the verdict came back {spoken} — no key worth tagging with.",
            row.Detail
        );
    }

    [Fact]
    public void A_stale_tag_is_said_out_loud()
    {
        var row = Row() with
        {
            Verdict = Judged("C minor", KeyConfidence.High),
            AuditNote = "tag disagrees",
        };

        Assert.Contains("Ableton's key on these files is not the current verdict.", row.Detail);
    }

    [Fact]
    public void Tags_that_agree_are_not_mentioned()
    {
        // Silence is not a claim that they agree. The tag store lags Ableton's own index and keeps
        // entries for files that have left, so the only reading worth repeating is a conflict.
        var row = Row() with
        {
            Verdict = Judged("C minor", KeyConfidence.High),
        };

        Assert.DoesNotContain("Ableton", row.Detail);
    }

    [Fact]
    public void A_staged_track_says_how_long_it_has_been_waiting()
    {
        var row = Row() with
        {
            Verdict = Judged("C minor", KeyConfidence.High),
            StagedSince = DateTimeOffset.UtcNow.AddDays(-21),
        };

        Assert.Equal("Judged C minor, high confidence. Staged 21 days ago.", row.Detail);
    }
}
