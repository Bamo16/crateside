using Crateside.Core;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration;

public class TrackAddTests : IDisposable
{
    private const string Upload = "wpf8JgkOrnQ";

    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_library_track_nothing_has_judged_may_be_added_to_be_judged()
    {
        var plan = Add().PlanFor(Upload, [Row(isLibraryOnly: true, hasVerdict: false)]);

        Assert.Null(plan.Refusal);
        Assert.True(plan.IsPossible);

        // The whole point of letting it through: the caller has to be able to say that this
        // buys a verdict rather than a separation.
        Assert.True(plan.JudgesWhatIsHere);
    }

    [Fact]
    public void A_library_track_already_judged_is_still_refused()
    {
        // A LOW or WITHHELD verdict leaves the row's key null and is still a judgement, which is
        // why this turns on the verdict rather than the key.
        var plan = Add().PlanFor(Upload, [Row(isLibraryOnly: true, hasVerdict: true)]);

        Assert.NotNull(plan.Refusal);
    }

    [Fact]
    public void An_upload_a_working_row_already_speaks_for_is_refused()
    {
        var plan = Add().PlanFor(Upload, [Row(isLibraryOnly: false, hasVerdict: false)]);

        Assert.NotNull(plan.Refusal);
    }

    [Fact]
    public void An_upload_no_row_holds_is_added_as_ordinary_work()
    {
        var plan = Add().PlanFor(Upload, []);

        Assert.True(plan.IsPossible);
        Assert.False(plan.JudgesWhatIsHere);
    }

    [Fact]
    public void A_source_the_row_merely_carries_is_still_refused()
    {
        // Uploads folds in the plan's added sources, so a library-only row would otherwise offer
        // to judge one as a track of its own — which is the second identity attaching it avoided.
        const string added = "C_DIhh3sWO0";

        var row = Row(isLibraryOnly: true, hasVerdict: false) with
        {
            Plan = StemPlan.Default.Adding(
                new AddedSource { VideoId = added, Label = "A Cappella" }
            ),
            Uploads = [Upload, added],
        };

        Assert.NotNull(Add().PlanFor(added, [row]).Refusal);
    }

    private static TrackRowFacts Row(bool isLibraryOnly, bool hasVerdict) =>
        new()
        {
            VideoId = Upload,
            Plan = StemPlan.Default,
            Uploads = [Upload],
            IsLibraryOnly = isLibraryOnly,
            HasVerdict = hasVerdict,
        };

    private TrackAdd Add()
    {
        Directory.CreateDirectory(_scratch);

        var paths = new CratesidePaths { Ledger = Path.Combine(_scratch, "runs.jsonl") };

        return new TrackAdd(
            new QueueSettingsStore(
                Options.Create(new QueueOptions()),
                Path.Combine(_scratch, "queue.json")
            ),
            paths,
            new SourceProbe(),
            new ResolvedNameStore(Path.Combine(_scratch, "names.json")),
            new IntakeStore(Path.Combine(_scratch, "intake.json"))
        );
    }
}
