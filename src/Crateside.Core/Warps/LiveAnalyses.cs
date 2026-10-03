using System.Text.Json.Serialization;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>Samples whose sidecar went to the Recycle Bin so Live analyses them afresh, and the warp to put back once it has (#97).</summary>
public sealed class LiveAnalyses(string? path = null)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, AwaitedAnalysis> _entries = Load(path);

    public static string DefaultPath { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "live-analyses.json");

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

            if (path is not null)
                LiveAnalysesJsonContext.Default.DictionaryStringAwaitedAnalysis.WriteFile(
                    path,
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

/// <param name="Backup">The sidecar as it was before it went to the Recycle Bin, whose clip and warp are put back.</param>
/// <param name="From">The sibling it was borrowed from, or null.</param>
/// <param name="WasStarter">Whether it held a starter warp crateside wrote.</param>
public sealed record AwaitedAnalysis(string Backup, string? From, bool WasStarter, DateTime Sent);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, AwaitedAnalysis>))]
internal sealed partial class LiveAnalysesJsonContext : JsonSerializerContext { }

/// <summary>Copies a sidecar aside before crateside replaces or recycles it.</summary>
public sealed class SidecarBackups(string root, TimeProvider time)
{
    public static string DefaultRoot { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "backups", "warps");

    /// <summary>The backup's path.</summary>
    public string Keep(string sidecarPath)
    {
        Directory.CreateDirectory(root);

        var backup = Path.Combine(
            root,
            $"{time.GetLocalNow():yyyy-MM-dd HHmmss} {Path.GetFileName(sidecarPath)}"
        );
        File.Copy(sidecarPath, backup, overwrite: true);

        return backup;
    }
}

/// <summary>The two things the warp view lets the user do to a stem's sidecar, and putting a warp back once Live has analysed (#97).</summary>
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

    /// <summary>Backs up the stem's sidecar and sends it to the Recycle Bin, so Live analyses the stem the next time it is opened.</summary>
    public LetLiveOutcome LetLiveAnalyse(string target)
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
                    time.GetUtcNow().UtcDateTime
                )
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
                    written.RecordStarter(target);

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
