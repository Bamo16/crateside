using System.Globalization;
using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>Samples whose sidecar went to the Recycle Bin so Live analyzes them afresh, and the warp to put back once it has (#97).</summary>
public sealed class LiveAnalyses
{
    private readonly Lock _lock = new();
    private readonly string? _path;
    private readonly Dictionary<string, AwaitedAnalysis> _entries;

    public LiveAnalyses(CratesidePaths paths)
        : this(paths.LiveAnalysesFile) { }

    /// <summary>Keeps nothing on disk when <paramref name="path"/> is null.</summary>
    internal LiveAnalyses(string? path = null)
    {
        _path = path;
        _entries = Load(path);
    }

    public AwaitedAnalysis? Of(string audioPath)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(Path.GetFullPath(audioPath));
        }
    }

    public IReadOnlyList<(string AudioPath, AwaitedAnalysis Awaited)> All()
    {
        lock (_lock)
        {
            return [.. _entries.Select(pair => (pair.Key, pair.Value))];
        }
    }

    public void Await(string audioPath, AwaitedAnalysis awaited) =>
        Change(entries => entries[Path.GetFullPath(audioPath)] = awaited);

    public void Forget(string audioPath) =>
        Change(entries => entries.Remove(Path.GetFullPath(audioPath)));

    private void Change(Action<Dictionary<string, AwaitedAnalysis>> change)
    {
        lock (_lock)
        {
            change(_entries);

            if (_path is not null)
                LiveAnalysesJsonContext.Default.DictionaryStringAwaitedAnalysis.WriteFile(
                    _path,
                    _entries
                );
        }
    }

    private static Dictionary<string, AwaitedAnalysis> Load(string? path) =>
        new(
            path is not null
            && LiveAnalysesJsonContext.Default.DictionaryStringAwaitedAnalysis.ReadFileOrDefault(
                path
            )
                is { } entries
                ? entries
                : [],
            StringComparer.OrdinalIgnoreCase
        );
}

/// <summary>A sidecar sent to the Recycle Bin for Live to analyze again, and what to put back once it has.</summary>
/// <param name="Backup">The sidecar as it was before it went to the Recycle Bin, whose clip and warp are put back.</param>
/// <param name="From">The sibling it was borrowed from, or null.</param>
/// <param name="WasStarter">Whether it held a starter warp crateside wrote.</param>
/// <param name="WasUnwarped">Whether it held a clip crateside saved with Warp off.</param>
public sealed record AwaitedAnalysis(
    string Backup,
    string? From,
    bool WasStarter,
    DateTime Sent,
    bool WasUnwarped = false
)
{
    public WrittenStarter? Starter { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(Dictionary<string, AwaitedAnalysis>))]
internal sealed partial class LiveAnalysesJsonContext : JsonSerializerContext { }

/// <summary>Copies a sidecar aside before crateside replaces or recycles it.</summary>
public sealed class SidecarBackups
{
    private const string Stamp = "yyyy-MM-dd HHmmss";

    private readonly string _root;
    private readonly TimeProvider _time;

    public SidecarBackups(CratesidePaths paths, TimeProvider time)
        : this(paths.WarpBackups, time) { }

    internal SidecarBackups(string root, TimeProvider time)
    {
        _root = root;
        _time = time;
    }

    /// <summary>The backup's path.</summary>
    public string Keep(string sidecarPath)
    {
        Directory.CreateDirectory(_root);

        var backup = Path.Combine(
            _root,
            $"{_time.GetLocalNow().ToString(Stamp, CultureInfo.InvariantCulture)} {Path.GetFileName(sidecarPath)}"
        );
        File.Copy(sidecarPath, backup, overwrite: true);

        return backup;
    }

    /// <summary>The copies kept of a sidecar, oldest first.</summary>
    public IReadOnlyList<string> Of(string sidecarPath)
    {
        var name = $" {Path.GetFileName(sidecarPath)}";

        return Directory.Exists(_root)
            ?
            [
                .. Directory
                    .EnumerateFiles(_root)
                    .Where(backup =>
                        Path.GetFileName(backup) is var file
                        && file.Length == Stamp.Length + name.Length
                        && file.EndsWith(name, StringComparison.OrdinalIgnoreCase)
                    )
                    .Order(StringComparer.Ordinal),
            ]
            : [];
    }
}

/// <summary>The two things the warp view lets the user do to a stem's sidecar, and putting a warp back once Live has analyzed (#97).</summary>
public sealed class WarpActions(
    SidecarWriter writer,
    WrittenWarps written,
    LiveAnalyses waiting,
    SidecarBackups backups,
    TimeProvider time
)
{
    /// <summary>Writes the siblings' warp over the stem's again, backing up what it held.</summary>
    public async Task<WarpOutcome> ReapplyAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    )
    {
        try
        {
            if (File.Exists(StemFileName.AnalysisFor(target)))
                backups.Keep(StemFileName.AnalysisFor(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WarpOutcome.Failed(ex.Message);
        }

        return await writer.ReplaceAsync(target, authorities, sameKind, ct);
    }

    /// <summary>Backs up the stem's sidecar and sends it to the Recycle Bin, so Live analyzes the stem the next time it is opened.</summary>
    public LetLiveOutcome LetLiveAnalyze(string target)
    {
        var sidecar = StemFileName.AnalysisFor(target);

        if (!File.Exists(sidecar))
            return new LetLiveOutcome.NoSidecar();

        try
        {
            var crateside = written.Of(target);
            var backup = backups.Keep(sidecar);

            RecycleBin.Remove(sidecar);
            waiting.Await(
                target,
                new AwaitedAnalysis(
                    backup,
                    crateside?.From,
                    crateside is { IsStarter: true },
                    time.GetUtcNow().UtcDateTime,
                    crateside is { IsUnwarped: true }
                )
                {
                    Starter = crateside?.Starter,
                }
            );

            return new LetLiveOutcome.Sent();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LetLiveOutcome.Failed(ex.Message);
        }
    }

    /// <summary>Puts each awaited warp back into the sidecar Live has written since, keeping Live's transients; the samples put back.</summary>
    public IReadOnlyList<string> PutBack()
    {
        List<string> putBack = [];

        foreach (var (target, awaited) in waiting.All())
        {
            var sidecar = StemFileName.AnalysisFor(target);

            try
            {
                if (!AbleKit.Analysis.AnalysisFile.TryRead(sidecar, out var fresh))
                    continue;

                // Saved in Live before crateside got to it: the warp is the user's now.
                if (fresh.IsDefaultClipSaved || !File.Exists(awaited.Backup))
                {
                    waiting.Forget(target);

                    continue;
                }

                var before = AbleKit.Analysis.AnalysisFile.Read(awaited.Backup);

                (
                    fresh with
                    {
                        Clip = before.Clip,
                        Warp = before.Warp,
                        IsDefaultClipSaved = before.IsDefaultClipSaved,
                    }
                ).Write(sidecar);

                if (awaited.From is { } from)
                    written.RecordBorrowed(target, from);
                else if (awaited.WasStarter)
                    written.RecordStarter(target, awaited.Starter);
                else if (awaited.WasUnwarped)
                    written.RecordUnwarped(target);

                waiting.Forget(target);
                putBack.Add(target);
            }
            catch (Exception ex)
                when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Live may still be writing it; tried again next time.
            }
        }

        return putBack;
    }
}

public abstract record LetLiveOutcome
{
    private LetLiveOutcome() { }

    public sealed record Sent : LetLiveOutcome;

    public sealed record NoSidecar : LetLiveOutcome;

    public sealed record Failed(string Error) : LetLiveOutcome;
}
