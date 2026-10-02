using AbleKit.Analysis;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// Gives a stem the finished warp of a sibling cut from the same upload, by writing its sidecar
/// with the sibling's clip and warp and its own waveform and transients (#54, ADR 0043, ADR 0044).
/// </summary>
public sealed class WarpBorrower(
    IAudioDecoder decoder,
    ITransientDetector detector,
    BorrowedWarps borrowed
)
{
    /// <param name="authorities">The target's siblings whose warp is the track's; see <see cref="WarpSiblings"/>.</param>
    /// <param name="sameKind">The target's siblings of its own kind, whose transients Live may have detected.</param>
    public async Task<WarpBorrowOutcome> BorrowAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    )
    {
        var sidecar = new FileInfo(StemFileName.AnalysisFor(target));

        try
        {
            var own = sidecar.Exists ? AnalysisFile.Read(sidecar.FullName) : null;

            if (own is { IsDefaultClipSaved: true })
                return new WarpBorrowOutcome.AlreadyWarped();

            var saved = authorities
                .Select(path => (Path: path, Analysis: ReadSaved(path)))
                .Where(sibling => sibling.Analysis is not null)
                .ToList();

            if (saved is [])
                return new WarpBorrowOutcome.NoWarpedSibling();

            if (saved.Skip(1).Any(sibling => !SameWarp(sibling.Analysis!, saved[0].Analysis!)))
                return new WarpBorrowOutcome.SiblingsDisagree([.. saved.Select(s => s.Path)]);

            var (from, warp) = (saved[0].Path, saved[0].Analysis!);

            if (await decoder.DecodeAsync(target, ct) is not { } audio)
                return new WarpBorrowOutcome.Failed(
                    $"ffmpeg could not decode {Path.GetFileName(target)}"
                );

            if (
                warp.Audio.HeadTable is not [.., var siblingFrames]
                || siblingFrames != audio.Frames
            )
                return new WarpBorrowOutcome.LengthsDiffer(
                    from,
                    warp.Audio.HeadTable is [.., var frames] ? frames : 0,
                    audio.Frames
                );

            var size = new FileInfo(target).Length;
            var (transientsFrom, transients) = TransientsFor(target, own, sameKind, audio, size);

            var analysis = AudioAnalysis.FromSamples(
                audio.Samples,
                audio.ChannelCount,
                transients,
                size
            );

            // The user may have saved this stem in Live while it was being decoded.
            sidecar.Refresh();

            if (sidecar.Exists && AnalysisFile.Read(sidecar.FullName).IsDefaultClipSaved)
                return new WarpBorrowOutcome.AlreadyWarped();

            (warp with { Audio = analysis }).Write(sidecar.FullName);
            borrowed.Record(target, from);

            return new WarpBorrowOutcome.Written(from, transientsFrom);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new WarpBorrowOutcome.Failed(ex.Message);
        }
    }

    /// <summary>The sibling's analysis when its default clip is saved, which Live needs to apply a warp.</summary>
    private static AnalysisFile? ReadSaved(string audioPath) =>
        AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
        && analysis.IsDefaultClipSaved
            ? analysis
            : null;

    /// <summary>Live's transients from the target's own analysis or a same-kind sibling's, else crateside's (ADR 0044).</summary>
    private (string? From, IReadOnlyList<Transient> Transients) TransientsFor(
        string target,
        AnalysisFile? own,
        IReadOnlyList<string> sameKind,
        DecodedAudio audio,
        long size
    )
    {
        if (own is not null && LiveTransients(own, size, audio.Frames) is { } live)
            return (target, live);

        foreach (var path in sameKind)
        {
            if (LiveTransients(path, audio.Frames) is { } copied)
                return (path, copied);
        }

        return (null, detector.Detect(audio));
    }

    private IReadOnlyList<Transient>? LiveTransients(string audioPath, int frames)
    {
        try
        {
            return
                borrowed.BorrowedFrom(audioPath) is null
                && AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
                ? LiveTransients(analysis, new FileInfo(audioPath).Length, frames)
                : null;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The transients Live detected, when the analysis describes audio of this size and length.</summary>
    private static IReadOnlyList<Transient>? LiveTransients(
        AnalysisFile analysis,
        long size,
        int frames
    ) =>
        analysis.Audio
            is { Transients: { Count: > 0 } transients, HeadTable: [.., var analysed] } audio
        && audio.OriginalFileSize == size
        && analysed == frames
            ? transients
            : null;

    private static bool SameWarp(AnalysisFile left, AnalysisFile right) =>
        left.DefaultClip == right.DefaultClip
        && left.Warp.Markers.SequenceEqual(right.Warp.Markers);
}

public abstract record WarpBorrowOutcome
{
    private WarpBorrowOutcome() { }

    /// <param name="TransientsFrom">The file whose Live-detected transients were copied, or null if crateside detected them.</param>
    public sealed record Written(string From, string? TransientsFrom) : WarpBorrowOutcome;

    /// <summary>The target's own default clip is saved: the user's warp, never replaced (ADR 0037).</summary>
    public sealed record AlreadyWarped : WarpBorrowOutcome;

    public sealed record NoWarpedSibling : WarpBorrowOutcome;

    /// <summary>Two instrumentals of the track hold different warps, which is the user's to settle (#54).</summary>
    public sealed record SiblingsDisagree(IReadOnlyList<string> Siblings) : WarpBorrowOutcome;

    /// <param name="SiblingFrames">The sibling's length in sample frames when Live analysed it.</param>
    public sealed record LengthsDiffer(string Sibling, int SiblingFrames, int TargetFrames)
        : WarpBorrowOutcome;

    public sealed record Failed(string Error) : WarpBorrowOutcome;
}
