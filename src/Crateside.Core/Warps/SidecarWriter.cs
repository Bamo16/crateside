using System.Diagnostics;
using AbleKit.Analysis;
using Crateside.Core.Samples;

namespace Crateside.Core.Warps;

/// <summary>
/// Writes a stem's sidecar: a sibling's finished warp (#54, ADR 0043–0045), or a starter warp at the
/// measured BPM when nothing of the upload is warped yet (#77, ADR 0047).
/// </summary>
public sealed class SidecarWriter(
    IAudioDecoder decoder,
    ITransientDetector detector,
    WrittenWarps written,
    TransientOrigins origins
)
{
    /// <summary>Live writes one marker more than it shows, this many beats after the last.</summary>
    private const double HiddenMarkerBeats = 1.0 / 32;

    /// <summary>Whether any of these siblings holds a saved warp the user made, which <see cref="BorrowAsync"/> would lend.</summary>
    public bool HasWarpToLend(IReadOnlyList<string> authorities, IReadOnlyList<string> sameKind) =>
        SavedAmong(authorities.Concat(sameKind)) is not [];

    /// <param name="authorities">The target's siblings whose warp is the track's; see <see cref="WarpSiblings"/>.</param>
    /// <param name="sameKind">The target's siblings of its own kind, whose warp it takes first, since a vocal's may be shifted.</param>
    public async Task<WarpOutcome> BorrowAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    ) => Settle(await PrepareBorrowAsync(target, authorities, sameKind, ct), target);

    /// <summary>As <see cref="BorrowAsync"/>, but over a warp the user saved too; only ever asked for by the user (#97).</summary>
    public async Task<WarpOutcome> ReplaceAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    ) =>
        await PrepareBorrowAsync(target, authorities, sameKind, replacing: true, ct) switch
        {
            SidecarPlan.Ready(var sidecar) => Commit(sidecar, target, replacing: true),
            SidecarPlan.Refused(var outcome) => outcome,
            _ => throw new UnreachableException(),
        };

    /// <summary>Writes one marker at the start of the clip at this tempo, where Live puts its own first marker.</summary>
    public async Task<WarpOutcome> StartAsync(
        string target,
        IReadOnlyList<string> sameKind,
        double bpm,
        CancellationToken ct = default
    ) => Settle(await PrepareStarterAsync(target, sameKind, bpm, ct), target);

    /// <summary>Works out the sidecar <see cref="BorrowAsync"/> would write, without writing it.</summary>
    public Task<SidecarPlan> PrepareBorrowAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    ) => PrepareBorrowAsync(target, authorities, sameKind, replacing: false, ct);

    private async Task<SidecarPlan> PrepareBorrowAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        bool replacing,
        CancellationToken ct
    )
    {
        try
        {
            if (!replacing && Own(target) is { IsUsers: true })
                return Refuse(new WarpOutcome.AlreadyWarped());

            var saved = SavedAmong(sameKind) is { Count: > 0 } ownKind
                ? ownKind
                : SavedAmong(authorities);

            if (saved is [])
                return Refuse(new WarpOutcome.NoWarpedSibling());

            if (saved.Skip(1).Any(sibling => !SameWarp(sibling.Analysis, saved[0].Analysis)))
                return Refuse(new WarpOutcome.SiblingsDisagree([.. saved.Select(s => s.Path)]));

            var (from, warp) = saved[0];

            if (await decoder.DecodeAsync(target, ct) is not { } audio)
                return Refuse(Undecodable(target));

            if (
                warp.Audio.HeadTable is not [.., var siblingFrames]
                || siblingFrames != audio.Frames
            )
                return Refuse(
                    new WarpOutcome.LengthsDiffer(
                        from,
                        warp.Audio.HeadTable is [.., var frames] ? frames : 0,
                        audio.Frames
                    )
                );

            return Prepare(
                target,
                sameKind,
                audio,
                from,
                null,
                analysis => warp with { Audio = analysis }
            );
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Refuse(new WarpOutcome.Failed(ex.Message));
        }
    }

    /// <summary>Works out the sidecar <see cref="StartAsync"/> would write, without writing it.</summary>
    public async Task<SidecarPlan> PrepareStarterAsync(
        string target,
        IReadOnlyList<string> sameKind,
        double bpm,
        CancellationToken ct = default
    )
    {
        try
        {
            if (Own(target) is { IsUsers: true } or { IsStarter: true })
                return Refuse(new WarpOutcome.AlreadyWarped());

            if (await decoder.DecodeAsync(target, ct) is not { } audio)
                return Refuse(Undecodable(target));

            return Prepare(
                target,
                sameKind,
                audio,
                null,
                bpm,
                analysis => Starter(bpm, (double)audio.Frames / audio.SampleRate, analysis)
            );
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Refuse(new WarpOutcome.Failed(ex.Message));
        }
    }

    /// <summary>
    /// Writes a prepared sidecar beside <paramref name="audioPath"/>, which may be where the target
    /// has been moved to since, and records it there; never over a warp the user saved.
    /// </summary>
    public WarpOutcome Commit(PreparedSidecar sidecar, string audioPath) =>
        Commit(sidecar, audioPath, replacing: false);

    private WarpOutcome Commit(PreparedSidecar sidecar, string audioPath, bool replacing)
    {
        try
        {
            if (!replacing && Own(audioPath) is { IsUsers: true })
                return new WarpOutcome.AlreadyWarped();

            sidecar.File.Write(StemFileName.AnalysisFor(audioPath));

            if (sidecar.From is { } lender)
                written.RecordBorrowed(audioPath, lender);
            else
                written.RecordStarter(audioPath);

            var transients = sidecar.File.Audio.Transients;
            var transientsFrom = sidecar.TransientsFrom switch
            {
                null => null,
                var own when Same(own, sidecar.Target) => audioPath,
                var copied => copied,
            };

            if (transientsFrom is null)
                origins.RecordDetected(transients, audioPath, detector.Version);
            else if (!Same(transientsFrom, audioPath))
                origins.RecordCopied(transients, audioPath, transientsFrom);

            return sidecar switch
            {
                { From: { } from } => new WarpOutcome.Written(from, transientsFrom),
                { Bpm: { } bpm } => new WarpOutcome.Started(bpm, transientsFrom),
                _ => throw new UnreachableException(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WarpOutcome.Failed(ex.Message);
        }
    }

    private WarpOutcome Settle(SidecarPlan plan, string target) =>
        plan switch
        {
            SidecarPlan.Ready(var sidecar) => Commit(sidecar, target),
            SidecarPlan.Refused(var outcome) => outcome,
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// A one-marker warp laid out as Live saves one: beat 0 at the start of the audio, the clip
    /// running to its end, looping off.
    /// </summary>
    private static AnalysisFile Starter(double bpm, double seconds, AudioAnalysis audio)
    {
        var beats = seconds * bpm / 60;

        return new AnalysisFile
        {
            Audio = audio,
            Warp = new Warp
            {
                Markers = [new(0, 0), new(HiddenMarkerBeats * 60 / bpm, HiddenMarkerBeats)],
            },
            Clip = new Clip
            {
                LoopStart = 0,
                LoopEnd = beats,
                HiddenLoopStart = 0,
                HiddenLoopEnd = beats,
                OutMarker = beats,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        };
    }

    /// <summary>The sidecar with the target's own waveform and the best transients it can have.</summary>
    private SidecarPlan.Ready Prepare(
        string target,
        IReadOnlyList<string> sameKind,
        DecodedAudio audio,
        string? from,
        double? bpm,
        Func<AudioAnalysis, AnalysisFile> sidecar
    )
    {
        var size = new FileInfo(target).Length;
        var (transientsFrom, transients) = TransientsFor(
            target,
            Own(target)?.Analysis,
            sameKind,
            audio,
            size
        );
        var analysis = AudioAnalysis.FromSamples(
            audio.Samples,
            audio.ChannelCount,
            transients,
            size
        );

        return new SidecarPlan.Ready(
            new PreparedSidecar(target, sidecar(analysis), from, bpm, transientsFrom)
        );
    }

    private OwnSidecar? Own(string target) =>
        AnalysisFile.TryRead(StemFileName.AnalysisFor(target), out var analysis)
            ? new OwnSidecar(
                analysis,
                analysis.IsDefaultClipSaved && !written.IsCratesides(target),
                written.IsStarter(target)
            )
            : null;

    /// <summary>The siblings with a saved warp the user made: never one crateside wrote, a copy or a starter.</summary>
    private List<(string Path, AnalysisFile Analysis)> SavedAmong(IEnumerable<string> siblings) =>
        [
            .. siblings
                .Where(path => !written.IsCratesides(path))
                .Select(path => (Path: path, Analysis: ReadSaved(path)))
                .Where(sibling => sibling.Analysis is not null)
                .Select(sibling => (sibling.Path, sibling.Analysis!)),
        ];

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
        if (own is not null && LiveTransients(own, target, size, audio.Frames) is { } live)
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
            return AnalysisFile.TryRead(StemFileName.AnalysisFor(audioPath), out var analysis)
                ? LiveTransients(analysis, audioPath, new FileInfo(audioPath).Length, frames)
                : null;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The transients Live detected, when the analysis describes audio of this size and length and
    /// crateside did not write them.
    /// </summary>
    private IReadOnlyList<Transient>? LiveTransients(
        AnalysisFile analysis,
        string audioPath,
        long size,
        int frames
    ) =>
        analysis.Audio
            is { Transients: { Count: > 0 } transients, HeadTable: [.., var analysed] } audio
        && audio.OriginalFileSize == size
        && analysed == frames
        && origins.Of(transients, audioPath) is null
            ? transients
            : null;

    private static bool SameWarp(AnalysisFile left, AnalysisFile right) =>
        left.DefaultClip == right.DefaultClip
        && left.Warp.Markers.SequenceEqual(right.Warp.Markers);

    private static bool Same(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase
        );

    private static SidecarPlan.Refused Refuse(WarpOutcome outcome) => new(outcome);

    private static WarpOutcome.Failed Undecodable(string target) =>
        new($"ffmpeg could not decode {Path.GetFileName(target)}");

    /// <param name="IsUsers">A saved default clip crateside did not write: the user's warp, never replaced.</param>
    private sealed record OwnSidecar(AnalysisFile Analysis, bool IsUsers, bool IsStarter);
}

public abstract record WarpOutcome
{
    private WarpOutcome() { }

    /// <param name="TransientsFrom">The file whose Live-detected transients were used, or null if crateside detected them.</param>
    public sealed record Written(string From, string? TransientsFrom) : WarpOutcome;

    /// <param name="TransientsFrom">As for <see cref="Written"/>.</param>
    public sealed record Started(double Bpm, string? TransientsFrom) : WarpOutcome;

    /// <summary>The target holds the user's warp, never replaced (ADR 0037), or already a starter.</summary>
    public sealed record AlreadyWarped : WarpOutcome;

    public sealed record NoWarpedSibling : WarpOutcome;

    /// <summary>Two stems it could take a warp from hold different ones, which is the user's to settle (#54).</summary>
    public sealed record SiblingsDisagree(IReadOnlyList<string> Siblings) : WarpOutcome;

    /// <param name="SiblingFrames">The sibling's length in sample frames when Live analysed it.</param>
    public sealed record LengthsDiffer(string Sibling, int SiblingFrames, int TargetFrames)
        : WarpOutcome;

    public sealed record Failed(string Error) : WarpOutcome;
}

/// <summary>A sidecar worked out and ready to write, or why there is none.</summary>
public abstract record SidecarPlan
{
    private SidecarPlan() { }

    public sealed record Ready(PreparedSidecar Sidecar) : SidecarPlan;

    public sealed record Refused(WarpOutcome Outcome) : SidecarPlan;
}

/// <param name="Target">The audio file it was worked out for.</param>
/// <param name="From">The sibling whose warp it carries, or null for a starter.</param>
/// <param name="Bpm">A starter's tempo; null for a borrowed warp.</param>
/// <param name="TransientsFrom">The file whose Live-detected transients it carries, or null if crateside detected them.</param>
public sealed record PreparedSidecar(
    string Target,
    AnalysisFile File,
    string? From,
    double? Bpm,
    string? TransientsFrom
);
