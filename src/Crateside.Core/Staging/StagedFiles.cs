namespace Crateside.Core.Staging;

/// <summary>A track's files as they currently sit in <c>Staging</c>.</summary>
public interface IStagedFiles
{
    IReadOnlyList<StagedFile> Of(string videoId);
}

/// <summary>Staging read fresh on every call.</summary>
public sealed class StagedFiles(StagingReader reader, CratesidePaths paths) : IStagedFiles
{
    public IReadOnlyList<StagedFile> Of(string videoId) =>
        reader.Read(paths).FirstOrDefault(song => song.SourceVideoId == videoId)?.Stems ?? [];
}
