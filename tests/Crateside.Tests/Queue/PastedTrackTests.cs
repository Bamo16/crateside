using Crateside.Core.Queue;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Queue;

/// <summary>
/// crateside's own record of a track the playlist does not hold — the second way in, and the
/// only one besides the playlist that reaches <see cref="QueueView.Outstanding"/> (ADR 0010).
/// </summary>
public class PastedTrackTests : IDisposable
{
    private const string Pasted = "aaaaaaaaaaa";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public PastedTrackTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private QueueSettingsStore Store() =>
        new(Options.Create(new QueueOptions()), Path.Combine(_directory, "queue.json"));

    [Fact]
    public void A_pasted_track_is_worth_storing_on_its_own()
    {
        // Every other instruction is something the user said *about* a track the playlist
        // already held. This one is the only reason the track exists at all, so the rule that
        // drops an instruction saying nothing has to know about it.
        var store = Store();

        store.AddByUrl(Pasted);

        var instruction = Assert.Single(store.Read().Tracks);

        Assert.True(instruction.AddedByUrl);
        Assert.Null(instruction.Plan);
    }

    [Fact]
    public void Forgetting_one_leaves_nothing_behind()
    {
        var store = Store();

        store.AddByUrl(Pasted);
        store.Forget(Pasted);

        Assert.Empty(store.Read().Tracks);
    }

    [Fact]
    public void Forgetting_it_drops_the_plan_with_it()
    {
        // Removing a pasted track means crateside was never told to work it. Keeping its plan
        // would leave an instruction for a row that no longer exists.
        var store = Store();

        store.AddByUrl(Pasted);
        store.SetPlan(Pasted, StemPlan.InstrumentalRelease);
        store.Forget(Pasted);

        Assert.Empty(store.Read().Tracks);
    }

    [Fact]
    public void It_survives_a_round_trip_through_the_file()
    {
        Store().AddByUrl(Pasted);

        Assert.True(Assert.Single(Store().Read().Tracks).AddedByUrl);
    }

    [Fact]
    public void A_plan_is_stored_without_the_answers_it_computes()
    {
        // `isEmpty` and `watchUrl` are derived from the fields beside them. Written out, they
        // are a stored answer that cannot disagree with its question until one of them changes.
        var store = Store();

        store.SetPlan(
            Pasted,
            StemPlan.Default.Adding(new AddedSource { VideoId = "bbbbbbbbbbb", Label = "Live" })
        );

        var json = File.ReadAllText(Path.Combine(_directory, "queue.json"));

        Assert.DoesNotContain("isEmpty", json, StringComparison.Ordinal);
        Assert.DoesNotContain("watchUrl", json, StringComparison.Ordinal);
        Assert.Contains("addedSources", json, StringComparison.Ordinal);
    }
}
