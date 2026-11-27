using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Playlist;

namespace Crateside.Core.Imports;

/// <summary>
/// Takes in a link that is not a YouTube video: <c>stemforge-cli download</c> fetches it and says
/// what the source is, which keys the track (ADR 0077).
/// </summary>
public sealed class LinkIntake(
    DownloadRunner downloads,
    TrackIntake files,
    IntakeStore intake,
    WorkingSet workingSet,
    CratesidePaths paths
)
{
    public const string StemForgeTooOld =
        "StemForge is too old: stemforge-cli download does not say what a source is. "
        + "Update stemforge-cli (StemForge's install task), then retry.";

    public async Task<IntakeOutcome> TakeInAsync(
        string url,
        Recipe? recipe = null,
        CancellationToken ct = default
    )
    {
        if (
            intake
                .Read()
                .FirstOrDefault(record =>
                    record.PageUrl?.Equals(url, StringComparison.Ordinal) is true
                ) is
            { } held
        )
            return new IntakeOutcome.AlreadyHeld(held.TrackKey, held.Title);

        var incoming = Path.Combine(paths.ImportsFolder, ".incoming", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(incoming);

        try
        {
            var result = await downloads.RunAsync(url, incoming, ct: ct);

            return result switch
            {
                { IsSuccess: false } => new IntakeOutcome.Refused(
                    result.Error ?? "the download failed"
                ),
                { Source: null } => new IntakeOutcome.Refused(StemForgeTooOld),
                { Source.ExtractorKey: "Youtube" } => new IntakeOutcome.Refused(
                    "That is a YouTube link crateside cannot read as one video."
                ),
                { Source.IsGeneric: true } => files.TakeInFile(
                    result.Path!,
                    IntakeMode.Move,
                    recipe,
                    url
                ),
                { Source: { Id: { Length: > 0 } id } source } => TakeInSite(
                    TrackKeys.ForSite(source.ExtractorKey, id),
                    result.Path!,
                    url,
                    source,
                    recipe
                ),
                _ => new IntakeOutcome.Refused("that source gave no ID to key the track by"),
            };
        }
        finally
        {
            TryDelete(incoming);
        }
    }

    private IntakeOutcome TakeInSite(
        string key,
        string downloaded,
        string url,
        DownloadedSource source,
        Recipe? recipe
    )
    {
        if (intake.Read().FirstOrDefault(record => record.TrackKey == key) is { } held)
            return new IntakeOutcome.AlreadyHeld(key, held.Title);

        var kept = Path.Combine(workingSet.Prepare(key), Path.GetFileName(downloaded));

        File.Move(downloaded, kept, overwrite: true);

        return files.TakeInSiteTrack(
            key,
            kept,
            url,
            new SiteFacts
            {
                Uploader = source.Uploader,
                UploadDate = source.UploadDate,
                ReleaseYear = source.ReleaseYear,
                Album = source.Album,
                AlbumArtist = source.AlbumArtist,
                TrackNumber = source.TrackNumber,
            },
            recipe
        );
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
