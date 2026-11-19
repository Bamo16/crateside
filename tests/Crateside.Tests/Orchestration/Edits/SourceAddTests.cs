using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Plans;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Edits;

public sealed class SourceAddTests : IDisposable
{
    private static readonly TrackRowFacts Track = new()
    {
        TrackKey = "aaaaaaaaaaa",
        Song = "kreayshawn - gucci gucci",
        Plan = StemPlan.Default,
        Uploads = ["aaaaaaaaaaa"],
    };

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private SourceAdd Sources() =>
        new(
            TestLedger.Store(),
            new TrackInstructionStore(_ledger),
            new SourceProbe(new ProcessRunner()),
            TimeProvider.System
        );

    [Theory]
    [InlineData(SampleType.Instrumental, true)]
    [InlineData(SampleType.Vocal, false)]
    public void An_instrumental_asks_for_a_cut_so_its_guide_is_made_and_a_vocal_does_not(
        SampleType type,
        bool asked
    )
    {
        var instructions = new TrackInstructionStore(_ledger);
        var source = new AddedSource
        {
            TrackKey = "bbbbbbbbbbb",
            Label = "Official Instrumental",
            Type = type,
        };

        Sources().Record(Track.TrackKey, Track.Plan, source);

        var recorded = Assert.Single(instructions.Read());

        Assert.Equal(source, Assert.Single(recorded.Plan!.AddedSources));
        Assert.Equal(asked, recorded.CutRequestedAt is not null);
    }

    [Fact]
    public void Nothing_is_added_until_the_user_says_what_it_is()
    {
        var plan = Sources().PlanFor(Track, "glF85prh3e4", "A Cappella", type: null);

        Assert.False(plan.IsPossible);
        Assert.NotNull(plan.Refusal);
    }

    [Fact]
    public void The_chosen_type_is_what_the_plan_records()
    {
        var plan = Sources().PlanFor(Track, "glF85prh3e4", "A Cappella", SampleType.Vocal);

        Assert.True(plan.IsPossible);
        Assert.Equal(SampleType.Vocal, plan.Source?.Type);
    }
}
