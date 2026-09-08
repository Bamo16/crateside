using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration;

/// <summary>
/// crateside's record of the work it has run, in <c>%APPDATA%\crateside\jobs.jsonl</c>.
/// </summary>
/// <remarks>
/// Append-only, one JSON object per line, latest line wins for an id.
/// </remarks>
/// <param name="hub">
/// Told about every recorded change, so a page can update the one row that moved. Optional.
/// </param>
public sealed class JobStore(string? path = null, TimeProvider? time = null, StageHub? hub = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path = path ?? DefaultPath();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    private Dictionary<string, TrackJob>? _cached;

    /// <summary>Where the file lives, for diagnostics and for tests to assert against.</summary>
    public string Path => _path;

    /// <summary>Every job crateside knows about, most recently updated first.</summary>
    public IReadOnlyList<TrackJob> All()
    {
        lock (_gate)
            return [.. Projection().Values.OrderByDescending(j => j.UpdatedAt)];
    }

    /// <summary>The current state of one job, if crateside has ever run this stage for it.</summary>
    public bool TryGet(string videoId, JobStage stage, [NotNullWhen(true)] out TrackJob? job)
    {
        lock (_gate)
            return Projection().TryGetValue(IdFor(videoId, stage), out job);
    }

    /// <summary>
    /// Jobs at a stage that crateside still believes are in flight. On startup this is the
    /// adoption list: anything here outlived the process that started it.
    /// </summary>
    public IReadOnlyList<TrackJob> Open(JobStage stage)
    {
        lock (_gate)
            return [.. Projection().Values.Where(j => j.Stage == stage && j.IsOpen)];
    }

    /// <summary>
    /// Puts a track a stage has given up on back in line.
    /// </summary>
    /// <remarks>
    /// Records <see cref="JobStatus.Cancelled"/> rather than clearing anything: the strikes stay
    /// on the record, but stop being a verdict on the track.
    /// </remarks>
    public TrackJob Forgive(string videoId, JobStage stage) =>
        Record(videoId, stage, JobStatus.Cancelled, error: "put back in line by hand");

    /// <summary>
    /// Appends an update and returns the job as it now stands. The only way state changes —
    /// nothing rewrites or removes a line.
    /// </summary>
    public TrackJob Record(
        string videoId,
        JobStage stage,
        JobStatus status,
        int? processId = null,
        DateTimeOffset? processStartedAt = null,
        string? error = null
    )
    {
        var update = new JobUpdate
        {
            Id = IdFor(videoId, stage),
            VideoId = videoId,
            Stage = stage,
            Status = status,
            Timestamp = _time.GetUtcNow(),
            ProcessId = processId,
            ProcessStartedAt = processStartedAt,
            Error = error,
        };

        TrackJob job;

        lock (_gate)
        {
            var projection = Projection();

            Append(update);

            job = projection[update.Id] = Apply(projection.GetValueOrDefault(update.Id), update);
        }

        // Outside the lock on purpose: a subscriber that reads the store would deadlock the
        // writer it is reacting to.
        hub?.Publish(new StageChange { Stage = stage, VideoId = videoId });

        return job;
    }

    /// <summary>Drops the in-memory projection so the next read comes off disk again.</summary>
    public void Invalidate()
    {
        lock (_gate)
            _cached = null;
    }

    private static string IdFor(string videoId, JobStage stage) => $"{videoId}:{stage}";

    private static string DefaultPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "crateside",
            "jobs.jsonl"
        );

    /// <summary>
    /// Folds one update onto what came before. Strikes accumulate here rather than being
    /// written down, so the count can never disagree with the lines it came from.
    /// </summary>
    private static TrackJob Apply(TrackJob? previous, JobUpdate update) =>
        new()
        {
            Id = update.Id,
            VideoId = update.VideoId,
            Stage = update.Stage,
            Status = update.Status,
            UpdatedAt = update.Timestamp,
            ProcessId = update.ProcessId,
            ProcessStartedAt = update.ProcessStartedAt,
            Error = update.Status switch
            {
                JobStatus.Failed => update.Error,
                JobStatus.Succeeded => null,
                _ => update.Error ?? previous?.Error,
            },
            Strikes = (previous?.Strikes ?? 0) + (update.Status is JobStatus.Failed ? 1 : 0),
        };

    private Dictionary<string, TrackJob> Projection() => _cached ??= ReadFile();

    private Dictionary<string, TrackJob> ReadFile()
    {
        var jobs = new Dictionary<string, TrackJob>(StringComparer.Ordinal);

        if (!File.Exists(_path))
            return jobs;

        try
        {
            using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if (line.AsSpan().IsWhiteSpace())
                    continue;

                // A half-written trailing line is expected, not exceptional.
                JobUpdate? update;

                try
                {
                    update = JsonSerializer.Deserialize<JobUpdate>(line, SerializerOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (update is { Id.Length: > 0 })
                    jobs[update.Id] = Apply(jobs.GetValueOrDefault(update.Id), update);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable store must not take the app down with it.
        }

        return jobs;
    }

    private void Append(JobUpdate update)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);

        File.AppendAllText(
            _path,
            JsonSerializer.Serialize(update, SerializerOptions) + Environment.NewLine
        );
    }
}

/// <summary>
/// One appended line: a single state change to a job. Lines are never edited or removed, so the
/// file is a history and <see cref="TrackJob"/> is the fold over it.
/// </summary>
public sealed record JobUpdate
{
    public required string Id { get; init; }
    public required string VideoId { get; init; }
    public required JobStage Stage { get; init; }
    public required JobStatus Status { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartedAt { get; init; }
    public string? Error { get; init; }
}
