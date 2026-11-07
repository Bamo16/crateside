using Crateside.Core;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Samples;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>Handing off a swapped track's replacement over the version it replaces (ADR 0014).</summary>
public sealed class HandoffSupersedeTests : IDisposable
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
        _staging = Path.Combine(_root, "Staging");
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

    [Fact]
    public void Superseded_never_names_the_incoming_uploads_own_files()
    {
        var paths = RealPaths.Create();

        Assert.SkipUnless(
            Directory.Exists(paths.StagingFolder) && TestLedger.HasReal,
            "The real staging root and ledger must be present."
        );

        var ledger = TestLedger.FromReal();

        // Only a song with an upload in staging can have a file there superseded.
        var stagedSongs = Directory
            .EnumerateFiles(paths.StagingFolder)
            .Where(SampleFiles.IsAudio)
            .Select(path => StemTagReader.Read(path)?.SourceTrackKey)
            .OfType<string>()
            .Select(ledger.SongFor)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var trackKeys = ledger
            .Runs.Select(run => run.SourceTrackKey)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Where(id => ledger.SongFor(id) is { } song && stagedSongs.Contains(song));

        foreach (var trackKey in trackKeys)
        {
            Assert.DoesNotContain(
                SupersededStems.In(paths.StagingFolder, trackKey, ledger),
                path => StemTagsSay(path, trackKey)
            );
        }
    }

    private static bool StemTagsSay(string path, string trackKey) =>
        Crateside.Core.Samples.StemTagReader.Read(path)?.Provenance?.SourceTrackKey is { } id
        && id.Equals(trackKey, StringComparison.Ordinal);

    [Fact]
    public void A_track_the_ledger_has_never_judged_supersedes_nothing() =>
        Assert.Empty(
            SupersededStems.In(_staging, "zzzzzzzzzzz", TestLedger.From(Path.Combine(_root, "x")))
        );

    private HandoffRunner Runner() => new(new CratesidePaths { StagingFolder = _staging });

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
