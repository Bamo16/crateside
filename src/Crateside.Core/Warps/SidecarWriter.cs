using System.Collections.Concurrent;
using System.Diagnostics;
using AbleKit.Analysis;
using Crateside.Core.Loudness;
using Crateside.Core.Samples;
using Crateside.Core.Tempo;

namespace Crateside.Core.Warps;

/// <summary>
/// Writes a stem's sidecar: a sibling's finished warp (#54, ADR 0043–0045), a starter warp at the
/// measured BPM (#77, ADR 0047), or a clip with Warp off; each at its clip gain (ADR 0059).
/// </summary>
public sealed class SidecarWriter(
    IAudioDecoder decoder,
    ITransientDetector detector,
    WrittenWarps written,
    TransientOrigins origins,
    ClipGains? gains = null
)
{
    /// <summary>Live writes one marker more than it shows, this many beats after the last.</summary>
    private const double HiddenMarkerBeats = 1.0 / 32;

    private const int MinTransientsToRefine = 32;

    /// <summary>How far apart any two 30 s of the track may sit on a starter's grid, in ms; more and the stem gets Warp off.</summary>
    public const double StarterHoldsWithinMs = 10;

    private const double HoldBlockSeconds = 30;
    private const int MinWindowsToHold = 6;

    private readonly ConcurrentDictionary<
        (string Reference, long Length, double Bpm),
        StarterTiming
    > _timings = new();

    // The warp worker and the levelling pass both rewrite sidecars; neither may undo the other.
    private readonly Lock _writing = new();

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

    /// <summary>Writes one marker at this tempo, on the first beat the transients place.</summary>
    /// <param name="timingFrom">The stem the group's tempo and beat are read from, so every stem gets the same marker; the target when null.</param>
    public async Task<WarpOutcome> StartAsync(
        string target,
        IReadOnlyList<string> sameKind,
        double bpm,
        string? timingFrom = null,
        CancellationToken ct = default
    ) => Settle(await PrepareStarterAsync(target, sameKind, bpm, timingFrom, ct), target);

    /// <summary>Writes a clip with Warp off over the whole file, for a stem that gets no starter (ADR 0059).</summary>
    public async Task<WarpOutcome> UnwarpAsync(
        string target,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    ) => Settle(await PrepareUnwarpedAsync(target, sameKind, ct), target);

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
                await GainAsync(target, ct),
                analysis => (warp with { Audio = analysis }, null)
            );
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Refuse(new WarpOutcome.Failed(ex.Message));
        }
    }

    /// <summary>
    /// Works out the sidecar <see cref="StartAsync"/> would write, without writing it: Warp off instead,
    /// over a starter too, when one tempo does not hold on the group's beats or no beat is placed.
    /// </summary>
    public async Task<SidecarPlan> PrepareStarterAsync(
        string target,
        IReadOnlyList<string> sameKind,
        double bpm,
        string? timingFrom = null,
        CancellationToken ct = default
    )
    {
        try
        {
            var own = Own(target);

            if (own is { IsUsers: true })
                return Refuse(new WarpOutcome.AlreadyWarped());

            if (await TimingAsync(timingFrom ?? target, bpm, ct) is not { } timing)
                return Refuse(Undecodable(timingFrom ?? target));

            if (timing is not { Holds: true, FirstBeat: { } firstBeat })
                return written.Of(target) is { IsStarter: false }
                    ? Refuse(new WarpOutcome.AlreadyWarped())
                    : await UnwarpedAsync(target, sameKind, ct);

            if (own is { IsStarter: true })
                return Refuse(new WarpOutcome.AlreadyWarped());

            if (await decoder.DecodeAsync(target, ct) is not { } audio)
                return Refuse(Undecodable(target));

            return Prepare(
                target,
                sameKind,
                audio,
                null,
                await GainAsync(target, ct),
                analysis =>
                    (
                        Starter(
                            timing.Bpm,
                            (double)audio.Frames / audio.SampleRate,
                            analysis,
                            firstBeat
                        ),
                        timing.Bpm
                    )
            );
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Refuse(new WarpOutcome.Failed(ex.Message));
        }
    }

    /// <summary>Works out the sidecar <see cref="UnwarpAsync"/> would write, without writing it; over a starter, but never a lent warp or the user's.</summary>
    public async Task<SidecarPlan> PrepareUnwarpedAsync(
        string target,
        IReadOnlyList<string> sameKind,
        CancellationToken ct = default
    )
    {
        try
        {
            return Own(target) is { IsUsers: true } || written.Of(target) is { IsStarter: false }
                ? Refuse(new WarpOutcome.AlreadyWarped())
                : await UnwarpedAsync(target, sameKind, ct);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Refuse(new WarpOutcome.Failed(ex.Message));
        }
    }

    private async Task<SidecarPlan> UnwarpedAsync(
        string target,
        IReadOnlyList<string> sameKind,
        CancellationToken ct
    ) =>
        await decoder.DecodeAsync(target, ct) is not { } audio
            ? Refuse(Undecodable(target))
            : Prepare(
                target,
                sameKind,
                audio,
                null,
                await GainAsync(target, ct),
                analysis => (Unwarped((double)audio.Frames / audio.SampleRate, analysis), null)
            );

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
            lock (_writing)
            {
                if (!replacing && Own(audioPath) is { IsUsers: true })
                    return new WarpOutcome.AlreadyWarped();

                Coloured(sidecar.File, audioPath).Write(StemFileName.AnalysisFor(audioPath));

                if (sidecar.From is { } lender)
                    written.RecordBorrowed(audioPath, lender);
                else if (sidecar.IsUnwarped)
                    written.RecordUnwarped(audioPath);
                else
                    written.RecordStarter(audioPath);
            }

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
                { IsUnwarped: true } => new WarpOutcome.Unwarped(transientsFrom),
                _ => throw new UnreachableException(),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WarpOutcome.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Gives a saved sidecar its stem's clip colour, changing nothing else, even over a warp the user
    /// saved; one crateside wrote stays crateside's. True when it was rewritten (#130).
    /// </summary>
    public bool Recolour(string audioPath) =>
        ClipColours.For(audioPath) is { } colour
        && Rewrite(
            audioPath,
            clip => clip.ColorIndex == colour ? null : clip with { ColorIndex = colour }
        );

    /// <summary>As <see cref="Recolour"/>, for the clip gain, linear: 1 is 0 dB (ADR 0059).</summary>
    public bool Level(string audioPath, float gain) =>
        Rewrite(
            audioPath,
            clip =>
                ClipGains.IsOnTarget(clip.SampleVolume, gain)
                    ? null
                    : clip with
                    {
                        SampleVolume = gain,
                    }
        );

    /// <param name="change">The clip to write, or null when the saved one needs nothing.</param>
    private bool Rewrite(string audioPath, Func<Clip, Clip?> change)
    {
        var path = StemFileName.AnalysisFor(audioPath);

        try
        {
            lock (_writing)
            {
                if (
                    !AnalysisFile.TryRead(path, out var analysis)
                    || analysis is not { IsDefaultClipSaved: true }
                    || change(analysis.Clip) is not { } clip
                )
                    return false;

                var ours = written.Of(audioPath);

                (analysis with { Clip = clip }).Write(path);

                if (ours is not null)
                    written.Rerecord(audioPath, ours);

                return true;
            }
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static AnalysisFile Coloured(AnalysisFile file, string audioPath) =>
        ClipColours.For(audioPath) is { } colour && file.Clip.ColorIndex != colour
            ? file with
            {
                Clip = file.Clip with { ColorIndex = colour },
            }
            : file;

    private async Task<float?> GainAsync(string target, CancellationToken ct) =>
        gains is null ? null : await gains.MeasureAsync(target, ct) ?? 1;

    private WarpOutcome Settle(SidecarPlan plan, string target) =>
        plan switch
        {
            SidecarPlan.Ready(var sidecar) => Commit(sidecar, target),
            SidecarPlan.Refused(var outcome) => outcome,
            _ => throw new UnreachableException(),
        };

    /// <summary>
    /// A one-marker warp laid out as Live saves one, the marker on the first beat (#72) and the clip
    /// running from it to the end of the audio, looping off, so it lands on the arrangement's grid.
    /// </summary>
    private static AnalysisFile Starter(
        double bpm,
        double seconds,
        AudioAnalysis audio,
        double markerSeconds
    )
    {
        const double MarkerBeat = 1;
        var beatSeconds = 60 / bpm;
        var end = MarkerBeat + (seconds - markerSeconds) / beatSeconds;

        return new AnalysisFile
        {
            Audio = audio,
            Warp = new Warp
            {
                Markers =
                [
                    new(markerSeconds, MarkerBeat),
                    new(
                        markerSeconds + HiddenMarkerBeats * beatSeconds,
                        MarkerBeat + HiddenMarkerBeats
                    ),
                ],
            },
            Clip = new Clip
            {
                LoopStart = MarkerBeat,
                LoopEnd = end,
                HiddenLoopStart = MarkerBeat,
                HiddenLoopEnd = end,
                OutMarker = end,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        };
    }

    /// <summary>
    /// A clip over the whole file with Warp off, which Live applies, gain and colour, then warps as a
    /// sample never warped; it ignores a warped clip with no markers whole (ADR 0059).
    /// </summary>
    private static AnalysisFile Unwarped(double seconds, AudioAnalysis audio) =>
        new()
        {
            Audio = audio,
            Warp = new Warp { IsWarped = false },
            Clip = new Clip
            {
                LoopEnd = seconds,
                HiddenLoopEnd = seconds,
                OutMarker = seconds,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        };

    /// <summary>The tempo and first beat one stem gives a whole group, so their warps match and Live can adjust them together.</summary>
    private async Task<StarterTiming?> TimingAsync(
        string reference,
        double bpm,
        CancellationToken ct
    )
    {
        var key = (reference, new FileInfo(reference).Length, bpm);

        if (_timings.TryGetValue(key, out var kept))
            return kept;

        if (await decoder.DecodeAsync(reference, ct) is not { } audio)
            return null;

        var (_, transients) = TransientsFor(
            reference,
            Own(reference)?.Analysis,
            [],
            audio,
            key.Length
        );
        var tempo = Refined(bpm, transients, audio.SampleRate);

        return _timings[key] = new StarterTiming(
            tempo,
            GridCheck.FirstBeat(transients, audio.SampleRate, tempo),
            Holds(GridCheck.Against(GridCheck.Strongest(transients, audio.SampleRate), tempo))
        );
    }

    /// <param name="Holds">Whether one tempo keeps the stronger transients on its grid through the whole track.</param>
    private sealed record StarterTiming(double Bpm, double? FirstBeat, bool Holds);

    private static bool Holds(GridReport report) =>
        report is { Windows.Count: >= MinWindowsToHold, Steps.Count: 0 }
        && report.OffsetsPer(HoldBlockSeconds).ToArray() is { Length: > 0 } blocks
        && blocks.Max() - blocks.Min() <= StarterHoldsWithinMs;

    /// <summary>The tempo within a hair of the reading that the stem's stronger transients fit best, or the reading when there are too few to say (ADR 0050).</summary>
    private static double Refined(
        double bpm,
        IReadOnlyList<Transient> transients,
        int sampleRate
    ) =>
        GridCheck.Strongest(transients, sampleRate) is { Length: >= MinTransientsToRefine } times
            ? GridCheck.Refine(times, bpm)
            : bpm;

    /// <summary>The sidecar with the target's own waveform and the best transients it can have.</summary>
    private SidecarPlan.Ready Prepare(
        string target,
        IReadOnlyList<string> sameKind,
        DecodedAudio audio,
        string? from,
        float? gain,
        Func<AudioAnalysis, (AnalysisFile File, double? Bpm)> sidecar
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

        var (file, bpm) = sidecar(analysis);

        if (gain is { } level)
            file = file with { Clip = file.Clip with { SampleVolume = level } };

        return new SidecarPlan.Ready(new PreparedSidecar(target, file, from, bpm, transientsFrom));
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

    /// <param name="TransientsFrom">As for <see cref="Written"/>.</param>
    public sealed record Unwarped(string? TransientsFrom) : WarpOutcome;

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
)
{
    public bool IsUnwarped => !File.Warp.IsWarped;
}
