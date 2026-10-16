using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

/// <summary>Ledgers for tests, imported from keytag-shaped JSONL into an in-memory store.</summary>
internal static class TestLedger
{
    /// <summary>The ledger a keytag <c>runs.jsonl</c> imports as; empty when the file is absent.</summary>
    public static KeyLedger From(string jsonl)
    {
        using var store = Store(jsonl);

        return store.Read();
    }

    public static IReadOnlyDictionary<string, RunEvidence> LatestEvidence(string jsonl)
    {
        using var store = Store(jsonl);

        return store.LatestEvidence();
    }

    public static RunEvidence? EvidenceFor(string jsonl, string song)
    {
        using var store = Store(jsonl);

        return store.EvidenceFor(song);
    }

    /// <summary>An in-memory store importing from <paramref name="jsonl"/>, re-read whenever the file changes.</summary>
    public static LedgerStore Store(string? jsonl = null) =>
        new(LedgerStore.InMemory, jsonl, TimeProvider.System);
}
