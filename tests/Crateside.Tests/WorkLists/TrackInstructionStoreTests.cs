using Crateside.Core.Ledger;
using Crateside.Core.Plans;
using Crateside.Core.WorkLists;

namespace Crateside.Tests.WorkLists;

public sealed class TrackInstructionStoreTests
{
    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    /// <summary>A new store over the same ledger is a restart.</summary>
    private TrackInstructionStore Instructions() => new(_ledger);

    [Fact]
    public void An_instruction_that_says_nothing_is_not_kept()
    {
        var store = Instructions();
        store.SetPlan("abc123", StemPlan.InstrumentalRelease);

        store.SetPlan("abc123", null);

        Assert.Empty(store.Read());
        Assert.Empty(Instructions().Read());
    }

    [Fact]
    public void A_name_on_its_own_is_enough_to_keep_an_instruction()
    {
        // SaysSomething decides whether the entry is stored at all.
        var store = Instructions();

        store.SetName("abc123", "Starjunk 95 - 99th Street");

        var track = Assert.Single(store.Read());
        Assert.Equal("Starjunk 95 - 99th Street", track.Name);
        Assert.Null(track.Plan);
    }

    [Fact]
    public void A_name_survives_a_restart()
    {
        Instructions().SetName("abc123", "Starjunk 95 - 99th Street");

        Assert.Equal("Starjunk 95 - 99th Street", Assert.Single(Instructions().Read()).Name);
    }

    [Fact]
    public void Every_field_of_an_instruction_survives_a_restart()
    {
        var at = DateTimeOffset.Parse("2026-11-01T10:00:00+00:00");
        var plan = new StemPlan
        {
            Presets = ["vocal_full"],
            KeepSource = true,
            AddedSources = [new AddedSource { TrackKey = "glF85prh3e4", Label = "A Cappella" }],
            AdoptedSamples = [new AdoptedSample { Label = "My edit" }],
        };
        var store = Instructions();

        store.SetPlan("abc123", plan);
        store.SetName("abc123", "Starjunk 95 - 99th Street");
        store.SetSourceUrl("abc123", "https://music.youtube.com/watch?v=glF85prh3e4");
        store.SetDifferentFrom("abc123", "Other Artist - Other Song");
        store.AcceptUpload("abc123", "glF85prh3e4");
        store.RequestCut("abc123", at);
        store.AddByUrl("abc123");
        store.MarkBackfill("abc123");

        var track = Assert.Single(Instructions().Read());

        Assert.Equal("abc123", track.TrackKey);
        Assert.True(plan.AsksTheSameAs(track.Plan!));
        Assert.Equal(plan.AddedSources, track.Plan!.AddedSources);
        Assert.Equal(plan.AdoptedSamples, track.Plan.AdoptedSamples);
        Assert.Equal("Starjunk 95 - 99th Street", track.Name);
        Assert.Equal("https://music.youtube.com/watch?v=glF85prh3e4", track.SourceUrl);
        Assert.Equal("Other Artist - Other Song", track.DifferentFrom);
        Assert.Equal("glF85prh3e4", track.AcceptedUpload);
        Assert.Equal(at, track.CutRequestedAt);
        Assert.True(track.AddedByUrl);
        Assert.True(track.Backfill);
    }

    [Fact]
    public void Video_ids_that_differ_only_in_case_are_different_tracks()
    {
        var store = Instructions();

        store.SetName("AbCdEfGhIjK", "One");
        store.SetName("abcdefghijk", "Two");

        Assert.Equal(["One", "Two"], Instructions().Read().Select(track => track.Name).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clearing_a_name_drops_the_instruction_with_it(string? cleared)
    {
        var store = Instructions();
        store.SetName("abc123", "Starjunk 95 - 99th Street");

        store.SetName("abc123", cleared);

        Assert.Empty(store.Read());
    }

    [Fact]
    public void A_name_is_stored_trimmed()
    {
        var store = Instructions();

        store.SetName("abc123", "  Starjunk 95 - 99th Street  ");

        Assert.Equal("Starjunk 95 - 99th Street", Assert.Single(store.Read()).Name);
    }

    [Fact]
    public void Clearing_a_name_keeps_another_instruction_that_was_also_stated()
    {
        var store = Instructions();
        store.SetPlan("abc123", StemPlan.InstrumentalRelease);
        store.SetName("abc123", "Starjunk 95 - 99th Street");

        store.SetName("abc123", null);

        var track = Assert.Single(Instructions().Read());
        Assert.True(track.Plan?.AsksTheSameAs(StemPlan.InstrumentalRelease));
        Assert.Null(track.Name);
    }

    [Fact]
    public void Forgetting_a_track_drops_all_it_was_told()
    {
        var store = Instructions();
        store.SetName("abc123", "Starjunk 95 - 99th Street");
        store.AddByUrl("abc123");
        store.SetName("other", "Kept");

        store.Forget("abc123");

        Assert.Equal("other", Assert.Single(Instructions().Read()).TrackKey);
    }
}
