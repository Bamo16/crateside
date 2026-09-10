using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// Handing off a swapped track's replacement over the version it replaces. Staging is
/// crateside's to manage, so a superseded stem there is cleared; one that has reached
/// `Mashup Samples` is the user's and is never touched (ADR 0014).
/// </summary>
public class HandoffSupersedeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _staging;
    private readonly string _cut;

    public HandoffSupersedeTests()
    {
        _staging = Path.Combine(_root, "Sample Staging");
        _cut = Path.Combine(_root, "Key Identified");

        Directory.CreateDirectory(_staging);
        Directory.CreateDirectory(_cut);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The replacement carries the same filename as the thing it replaces, which is the case the
    /// clearing exists for: without it the handoff refuses and the swap stalls half-done.
    /// </summary>
    [Fact]
    public void A_replacement_lands_over_the_version_it_supersedes()
    {
        var old = Staged("Southern Hospitality (Vocal - Full).flac");
        var fresh = Cut("Southern Hospitality (Vocal - Full).flac");

        var result = Runner().Run([fresh], superseded: [old]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Moved);
        Assert.Equal(1, result.Cleared);
        Assert.Equal("fresh", File.ReadAllText(old));
    }

    [Fact]
    public void Without_the_clearing_the_same_handoff_still_refuses()
    {
        _ = Staged("Southern Hospitality (Vocal - Full).flac");
        var fresh = Cut("Southern Hospitality (Vocal - Full).flac");

        var result = Runner().Run([fresh]);

        Assert.False(result.IsSuccess);
        Assert.Contains("already in staging", result.Error);
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Nothing_is_cleared_when_there_is_nothing_to_supersede()
    {
        var fresh = Cut("Southern Hospitality (Vocal - Full).flac");

        var result = Runner().Run([fresh]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Cleared);
    }

    /// <summary>
    /// A superseded file whose name differs still goes. The rule is about the upload being
    /// replaced, not about a collision — a swap to an upload with different metadata produces
    /// differently named stems and the old cut is no more wanted for that.
    /// </summary>
    [Fact]
    public void A_superseded_file_goes_even_when_its_name_does_not_collide()
    {
        var old = Staged("Southern Hospitality (Official Video) (Vocal - Full).flac");
        var fresh = Cut("Southern Hospitality (Vocal - Full).flac");

        var result = Runner().Run([fresh], superseded: [old]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Cleared);
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void A_superseded_file_that_has_already_gone_is_not_an_error()
    {
        var fresh = Cut("Southern Hospitality (Vocal - Full).flac");

        var result = Runner()
            .Run([fresh], superseded: [Path.Combine(_staging, "never existed.flac")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Cleared);
    }

    /// <summary>
    /// The safety property. If this predicate were ever inverted the handoff would delete the
    /// very stems it is about to move, and the replacement would be lost with the original.
    /// </summary>
    [Fact]
    public void Superseded_never_names_the_incoming_uploads_own_files()
    {
        var paths = new CratesidePaths();

        Assert.SkipUnless(
            Directory.Exists(paths.SampleStaging) && File.Exists(paths.Ledger),
            "The real staging root and ledger must be present."
        );

        var ledger = KeyLedger.Read(paths.Ledger);

        foreach (var videoId in ledger.Runs.Select(run => run.SourceVideoId).OfType<string>())
        {
            Assert.DoesNotContain(
                SupersededStems.In(paths.SampleStaging, videoId, ledger),
                path => StemTagsSay(path, videoId)
            );
        }
    }

    private static bool StemTagsSay(string path, string videoId) =>
        Crateside.Core.Staging.StemTagReader.Read(path)?.Provenance?.SourceVideoId is { } id
        && id.Equals(videoId, StringComparison.Ordinal);

    [Fact]
    public void A_track_the_ledger_has_never_judged_supersedes_nothing() =>
        Assert.Empty(
            SupersededStems.In(_staging, "zzzzzzzzzzz", KeyLedger.Read(Path.Combine(_root, "x")))
        );

    private HandoffRunner Runner() => new(new CratesidePaths { SampleStaging = _staging });

    private string Staged(string name)
    {
        var path = Path.Combine(_staging, name);
        File.WriteAllText(path, "old");

        return path;
    }

    private string Cut(string name)
    {
        var path = Path.Combine(_cut, name);
        File.WriteAllText(path, "fresh");

        return path;
    }
}
