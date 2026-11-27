using Crateside.Core.Plans;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.Plans;

/// <summary>An adopted sample: how it is recognized, and what a re-cut may do to it.</summary>
public sealed class AdoptedSampleTests
{
    private const string TrackKey = "aaaaaaaaaaa";
    private const string Label = "Instrumental - Mix";

    private static StemOnDisk Adopted(string label = Label) =>
        new()
        {
            Path = $@"P:\Staging\Song ({label}).flac",
            Root = @"P:\Staging",
            Label = label,
            IsAdopted = true,
            SourceTrackKey = TrackKey,
        };

    private static StemPlan PlanKeeping(params string[] labels) =>
        new()
        {
            Presets = ["vocal_full"],
            AdoptedSamples = [.. labels.Select(label => new AdoptedSample { Label = label })],
        };

    [Fact]
    public void Crateside_provenance_round_trips_through_the_parser()
    {
        var comment = StemAdoption.Comment(
            TrackKey,
            Label,
            SampleType.Instrumental,
            new DateOnly(2026, 9, 19)
        );

        var parsed = StemProvenance.Parse(comment);

        Assert.NotNull(parsed);
        Assert.True(parsed.IsAdopted);
        Assert.Equal(Label, parsed.Label);
        Assert.Equal(TrackKey, parsed.SourceTrackKey);
        Assert.Equal(SampleType.Instrumental, parsed.Type);
        Assert.Null(parsed.Preset);
    }

    [Fact]
    public void A_separated_stem_is_not_adopted()
    {
        var parsed = StemProvenance.Parse(
            "stemforge/0.3.1.0 | preset: Vocal - Full | source: https://www.youtube.com/watch?v="
                + TrackKey
        );

        Assert.NotNull(parsed);
        Assert.False(parsed.IsAdopted);
        Assert.Null(parsed.Label);
    }

    [Fact]
    public void A_comment_carrying_neither_prefix_is_still_rejected()
    {
        Assert.Null(StemProvenance.Parse("Lavf62.18.100"));
        Assert.Null(StemProvenance.Parse(null));
    }

    [Fact]
    public void An_adopted_file_is_classified_before_the_catalog_is_consulted()
    {
        var classified = ClassifiedStem.Of(Adopted(), PresetCatalog.Empty);

        Assert.Equal(StemKind.Adopted, classified.Kind);
        Assert.Equal(Label, classified.DisplayName);
        Assert.Null(classified.PresetId);
    }

    [Fact]
    public void A_recut_leaves_an_adopted_sample_the_plan_still_names()
    {
        var inventory = StemInventory.Of([Adopted()], PresetCatalog.Empty);

        Assert.Empty(inventory.Superseded(PlanKeeping(Label)));
    }

    [Fact]
    public void A_recut_removes_an_adopted_sample_the_plan_has_dropped()
    {
        var inventory = StemInventory.Of([Adopted()], PresetCatalog.Empty);

        var going = inventory.Superseded(
            PlanKeeping().Keeping(new AdoptedSample { Label = "Other" })
        );

        Assert.Equal(Label, Assert.Single(going).DisplayName);
    }

    [Fact]
    public void An_adopted_sample_is_never_asked_for_again()
    {
        var inventory = StemInventory.Of([Adopted()], PresetCatalog.Empty);

        Assert.Empty(inventory.Shortfall(PlanKeeping(Label)).AdoptedSamples);
    }

    [Fact]
    public void A_plan_holding_only_an_adopted_sample_is_not_empty()
    {
        var plan = new StemPlan { Presets = [] }.Keeping(new AdoptedSample { Label = Label });

        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Dropping_and_keeping_an_adopted_sample_is_a_change_of_plan()
    {
        var plan = PlanKeeping(Label);

        Assert.False(plan.AsksTheSameAs(plan.Forgetting(Label)));
        Assert.True(plan.AsksTheSameAs(plan.Forgetting(Label).Keeping(new() { Label = Label })));
    }

    [Fact]
    public void Adoption_refuses_a_file_that_already_belongs_to_a_track()
    {
        var path = Path.Combine(Path.GetTempPath(), $"crateside-adopt-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "not audio");

        try
        {
            Assert.False(
                StemAdoption.TryAdopt(
                    path,
                    TrackKey,
                    Label,
                    SampleType.Instrumental,
                    TimeProvider.System,
                    out var problem
                )
            );
            Assert.NotNull(problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Adoption_refuses_a_missing_file_and_a_blank_label()
    {
        Assert.False(
            StemAdoption.TryAdopt(
                @"P:\nowhere\absent.flac",
                TrackKey,
                Label,
                SampleType.Instrumental,
                TimeProvider.System,
                out _
            )
        );
        Assert.False(
            StemAdoption.TryAdopt(
                @"P:\nowhere\absent.flac",
                TrackKey,
                "  ",
                SampleType.Instrumental,
                TimeProvider.System,
                out _
            )
        );
    }
}
