using Crateside.Core.Samples;
using Crateside.Core.Staging;

namespace Crateside.Core.Queue;

/// <summary>
/// What a track already has, read off disk on every read and said in the vocabulary the plan
/// speaks.
/// </summary>
public sealed record StemInventory
{
    public static StemInventory Empty { get; } = new() { Files = [] };

    /// <summary>Every file of this track, with what each one is.</summary>
    public required IReadOnlyList<ClassifiedStem> Files { get; init; }

    /// <summary>
    /// Read against a catalog that could say which preset each file is. When false, the whole plan
    /// looks missing and must not be believed.
    /// </summary>
    public bool IsClassified { get; init; }

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
    /// Files carrying a preset the separator no longer offers, which can be removed but never
    /// asked for again.
    /// </summary>
    public IReadOnlyList<ClassifiedStem> Unrecognised =>
        [.. Files.Where(file => file.Kind is StemKind.Unrecognised)];

    /// <summary>
    /// What <paramref name="wanted"/> asks for that is not here yet; a preset on disk is never cut
    /// again (ADR 0027).
    /// </summary>
    public StemPlan Shortfall(StemPlan wanted) =>
        new()
        {
            Presets = [.. wanted.Presets.Where(preset => !Presets.Contains(preset))],
            KeepSource = wanted.KeepSource && !HasSource,
        };

    /// <summary>
    /// Whether an added source has been fetched, matched on the upload, which a rename cannot
    /// change, and on the label only without provenance.
    /// </summary>
    public bool Has(AddedSource added) =>
        Files.Any(file =>
            file.Kind is StemKind.Source
            && (
                file.File.SourceVideoId is { } upload
                    ? upload.Equals(added.VideoId, StringComparison.Ordinal)
                    : added.Label.Equals(file.DisplayName, StringComparison.OrdinalIgnoreCase)
            )
        );

    /// <summary>
    /// The files <paramref name="wanted"/> no longer asks for, which a re-cut removes. Never the
    /// warp guide, which is not the plan's to drop.
    /// </summary>
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

    /// <summary>
    /// Matched on the upload, never the label: a source download whose title ends in brackets
    /// reads as labelled.
    /// </summary>
    private static bool IsDropped(ClassifiedStem file, StemPlan wanted, StemPlan stored) =>
        file.File.SourceVideoId is { } upload
        && stored.AddedSources.Any(added => added.VideoId.Equals(upload, StringComparison.Ordinal))
            ? !wanted.AddedSources.Any(added =>
                added.VideoId.Equals(upload, StringComparison.Ordinal)
            )
            : !wanted.KeepSource;

    /// <summary>Reads a track's files back as presets, a source, and a guide.</summary>
    public static StemInventory Of(IEnumerable<StemOnDisk> files, PresetCatalog catalog) =>
        new()
        {
            Files = [.. files.Select(file => ClassifiedStem.Of(file, catalog))],
            IsClassified = !catalog.IsEmpty,
        };
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
    /// What one file is, from its provenance first; the filename is asked only when that carries
    /// no preset, and only an exact catalog match counts.
    /// </summary>
    public static ClassifiedStem Of(StemOnDisk file, PresetCatalog catalog) =>
        file switch
        {
            // Before the catalog is consulted: an adopted sample carries a label of the user's
            // choosing, which nothing can be asked to make again (ADR 0022).
            { IsAdopted: true } => new() { File = file, Kind = StemKind.Adopted },
            { Preset: { } guide } when StagedFile.IsWarpGuidePreset(guide) => new()
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
