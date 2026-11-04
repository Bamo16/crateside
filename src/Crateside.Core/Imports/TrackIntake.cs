using Crateside.Core.Naming;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Imports;

/// <summary>
/// Takes an audio file in as a track keyed by its content: its name, release and cover come from
/// its own tags, and its plan from a recipe (ADR 0077).
/// </summary>
public sealed class TrackIntake(
    ImportStore imports,
    IntakeStore intake,
    ResolvedNameStore names,
    TrackInstructionStore instructions,
    RecipeStore recipes,
    CratesidePaths paths,
    TimeProvider time
)
{
    /// <summary>The file is copied, moved (the inbox, a download) or already in its place.</summary>
    public IntakeOutcome TakeInFile(
        string path,
        IntakeMode mode,
        Recipe? recipe = null,
        string? pageUrl = null
    )
    {
        string key;

        try
        {
            using var content = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );

            key = TrackKeys.ForFileContent(content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new IntakeOutcome.Refused(
                $"{Path.GetFileName(path)} could not be read: {ex.Message}"
            );
        }

        if (Known(key) is { } held)
        {
            if (mode is IntakeMode.Move)
                TryDelete(path);

            return new IntakeOutcome.AlreadyHeld(key, held.Title);
        }

        if (TrackTags.TryRead(path) is not { } tags)
            return new IntakeOutcome.Refused(
                $"{Path.GetFileName(path)} is not audio crateside can read"
            );

        var name = TrackTags.NameFor(tags, path);
        var stored = imports.Take(key, path, move: mode is IntakeMode.Move);

        Record(key, name, stored, tags, pageUrl, recipe);

        return new IntakeOutcome.TakenIn(key, name);
    }

    /// <summary>Takes in a track a site gave, whose audio is already in the working set.</summary>
    public IntakeOutcome TakeInSiteTrack(
        string key,
        string audioPath,
        string pageUrl,
        SiteFacts facts,
        Recipe? recipe = null
    )
    {
        if (Known(key) is { } held)
            return new IntakeOutcome.AlreadyHeld(key, held.Title);

        var tags = TrackTags.TryRead(audioPath);
        var name = SiteName(tags, facts, audioPath);
        var own = tags ?? new TrackTags();
        var merged = own with
        {
            Release = own.Release with
            {
                Album = own.Release.Album ?? facts.Album,
                AlbumArtist = own.Release.AlbumArtist ?? facts.AlbumArtist,
                Track = own.Release.Track ?? facts.TrackNumber,
            },
        };

        Record(key, name, audioPath, merged, pageUrl, recipe, facts);

        return new IntakeOutcome.TakenIn(key, name);
    }

    private static string SiteName(TrackTags? tags, SiteFacts facts, string audioPath) =>
        tags switch
        {
            // StemForge has already put the artist in the title it wrote.
            { Title: { } title, Artists: [_, ..] } => title,
            { Title: { } title } when facts.Uploader is { Length: > 0 } uploader =>
                $"{uploader} - {title}",
            { Title: { } title } => title,
            _ => TrackTags.NameFor(null, audioPath),
        };

    private void Record(
        string key,
        string name,
        string audioPath,
        TrackTags tags,
        string? pageUrl,
        Recipe? recipe,
        SiteFacts? facts = null
    )
    {
        var release = tags.Release with { Cover = SaveCover(key, tags) };

        if (release.Year is null && facts is not null)
            release = facts switch
            {
                { ReleaseYear: { } year } => release with
                {
                    Year = year,
                    YearText = $"{year}",
                    YearKind = YearKind.Release,
                },
                { UploadDate: { Length: 8 } day } when int.TryParse(day[..4], out var year) =>
                    release with
                    {
                        Year = year,
                        YearText = day,
                        YearKind = YearKind.Upload,
                    },
                _ => release,
            };

        intake.TakeIn(
            new IntakeRecord
            {
                TrackKey = key,
                Title = name,
                AddedAt = time.GetUtcNow(),
                PageUrl = pageUrl,
                Release = release,
            }
        );

        names.Save(
            key,
            new ResolvedName
            {
                DisplayTitle = name,
                BaseName = Path.GetFileNameWithoutExtension(audioPath),
            }
        );

        if ((recipe ?? recipes.Default) is var chosen && chosen is not Recipe.Standard)
            instructions.SetPlan(key, Recipes.PlanOf(chosen));
    }

    private string? SaveCover(string key, TrackTags tags)
    {
        if (tags is not { CoverBytes: { Length: > 0 } bytes })
            return null;

        var fileName =
            $"{key}{(tags.CoverMimeType?.Contains("png", StringComparison.OrdinalIgnoreCase) is true ? ".png" : ".jpg")}";

        try
        {
            Directory.CreateDirectory(paths.CoversFolder);
            File.WriteAllBytes(Path.Combine(paths.CoversFolder, fileName), bytes);

            return fileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IntakeRecord? Known(string key) =>
        intake
            .Read()
            .FirstOrDefault(record => record.TrackKey.Equals(key, StringComparison.Ordinal));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public enum IntakeMode
{
    /// <summary>The original stays where it is.</summary>
    Copy,

    /// <summary>The original is consumed: the inbox, or a download crateside made.</summary>
    Move,
}

/// <summary>What a source says about a track that is not a file's own tags.</summary>
public sealed record SiteFacts
{
    public string? Uploader { get; init; }

    public string? UploadDate { get; init; }

    public int? ReleaseYear { get; init; }

    public string? Album { get; init; }

    public string? AlbumArtist { get; init; }

    public int? TrackNumber { get; init; }
}

public abstract record IntakeOutcome
{
    private IntakeOutcome() { }

    public sealed record TakenIn(string TrackKey, string Name) : IntakeOutcome;

    public sealed record AlreadyHeld(string TrackKey, string Name) : IntakeOutcome;

    public sealed record Refused(string Reason) : IntakeOutcome;
}
