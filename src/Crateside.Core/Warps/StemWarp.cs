using System.Text.Json.Serialization;
using AbleKit.Analysis;
using Crateside.Core.Ableton;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;

namespace Crateside.Core.Warps;

/// <summary>One stem's warp as the warp view draws it: its sidecar, whose warp it is, and the audio's length (#97).</summary>
public sealed record StemWarp
{
    public required string Path { get; init; }

    public required WarpOwner Owner { get; init; }

    /// <summary>Null when there is none, or it cannot be read.</summary>
    public AnalysisFile? Sidecar { get; init; }

    public int SampleRate { get; init; } = 44_100;

    /// <summary>The audio's length in seconds, 0 when nothing says.</summary>
    public double Seconds { get; init; }

    /// <summary>Whose transients the sidecar holds: Live's unless crateside wrote them.</summary>
    public TransientOrigin? TransientOrigin { get; init; }

    /// <summary>Whether its sibling's warp is the track's: an instrumental, karaoke or the kept source.</summary>
    public bool IsAuthority { get; init; }

    /// <summary>The starter the sidecar still holds, with why its marker got its beat; null for anything else.</summary>
    public WrittenStarter? Starter { get; init; }

    /// <summary>The markers Live shows: every one but the last, which Live keeps and never draws.</summary>
    // Usually 1/32 beat past the anchor, but not always: a few hold it hundreds of beats out and
    // Live hides those too, so the count is what defines it rather than the distance.
    public IReadOnlyList<WarpMarker> Markers =>
        Sidecar?.Warp.Markers is { Count: > 1 } markers ? [.. markers.Take(markers.Count - 1)] : [];

    /// <summary>Whether Live applies a warp, which it does only once the default clip is saved with Warp on.</summary>
    public bool IsWarped => Sidecar is { IsDefaultClipSaved: true, Warp.IsWarped: true };

    /// <summary>The clip's start and end in seconds of audio; the whole file when no clip is saved.</summary>
    public (double Start, double End) Clip =>
        // With Warp off the clip is in seconds, and the marker pair Live needs says nothing of beats.
        Sidecar is { DefaultClip: { } clip, Warp: { IsWarped: true } warp }
        && warp.SecondsAt(clip.Start) is { } start
        && warp.SecondsAt(clip.End) is { } end
            ? (start, end)
            : (0, Seconds);

    /// <summary>The loop in seconds of audio, and whether it is on; null without a saved clip.</summary>
    public (double Start, double End, bool On)? Loop =>
        Sidecar is { IsDefaultClipSaved: true, Clip: var clip, Warp: { IsWarped: true } warp }
        && (
            clip.LoopOn
                ? (clip.LoopStart, clip.LoopEnd)
                : (clip.HiddenLoopStart, clip.HiddenLoopEnd)
        )
            is var (from, to)
        && warp.SecondsAt(from) is { } start
        && warp.SecondsAt(to) is { } end
            ? (start, end, clip.LoopOn)
            : null;

    /// <summary>Each transient's start in seconds.</summary>
    public IReadOnlyList<double> TransientTimes =>
        Sidecar is { Audio.Transients: var transients }
            ? [.. transients.Select(transient => (double)transient.Position / SampleRate)]
            : [];

    /// <summary>The first time this stem's markers part from <paramref name="shared"/>, or null when they match.</summary>
    public double? PartsFrom(IReadOnlyList<WarpMarker> shared)
    {
        var own = Markers;

        return Unmatched(own, shared).Concat(Unmatched(shared, own)).Min();
    }

    private static IEnumerable<double?> Unmatched(
        IReadOnlyList<WarpMarker> markers,
        IReadOnlyList<WarpMarker> others
    ) =>
        markers
            .Where(marker => !others.Any(other => Same(marker, other)))
            .Select(marker => (double?)marker.Seconds)
            .Take(1);

    private static bool Same(WarpMarker left, WarpMarker right) =>
        Math.Abs(left.Seconds - right.Seconds) <= 0.001
        && Math.Abs(left.Beat - right.Beat) <= 0.001;
}

/// <summary>Whose warp a stem's sidecar holds.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Yours), "yours")]
[JsonDerivedType(typeof(Borrowed), "borrowed")]
[JsonDerivedType(typeof(Starter), "starter")]
[JsonDerivedType(typeof(Unwarped), "unwarped")]
[JsonDerivedType(typeof(LiveAnalysis), "liveAnalysis")]
[JsonDerivedType(typeof(None), "none")]
[JsonDerivedType(typeof(WaitingForLive), "waitingForLive")]
[JsonDerivedType(typeof(PuttingBack), "puttingBack")]
public abstract record WarpOwner
{
    private WarpOwner() { }

