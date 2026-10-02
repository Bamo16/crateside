using AbleKit.Analysis;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// Gives a stem the finished warp of a sibling cut from the same upload, by writing its sidecar
/// with the sibling's clip and warp and its own waveform and transients (#54, ADR 0043).
/// </summary>
public sealed class WarpBorrower(
    IAudioDecoder decoder,
    ITransientDetector detector,
    BorrowedWarps borrowed
)
{
    /// <param name="authorities">The target's siblings whose warp is the track's; see <see cref="WarpSiblings"/>.</param>
    public async Task<WarpBorrowOutcome> BorrowAsync(
        string target,
        IReadOnlyList<string> authorities,
        CancellationToken ct = default
    )
    {
        var sidecar = new FileInfo(StemFileName.AnalysisFor(target));

        try
        {
            if (sidecar.Exists && AnalysisFile.Read(sidecar.FullName).IsDefaultClipSaved)
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

            var analysis = AudioAnalysis.FromSamples(
                audio.Samples,
                audio.ChannelCount,
                detector.Detect(audio),
                new FileInfo(target).Length
            );

            // The user may have saved this stem in Live while it was being decoded.
            sidecar.Refresh();

            if (sidecar.Exists && AnalysisFile.Read(sidecar.FullName).IsDefaultClipSaved)
                return new WarpBorrowOutcome.AlreadyWarped();

            (warp with { Audio = analysis }).Write(sidecar.FullName);
            borrowed.Record(target, from);

            return new WarpBorrowOutcome.Written(from);
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

    private static bool SameWarp(AnalysisFile left, AnalysisFile right) =>
        left.DefaultClip == right.DefaultClip
        && left.Warp.Markers.SequenceEqual(right.Warp.Markers);
}

public abstract record WarpBorrowOutcome
{
    private WarpBorrowOutcome() { }

    public sealed record Written(string From) : WarpBorrowOutcome;

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
