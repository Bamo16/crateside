using AbleKit.Analysis;
using AbleKit.Tags;
using Crateside.Core.Ableton;
using Crateside.Core.Samples;
using Crateside.Core.Warps;

namespace Crateside.Core.Tracks;

/// <summary>One sample of a track: its file, the tags Live holds for it, and its warp.</summary>
public sealed record StemDetails
{
    public required string Path { get; init; }

    /// <summary>What the separator called the file, such as <c>Vocal - Full</c>, or <c>source</c>.</summary>
    public required string Label { get; init; }

    public bool IsInLibrary { get; init; }

    /// <summary>The keywords and Collections Live holds for the file; null when it holds none.</summary>
    public FileTags? Tags { get; init; }

    public required StemWarpDetails Warp { get; init; }
}

/// <summary>A sample's <c>.asd</c> sidecar as parsed, and crateside's records about the warp it holds.</summary>
public sealed record StemWarpDetails
{
    /// <summary>Whose warp the sidecar holds.</summary>
    public required WarpOwner Owner { get; init; }

    /// <summary>False when there is no sidecar, or it cannot be read.</summary>
    public bool HasSidecar { get; init; }

    /// <summary>Whether Live applies the warp: the default clip is saved, with Warp on.</summary>
    public bool IsWarped { get; init; }

    public bool IsDefaultClipSaved { get; init; }

    public int SampleRate { get; init; }

    /// <summary>The audio's length in seconds, 0 when nothing says.</summary>
    public double Seconds { get; init; }

    /// <summary>The markers Live shows, each at a time in the audio and a beat.</summary>
    public IReadOnlyList<WarpMarker> Markers { get; init; } = [];

    /// <summary>The tempo the warp holds, bar by bar across the default clip; null without a saved clip.</summary>
    public WarpTempo? Tempo { get; init; }

    /// <summary>The default clip's start and end in seconds of audio; the whole file when no clip is saved.</summary>
    public required AudioSpan Clip { get; init; }

    /// <summary>The loop in seconds of audio; null without a saved clip.</summary>
    public AudioLoop? Loop { get; init; }

    /// <summary>Whose transients the sidecar holds: Live's unless crateside wrote them.</summary>
    public TransientOrigin? TransientOrigin { get; init; }

    /// <summary>Whether this sample's warp is the one its siblings take: an instrumental, karaoke or the kept source.</summary>
    public bool IsAuthority { get; init; }

    /// <summary>The warp crateside wrote that the sidecar still holds; null once Live or the user saved over it.</summary>
    public WrittenWarp? Written { get; init; }

    /// <summary>The warp crateside last wrote into the sidecar, even if it has been saved over since.</summary>
    public WrittenWarp? LastWritten { get; init; }

    /// <summary>The retype to a truer tempo the sidecar still holds, or null (#99).</summary>
    public RetypedWarp? Retyped { get; init; }

    /// <summary>The copies of the sidecar crateside kept before replacing or recycling it, oldest first.</summary>
    public IReadOnlyList<string> Backups { get; init; } = [];
}

/// <summary>A stretch of audio, in seconds.</summary>
public sealed record AudioSpan(double Start, double End);

/// <summary>A clip's loop in seconds of audio, and whether it is on.</summary>
public sealed record AudioLoop(double Start, double End, bool On);

/// <summary>Reads each sample's tags and warp for the API, from the readers the warp view uses.</summary>
public sealed class StemDetailsReader(
    StemWarpReader warps,
    WrittenWarps written,
    RetypedWarps retyped,
    SidecarBackups backups,
    FolderInfoReader folderInfo,
    FileIndexReader index
)
{
    public StemDetails Read(TrackSample sample) =>
        new()
        {
            Path = sample.Path,
            Label = sample.Label,
            IsInLibrary = sample.IsInLibrary,
            Tags = TagsOf(sample),
            Warp = WarpOf(warps.Read(sample.Path)),
        };

    /// <summary>From Live's index once the sample is in the library, else from the folder's tag store (ADR 0013).</summary>
    private FileTags? TagsOf(TrackSample sample) =>
        System.IO.Path.GetDirectoryName(sample.Path) is { Length: > 0 } folder
        && AbletonReads
            .TagsOrEmpty(sample.IsInLibrary ? index.Read : folderInfo.Read, folder)
            .TryGet(sample.Path, out var tags)
            ? tags
            : null;

    private StemWarpDetails WarpOf(StemWarp warp) =>
        new()
        {
            Owner = warp.Owner,
            HasSidecar = warp.Sidecar is not null,
            IsWarped = warp.IsWarped,
            IsDefaultClipSaved = warp.Sidecar is { IsDefaultClipSaved: true },
            SampleRate = warp.SampleRate,
            Seconds = warp.Seconds,
            Markers = warp.Markers,
            Tempo = warp.Sidecar is { } sidecar ? WarpTempo.Of(sidecar, warp.SampleRate) : null,
            Clip = new(warp.Clip.Start, warp.Clip.End),
            Loop = warp.Loop is var (start, end, on) ? new(start, end, on) : null,
            TransientOrigin = warp.TransientOrigin,
            IsAuthority = warp.IsAuthority,
            Written = written.Of(warp.Path),
            LastWritten = written.Ever(warp.Path),
            Retyped = retyped.Of(warp.Path),
            Backups = backups.Of(StemFileName.AnalysisFor(warp.Path)),
        };
}
