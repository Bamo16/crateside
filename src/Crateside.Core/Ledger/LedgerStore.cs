using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Ledger;

/// <summary>
/// crateside's ledger of key verdicts, in SQLite (docs/ledger.md): every read and write goes
/// through here (ADR 0055).
/// </summary>
public sealed class LedgerStore : IDisposable
{
    public const string InMemory = ":memory:";

    private const int BackupsKept = 7;

    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LedgerContext> _options;
    private readonly string _databasePath;
    private readonly TimeProvider _time;
    private KeyLedger? _snapshot;

    private bool IsFile => _databasePath != InMemory;

    /// <summary>The JSONL export for reading by eye, rewritten after every change.</summary>
    public string? ExportPath => IsFile ? Path.ChangeExtension(_databasePath, ".jsonl") : null;

    /// <summary>Where the dated backups go: beside the database.</summary>
    public string? BackupFolder =>
        IsFile
            ? Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(_databasePath))!,
                "ledger-backups"
            )
            : null;

    public LedgerStore(CratesidePaths paths, TimeProvider time)
        : this(paths.LedgerDatabase, time) { }

    /// <param name="databasePath">The SQLite file, or <see cref="InMemory"/>, which keeps no backups or export.</param>
    public LedgerStore(string databasePath, TimeProvider time)
    {
        _databasePath = databasePath;
        _time = time;

        if (IsFile && Path.GetDirectoryName(Path.GetFullPath(databasePath)) is { } folder)
            Directory.CreateDirectory(folder);

        // One connection for the store's life: an in-memory database lives only as long as it.
        _connection = new SqliteConnection(ConnectionString(databasePath));
        _connection.Open();
        _options = new DbContextOptionsBuilder<LedgerContext>().UseSqlite(_connection).Options;

        using var context = Context();

        if (
            IsFile
            && context.Database.GetAppliedMigrations().Any()
            && context.Database.GetPendingMigrations().ToList() is [.., var newest]
        )
            BackUpBeforeMigrating(newest);

        context.Database.Migrate();
    }

    /// <summary>What crateside does with the ledger at each start: back it up, import, export.</summary>
    public void Start()
    {
        lock (_gate)
        {
            BackUp();
            Read();
            Export();
        }
    }

    /// <summary>The ledger as it stands.</summary>
    public KeyLedger Read()
    {
        lock (_gate)
        {
            return _snapshot ??= Load();
        }
    }

    /// <summary>The evidence behind a song's latest verdict, or null when it has none.</summary>
    public RunEvidence? EvidenceFor(string song)
    {
        lock (_gate)
        {
            Read();

            using var context = Context();

            return
                context
                    .Runs.AsNoTracking()
                    .Where(run => run.Song == song)
                    .OrderByDescending(run => run.RecordedAt)
                    .FirstOrDefault()
                    is { } latest
                ? RunEvidence.From(latest.ToRecord())
                : null;
        }
    }

    /// <summary>Resolves the video ID to its song first, so the song's latest run wins.</summary>
    public RunEvidence? EvidenceForVideoId(string videoId) =>
        Read().SongFor(videoId) is { } song ? EvidenceFor(song) : null;

    /// <summary>The evidence behind every song's latest verdict, keyed by song identity.</summary>
    public IReadOnlyDictionary<string, RunEvidence> LatestEvidence()
    {
        lock (_gate)
        {
            Read();

            using var context = Context();

            return context
                .Runs.AsNoTracking()
                .AsEnumerable()
                .GroupBy(run => run.Song, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    song => song.Key,
                    song => RunEvidence.From(song.OrderBy(run => run.RecordedAt).Last().ToRecord()),
                    StringComparer.OrdinalIgnoreCase
                );
        }
    }

    /// <summary>
    /// The detectors' JSON behind every song's latest run that has any, keyed by song identity; a latest run with
    /// none is given as <c>{}</c> when <paramref name="withEmpty"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> LatestEvidenceJson(bool withEmpty = false)
    {
        lock (_gate)
        {
            using var context = Context();

            return context
                .Runs.AsNoTracking()
                .Select(run => new
                {
                    run.Song,
                    run.RecordedAt,
                    run.Evidence,
                })
                .AsEnumerable()
                .GroupBy(run => run.Song, StringComparer.OrdinalIgnoreCase)
                .Select(song => song.OrderBy(run => run.RecordedAt).Last())
                .Where(run => withEmpty || run.Evidence is not null)
                .ToDictionary(
                    run => run.Song,
                    run => run.Evidence ?? "{}",
                    StringComparer.OrdinalIgnoreCase
                );
        }
    }

    /// <summary>
    /// Rewrites the evidence of a song's latest run in place, without recording a run: the verdict, its
    /// time and its judging are untouched; a run with no evidence is edited from <c>{}</c>. False when the song has no
    /// such run or <paramref name="edit"/> returns null.
    /// </summary>
    public bool EditLatestEvidence(string song, Func<string, string?> edit)
    {
        lock (_gate)
        {
            using var context = Context();

            var latest = context
                .Runs.Where(run => run.Song == song)
                .OrderByDescending(run => run.RecordedAt)
                .FirstOrDefault();

            if (latest is null || edit(latest.Evidence ?? "{}") is not { } edited)
                return false;

            latest.Evidence = edited;
            context.SaveChanges();

            return true;
        }
    }

    /// <summary>
    /// Rewrites the evidence of every song's latest run that <paramref name="edit"/> returns a new text for, given
    /// the run's time; returns how many were rewritten. No run is recorded.
    /// </summary>
    public int EditEveryLatestEvidence(Func<DateTimeOffset, string, string?> edit)
    {
        lock (_gate)
        {
            using var context = Context();

            var edited = 0;

            foreach (
                var latest in context
                    .Runs.AsEnumerable()
                    .GroupBy(run => run.Song, StringComparer.OrdinalIgnoreCase)
                    .Select(song => song.OrderBy(run => run.RecordedAt).Last())
                    .Where(run => run.Evidence is not null)
            )
                if (edit(latest.RecordedAt, latest.Evidence!) is { } text)
                {
                    latest.Evidence = text;
                    edited++;
                }

            context.SaveChanges();

            return edited;
        }
    }

    /// <summary>Validates and stores a run, or says which field was refused and why.</summary>
    public RecordOutcome Record(RunRecord run)
    {
        lock (_gate)
        {
            using var context = Context();

            var outcome = Add(context, run);

            if (outcome is RecordOutcome.Recorded)
                Export();

            return outcome;
        }
    }

    /// <summary>
    /// Says an upload is the song another upload already is, carrying its verdict over; an upload
    /// the song already has is a skip.
    /// </summary>
    public RestateOutcome Restate(string fromVideoId, string toUrl)
    {
        lock (_gate)
        {
            var ledger = Read();

            if (!YouTubeUrl.TryNormalize(toUrl, out var url))
                return new RestateOutcome.Refused($"{toUrl} is not an http(s) URL.");

            if (ledger.SongFor(fromVideoId) is not { } song)
                return new RestateOutcome.Refused(
                    $"No run recorded for video ID {fromVideoId}. A restatement adds an upload to a "
                        + "song the ledger already knows."
                );

            if (
                YouTubeUrl.TryGetVideoId(url, out var toVideoId)
                && ledger.SongFor(toVideoId) is { } held
                && held.Equals(song, StringComparison.OrdinalIgnoreCase)
            )
                return new RestateOutcome.AlreadyHeld(song);

            using var context = Context();

            if (LatestRunId(context, song) is not { } runId)
                return new RestateOutcome.Refused($"{song} has no run to restate.");

            context.Restatements.Add(
                new RestatementRow
                {
                    RecordedAt = _time.GetUtcNow(),
                    RunId = runId,
                    SourceUrl = url,
                }
            );
            context.SaveChanges();
            _snapshot = null;
            Export();

            return new RestateOutcome.Restated(song);
        }
    }

    /// <summary>A dated copy through SQLite's backup API, keeping the last seven.</summary>
    public void BackUp()
    {
        lock (_gate)
        {
            if (BackupFolder is not { } folder)
                return;

            Directory.CreateDirectory(folder);

            using (
                var backup = new SqliteConnection(
                    ConnectionString(
                        Path.Combine(folder, $"ledger-{_time.GetLocalNow():yyyyMMdd-HHmmss}.db")
                    )
                )
            )
            {
                backup.Open();
                _connection.BackupDatabase(backup);
            }

            foreach (
                var old in Directory
                    .EnumerateFiles(folder, "ledger-*.db")
                    .Order(StringComparer.Ordinal)
                    .SkipLast(BackupsKept)
            )
                File.Delete(old);
        }
    }

    private void BackUpBeforeMigrating(string migration)
    {
        var folder = BackupFolder!;

        Directory.CreateDirectory(folder);

        using var backup = new SqliteConnection(
            ConnectionString(Path.Combine(folder, $"before-{migration}.db"))
        );

        backup.Open();
        _connection.BackupDatabase(backup);
    }

    /// <summary>Every run and restatement as one JSON line each, oldest first.</summary>
    public void Export()
    {
        lock (_gate)
        {
            if (ExportPath is not { } path)
                return;

            using var context = Context();

            var runs = context.Runs.AsNoTracking().ToDictionary(run => run.Id);
            var lines = runs
                .Values.Select(run =>
                    (
                        run.RecordedAt,
                        Line: new ExportLine
                        {
                            Run = run.ToRecord() with { Evidence = null, Lookups = null },
                            Evidence = run.Evidence is { } json
                                ? JsonSerializer.Deserialize(
                                    json,
                                    LedgerStoreJsonContext.Default.JsonElement
                                )
                                : null,
                            Lookups = run.Lookups is { } found
                                ? JsonSerializer.Deserialize(
                                    found,
                                    LedgerStoreJsonContext.Default.JsonElement
                                )
                                : null,
                        }
                    )
                )
                .Concat(
                    context
                        .Restatements.AsNoTracking()
                        .AsEnumerable()
                        .Select(restatement =>
                            (
                                restatement.RecordedAt,
                                Line: new ExportLine
                                {
                                    Restatement = new ExportedRestatement
                                    {
                                        RecordedAt = restatement.RecordedAt,
                                        Song = runs[restatement.RunId].Song,
                                        SourceUrl = restatement.SourceUrl,
                                        Restates = runs[restatement.RunId].RecordedAt,
                                    },
                                }
                            )
                        )
                )
                .OrderBy(line => line.RecordedAt)
                .Select(line =>
                    JsonSerializer.Serialize(line.Line, LedgerStoreJsonContext.Default.ExportLine)
                );

            var temporary = $"{path}.tmp";

            File.WriteAllLines(temporary, lines);
            File.Move(temporary, path, overwrite: true);
        }
    }

    public void Dispose() => _connection.Dispose();

    private LedgerContext Context() => new(_options);

    private RecordOutcome Add(LedgerContext context, RunRecord given)
    {
        var run = RunValidation.Normalize(given);

        if (RunValidation.Refusal(run) is { } refusal)
            return refusal;

        var row = RunRow.From(run);

        context.Runs.Add(row);
        context.SaveChanges();
        _snapshot = null;

        return new RecordOutcome.Recorded(row.Id);
    }

    private static long? LatestRunId(LedgerContext context, string song) =>
        context
            .Runs.Where(run => run.Song == song)
            .OrderByDescending(run => run.RecordedAt)
            .Select(run => (long?)run.Id)
            .FirstOrDefault();

    /// <summary>Every run and restatement, without the evidence, for the lookups.</summary>
    private KeyLedger Load()
    {
        using var context = Context();

        var runs = context
            .Runs.AsNoTracking()
            .Select(run => new
            {
                run.Id,
                run.RecordedAt,
                run.Song,
                run.Artist,
                run.Title,
                run.SourceUrl,
                run.Confidence,
                run.Tonic,
                run.Mode,
                run.Mixed,
                run.OldBpm,
                run.Sections,
            })
            .AsEnumerable()
            .ToDictionary(
                row => row.Id,
                row => new LedgerRun
                {
                    Timestamp = row.RecordedAt,
                    Song = row.Song,
                    Artist = row.Artist,
                    Title = row.Title,
                    SourceUrl = row.SourceUrl,
                    Confidence = row.Confidence,
                    Center = row is { Tonic: { } tonic, Mode: { } mode } ? new(tonic, mode) : null,
                    Mixed = row.Mixed,
                    Bpm = row.OldBpm,
                    Sections =
                    [
                        .. row
                            .Sections.OrderBy(section => section.Position)
                            .Select(section => new Section(
                                section.Label,
                                new(section.Tonic, section.Mode),
                                section.Start
                            )),
                    ],
                }
            );

        var restated = context
            .Restatements.AsNoTracking()
            .AsEnumerable()
            .Select(restatement =>
                runs[restatement.RunId] with
                {
                    Timestamp = restatement.RecordedAt,
                    SourceUrl = restatement.SourceUrl,
                    Restates = runs[restatement.RunId].Timestamp,
                }
            );

        return new KeyLedger([.. runs.Values.Concat(restated).OrderBy(run => run.Timestamp)]);
    }

    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pooling would hold the file open after a dispose, and a backup's copy with it.
            Pooling = false,
        }.ToString();
}

internal sealed record ExportLine
{
    public RunRecord? Run { get; init; }

    /// <summary>The detectors' report, as JSON rather than a string of it.</summary>
    public JsonElement? Evidence { get; init; }

    /// <summary>What the lookup found, as JSON rather than a string of it.</summary>
    public JsonElement? Lookups { get; init; }

    public ExportedRestatement? Restatement { get; init; }
}

internal sealed record ExportedRestatement
{
    public required DateTimeOffset RecordedAt { get; init; }
    public required string Song { get; init; }
    public required string SourceUrl { get; init; }

    /// <summary>When the run whose verdict this carries over was recorded.</summary>
    public required DateTimeOffset Restates { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(ExportLine))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class LedgerStoreJsonContext : JsonSerializerContext { }
