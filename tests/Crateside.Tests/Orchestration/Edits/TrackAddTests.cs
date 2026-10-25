using Crateside.Core;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Edits;

public sealed class TrackAddTests : IDisposable
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

        Assert.True(plan.JudgesWhatIsHere);
    }

    [Fact]
    public void A_library_track_already_judged_is_still_refused()
    {
        // A LOW or WITHHELD verdict leaves the key null and is still a judgment.
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
        // Uploads folds in the plan's added sources.
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

    [Fact]
    public void Queueing_the_unjudged_library_marks_each_as_backfill()
    {
        var add = Add(out var settings);

        Assert.Equal(1, add.QueueUnjudged([Row(isLibraryOnly: true, hasVerdict: false)]));

        var track = Assert.Single(settings.Read());
        Assert.True(track.Backfill);
        Assert.True(track.AddedByUrl);
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

    private TrackAdd Add() => Add(out _);

    private TrackAdd Add(out TrackInstructionStore settings)
    {
        Directory.CreateDirectory(_scratch);

        var paths = new CratesidePaths();

        settings = new TrackInstructionStore(
            new QueueFile(
                Options.Create(new PlaylistOptions()),
                Path.Combine(_scratch, "queue.json")
            )
        );

        return new TrackAdd(
            TestLedger.Store(Path.Combine(_scratch, "runs.jsonl")),
            settings,
            new SourceProbe(new ProcessRunner()),
            new ResolvedNameStore(Path.Combine(_scratch, "names.json"))
        );
    }
}
