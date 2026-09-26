using Crateside.Core.Queue;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;

namespace Crateside.Core.Orchestration.Edits;

/// <summary>Takes a file the user made into a track, where it already is (ADR 0022).</summary>
public sealed class SampleAdoption(QueueSettingsStore settings, CratesidePaths paths)
{
    /// <summary>
    /// Every sample in either root with no provenance to place it. Reads every file's tag, so never
    /// call it per render.
    /// </summary>
    public IReadOnlyList<string> Candidates() =>
        [
            .. new[] { paths.SampleStaging, paths.MashupSamples }
                .Where(Directory.Exists)
                .SelectMany(root =>
                    Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                )
                .Where(SampleFiles.IsAudio)
                .Where(path => StemTagReader.Read(path) is { Provenance: null })
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

    /// <summary>What adopting this file into the track would do, or why it cannot be done.</summary>
    public AdoptPlan PlanFor(TrackRowFacts track, string? path, SampleType? type) =>
        (path, StemFileName.PresetOf(path ?? string.Empty)) switch
        {
            (not { Length: > 0 }, _) => Refused("Choose the file to adopt."),
            (_, not { Length: > 0 }) => Refused(
                "The file needs a name ending in a label in brackets, such as (Instrumental - Mixed), "
                    + "which is what the track will call it."
            ),
            (_, { } label) when IsTaken(track, label) => Refused(
                $"This track already has a sample called {label}."
            ),
            _ when type is null => Refused("Say whether it is a vocal or an instrumental."),
            (_, { } label) => new AdoptPlan { Label = label },
        };

    /// <summary>Writes the provenance into the file, then names it on the track's plan.</summary>
    public string? Apply(TrackRowFacts track, string? path, SampleType? type)
    {
        var plan = PlanFor(track, path, type);

        if (plan is not { Label: { } label })
            return plan.Refusal;

        if (!StemAdoption.TryAdopt(path!, track.VideoId, label, type!.Value, out var problem))
            return problem;

        settings.SetPlan(track.VideoId, track.Plan.Keeping(new AdoptedSample { Label = label }));

        return null;
    }

    private static bool IsTaken(TrackRowFacts track, string label) =>
        track
            .Labels.Concat(track.Plan.AdoptedSamples.Select(adopted => adopted.Label))
            .Contains(label, StringComparer.OrdinalIgnoreCase);

    private static AdoptPlan Refused(string refusal) => new() { Refusal = refusal };
}

/// <summary>What adopting a file would do. Read before it happens, to say so on screen.</summary>
public sealed record AdoptPlan
{
    public string? Label { get; init; }

    public string? Refusal { get; init; }

    public bool IsPossible => Refusal is null && Label is not null;
}
