using Crateside.Core.Queue;
using Humanizer;

namespace Crateside.Core.Tracks;

/// <summary>
/// Every file a track could have, and whether the user wants it.
/// </summary>
/// <remarks>
/// The stem list <em>is</em> the picker: a line is either on disk or not, and the tick says
/// whether it is wanted. The two cross to give <see cref="StemState"/>.
/// </remarks>
public sealed record StemPicker
{
    public required IReadOnlyList<StemBand> Bands { get; init; }

    /// <summary>What the user has ticked, which is what a confirm would record.</summary>
    public required StemPlan Wanted { get; init; }

    /// <summary>
    /// A cut is running or queued, so a tick records the want at once and unticking waits.
    /// </summary>
    /// <remarks>
    /// Nothing says which wanted lines the running cut is making: its recipe was read at pickup,
    /// and one ticked since is made only by a re-cut once it lands (ADR 0027).
    /// </remarks>
    public bool IsCutInFlight { get; init; }

    public IEnumerable<StemChoice> Lines => Bands.SelectMany(band => band.Lines);

    /// <summary>Samples to be made. The warp guide is not one — it is not kept.</summary>
    public int Adding => Lines.Count(line => line is { State: StemState.Coming, IsSample: true });

    /// <summary>Staged files the plan has stopped asking for.</summary>
    public int Removing => Lines.Count(line => line.State is StemState.Going);

    /// <summary>Files already made that the plan still wants, and that nothing will touch.</summary>
    public int Keeping => Lines.Count(line => line is { State: StemState.Kept, IsSample: true });

    /// <summary>Nothing is lost: every file already made stays where it is.</summary>
    public bool IsAdditive => Removing is 0;

    /// <summary>Whether pressing confirm would do anything at all.</summary>
    public bool ChangesAnything => Adding > 0 || Removing > 0;

    /// <summary>
    /// The files a track could have, in the separator's own vocabulary.
    /// </summary>
    /// <param name="wanted">
    /// The plan being edited, which is the row's own until the user starts ticking.
    /// </param>
    public static StemPicker For(TrackRow row, PresetCatalog catalog, StemPlan? wanted = null)
    {
        var plan = wanted ?? row.Plan;

        var byPreset = row
            .Samples.Where(sample => sample.PresetId is not null)
            .ToLookup(sample => sample.PresetId!, StringComparer.OrdinalIgnoreCase);

        var sources = row.Samples.Where(sample => sample.Kind is StemKind.Source).ToArray();

        // Files belonging to an added source, so the separation source line does not claim them.
        var added = sources
            .Where(sample =>
                plan.AddedSources.Any(source =>
                    source.VideoId.Equals(sample.SourceVideoId, StringComparison.Ordinal)
                )
            )
            .ToHashSet();

        List<StemBand> bands =
        [
            .. catalog
                .Presets.Where(preset =>
                    !Unoffered.Contains(preset.Id, StringComparer.OrdinalIgnoreCase)
                    || byPreset[preset.Id].Any()
                )
                .GroupBy(preset => preset.Category, StringComparer.OrdinalIgnoreCase)
                .Select(group => new StemBand(
                    group.Key.Singularize(inputIsKnownToBePlural: false),
                    [
                        .. group.Select(preset => new StemChoice
                        {
                            Label = preset.DisplayName,
                            Description = preset.Description,
                            PresetId = preset.Id,
                            Kind = StemKind.Preset,
                            IsWanted = plan.Presets.Contains(
                                preset.Id,
                                StringComparer.OrdinalIgnoreCase
                            ),
                            Files = [.. byPreset[preset.Id]],
                        }),
                    ]
                )),
            new StemBand(
                "Sources",
                [
                    new StemChoice
                    {
                        Label = $"{row.Title} (Source)",
                        Description = "copied, not separated · costs nothing",
                        Kind = StemKind.Source,
                        IsWanted = plan.KeepSource,
                        Files = [.. sources.Where(sample => !added.Contains(sample))],
                    },
                    .. plan.AddedSources.Select(source => new StemChoice
                    {
                        Label = $"{row.Title} ({source.Label})",
                        Description = "added by you · downloaded whole, never separated",
                        Kind = StemKind.Source,
                        AddedSource = source,
                        IsWanted = true,
                        Files =
                        [
                            .. sources.Where(sample =>
                                source.VideoId.Equals(
                                    sample.SourceVideoId,
                                    StringComparison.Ordinal
                                )
                            ),
                        ],
                    }),
                ]
            ),
        ];

        // Read off the lines rather than the recipe, so the panel and the separator cannot
        // disagree about whether a guide is being cut.
        var making = bands
            .SelectMany(band => band.Lines)
            .Any(line => line.State is StemState.Coming);

        bands.Add(
            new StemBand(
                "Warp guide",
                [
                    new StemChoice
                    {
                        Label = "Drums",
                        Description = "always made · delete it once the track is warped",
                        Kind = StemKind.WarpGuide,
                        IsWanted = row.HasWarpGuide || making,
                        Files = row.HasWarpGuide ? [Guide] : [],
                    },
                ]
            )
        );

        if (
            row.Samples.Where(sample => sample.Kind is StemKind.Unrecognised).ToArray() is
            { Length: > 0 } orphaned
        )
            bands.Add(
                new StemBand(
                    "No longer offered",
                    [
                        .. orphaned.Select(sample => new StemChoice
                        {
                            Label = sample.Label,
                            Description =
                                "the separator no longer offers this preset, so it cannot be cut again",
                            Kind = StemKind.Unrecognised,
                            IsWanted = true,
                            Files = [sample],
                        }),
                    ]
                )
            );

        return new StemPicker
        {
            Bands = [.. bands.Where(band => band.Lines.Count > 0)],
            Wanted = plan,
            IsCutInFlight = row.IsCutInFlight,
        };
    }

