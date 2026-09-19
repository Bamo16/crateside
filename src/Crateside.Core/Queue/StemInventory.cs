using Crateside.Core.Staging;

namespace Crateside.Core.Queue;

/// <summary>
/// What a track already has, read off disk and said in the vocabulary the plan speaks.
/// </summary>
/// <remarks>
/// Derived on every read and stored nowhere, so a file deleted by hand is simply outstanding
/// again.
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
    /// One of these can be shown and removed, but never asked for again — nothing knows what
    /// to ask for.
    /// </remarks>
    public IReadOnlyList<ClassifiedStem> Unrecognised =>
        [.. Files.Where(file => file.Kind is StemKind.Unrecognised)];

    /// <summary>
    /// What <paramref name="wanted"/> asks for that is not here yet.
    /// </summary>
    /// <remarks>
    /// A preset already on disk is <em>kept</em> and never cut again, so the separator is only
    /// ever asked for filenames that are free.
    /// </remarks>
    public StemPlan Outstanding(StemPlan wanted) =>
        new()
        {
            Presets = [.. wanted.Presets.Where(preset => !Presets.Contains(preset))],
            KeepSource = wanted.KeepSource && !HasSource,
        };

    /// <summary>Whether an added source has been fetched.</summary>
    /// <remarks>
    /// Matched on the upload, the only thing a rename cannot change. The label is asked only of a
    /// file carrying no provenance at all.
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
    /// The plan the track currently has, the only thing that says which files were added sources.
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
                    // The label is the only handle on an adopted sample: nothing can re-make it,
                    // so dropping it from the plan is the only way it ever goes.
                    { Kind: StemKind.Adopted, DisplayName: { } label } =>
                        !wanted.AdoptedSamples.Any(adopted =>
                            adopted.Label.Equals(label, StringComparison.OrdinalIgnoreCase)
                        ),
                    { Kind: StemKind.Source } => IsDropped(file, wanted, stored ?? wanted),
                    _ => false,
                }
            ),
        ];

    /// <remarks>
    /// Matched on the upload, not the label: a source download whose title ends in brackets reads
    /// as labelled and would be deleted as a dropped added source.
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

    /// <summary>What the separator called it, the user's label, or null for a source download.</summary>
    public string? DisplayName => File.Label ?? File.Preset ?? StemFileName.PresetOf(File.Path);

    /// <summary>
    /// What one file is: the preset it came from, the source track, or the warp guide.
    /// </summary>
    /// <remarks>
    /// Provenance first; the filename is asked only where provenance carries no preset at all,
    /// and only an exact catalog match is believed.
    /// </remarks>
    public static ClassifiedStem Of(StemOnDisk file, PresetCatalog catalog) =>
        file switch
        {
            // Before the catalog is consulted: an adopted sample carries a label of the user's
            // choosing, which nothing can be asked to make again (ADR 0022).
            { IsAdopted: true } => new() { File = file, Kind = StemKind.Adopted },
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

    /// <summary>A file crateside took into the track rather than made (ADR 0022).</summary>
    Adopted,
}
