using Crateside.Core.Staging;

namespace Crateside.Tests.Orchestration.Edits;

/// <summary>The same files for every track, read from the list on each call.</summary>
internal sealed class ListedStagedFiles(Func<IReadOnlyList<StagedFile>> files) : IStagedFiles
{
    public IReadOnlyList<StagedFile> Of(string videoId) => files();
}
