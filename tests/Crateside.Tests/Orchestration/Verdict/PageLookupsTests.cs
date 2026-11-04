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

    private sealed class AskedPages : ISourcePages
    {
        public List<(string Artist, string Title)> Asked { get; } = [];

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
