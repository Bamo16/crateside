using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Crateside.Tests.Ledger;

/// <summary>ADR 0088 starts genre over: the migration drops every answer and keeps the old ledger beside it.</summary>
public sealed class GenreMigrationTests : IDisposable
{
    private const string Before = "20261108192456_MeasuringExclusions";

    // The copy is named after the newest pending migration, which is the one that follows genre.
    private const string Migration = "20261109225826_FamiliarityFromListeners";

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
    public void Every_answer_and_learned_genre_is_dropped_and_the_old_ledger_is_kept_beside_it()
    {
        Directory.CreateDirectory(_folder);
        SeedPreviousSchema();

        using (var store = new LedgerStore(Database, TimeProvider.System))
        {
            var records = new GenreStore(store).Read();

            Assert.Equal(2, records.Count);
            Assert.All(
                records.Values,
                record =>
                {
                    Assert.False(record.IsTheUsersAnswer);
                    Assert.Null(record.Learned);
                    Assert.Null(record.LearnedFrom);
                    Assert.Null(record.LookedUpAt);
                    Assert.Empty(record.Mine);
                }
            );
            Assert.Equal(["pop", "dance pop"], records["c - d"].Heard);
            Assert.Equal(["Pop"], records["c - d"].Genres);
        }

        var backup = Path.Combine(_folder, "ledger-backups", $"before-{Migration}.db");

        Assert.True(File.Exists(backup));

        using var kept = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False");
        kept.Open();

        using var command = kept.CreateCommand();
        command.CommandText =
            "SELECT Song, Genre, IsSettled, LearnedFrom FROM Genres ORDER BY Song";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(
            ("a - b", "Rock", 1L, true),
            (reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.IsDBNull(3))
        );
        Assert.True(reader.Read());
        Assert.Equal(
            ("c - d", "Pop", 1L, "Someone"),
            (reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetString(3))
        );
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
            INSERT INTO Genres (Song, Genre, IsSettled, LearnedFrom, Suggested, Heard, LookedUpAt) VALUES
              ('a - b', 'Rock', 1, NULL, 'Rock', '["rock"]', '2026-11-01T00:00:00+00:00'),
              ('c - d', 'Pop', 1, 'Someone', 'Pop', '["pop","dance pop"]', '2026-11-02T00:00:00+00:00')
            """;
        insert.ExecuteNonQuery();
    }
}
