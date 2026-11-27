using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// Keeps the warps saved on the stems a re-cut removes, so the new stems can be given them when
/// they are handed off (ADR 0103). Each is a copy of the sidecar under a stand-in name for its preset.
/// </summary>
public sealed class KeptWarps(CratesidePaths paths, WrittenWarps written)
{
    private const string SourceName = "(source)";

    /// <summary>Copies the stem's sidecar if it holds a warp the user saved, or crateside's copy of one lent to it (#235); a stem without one is left alone.</summary>
    public void Keep(string audioPath)
    {
        if (
            StemTagReader.Read(audioPath)?.Provenance
                is not { IsAdopted: false, SourceTrackKey: { } trackKey } provenance
            || written.Of(audioPath) is { From: null }
            || !IsSaved(audioPath)
        )
            return;

        var preset = provenance.Preset ?? StemFileName.PresetOf(audioPath);
        var kept = Path.Combine(
            paths.KeptWarpsFolder,
            trackKey,
            $"{StemFileName.FileSafe(preset ?? SourceName)}.flac.asd"
        );

        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.Copy(StemFileName.AnalysisFor(audioPath), kept, overwrite: true);
    }

    private static bool IsSaved(string audioPath) =>
        AbleKit.Analysis.AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
        && analysis.IsDefaultClipSaved;

    /// <summary>The warps kept for a track, as the cuts they were saved on.</summary>
    public WarpSiblings Of(string trackKey)
    {
        var folder = Path.Combine(paths.KeptWarpsFolder, trackKey);

        return WarpSiblings.From(
            Directory.Exists(folder)
                ? Directory
                    .EnumerateFiles(folder, "*.flac.asd")
                    .Select(sidecar => sidecar[..^".asd".Length])
                    .Select(audio =>
                        WarpSiblings.Describe(
                            // The stand-in audio file does not exist; only its sidecar is read.
                            audio,
                            trackKey,
                            Path.GetFileNameWithoutExtension(audio) is var name
                            && name != SourceName
                                ? name
                                : null
                        )
                    )
                : []
        );
    }

    /// <summary>Drops what was kept for a track, once its new stems have been handed off.</summary>
    public void Clear(string trackKey)
    {
        var folder = Path.Combine(paths.KeptWarpsFolder, trackKey);

        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
}