    /// <summary>
    /// Presets the picker leaves out, because nothing in this workflow wants them.
    /// </summary>
    /// <remarks>
    /// Left in the catalog rather than filtered out of it: the catalog is what says a file on disk
    /// <em>is</em> a preset, so removing them would make an already-cut stem read as unrecognised.
    /// </remarks>
    private static readonly string[] Unoffered = ["vocal_rvc", "instrumental_low_resource"];

    /// <summary>Stands in for the guide's file, which the row counts but does not list.</summary>
    /// <remarks>
    /// <see cref="TrackRow.Samples"/> excludes warp guides, so the only thing the row can say
    /// about one is that it is there.
    /// </remarks>
    private static readonly TrackSample Guide = new()
    {
        Label = "Drums",
        Kind = StemKind.WarpGuide,
    };
}

/// <summary>A run of lines under one heading — the separator's categories, then the rest.</summary>
public sealed record StemBand(string Label, IReadOnlyList<StemChoice> Lines);

/// <summary>One file a track could have, and whether it does.</summary>
public sealed record StemChoice
{
    /// <summary>What the separator calls it, which is what the filename and Ableton show.</summary>
    public required string Label { get; init; }

    public required StemKind Kind { get; init; }

    /// <summary>The preset to tick, or null for a line that is not chosen by name.</summary>
    public string? PresetId { get; init; }

    /// <summary>The source this line stands for, when the user attached it.</summary>
    public AddedSource? AddedSource { get; init; }

    public required bool IsWanted { get; init; }

    /// <summary>
    /// The files on disk for this line. More than one where a source swap left the old upload's
    /// samples behind under the same preset (ADR 0014).
    /// </summary>
    public required IReadOnlyList<TrackSample> Files { get; init; }

    /// <summary>What the preset is for, in the separator's words. Null for anything else.</summary>
    public string? Description { get; init; }

    public bool IsOnDisk => Files.Count > 0;

    /// <summary>A file meant to be kept, as opposed to a guide that is used up (ADR 0004).</summary>
    public bool IsSample => Kind is StemKind.Preset or StemKind.Source;

    /// <summary>
    /// Whether ticking it is the user's to do.
    /// </summary>
    /// <remarks>
    /// The guide is never chosen, a dropped preset never asked for again, and a file in
    /// <c>Mashup Samples</c> never unticked (ADR 0019). Adding stays open either way.
    /// </remarks>
    public bool IsPinned => !IsSample || IsInLibrary;

    public bool IsInLibrary => Files.Any(file => file.IsInLibrary);

    public string? AbletonKey =>
        Files.Select(file => file.AbletonKey).FirstOrDefault(key => key is not null);

    /// <summary>The uploads this line's files were cut from, where they differ (ADR 0014).</summary>
    public IReadOnlyList<string> Uploads =>
        [
            .. Files
                .Select(file => file.SourceVideoId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];

    public StemState State =>
        (IsOnDisk, IsWanted) switch
        {
            (true, true) => StemState.Kept,
            (false, true) => StemState.Coming,
            (true, false) => StemState.Going,
            _ => StemState.NotMade,
        };
}

/// <summary>Where a line stands between what is on disk and what is ticked.</summary>
public enum StemState
{
    /// <summary>Made already, still wanted. Nothing touches it.</summary>
    Kept,

    /// <summary>Wanted and not made. What a cut would produce.</summary>
    Coming,

    /// <summary>Made and no longer wanted. What a cut would delete.</summary>
    Going,

    /// <summary>Neither. The ordinary state of most of the catalog.</summary>
    NotMade,
}
