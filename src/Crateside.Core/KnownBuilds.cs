using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Loop;
using Humanizer;

namespace Crateside.Core;

/// <summary>An ffmpeg build crateside has been pointed at, and where its executable was when it was.</summary>
public sealed record KnownBuild(string Path, FfmpegBuild Build);

public abstract record BuildAddOutcome
{
    private BuildAddOutcome() { }

    public sealed record Added(KnownBuild Known) : BuildAddOutcome;

    public sealed record AlreadyKnown(KnownBuild Known) : BuildAddOutcome;

    public sealed record NotFfmpeg(string Path) : BuildAddOutcome;
}

/// <summary>
/// The builds crateside knows for each filter and the one it pins for that filter alone; the ffmpeg
/// on PATH is untouched (#102, ADR 0066). Kept across restarts.
/// </summary>
public sealed class KnownBuilds(
    IProcessRunner processes,
    CratesidePaths paths,
    BuildFetcher? fetcher = null
)
{
    private readonly Lock _gate = new();
    private readonly Lock _seeding = new();
    private readonly ConcurrentDictionary<FfmpegFilter, SeedingNote> _notes = [];
    private Dictionary<string, FilterBuilds> _state = Load(paths.KnownBuildsFile);
    private Task? _seeded;

    /// <summary>How long a failed fetch waits before it is tried again.</summary>
    public TimeSpan RetryAfter { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Why detection is held on a build, or null when none is.</summary>
    public string? Waiting =>
        _notes.IsEmpty
            ? null
            : string.Join(" ", _notes.OrderBy(note => note.Key).Select(note => note.Value.Text));

    public IReadOnlyList<KnownBuild> All(FfmpegFilter filter)
    {
        lock (_gate)
            return Of(filter).Builds;
    }

    public KnownBuild? Pinned(FfmpegFilter filter)
    {
        lock (_gate)
            return Of(filter) is { Pinned: { } sha } builds
                ? builds.Builds.FirstOrDefault(known => IsSame(known.Build.Sha256, sha))
                : null;
    }

    /// <summary>The pinned build's executable, else the configured one the filter would be seeded from.</summary>
    public string FileNameFor(FfmpegFilter filter) => Pinned(filter)?.Path ?? Seed(filter);

    /// <summary>Identifies the executable, a name on PATH included, and remembers it; the filter's first build is pinned.</summary>
    public async Task<BuildAddOutcome> AddAsync(
        FfmpegFilter filter,
        string executable,
        CancellationToken ct
    )
    {
        if (
            FfmpegBuild.Located(executable) is not { Exists: true, FullName: var full }
            || await FfmpegBuild.IdentifyAsync(processes, full, filter, ct) is not { } build
        )
            return new BuildAddOutcome.NotFfmpeg(executable);

        var known = new KnownBuild(full, build);

        lock (_gate)
        {
            var builds = Of(filter);

            if (builds.Builds.FirstOrDefault(c => IsSame(c.Build.Sha256, build.Sha256)) is { } same)
            {
                if (same.Path == known.Path)
                    return new BuildAddOutcome.AlreadyKnown(same);

                Set(
                    filter,
                    builds with
                    {
                        Builds = [.. builds.Builds.Select(c => c == same ? known : c)],
                    }
                );

                return new BuildAddOutcome.AlreadyKnown(known);
            }

            Set(filter, new FilterBuilds([.. builds.Builds, known], builds.Pinned ?? build.Sha256));

            return new BuildAddOutcome.Added(known);
        }
    }

    /// <summary>False when the filter knows no build with that hash.</summary>
    public bool Pin(FfmpegFilter filter, string sha256)
    {
        lock (_gate)
        {
            var builds = Of(filter);

            if (!builds.Builds.Any(known => IsSame(known.Build.Sha256, sha256)))
                return false;

            Set(filter, builds with { Pinned = sha256 });

            return true;
        }
    }

    /// <summary>Forgets a build that is not the pinned one; false when it is pinned or unknown.</summary>
    public bool Remove(FfmpegFilter filter, string sha256)
    {
        lock (_gate)
        {
            var builds = Of(filter);

            if (
                builds.Pinned is { } pinned && IsSame(pinned, sha256)
                || !builds.Builds.Any(known => IsSame(known.Build.Sha256, sha256))
            )
                return false;

            Set(
                filter,
                builds with
                {
                    Builds = [.. builds.Builds.Where(known => !IsSame(known.Build.Sha256, sha256))],
                }
            );

            return true;
        }
    }

    /// <summary>Starts seeding in the background, once, and retries a failed fetch until every filter has its build.</summary>
    public Task StartSeeding(CancellationToken ct)
    {
        lock (_seeding)
            return _seeded ??= Task.Run(() => SeedUntilDoneAsync(ct), CancellationToken.None);
    }

    /// <summary>Completes once every filter has its build, starting the seeding if nothing has.</summary>
    public Task WhenSeededAsync(CancellationToken ct) =>
        StartSeeding(CancellationToken.None).WaitAsync(ct);

    /// <summary>
    /// For each filter that knows no build yet, adds the configured one and pins it; where nothing is
    /// configured, the built-in build is fetched and pinned instead (#163). A build pinned any other way
    /// is never replaced, and a built-in pin left by an older crateside moves to the current one. The
    /// filters are seeded side by side.
    /// </summary>
    public Task SeedAsync(CancellationToken ct) =>
        Task.WhenAll(Enum.GetValues<FfmpegFilter>().Select(filter => SeedAsync(filter, ct)));

    private async Task SeedUntilDoneAsync(CancellationToken ct)
    {
        while (true)
        {
            await SeedAsync(ct);

            if (!_notes.Values.Any(note => note.Failed))
                return;

            await Task.Delay(RetryAfter, ct);
        }
    }

    private async Task SeedAsync(FfmpegFilter filter, CancellationToken ct)
    {
        if (await SeedBuiltInAsync(filter, ct) is { } known)
            Pin(filter, known.Build.Sha256);
        else if (All(filter) is [])
        {
            await AddAsync(filter, Seed(filter), ct);

            if (All(filter) is not [])
                _notes.TryRemove(filter, out _);
        }
    }

    private async Task<KnownBuild?> SeedBuiltInAsync(FfmpegFilter filter, CancellationToken ct)
    {
        if (
            fetcher?.PinFor(filter) is not { } builtIn
            || !IsUnconfigured(Seed(filter))
            || !Wants(builtIn)
        )
        {
            _notes.TryRemove(filter, out _);

            return null;
        }

        var name = FfmpegBuild.NameOf(filter);

        _notes[filter] = new(
            $"Fetching the {name} build ({builtIn.Tag}); detection that needs it starts when it is ready.",
            Failed: false
        );

        var error = "the fetched executable is not an ffmpeg with the filter";

        switch (await fetcher.EnsureAsync(builtIn, ct))
        {
            case BuildFetchOutcome.Ready(var fileName):
                if (KnownOf(await AddAsync(filter, fileName, ct)) is { } added)
                {
                    _notes.TryRemove(filter, out _);

                    return added;
                }

                break;
            case BuildFetchOutcome.Failed(var message):
                error = message;

                break;
        }

        _notes[filter] = new(
            $"Could not fetch the {name} build: {error}. Detection that needs it is held; trying again in {RetryAfter.Humanize()}.",
            Failed: true
        );

        return null;
    }

    private static KnownBuild? KnownOf(BuildAddOutcome outcome) =>
        outcome switch
        {
            BuildAddOutcome.Added(var known) => known,
            BuildAddOutcome.AlreadyKnown(var known) => known,
            _ => null,
        };

    // Nothing pinned, or a pin that was fetched and has since been superseded.
    private bool Wants(BuiltInBuild builtIn) =>
        Pinned(builtIn.Filter) is not { } pinned
        || fetcher!.Fetched(pinned.Path) && !IsSame(pinned.Build.Sha256, builtIn.ExecutableSha256);

    private static bool IsUnconfigured(string seed) => seed is "" or "ffmpeg";

    private string Seed(FfmpegFilter filter) =>
        filter switch
        {
            FfmpegFilter.Beatdetect => paths.BeatdetectFfmpeg,
            FfmpegFilter.Keydetect => paths.KeydetectFfmpeg,
            _ => throw new UnreachableException(),
        };

    private FilterBuilds Of(FfmpegFilter filter) =>
        _state.TryGetValue(FfmpegBuild.NameOf(filter), out var builds)
            ? builds
            : new FilterBuilds([], null);

    private void Set(FfmpegFilter filter, FilterBuilds builds)
    {
        _state = new(_state, StringComparer.Ordinal) { [FfmpegBuild.NameOf(filter)] = builds };

        try
        {
            BuildsJsonContext.Default.KnownBuildsFile.WriteFile(
                paths.KnownBuildsFile,
                new KnownBuildsFile { Filters = _state }
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsSame(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    /// <summary>A file from before keydetect had a pin holds beatdetect's builds at its top level.</summary>
    private static Dictionary<string, FilterBuilds> Load(string path) =>
        BuildsJsonContext.Default.KnownBuildsFile.ReadFileOrDefault(path) switch
        {
            { Filters: { } filters } => new(filters, StringComparer.Ordinal),
            { Builds: { } builds, Pinned: var pinned } => new(StringComparer.Ordinal)
            {
                [FfmpegBuild.NameOf(FfmpegFilter.Beatdetect)] = new(builds, pinned),
            },
            _ => new(StringComparer.Ordinal),
        };
}

internal sealed record SeedingNote(string Text, bool Failed);

internal sealed record FilterBuilds(IReadOnlyList<KnownBuild> Builds, string? Pinned);

internal sealed record KnownBuildsFile
{
    public IReadOnlyDictionary<string, FilterBuilds>? Filters { get; init; }

    public IReadOnlyList<KnownBuild>? Builds { get; init; }

    public string? Pinned { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(KnownBuildsFile))]
[JsonSerializable(typeof(BuildInfo))]
internal sealed partial class BuildsJsonContext : JsonSerializerContext { }
