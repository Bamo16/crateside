using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Handoff;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>
/// Which of a song's staged files a handoff may clear out of the way — never an added source
/// (ADR 0019).
/// </summary>
public sealed class SupersededStemsTests : IDisposable
{
    private const string Song = "chris brown, lil' wayne, busta rhymes - look at me now";
    private const string Cut = "x2_zcyJp2mE";
    private const string Added = "H4jDiyL4S2E";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _staging;
    private readonly string _ledgerPath;

    public SupersededStemsTests()
    {
        _staging = Path.Combine(_root, "Staging");
        _ledgerPath = Path.Combine(_root, "runs.jsonl");

        Directory.CreateDirectory(_staging);

        File.WriteAllLines(
            _ledgerPath,
            new[] { Cut, Added }.Select(id =>
                $$$"""
                {"song":"{{{Song}}}","source_url":"https://music.youtube.com/watch?v={{{id}}}","verdict":{"key":"E minor","confidence":"MEDIUM"}}
                """
            )
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private KeyLedger Ledger() => KeyLedger.Read(_ledgerPath);

    [Fact]
    public void A_swap_still_clears_the_upload_it_replaced()
    {
        // After a swap both uploads' files carry the same filenames.
        var superseded = SupersededStems.In(_staging, Added, Ledger());

        Assert.NotNull(superseded);
    }

    [Fact]
    public void An_added_sources_handoff_clears_nothing()
    {
        var kept = new HashSet<string>(StringComparer.Ordinal) { Added };

        Assert.Empty(SupersededStems.In(_staging, Added, Ledger(), kept));
    }

    [Fact]
    public void A_swap_does_not_eat_the_added_sources_of_the_song()
    {
        var kept = new HashSet<string>(StringComparer.Ordinal) { Added };

        Assert.Empty(SupersededStems.In(_staging, Cut, Ledger(), kept));
    }

    [Fact]
    public void A_song_the_ledger_has_never_seen_supersedes_nothing()
    {
        Assert.Empty(SupersededStems.In(_staging, "zzzzzzzzzzz", Ledger()));
    }
}
