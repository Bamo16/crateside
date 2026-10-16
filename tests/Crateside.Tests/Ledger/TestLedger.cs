using Crateside.Core;
using Crateside.Core.Ledger;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Ledger;

/// <summary>Ledgers for tests: fixture lines loaded into an in-memory store, or a copy of this machine's real ledger.</summary>
internal static class TestLedger
{
    private static readonly Lazy<string?> RealCopy = new(CopyReal);

    /// <summary>Whether crateside's own ledger exists on this machine.</summary>
    public static bool HasReal => File.Exists(new CratesidePaths().LedgerDatabase);

    /// <summary>The ledger the fixture lines in <paramref name="jsonl"/> make; empty when the file is absent.</summary>
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

    /// <summary>An in-memory store holding the fixture lines of <paramref name="jsonl"/>, if given.</summary>
    public static LedgerStore Store(string? jsonl = null)
    {
        FakeTimeProvider time = new();
        LedgerStore store = new(LedgerStore.InMemory, time);

        if (jsonl is not null)
            RunLines.Load(store, time, jsonl);

        return store;
    }

    public static KeyLedger FromReal()
    {
        using var store = StoreReal();

        return store.Read();
    }

    public static IReadOnlyDictionary<string, RunEvidence> LatestEvidenceReal()
    {
        using var store = StoreReal();

        return store.LatestEvidence();
    }

    /// <summary>A store over a snapshot of this machine's ledger, taken once per test run, so the app running beside it is never touched.</summary>
    public static LedgerStore StoreReal() =>
        new(
            RealCopy.Value ?? throw new InvalidOperationException("No ledger.db."),
            TimeProvider.System
        );

    private static string? CopyReal()
    {
        var source = new CratesidePaths().LedgerDatabase;

        if (!File.Exists(source))
            return null;

        var copy = Path.Combine(
            Path.GetTempPath(),
            $"crateside-tests-ledger-{Guid.NewGuid():n}.db"
        );

        using (var connection = new SqliteConnection($"Data Source={source};Mode=ReadOnly"))
        {
            connection.Open();

            using var command = connection.CreateCommand();

            command.CommandText = $"VACUUM INTO '{copy}'";
            command.ExecuteNonQuery();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            SqliteConnection.ClearAllPools();
            File.Delete(copy);
        };

        return copy;
    }
}
