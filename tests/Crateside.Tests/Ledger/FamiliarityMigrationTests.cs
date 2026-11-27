using Crateside.Core.Familiarity;
using Crateside.Core.Ledger;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Crateside.Tests.Ledger;

/// <summary>ADR 0089 starts familiarity over: the migration drops every row and keeps the old ledger beside it.</summary>
public sealed class FamiliarityMigrationTests : IDisposable
{
    private const string Before = "20261109223108_GenresFromAbleton";
    private const string Migration = "20261110212347_LiveAnswers";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private string Database => Path.Combine(_folder, "ledger.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Every_row_and_answer_is_dropped_and_the_old_ledger_is_kept_beside_it()
    {
        Directory.CreateDirectory(_folder);
        SeedPreviousSchema();

        using (var store = new LedgerStore(Database, TimeProvider.System))
        {
            var familiarity = new FamiliarityStore(store);

            Assert.Empty(familiarity.Read());

            familiarity.Measure(
                "a - b",
                new FamiliarityReading(4_000, 25_000, "Q1"),
                DateTimeOffset.UtcNow,
                FamiliarityLookup.Version
            );

            Assert.Equal("Mainstream", familiarity.Read()["a - b"].Written);
        }

        var backup = Path.Combine(_folder, "ledger-backups", $"before-{Migration}.db");

        Assert.True(File.Exists(backup));

        using var kept = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        kept.Open();

        using var command = kept.CreateCommand();
        command.CommandText =
            "SELECT Song, ArtistFans, SongRank, Settled FROM Familiarities ORDER BY Song";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(
            ("a - b", 17_522L, 167_703L, "Obscure"),
            (reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3))
        );
        Assert.True(reader.Read());
        Assert.Equal("c - d", reader.GetString(0));
        Assert.Equal("Mainstream", reader.GetString(3));
    }

    private void SeedPreviousSchema()
    {
        using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
        connection.Open();

        var options = new DbContextOptionsBuilder<LedgerContext>().UseSqlite(connection).Options;

        using (var context = new LedgerContext(options))
            context.GetInfrastructure().GetRequiredService<IMigrator>().Migrate(Before);

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO Familiarities (Song, Artist, ArtistFans, SongRank, LookedUpAt, LookupVersion, Settled) VALUES
              ('a - b', 'Moe Shop', 17522, 167703, '2026-11-01T00:00:00+00:00', 3, 'Obscure'),
              ('c - d', NULL, NULL, NULL, NULL, 0, 'Mainstream')
            """;
        insert.ExecuteNonQuery();
    }
}
