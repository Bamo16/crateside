using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class PageLookupsTests : IDisposable
{
    private const string TrackKey = "IijgrkA_P50";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly Core.Ledger.LedgerStore _store = TestLedger.Store();
    private readonly AskedPages _pages = new();

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private PageLookups Lookups(ResolvedNameStore? names = null) =>
        new(_pages, new WorkingSet(Path.Combine(_directory, "working")), _store, names);

    private WorkOrder Order(string title) =>
        WorkOrder.Compose(new WorkItem { TrackKey = TrackKey, Title = title }, _store.Read());

    [Fact]
    public async Task A_title_naming_its_artist_is_looked_up_by_it()
    {
        await Lookups().LookUpAsync(Order("TaQ - sync"), TestContext.Current.CancellationToken);

        Assert.Equal(("TaQ", "sync"), Assert.Single(_pages.Asked));
    }

    [Fact]
    public async Task A_title_without_an_artist_is_looked_up_by_the_name_stemforge_resolved()
    {
        var names = new ResolvedNameStore(Path.Combine(_directory, "names.json"));
        names.Save(
            TrackKey,
            new ResolvedName { DisplayTitle = "TaQ - sync", BaseName = "TaQ - sync" }
        );

        await Lookups(names).LookUpAsync(Order("sync"), TestContext.Current.CancellationToken);

        Assert.Equal(("TaQ", "sync"), Assert.Single(_pages.Asked));
    }

    [Fact]
    public async Task A_title_without_an_artist_and_no_resolved_name_is_not_looked_up()
    {
        var names = new ResolvedNameStore(Path.Combine(_directory, "names.json"));

        await Lookups(names).LookUpAsync(Order("sync"), TestContext.Current.CancellationToken);

        Assert.Empty(_pages.Asked);
    }

    [Fact]
    public async Task A_file_with_tags_is_looked_up_by_them_and_its_other_credits_are_passed_on()
    {
        var intake = new Core.Playlist.IntakeStore(Path.Combine(_directory, "intake.json"));

        intake.TakeIn(
            new Core.Playlist.IntakeRecord
            {
                TrackKey = TrackKey,
                Title = "Dave Wise - Stickerbush Symphony",
                Artist = "Dave Wise",
                SongTitle = "Stickerbush Symphony",
                Release = new Core.Imports.Release
                {
                    AlbumArtist = "Rare",
                    Composer = "David Wise",
                    Organization = "Rare, Nintendo",
                },
            }
        );

        var lookups = new PageLookups(
            _pages,
            new WorkingSet(Path.Combine(_directory, "working")),
            _store,
            intake: intake
        );

        await lookups.LookUpAsync(
            Order("Dave Wise - Stickerbush Symphony"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal([("Dave Wise", "Stickerbush Symphony")], _pages.Asked);
        Assert.Equal(["Rare", "David Wise", "Rare, Nintendo"], _pages.AlsoCredited.Single());
    }

    [Fact]
    public async Task A_file_with_no_title_tag_is_looked_up_by_its_whole_name_not_split_at_a_dash()
    {
        var intake = new Core.Playlist.IntakeStore(Path.Combine(_directory, "intake.json"));

        intake.TakeIn(
            new Core.Playlist.IntakeRecord
            {
                TrackKey = TrackKey,
                Title = "Chemical Plant Zone - Act 1",
                Release = new Core.Imports.Release(),
            }
        );

        await new PageLookups(
            _pages,
            new WorkingSet(Path.Combine(_directory, "working")),
            _store,
            intake: intake
        ).LookUpAsync(Order("Chemical Plant Zone - Act 1"), TestContext.Current.CancellationToken);

        Assert.Equal([(string.Empty, "Chemical Plant Zone - Act 1")], _pages.Asked);
    }

    private sealed class AskedPages : ISourcePages
    {
        public List<(string Artist, string Title)> Asked { get; } = [];

        public List<IReadOnlyList<string>> AlsoCredited { get; } = [];

        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            IReadOnlyList<string> alsoCredited,
            CancellationToken ct = default
        )
        {
            AlsoCredited.Add(alsoCredited);

            return FetchAsync(artist, title, ct);
        }

        public Task<IReadOnlyList<SiteLookup>> FetchAsync(
            string artist,
            string title,
            CancellationToken ct = default
        )
        {
            Asked.Add((artist, title));

            return Task.FromResult<IReadOnlyList<SiteLookup>>([]);
        }
    }
}
