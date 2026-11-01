using Crateside.Core.Ledger;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Ledger;

public sealed class LedgerMigrationTests : IDisposable
{
    private const string BeforeSpelling = "20261024201525_SectionStart";
    private const string SpellingMigration = "20261025192752_AmericanSpelling";
    private const string LateReadingMigration = "20261029013237_InheritedLateReading";
    private const string NewestMigration = "20261101225614_LookupsKept";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        DateTimeOffset.Parse("2026-10-25T12:00:00+00:00")
    );

    private string DatabasePath => Path.Combine(_root, "ledger.db");

    public LedgerMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private void CreateLedgerBeforeTheSpellingChange()
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        connection.Open();

        var options = new DbContextOptionsBuilder<LedgerContext>().UseSqlite(connection).Options;

        using (var context = new LedgerContext(options))
            context.GetService<IMigrator>().Migrate(BeforeSpelling);

        Execute(
            connection,
            """
            INSERT INTO Runs (Id, Artist, Title, Song, Confidence, Mixed, LocalSource, SourceUrl, InputKind, RecordedAt, Evidence, Reasoning, Tonic, Mode)
            VALUES (1, 'A', 'T', 'a - t', 'High', 0, 0, 'https://www.youtube.com/watch?v=x', 'full_mix', '2026-10-01T00:00:00+00:00', '{}', '', 'C', 'Ionian');
            INSERT INTO Sources (RunId, Position, Kind, Url) VALUES (1, 0, 'ChordRecogniser', 'https://chordify.net/x');
            INSERT INTO Sources (RunId, Position, Kind, Url) VALUES (1, 1, 'Authoritative', 'https://hooktheory.com/x');
            """
        );
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    [Fact]
    public void A_ledger_with_the_old_spelling_comes_out_American_and_keeps_every_run()
    {
        CreateLedgerBeforeTheSpellingChange();

        using (var store = new LedgerStore(DatabasePath, _time))
        {
            var evidence = store.EvidenceFor("a - t")!;

            Assert.Equal(
                [SourceKind.ChordRecognizer, SourceKind.Authoritative],
                evidence.Sources.Select(source => source.Kind)
            );
        }

        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM Sources WHERE Kind = 'ChordRecogniser'"));
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM Sources WHERE Kind = 'ChordRecognizer'"));
    }

    [Fact]
    public void The_check_constraint_is_renamed_and_still_enforced()
    {
        CreateLedgerBeforeTheSpellingChange();

        using (var store = new LedgerStore(DatabasePath, _time)) { }

        var sql = (string)Scalar("SELECT sql FROM sqlite_master WHERE name = 'Runs'")!;

        Assert.Contains("CK_Runs_Center", sql);
        Assert.DoesNotContain("CK_Runs_Centre", sql);
        Assert.Throws<SqliteException>(() => Scalar("UPDATE Runs SET Tonic = NULL WHERE Id = 1"));
    }

    [Fact]
    public void The_ledger_is_copied_aside_before_the_migration_applies()
    {
        CreateLedgerBeforeTheSpellingChange();

        using var store = new LedgerStore(DatabasePath, _time);

        var backup = Assert.Single(Directory.GetFiles(store.BackupFolder!, "before-*.db"));

        Assert.EndsWith($"before-{NewestMigration}.db", backup);

        using var connection = new SqliteConnection($"Data Source={backup};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM Sources WHERE Kind = 'ChordRecogniser'";

        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public void Opening_a_migrated_ledger_again_changes_nothing_and_takes_no_second_copy()
    {
        CreateLedgerBeforeTheSpellingChange();

        using (var first = new LedgerStore(DatabasePath, _time)) { }

        var backups = Directory.GetFiles(Path.Combine(_root, "ledger-backups"));

        using (var second = new LedgerStore(DatabasePath, _time))
        {
            Assert.Equal(SourceKind.ChordRecognizer, second.EvidenceFor("a - t")!.Sources[0].Kind);
        }

        Assert.Equal(backups, Directory.GetFiles(Path.Combine(_root, "ledger-backups")));
    }

    [Fact]
    public void A_late_reading_marker_older_than_its_run_is_dropped_and_every_true_one_is_kept()
    {
        using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            connection.Open();

            var options = new DbContextOptionsBuilder<LedgerContext>()
                .UseSqlite(connection)
                .Options;

            using (var context = new LedgerContext(options))
                context.GetService<IMigrator>().Migrate(SpellingMigration);

            Execute(
                connection,
                """
                INSERT INTO Runs (Id, Song, Confidence, Mixed, LocalSource, SourceUrl, RecordedAt, Evidence, Tonic, Mode) VALUES
                  (1, 'inherited', 'Medium', 0, 0, 'https://www.youtube.com/watch?v=a', '2026-10-28T16:40:28+00:00', '{"chords":["Am"],"read_after_verdict":{"at":"2026-10-25T13:59:37+00:00"}}', 'A', 'Aeolian'),
                  (2, 'read later', 'Medium', 0, 0, 'https://www.youtube.com/watch?v=b', '2026-10-28T15:28:39+00:00', '{"read_after_verdict":{"at":"2026-10-28T15:28:54+00:00"}}', 'A', 'Aeolian'),
                  (3, 'before the pin', 'Medium', 0, 0, 'https://www.youtube.com/watch?v=c', '2026-10-01T00:00:00+00:00', '{"read_after_verdict":{"at":null}}', 'A', 'Aeolian'),
                  (4, 'inherited untimed', 'Medium', 0, 0, 'https://www.youtube.com/watch?v=d', '2026-10-28T16:40:28+00:00', '{"read_after_verdict":{"at":null}}', 'A', 'Aeolian');
                """
            );
        }

        using var store = new LedgerStore(DatabasePath, _time);

        Assert.Null(store.EvidenceFor("inherited")!.LateReading);
        Assert.Equal(["Am"], store.EvidenceFor("inherited")!.Chords);
        Assert.NotNull(store.EvidenceFor("read later")!.LateReading);
        Assert.NotNull(store.EvidenceFor("before the pin")!.LateReading);
        Assert.Null(store.EvidenceFor("inherited untimed")!.LateReading);
    }

    [Fact]
    public void Bars_counted_from_the_clip_start_are_cleared_and_their_seconds_kept()
    {
        using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            connection.Open();

            var options = new DbContextOptionsBuilder<LedgerContext>()
                .UseSqlite(connection)
                .Options;

            using (var context = new LedgerContext(options))
                context.GetService<IMigrator>().Migrate(LateReadingMigration);

            Execute(
                connection,
                """
                INSERT INTO Runs (Id, Song, Confidence, Mixed, LocalSource, SourceUrl, RecordedAt, Evidence, Tonic, Mode)
                VALUES (1, 'fusq - perfume!', 'Medium', 0, 0, 'https://www.youtube.com/watch?v=a', '2026-10-28T16:40:28+00:00', '{}', 'A', 'Aeolian');
                INSERT INTO Windows (RunId, Position, Start, End, Label, AskedBy, FirstBar, LastBar)
                VALUES (1, 0, 37.79539574431894, 158.74199331266047, NULL, 'User', 1, 64);
                """
            );
        }

        using var store = new LedgerStore(DatabasePath, _time);

        var window = Assert.Single(store.EvidenceFor("fusq - perfume!")!.Windows);

        Assert.Equal(
            new RunWindow(37.79539574431894, 158.74199331266047, null, WindowAsker.User),
            window
        );
    }

    [Fact]
    public void A_new_ledger_has_nothing_to_copy_aside()
    {
        using (var store = new LedgerStore(DatabasePath, _time)) { }

        Assert.False(Directory.Exists(Path.Combine(_root, "ledger-backups")));
    }
}
