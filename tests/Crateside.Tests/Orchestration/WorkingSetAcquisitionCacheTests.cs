using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;

namespace Crateside.Tests.Orchestration;

public sealed class WorkingSetAcquisitionCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_acquisition_rewritten_by_another_writer_reads_as_its_new_contents()
    {
        var reading = new WorkingSet(_root);
        var writing = new WorkingSet(_root);
        reading.Prepare("aaaaaaaaaaa");

        writing.SaveAcquisition("aaaaaaaaaaa", new Acquisition { IsAccepted = false });
        Assert.False(reading.Acquisitions()["aaaaaaaaaaa"].IsAccepted);
        Assert.False(reading.Acquisitions()["aaaaaaaaaaa"].IsAccepted);

        writing.SaveAcquisition("aaaaaaaaaaa", new Acquisition { IsAccepted = true });

        // The folder's time is the cache's key and the clock ticks only every 15.6 ms on Windows.
        var folder = Path.Combine(_root, "aaaaaaaaaaa");
        Directory.SetLastWriteTimeUtc(folder, Directory.GetLastWriteTimeUtc(folder).AddMinutes(1));

        Assert.True(reading.Acquisitions()["aaaaaaaaaaa"].IsAccepted);
    }

    [Fact]
    public void A_discarded_working_set_no_longer_has_an_acquisition()
    {
        var set = new WorkingSet(_root);
        set.Prepare("aaaaaaaaaaa");
        set.SaveAcquisition("aaaaaaaaaaa", new Acquisition());
        Assert.Single(set.Acquisitions());

        set.Discard("aaaaaaaaaaa");

        Assert.Empty(set.Acquisitions());
    }
}
