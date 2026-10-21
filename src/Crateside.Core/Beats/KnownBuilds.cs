using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Beats;

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
/// The beatdetect builds crateside knows and the one it pins for beatdetect alone; the ffmpeg on
/// PATH is untouched (#102). Kept across restarts.
/// </summary>
public sealed class KnownBuilds(IProcessRunner processes, CratesidePaths paths)
{
    private readonly Lock _gate = new();
    private KnownBuildsFile _state = Load(paths.KnownBuildsFile);

    public IReadOnlyList<KnownBuild> All
    {
        get
        {
            lock (_gate)
                return _state.Builds;
        }
    }

    /// <summary>The sha256 of the pinned build, or null when none is.</summary>
    public string? PinnedSha256
    {
        get
        {
            lock (_gate)
                return _state.Pinned;
        }
    }

    public KnownBuild? Pinned
    {
        get
        {
            lock (_gate)
                return _state is { Pinned: { } sha }
                    ? _state.Builds.FirstOrDefault(known => IsSame(known.Build.Sha256, sha))
                    : null;
        }
    }

    /// <summary>Identifies the executable and remembers it; the first build ever added is pinned.</summary>
    public async Task<BuildAddOutcome> AddAsync(string executable, CancellationToken ct)
    {
        if (
            Path.GetFullPath(executable) is var full
            && await FfmpegBuild.IdentifyAsync(processes, full, ct) is not { } build
        )
            return new BuildAddOutcome.NotFfmpeg(executable);

        var known = new KnownBuild(Path.GetFullPath(executable), build);

        lock (_gate)
        {
            if (_state.Builds.FirstOrDefault(c => IsSame(c.Build.Sha256, build.Sha256)) is { } same)
            {
                if (same.Path == known.Path)
                    return new BuildAddOutcome.AlreadyKnown(same);

                _state = _state with
                {
                    Builds = [.. _state.Builds.Select(c => c == same ? known : c)],
                };
                Save();

                return new BuildAddOutcome.AlreadyKnown(known);
            }

            _state = _state with
            {
                Builds = [.. _state.Builds, known],
                Pinned = _state.Pinned ?? build.Sha256,
            };
            Save();

            return new BuildAddOutcome.Added(known);
        }
    }

    /// <summary>False when no known build has that hash.</summary>
    public bool Pin(string sha256)
    {
        lock (_gate)
        {
            if (!_state.Builds.Any(known => IsSame(known.Build.Sha256, sha256)))
                return false;

            _state = _state with { Pinned = sha256 };
            Save();

            return true;
        }
    }

    /// <summary>Forgets a build that is not the pinned one; false when it is pinned or unknown.</summary>
    public bool Remove(string sha256)
    {
        lock (_gate)
        {
            if (
                _state.Pinned is { } pinned && IsSame(pinned, sha256)
                || !_state.Builds.Any(known => IsSame(known.Build.Sha256, sha256))
            )
                return false;

            _state = _state with
            {
                Builds = [.. _state.Builds.Where(known => !IsSame(known.Build.Sha256, sha256))],
            };
            Save();

            return true;
        }
    }

    /// <summary>On a machine that knows no build yet, adds the configured one and pins it.</summary>
    public async Task SeedAsync(CancellationToken ct)
    {
        if (All is [])
            await AddAsync(paths.BeatdetectFfmpeg, ct);
    }

    private void Save()
    {
        try
        {
            BeatJsonContext.Default.KnownBuildsFile.WriteFile(paths.KnownBuildsFile, _state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsSame(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static KnownBuildsFile Load(string path) =>
        BeatJsonContext.Default.KnownBuildsFile.ReadFileOrDefault(path) is { } file
            ? file
            : new KnownBuildsFile([], null);
}

internal sealed record KnownBuildsFile(IReadOnlyList<KnownBuild> Builds, string? Pinned);