    /// <summary>A warp the user saved in Live.</summary>
    /// <param name="LentTo">The siblings crateside lent this warp to, whose sidecars still hold it.</param>
    /// <param name="SavedOver">The warp crateside wrote that the user saved over, if any.</param>
    public sealed record Yours(DateTime Saved, IReadOnlyList<string> LentTo, WrittenWarp? SavedOver)
        : WarpOwner
    {
        /// <summary>The save from the warp view this sidecar still holds, null once Live has saved over it.</summary>
        public SavedWarp? SavedHere { get; init; }
    }

    /// <summary>A sibling's warp crateside copied onto this stem (ADR 0043).</summary>
    public sealed record Borrowed(string From, DateTime Written) : WarpOwner;

    /// <summary>A starter warp crateside wrote from beatdetect's reading, not yet saved over.</summary>
    public sealed record Starter(DateTime Written) : WarpOwner;

    /// <summary>A clip crateside saved with Warp off, so the stem arrives at its gain and color for the user to warp (ADR 0059).</summary>
    public sealed record Unwarped(DateTime Written) : WarpOwner;

    /// <summary>Live analyzed the file but no warp was saved, so Live ignores its markers.</summary>
    public sealed record LiveAnalysis : WarpOwner;

    /// <summary>No sidecar: Live has never opened the file.</summary>
    public sealed record None : WarpOwner;

    /// <summary>The sidecar is in the Recycle Bin until Live opens the stem again.</summary>
    public sealed record WaitingForLive(AwaitedAnalysis Awaited) : WarpOwner;

    /// <summary>Live has written a fresh sidecar, and crateside has yet to put the warp back into it.</summary>
    public sealed record PuttingBack(AwaitedAnalysis Awaited) : WarpOwner;
}

/// <summary>Reads each stem's warp for the warp view, from its sidecar and crateside's records of what it wrote.</summary>
public sealed class StemWarpReader(
    WrittenWarps written,
    TransientOrigins origins,
    LiveAnalyses waiting,
    SavedWarps? saved = null
)
{
    /// <summary>The track's lanes on one time axis, as the warp view draws them.</summary>
    public WarpGroup GroupOf(TrackRow row) => WarpGroup.Of([.. LanePaths(row).Select(Read)]);

    /// <summary>The samples cut from the upload most of the track's samples share, in the sample list's order.</summary>
    public static List<string> LanePaths(TrackRow row)
    {
        var upload = row
            .Samples.GroupBy(sample => sample.SourceTrackKey)
            .OrderByDescending(cut => cut.Count())
            .Select(cut => cut.Key)
            .FirstOrDefault();

        return
        [
            .. row
                .Samples.Where(sample =>
                    sample.SourceTrackKey == upload && sample.Path is { Length: > 0 }
                )
                .Select(sample => sample.Path),
        ];
    }

    public StemWarp Read(string audioPath)
    {
        var sidecarPath = StemFileName.AnalysisFor(audioPath);
        var awaited = waiting.Of(audioPath);
        var (rate, duration) = AudioOf(audioPath);
        var isAuthority = WarpSiblings.Read(audioPath)?.IsAuthority ?? false;

        if (!AbletonReads.TryReadAnalysis(sidecarPath, out var sidecar))
            return new StemWarp
            {
                Path = audioPath,
                Owner = awaited is null
                    ? new WarpOwner.None()
                    : new WarpOwner.WaitingForLive(awaited),
                SampleRate = rate,
                Seconds = duration,
                IsAuthority = isAuthority,
            };

        var changed = File.GetLastWriteTime(sidecarPath);
        var ours = written.Of(audioPath);
        WarpOwner owner = (awaited, ours, sidecar.IsDefaultClipSaved) switch
        {
            ({ } waited, _, false) => new WarpOwner.PuttingBack(waited),
            (_, { IsUnwarped: true }, _) => new WarpOwner.Unwarped(changed),
            (_, { IsStarter: true }, _) => new WarpOwner.Starter(changed),
            (_, { From: { } from }, _) => new WarpOwner.Borrowed(from, changed),
            (_, _, true) => new WarpOwner.Yours(
                changed,
                written.LentTo(audioPath),
                written.Ever(audioPath)
            )
            {
                SavedHere = saved?.Of(audioPath),
            },
            _ => new WarpOwner.LiveAnalysis(),
        };

        return new StemWarp
        {
            Path = audioPath,
            Owner = owner,
            Sidecar = sidecar,
            SampleRate = rate,
            Seconds = sidecar.Audio.HeadTable is [.., > 0 and var frames]
                ? (double)frames / rate
                : duration,
            TransientOrigin = origins.Of(sidecar.Audio.Transients, audioPath),
            IsAuthority = isAuthority,
            Starter = owner is WarpOwner.Starter ? ours?.Starter : null,
        };
    }

    private static (int Rate, double Seconds) AudioOf(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);

            return file.Properties is { AudioSampleRate: > 0 and var rate, Duration: var length }
                ? (rate, length.TotalSeconds)
                : (44_100, 0);
        }
        catch (Exception)
        {
            return (44_100, 0);
        }
    }
}

