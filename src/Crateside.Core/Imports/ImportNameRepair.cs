using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Imports;

/// <summary>
/// Puts the tag-built name on the files of imported tracks that were staged under their file name,
/// through the same rename a person's Rename uses (ADR 0079).
/// </summary>
public sealed class ImportNameRepair(
    ResolvedNameStore names,
    TrackInstructionStore instructions,
    TrackRename rename
)
{
    /// <summary>Tracks renamed, and those the rename refused (an open set, a clash) for a later pass.</summary>
    public (IReadOnlyList<string> Renamed, IReadOnlyList<string> Refused) Run()
    {
        var stated = instructions
            .Read()
            .Where(track => track.Name is { Length: > 0 })
            .Select(track => track.TrackKey)
            .ToHashSet(StringComparer.Ordinal);

        List<string> renamed = [];
        List<string> refused = [];

        foreach (var (trackKey, resolved) in names.Read())
        {
            if (TrackKeys.IsVideoId(trackKey) || stated.Contains(trackKey))
                continue;

            var name = StemFileName.FileSafe(resolved.DisplayTitle);

            if (rename.PlanFor(trackKey, name) is not { Renaming.Count: > 0 } plan)
                continue;

            if (plan.Refusal is not null)
                refused.Add(trackKey);
            else if (rename.Revert(trackKey, name) is { Succeeded: true })
                renamed.Add(trackKey);
            else
                refused.Add(trackKey);
        }

        return (renamed, refused);
    }
}
