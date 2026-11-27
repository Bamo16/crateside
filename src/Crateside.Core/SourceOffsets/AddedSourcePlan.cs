using AbleKit.Analysis;
using Crateside.Core.Samples;
using Crateside.Core.Warps;

namespace Crateside.Core.SourceOffsets;

/// <summary>An added source of the track whose mix's warp is being saved.</summary>
public sealed record AddedSourceFile(
    string Path,
    string SourceKey,
    string Label,
    SampleType? Type = null
);

/// <summary>Why an added source is not written with the mix's warp (ADR 0111, rules 14 and 19).</summary>
public enum AddedSkip
{
    /// <summary>No measurement stands for it yet: it waits for the worker, or a file changed since.</summary>
    Measuring,

    Edit,

    Drift,

    NotTheSameRecording,

    /// <summary>It holds a warp the user saved.</summary>
    HasOwnWarp,

    /// <summary>It has no analysis file to derive from yet.</summary>
    NoSidecar,
}

/// <param name="Skip">Null when the save writes this source.</param>
/// <param name="Stamp">The source's sidecar as it stood when the draft began; set when it is written.</param>
/// <param name="Record">The measurement the shift rests on.</param>
public sealed record AddedSourcePlan(
    AddedSourceFile File,
    AddedSkip? Skip,
    SidecarStamp? Stamp,
    SourceOffsetRecord? Record
)
{
    public bool IsWritten => Skip is null;

    /// <summary>Whether its sidecar was saved over since the draft began.</summary>
    public bool IsChanged => Stamp is { } stamp && SidecarStamp.Of(stamp.Path) != stamp;

    /// <summary>The shift, set for a source that is written.</summary>
    public double? OffsetMilliseconds => IsWritten ? Record?.Reading.OffsetMilliseconds : null;

    public static IReadOnlyList<AddedSourcePlan> Of(
        IReadOnlyList<AddedSourceFile> files,
        SourceOffsetStore offsets,
        WrittenWarps written
    ) => [.. files.Select(file => Of(file, offsets.Of(file.SourceKey), written))];

    private static AddedSourcePlan Of(
        AddedSourceFile file,
        SourceOffsetRecord? record,
        WrittenWarps written
    )
    {
        AddedSourcePlan Skipped(AddedSkip skip) => new(file, skip, null, record);

        if (record is null || !record.IsUsableAt(file.Path))
            return Skipped(AddedSkip.Measuring);

        switch (record.Reading.Verdict)
        {
            case OffsetVerdict.Edit:
                return Skipped(AddedSkip.Edit);
            case OffsetVerdict.Drift:
                return Skipped(AddedSkip.Drift);
            case OffsetVerdict.NotTheSameRecording:
                return Skipped(AddedSkip.NotTheSameRecording);
        }

        return AnalysisFile.TryRead(StemFileName.AnalysisFor(file.Path), out var sidecar) switch
        {
            false => Skipped(AddedSkip.NoSidecar),
            true when sidecar.IsDefaultClipSaved && !written.IsCratesides(file.Path) => Skipped(
                AddedSkip.HasOwnWarp
            ),
            true => new AddedSourcePlan(file, null, SidecarStamp.Of(file.Path), record),
        };
    }
}
