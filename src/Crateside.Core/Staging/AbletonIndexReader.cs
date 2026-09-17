using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Staging;

/// <summary>
/// Reads Ableton's own file index — the SQLite database behind the browser — as the same
/// <see cref="AbletonFolderInfo"/> the folder XMP yields.
/// </summary>
/// <remarks>
/// Opened read-only and never written. This is Ableton's store, not a mirror it merges.
/// </remarks>
public sealed class AbletonIndexReader(IOptions<CratesidePaths> paths)
{
    private const string DatabasePattern = "Live-files-*.db";

    /// <summary>Every place Ableton knows, against the absolute path it stands for.</summary>
    private const string PlacePathsSql = """
        WITH RECURSIVE up(place_id, parent_id, path) AS (
            SELECT p.file_id, f.parent_id, f.name
            FROM places p
            JOIN files f ON f.file_id = p.file_id
            UNION ALL
            SELECT up.place_id, f.parent_id,
                   f.name || CASE WHEN f.name LIKE '%\' THEN '' ELSE '\' END || up.path
            FROM files f
            JOIN up ON f.file_id = up.parent_id
        )
        SELECT place_id, path FROM up
        """;

    /// <summary>
    /// One row per keyword, so a file with three tags arrives as three rows.
    /// </summary>
    private const string TagsSql = """
        WITH RECURSIVE tree(file_id, path) AS (
            SELECT file_id, '' FROM files WHERE file_id = $place
            UNION ALL
            SELECT f.file_id,
                   CASE WHEN tree.path = '' THEN f.name ELSE tree.path || '/' || f.name END
            FROM files f
            JOIN tree ON f.parent_id = tree.file_id
        )
        SELECT tree.path, mv.value
        FROM tree
        JOIN metadata m ON m.file_id = tree.file_id AND m.key = $keyword
        JOIN metadata_values mv ON mv.id = m.value_id
        ORDER BY tree.path
        """;

    private static readonly long UserKeyword = FourCc("UKey");

    private readonly CratesidePaths _paths = paths.Value;

    /// <summary>
    /// Reads what the index holds for <paramref name="rootFolder"/>. Never throws — an absent
    /// database, or a folder Ableton has never been shown, both read as empty.
    /// </summary>
    public AbletonFolderInfo Read(string rootFolder)
    {
        try
        {
            if (FindDatabase() is not { } database)
                return AbletonFolderInfo.Empty(rootFolder);

            using var connection = Open(database);

            return PlaceFor(connection, rootFolder) is { } place
                ? new AbletonFolderInfo(rootFolder, TagsUnder(connection, place))
                : AbletonFolderInfo.Empty(rootFolder);
        }
        catch (Exception e)
            when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            return AbletonFolderInfo.Empty(rootFolder);
        }
    }

    /// <summary>
    /// The database Live is currently using: the one being written, not the highest-numbered.
    /// </summary>
    /// <remarks>
    /// The number is a schema version, not an app version, so a stale file may sit beside the
    /// live one.
    /// </remarks>
    private string? FindDatabase() =>
        Directory.Exists(_paths.AbletonIndex)
            ? Directory
                .EnumerateFiles(_paths.AbletonIndex, DatabasePattern)
                .MaxBy(File.GetLastWriteTimeUtc)
            : null;

    /// <remarks>
    /// Unpooled: a pooled connection holds the file open after it is closed, and this file is
    /// Ableton's.
    /// </remarks>
    private static SqliteConnection Open(string database)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString()
        );

        connection.Open();

        return connection;
    }

    private static long? PlaceFor(SqliteConnection connection, string rootFolder)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PlacePathsSql;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (SamePath(reader.GetString(1), rootFolder))
                return reader.GetInt64(0);
        }

        return null;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase
        );

    private static IReadOnlyList<AbletonFileTags> TagsUnder(SqliteConnection connection, long place)
    {
        using var command = connection.CreateCommand();
        command.CommandText = TagsSql;
        command.Parameters.AddWithValue("$place", place);
        command.Parameters.AddWithValue("$keyword", UserKeyword);

        using var reader = command.ExecuteReader();

        var rows = new List<(string Path, string Value)>();

        while (reader.Read())
            rows.Add((reader.GetString(0), reader.GetString(1)));

        return
        [
            .. rows.GroupBy(r => r.Path, StringComparer.Ordinal)
                .Select(g => AbletonFileTags.FromKeywords(g.Key, [.. g.Select(r => r.Value)])),
        ];
    }

    /// <summary>
    /// Ableton stores its integer keys as four-character codes — <c>UKey</c> for a user-applied
    /// keyword, beside <c>Keyw</c> for the ones it derives itself.
    /// </summary>
    private static long FourCc(string code) => code.Aggregate(0L, (value, c) => (value << 8) | c);
}
