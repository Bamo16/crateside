using Crateside.Core.Ledger;
using Crateside.Core.Staging;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Staging;

public sealed class StagingReaderSidecarCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public StagingReaderSidecarCacheTests()
    {
        Directory.CreateDirectory(_root);
        File.Copy(Fixture("silence.flac"), Path.Combine(_root, "Song.flac"));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_sidecar_replaced_after_a_read_shows_its_new_default_clip()
    {
        var reader = new StagingReader(TestLedger.Store());
        var sidecar = Path.Combine(_root, "Song.flac.asd");

        File.Copy(Fixture("sidecar-clicks-unsaved.asd"), sidecar);
        Assert.False(Read(reader).HasDefaultClip);
        Assert.False(Read(reader).HasDefaultClip);

        File.Copy(Fixture("sidecar-clicks.asd"), sidecar, overwrite: true);
        Assert.True(Read(reader).HasDefaultClip);

        File.Delete(sidecar);
        Assert.False(Read(reader).HasDefaultClip);
    }

    private StagedFile Read(StagingReader reader) =>
        Assert.Single(Assert.Single(reader.Build(_root, KeyLedger.Empty)).Stems);

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
