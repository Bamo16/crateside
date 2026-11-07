using System.Diagnostics;
using System.Text.Json.Serialization;
using Crateside.Core.Orchestration.Loop;

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
public sealed class KnownBuilds(IProcessRunner processes, CratesidePaths paths)
{
    private readonly Lock _gate = new();
    private Dictionary<string, FilterBuilds> _state = Load(paths.KnownBuildsFile);

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

    /// <summary>For each filter that knows no build yet, adds the configured one and pins it.</summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        foreach (var filter in Enum.GetValues<FfmpegFilter>())
        {
            if (All(filter) is [])
                await AddAsync(filter, Seed(filter), ct);
        }
    }

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
