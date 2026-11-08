using Crateside.Core.Ledger;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.WorkLists;

/// <summary>crateside's own record of a track the playlist does not hold (ADR 0010).</summary>
public sealed class PastedTrackTests
{
    private const string Pasted = "aaaaaaaaaaa";

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private TrackInstructionStore Store() => new(_ledger);

    [Fact]
    public void A_pasted_track_is_worth_storing_on_its_own()
    {
        var store = Store();

        store.AddByUrl(Pasted);

        var instruction = Assert.Single(store.Read());

        Assert.True(instruction.AddedByUrl);
        Assert.Null(instruction.Plan);
    }

    [Fact]
    public void Forgetting_one_leaves_nothing_behind()
    {
        var store = Store();

        store.AddByUrl(Pasted);
        store.Forget(Pasted);

        Assert.Empty(store.Read());
    }

    [Fact]
    public void Forgetting_it_drops_the_plan_with_it()
    {
        var store = Store();

        store.AddByUrl(Pasted);
        store.SetPlan(Pasted, StemPlan.InstrumentalRelease);
        store.Forget(Pasted);

        Assert.Empty(store.Read());
    }

    [Fact]
    public void It_survives_a_round_trip_through_the_file()
    {
        Store().AddByUrl(Pasted);

        Assert.True(Assert.Single(Store().Read()).AddedByUrl);
    }

    [Fact]
    public void A_plan_is_stored_without_the_answers_it_computes()
    {
        // `isEmpty` and `watchUrl` are derived, so they are never written.
        var store = Store();

        store.SetPlan(
            Pasted,
            StemPlan.Default.Adding(new AddedSource { TrackKey = "bbbbbbbbbbb", Label = "Live" })
        );

        var json = _ledger.Query(context => context.TrackInstructions.Single().Plan)!;

        Assert.DoesNotContain("isEmpty", json, StringComparison.Ordinal);
        Assert.DoesNotContain("watchUrl", json, StringComparison.Ordinal);
        Assert.Contains("addedSources", json, StringComparison.Ordinal);
    }
}
