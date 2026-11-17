namespace Crateside.Core.Staging;

/// <summary>A track's files as they currently sit in <c>Staging</c>.</summary>
public interface IStagedFiles
{
    IReadOnlyList<StagedFile> Of(string trackKey);

    /// <summary>Several tracks' files at once; an implementation that can read them in one pass does.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<StagedFile>> Of(
        IReadOnlyCollection<string> trackKeys
    ) => trackKeys.ToDictionary(key => key, Of);
}

/// <summary>Staging read fresh on every call.</summary>
public sealed class StagedFiles(StagingReader reader, CratesidePaths paths) : IStagedFiles
{
    public IReadOnlyList<StagedFile> Of(string trackKey) =>
        reader.Read(paths).FirstOrDefault(song => song.SourceTrackKey == trackKey)?.Stems ?? [];

    public IReadOnlyDictionary<string, IReadOnlyList<StagedFile>> Of(
        IReadOnlyCollection<string> trackKeys
    )
    {
        var songs = reader.Read(paths);

        return trackKeys.ToDictionary(
            key => key,
            key => songs.FirstOrDefault(song => song.SourceTrackKey == key)?.Stems ?? []
        );
    }
}
