using Microsoft.Data.Sqlite;

namespace Crateside.Demo;

/// <summary>A file index laid out like Live's, holding the keywords of the demo's Staging and Library, so the app reads keys the way it does on a real machine.</summary>
internal sealed class LiveIndex : IDisposable
{
    private const string Schema = """
        CREATE TABLE files (file_id INTEGER PRIMARY KEY AUTOINCREMENT, parent_id INTEGER, name TEXT, colors INTEGER DEFAULT 1);
        CREATE TABLE places (file_id INTEGER, folder_kind INTEGER, level INTEGER NOT NULL DEFAULT 0, name TEXT);
        CREATE TABLE metadata (file_id INTEGER, key INTEGER, value_id INTEGER);
        CREATE TABLE metadata_values (id INTEGER PRIMARY KEY AUTOINCREMENT, value TEXT);
        """;

    private static readonly long UserKeyword = FourCc("UKey");

    private readonly SqliteConnection _db;
    private readonly Dictionary<string, long> _places = new(StringComparer.OrdinalIgnoreCase);

    public LiveIndex(string folder)
    {
        Directory.CreateDirectory(folder);

        _db = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(folder, "Live-files-12300.db"),
                Pooling = false,
            }.ToString()
        );
        _db.Open();
        Execute(Schema);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Puts a file under a place and gives it keywords, the way Live indexes a browsed folder.</summary>
    public void Add(string root, string fileName, IReadOnlyList<string> keywords)
    {
        var file = Insert(Place(root), fileName);

        foreach (var keyword in keywords)
        {
            using var command = _db.CreateCommand();

            command.CommandText = """
                INSERT INTO metadata_values (value) VALUES ($value);
                INSERT INTO metadata (file_id, key, value_id) VALUES ($file, $key, last_insert_rowid());
                """;
            command.Parameters.AddWithValue("$value", keyword);
            command.Parameters.AddWithValue("$file", file);
            command.Parameters.AddWithValue("$key", UserKeyword);
            command.ExecuteNonQuery();
        }
    }

    private long Place(string root)
    {
        if (_places.TryGetValue(root, out var known))
            return known;

        var drive = Path.GetPathRoot(root)!;
        var parent = Insert(0, drive);

        foreach (
            var segment in root[drive.Length..]
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
        )
            parent = Insert(parent, segment);

        Execute($"INSERT INTO places (file_id, folder_kind, name) VALUES ({parent}, 2, 'root')");

        return _places[root] = parent;
    }

    private long Insert(long parent, string name)
    {
        using var command = _db.CreateCommand();

        command.CommandText =
            "INSERT INTO files (parent_id, name) VALUES ($parent, $name); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$parent", parent);
        command.Parameters.AddWithValue("$name", name);

        return (long)command.ExecuteScalar()!;
    }

    private void Execute(string sql)
    {
        using var command = _db.CreateCommand();

        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long FourCc(string code) => code.Aggregate(0L, (value, c) => (value << 8) | c);
}
