using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Ledger;

/// <summary>
/// crateside's ledger of key verdicts, in SQLite (docs/ledger.md): every read and write goes
/// through here, and keytag's JSONL is imported once, into an empty ledger (ADR 0055).
/// </summary>
public sealed class LedgerStore : IDisposable
{
    public const string InMemory = ":memory:";

    private const int BackupsKept = 7;

    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LedgerContext> _options;
    private readonly string _databasePath;
    private readonly string? _keytagLedger;
    private readonly TimeProvider _time;
    private (long Length, DateTime Written)? _keytagSeen;
    private bool _holdsRuns;
    private KeyLedger? _snapshot;

    /// <summary>Lines of keytag's ledger the import could not take, and why.</summary>
    public IReadOnlyList<(string Line, string Problem)> ImportRefusals { get; private set; } = [];

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

    /// <param name="databasePath">The SQLite file, or <see cref="InMemory"/>, which keeps no backups or export.</param>
    /// <param name="keytagLedger">keytag's <c>runs.jsonl</c> to import from while the ledger is empty, or null for none.</param>
    public LedgerStore(string databasePath, string? keytagLedger, TimeProvider time)
    {
        _databasePath = databasePath;
        _keytagLedger = keytagLedger;
        _time = time;

        if (IsFile && Path.GetDirectoryName(Path.GetFullPath(databasePath)) is { } folder)
            Directory.CreateDirectory(folder);

        // One connection for the store's life: an in-memory database lives only as long as it.
        _connection = new SqliteConnection(ConnectionString(databasePath));
        _connection.Open();
        _options = new DbContextOptionsBuilder<LedgerContext>().UseSqlite(_connection).Options;

        using var context = Context();

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

    /// <summary>The ledger as it stands, with keytag's history imported first if it holds no run yet.</summary>
    public KeyLedger Read()
    {
        lock (_gate)
        {
            if (ImportKeytag())
                Export();

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

    /// <summary>Validates and stores a run, or says which field was refused and why.</summary>
    public RecordOutcome Record(RunRecord run)
    {
        lock (_gate)
        {
            using var context = Context();

            var outcome = Add(context, run, keytagLine: null);

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
                            Run = run.ToRecord() with { Evidence = null },
                            Evidence = run.Evidence is { } json
                                ? JsonSerializer.Deserialize(
                                    json,
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

    /// <summary>Imports keytag's history into a ledger that holds no run yet; true when anything was.</summary>
    private bool ImportKeytag()
    {
        if (_keytagLedger is null || _holdsRuns)
            return false;

        var file = new FileInfo(_keytagLedger);
        (long, DateTime)? seen = file.Exists ? (file.Length, file.LastWriteTimeUtc) : null;

        if (seen == _keytagSeen)
            return false;

        _keytagSeen = seen;

        using var context = Context();

        if (context.Runs.Any())
        {
            _holdsRuns = true;

            return false;
        }

        using var transaction = context.Database.BeginTransaction();

        var held = context
            .Runs.Where(run => run.KeytagLine != null)
            .Select(run => run.KeytagLine)
            .Concat(
                context
                    .Restatements.Where(restatement => restatement.KeytagLine != null)
                    .Select(restatement => restatement.KeytagLine)
            )
            .AsEnumerable()
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        List<(string, string)> refusals = [];
        var imported = 0;

        HashSet<string> read = new(StringComparer.Ordinal);

        foreach (var line in KeytagImport.Read(_keytagLedger))
        {
            if (!read.Add(line.Timestamp!))
            {
                refusals.Add(
                    (line.Timestamp!, "a second line with this timestamp, which names a line")
                );

                continue;
            }

            if (held.Contains(line.Timestamp!))
                continue;

            if (Import(context, line) is { } problem)
                refusals.Add((line.Timestamp!, problem));
            else
                imported++;
        }

        transaction.Commit();
        ImportRefusals = refusals;

        return imported > 0;
    }

    /// <summary>One keytag line into the ledger, or why it was not taken.</summary>
    private string? Import(LedgerContext context, KeytagLine line)
    {
        if (line.Restates is not { } restates)
            return
                Add(context, KeytagImport.ToRun(line), line.Timestamp)
                    is RecordOutcome.Refused(var field, var problem)
                ? $"{field}: {problem}"
                : null;

        if (
            context
                .Runs.Where(run => run.KeytagLine == restates)
                .Select(run => new { run.Id, run.Song })
                .FirstOrDefault()
            is not { } restated
        )
            return $"restates {restates}, which is not a recorded run";

        if (
            line.SourceUrl is not { } url
            || !RunValidation.IsHttp(url)
            || !restated.Song.Equals(line.Song, StringComparison.OrdinalIgnoreCase)
        )
            return "a restatement needs an http(s) URL and the restated run's song";

        context.Restatements.Add(
            new RestatementRow
            {
                RecordedAt = DateTimeOffset.Parse(line.Timestamp!, CultureInfo.InvariantCulture),
                RunId = restated.Id,
                SourceUrl = url,
                KeytagLine = line.Timestamp,
            }
        );
        context.SaveChanges();
        _snapshot = null;

        return null;
    }

    private RecordOutcome Add(LedgerContext context, RunRecord given, string? keytagLine)
    {
        var run = RunValidation.Normalize(given);

        if (RunValidation.Refusal(run) is { } refusal)
            return refusal;

        var row = RunRow.From(run, keytagLine);

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
                    Centre = row is { Tonic: { } tonic, Mode: { } mode } ? new(tonic, mode) : null,
                    Mixed = row.Mixed,
                    Bpm = row.OldBpm,
                    Sections =
                    [
                        .. row
                            .Sections.OrderBy(section => section.Position)
                            .Select(section => new Section(
                                section.Label,
                                new(section.Tonic, section.Mode)
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
