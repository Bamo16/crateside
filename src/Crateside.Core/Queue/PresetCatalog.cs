using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Crateside.Core.Queue;

/// <summary>
/// What the separator can make, and what it calls each one.
/// </summary>
/// <remarks>
/// The link between the two vocabularies a track is described in: a <see cref="Queue.StemPlan"/>
/// says <c>vocal_full</c>, and the file it produces says <c>Vocal - Full</c>. The rule relating
/// them is StemForge's — it follows the audio-separator catalog underneath — so it is read rather
/// than mirrored (ADR 0018).
/// </remarks>
public sealed record PresetCatalog
{
    /// <summary>What a read that could not reach the separator produces.</summary>
    public static PresetCatalog Empty { get; } = new() { Presets = [] };

    public required IReadOnlyList<SeparatorPreset> Presets { get; init; }

    public bool IsEmpty => Presets.Count is 0;

    /// <summary>The preset a plan's id names, or null when the separator no longer offers it.</summary>
    public SeparatorPreset? ById(string id) =>
        Presets.FirstOrDefault(preset => preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The id behind a name the separator wrote, which is the join a file on disk needs.
    /// </summary>
    /// <remarks>
    /// A warp guide never resolves: <c>Drums - htdemucs_ft</c> carries the model name, is not a
    /// catalog preset, and is not something the plan can ask for — see
    /// <see cref="Staging.StagingStem.IsWarpGuidePreset"/>.
    /// </remarks>
    public bool TryGetId(string? displayName, [NotNullWhen(true)] out string? id)
    {
        id = displayName is null
            ? null
            : Presets
                .FirstOrDefault(preset =>
                    preset.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)
                )
                ?.Id;

        return id is not null;
    }
}

/// <summary>One thing the separator can make.</summary>
public sealed record SeparatorPreset
{
    /// <summary>What the plan and the command line call it: <c>vocal_full</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// What the separator calls it: <c>Vocal - Full</c>. What ends up in the filename, in the
    /// file's provenance, and in Ableton — so it is the only name the user is ever shown.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>The separator's grouping, plural as it says it: <c>Vocals</c>, <c>Instrumentals</c>.</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>What the preset is for, in the separator's own words.</summary>
    /// <remarks>
    /// Worth showing: the picker offers nine presets where the app used to ask for two, and this
    /// is the only thing that tells <c>Clean</c> from <c>Balanced</c>.
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
