using Crateside.Core.Staging;

namespace Crateside.Core.Queue;

/// <summary>
/// What a track already has, read off disk and said in the vocabulary the plan speaks.
/// </summary>
/// <remarks>
/// The other half of <see cref="PresetCatalog"/>: the plan asks for <c>vocal_full</c> and the
/// files answer <c>Vocal - Full</c>. Derived on every read and stored nowhere, so a file deleted
/// by hand is simply outstanding again.
/// </remarks>
public sealed record StemInventory
{
    public static StemInventory Empty { get; } = new() { Files = [] };

    /// <summary>Every file of this track, with what each one is.</summary>
    public required IReadOnlyList<ClassifiedStem> Files { get; init; }

    /// <summary>The presets this track already has a file for.</summary>
    public IReadOnlySet<string> Presets =>
        Files
            .Select(file => file.PresetId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The source track is on disk as a sample in its own right.</summary>
    public bool HasSource => Files.Any(file => file.Kind is StemKind.Source);

    /// <summary>A warp guide is on disk, so a re-cut need not make another (ADR 0018).</summary>
    public bool HasWarpGuide => Files.Any(file => file.Kind is StemKind.WarpGuide);

    /// <summary>
    /// Files carrying a preset the separator no longer offers.
    /// </summary>
    /// <remarks>
    /// Kept apart rather than folded in with the sources, which is the mistake that made 68
    /// pre-1.0 stems read as source tracks. One of these can be shown and removed; it can never
    /// be asked for again, because nothing knows what to ask for.
    /// </remarks>
    public IReadOnlyList<ClassifiedStem> Unrecognised =>
        [.. Files.Where(file => file.Kind is StemKind.Unrecognised)];

    /// <summary>
    /// What <paramref name="wanted"/> asks for that is not here yet.
    /// </summary>
    /// <remarks>
    /// The whole of an additive re-cut. A preset already on disk is <em>kept</em> and never cut
    /// again, so the separator is only ever asked for filenames that are free and the handoff
    /// cannot collide with what is already staged.
    /// </remarks>
    public StemPlan Outstanding(StemPlan wanted) =>
        new()
        {
            Presets = [.. wanted.Presets.Where(preset => !Presets.Contains(preset))],
            KeepSource = wanted.KeepSource && !HasSource,
        };

    /// <summary>Whether an added source has been fetched.</summary>
    /// <remarks>
    /// Matched on the upload, which is what <see cref="Superseded"/> already matches on and the
    /// only thing a rename cannot change. The label was the join once, and renaming a file in
    /// Ableton — which the row invites — parted it from the plan and fetched the upload again.
    /// The label is asked only of a file carrying no provenance at all.
    /// </remarks>
    public bool Has(AddedSource added) =>
        Files.Any(file =>
            file.Kind is StemKind.Source
            && (
                file.File.SourceVideoId is { } upload
                    ? upload.Equals(added.VideoId, StringComparison.Ordinal)
                    : added.Label.Equals(file.DisplayName, StringComparison.OrdinalIgnoreCase)
            )
        );

    /// <summary>The files <paramref name="wanted"/> no longer asks for, which a re-cut removes.</summary>
    /// <remarks>
    /// The warp guide is never among them: it is used up rather than kept, and is not the plan's
    /// to drop.
    /// </remarks>
    /// <param name="stored">
    /// The plan the track currently has, which is the only thing that says which files were
    /// added sources — a dropped one is gone from <paramref name="wanted"/>, and its file is
    /// otherwise indistinguishable from the separation source.
    /// </param>
    public IReadOnlyList<ClassifiedStem> Superseded(StemPlan wanted, StemPlan? stored = null) =>
        [
            .. Files.Where(file =>
                file switch
                {
                    { Kind: StemKind.Preset, PresetId: { } preset } => !wanted.Presets.Contains(
                        preset,
                        StringComparer.OrdinalIgnoreCase
                    ),
                    { Kind: StemKind.Source } => IsDropped(file, wanted, stored ?? wanted),
                    _ => false,
                }
            ),
        ];

    /// <remarks>
    /// Matched on the upload rather than the label, because a source download whose own title
    /// ends in brackets — <c>Schala's Theme (Instrumental)</c> — reads as labelled and would be
    /// deleted as an added source the plan no longer names.
    /// </remarks>
    private static bool IsDropped(ClassifiedStem file, StemPlan wanted, StemPlan stored) =>
        file.File.SourceVideoId is { } upload
        && stored.AddedSources.Any(added => added.VideoId.Equals(upload, StringComparison.Ordinal))
            ? !wanted.AddedSources.Any(added =>
                added.VideoId.Equals(upload, StringComparison.Ordinal)
            )
            : !wanted.KeepSource;

    /// <summary>Reads a track's files back as presets, a source, and a guide.</summary>
    public static StemInventory Of(IEnumerable<StemOnDisk> files, PresetCatalog catalog) =>
        new() { Files = [.. files.Select(file => ClassifiedStem.Of(file, catalog))] };
}

/// <summary>One of a track's files, and what it is to the plan.</summary>
public sealed record ClassifiedStem
{
    public required StemOnDisk File { get; init; }

    public required StemKind Kind { get; init; }

    /// <summary>What the plan would ask for to make this file, or null when it cannot ask.</summary>
    public string? PresetId { get; init; }

    /// <summary>What the separator called it, or null for a source download.</summary>
    public string? DisplayName => File.Preset ?? StemFileName.PresetOf(File.Path);

    /// <summary>
    /// What one file is: the preset it came from, the source track, or the warp guide.
    /// </summary>
    /// <remarks>
    /// Provenance first, because StemForge writes it and it survives a rename. The filename is
    /// asked only where provenance carries no preset at all — StemForge before 1.0 wrote none,
    /// leaving the name as the only thing that says what 68 files in this library are — and only
    /// an exact catalog match is believed, so a source download called
    /// <c>SUPADOPA (Official Instrumental)</c> stays a source.
    /// </remarks>
    public static ClassifiedStem Of(StemOnDisk file, PresetCatalog catalog) =>
        file switch
        {
            { Preset: { } guide } when StagingStem.IsWarpGuidePreset(guide) => new()
            {
                File = file,
                Kind = StemKind.WarpGuide,
            },
            { Preset: { } written } => catalog.TryGetId(written, out var id)
                ? new()
                {
                    File = file,
                    Kind = StemKind.Preset,
                    PresetId = id,
                }
                : new() { File = file, Kind = StemKind.Unrecognised },
            _ when catalog.TryGetId(StemFileName.PresetOf(file.Path), out var named) => new()
            {
                File = file,
                Kind = StemKind.Preset,
                PresetId = named,
            },
            _ => new() { File = file, Kind = StemKind.Source },
        };
}

/// <summary>What a file on disk is to the plan.</summary>
public enum StemKind
{
    /// <summary>A stem the plan can ask for again by name.</summary>
    Preset,

    /// <summary>The download itself, kept whole — a sample and no kind of stem (ADR 0004).</summary>
    Source,

    /// <summary>The drums, made to be looked at and deleted after warping.</summary>
    WarpGuide,

    /// <summary>A stem carrying a preset the separator no longer offers.</summary>
    Unrecognised,
}
