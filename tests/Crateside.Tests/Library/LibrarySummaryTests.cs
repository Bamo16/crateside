using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Library;
using Crateside.Tests.Ledger;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Library;

/// <summary>The cache in front of the reader, exercised against a real folder.</summary>
public sealed class LibrarySummaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"crateside-library-{Guid.NewGuid()}"
    );

    public LibrarySummaryTests()
    {
        Directory.CreateDirectory(_root);
        Add("one.flac");
        Add("two.flac");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_second_look_does_not_re_scan_the_library()
    {
        var summary = Summary();

        Assert.Equal(2, summary.Current.FileCount);

        Add("three.flac");

        Assert.Equal(2, summary.Current.FileCount);
    }

    [Fact]
    public void Re_reading_picks_up_what_changed()
    {
        var summary = Summary();

        _ = summary.Current;

        Add("three.flac");

        Assert.Equal(3, summary.Refresh().FileCount);
        Assert.Equal(3, summary.Current.FileCount);
    }

    [Fact]
    public void A_file_Ableton_knows_nothing_about_counts_as_unkeyed()
    {
        var distribution = Summary().Current;

        Assert.Equal(2, distribution.Unkeyed.Count);
        Assert.Equal(0, distribution.KeyedTrackCount);
        Assert.All(distribution.Counts, c => Assert.Equal(0, c.Tracks));
    }

    [Fact]
    public void A_songs_samples_count_as_one_track()
    {
        Add("Yes - Owner of a Lonely Heart (Vocal - Full).flac");
        Add("Yes - Owner of a Lonely Heart (Instrumental - Full).flac");

        var distribution = Summary().Current;

        Assert.Equal(4, distribution.FileCount);
        Assert.Equal(3, distribution.TrackCount);
        Assert.Equal(3, distribution.UngenredCount);
    }

    [Fact]
    public void A_root_that_does_not_exist_reads_as_nothing_rather_than_throwing()
    {
        var paths = Options.Create(
            new CratesidePaths { LibraryFolder = Path.Combine(_root, "gone") }
        );

        var distribution = new LibraryKeyReader(
            TestLedger.Store(paths.Value.KeytagLedger),
            paths,
            new FileIndexReader(paths.Value.AbletonIndex),
            new FolderInfoReader(),
            new WarpTempoReader()
        ).Read();

        Assert.Equal(0, distribution.FileCount);
        Assert.Empty(distribution.Counts);
    }

    private LibrarySummary Summary()
    {
        var paths = Options.Create(new CratesidePaths { LibraryFolder = _root });

        return new LibrarySummary(
            new LibraryKeyReader(
                TestLedger.Store(paths.Value.KeytagLedger),
                paths,
                new FileIndexReader(paths.Value.AbletonIndex),
                new FolderInfoReader(),
                new WarpTempoReader()
            )
        );
    }

    private void Add(string name) => File.WriteAllText(Path.Combine(_root, name), string.Empty);
}