/// <summary>A track's stems on one time axis, and the warp they share (#97).</summary>
public sealed record WarpGroup(IReadOnlyList<StemWarp> Stems, StemWarp? Lender)
{
    /// <summary>The longest stem's length, the axis every lane is drawn on.</summary>
    public double Seconds => Stems.Count is 0 ? 0 : Stems.Max(stem => stem.Seconds);

    public IReadOnlyList<WarpMarker> SharedMarkers => Lender?.Markers ?? [];

    /// <summary>The markers Live keeps, the hidden last one included, which carry the last stretch's tempo.</summary>
    public IReadOnlyList<WarpMarker> SharedAllMarkers => Lender?.Sidecar?.Warp.Markers ?? [];

    /// <summary>Where a stem's warp parts from the shared one, for a stem with a saved warp of its own.</summary>
    public double? PartsFrom(StemWarp stem) =>
        Lender is null || stem == Lender || !stem.IsWarped ? null : stem.PartsFrom(SharedMarkers);

    public IEnumerable<StemWarp> OnTheirOwn => Stems.Where(stem => PartsFrom(stem) is not null);

    /// <summary>
    /// Where the track's warp is read from: the user's warp on the track's authority first, then
    /// any warp of theirs, then one crateside wrote.
    /// </summary>
    public static WarpGroup Of(IReadOnlyList<StemWarp> stems) =>
        new(
            stems,
            stems
                .Where(stem => stem.IsWarped)
                .OrderBy(stem =>
                    stem switch
                    {
                        { Owner: WarpOwner.Yours, IsAuthority: true } => 0,
                        { Owner: WarpOwner.Yours } => 1,
                        { Owner: WarpOwner.Borrowed } => 2,
                        _ => 3,
                    }
                )
                .FirstOrDefault()
        );

    /// <summary>The shared warp's beat at a time in the audio, extending its first and last stretches.</summary>
    public double? BeatAt(double seconds)
    {
        if (SharedAllMarkers is not { Count: > 1 } markers)
            return null;

        var i = 1;

        while (i < markers.Count - 1 && seconds > markers[i].Seconds)
            i++;

        var (from, to) = (markers[i - 1], markers[i]);

        return from.Beat
            + (seconds - from.Seconds) * (to.Beat - from.Beat) / (to.Seconds - from.Seconds);
    }

    public double? SecondsAt(double beat) => Lender?.Sidecar?.Warp.SecondsAt(beat);

    /// <summary>The beat the shared warp's clip starts on: a point on the ruler, not its bar 1.</summary>
    public double ClipStartBeat => Lender?.Sidecar?.DefaultClip?.Start ?? 0;

    public BarRuler Bars => BarRuler.Of(Lender?.Sidecar?.Warp.TimeSignature);

    /// <summary>The bar the clip starts in.</summary>
    public int ClipBar => Bars.BarAt(ClipStartBeat + 0.001);

    /// <summary>The clip start's marker on the ruler; while editing, a clip that starts before the audio is drawn at the first warp marker's bar, unset.</summary>
    public ClipMark? ClipMarkFor(bool editing) =>
        Lender is not { Clip.Start: var start } ? null
        : start >= 0 ? new(start, ClipBar, false)
        : editing && SharedMarkers is [{ Beat: var beat }, ..]
            ? new(
                Math.Max(0, SecondsAt(Bars.IndexAt(beat) * Bars.BeatsPerBar) ?? 0),
                Bars.BarAt(beat + 0.001),
                true
            )
        : null;
}

/// <summary>Where the clip start is drawn; <paramref name="Unset"/> when the clip starts before the audio, so the marker only stands at a bar to be dragged from.</summary>
public readonly record struct ClipMark(double Seconds, int Bar, bool Unset);
