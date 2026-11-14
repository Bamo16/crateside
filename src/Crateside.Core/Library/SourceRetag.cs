using System.Diagnostics.CodeAnalysis;
using Crateside.Core.Samples;

namespace Crateside.Core.Library;

/// <summary>A reviewed match of a library song, by its file name without the preset, to its video.</summary>
public sealed record SourceMatch(string Song, string VideoId)
{
    public string Source => $"https://www.youtube.com/watch?v={VideoId}";
}

public enum RetagFate
{
    Retag,
    AlreadyTagged,
    HasOtherSource,
}

public sealed record RetagFile(string Path, SourceMatch Match, RetagFate Fate);

/// <summary>
/// Appends <c>| source: &lt;url&gt;</c> to the comment of the stems StemForge cut before it recorded
/// a source, from a reviewed list of matches (#148). A file that names a source is never touched.
/// </summary>
public static class SourceRetag
{
    /// <summary>One match per line as <c>&lt;videoId&gt; &lt;song&gt;</c>; blank lines and <c>#</c> lines are skipped.</summary>
    public static bool TryParse(
        string text,
        [NotNullWhen(true)] out IReadOnlyList<SourceMatch>? matches,
        [NotNullWhen(false)] out string? problem
    )
    {
        List<SourceMatch> parsed = [];

        foreach (
            var line in text.Split('\n', StringSplitOptions.TrimEntries)
                .Where(line => line is { Length: > 0 } && line[0] != '#')
        )
        {
            if (
                line.Split(' ', 2, StringSplitOptions.TrimEntries)
                    is not [var id, { Length: > 0 } song]
                || !YouTubeUrl.TryGetVideoId(id, out var videoId)
                || videoId != id
            )
            {
                matches = null;
                problem = $"Not a '<videoId> <song>' line: {line}";

                return false;
            }

            parsed.Add(new SourceMatch(song, id));
        }

        matches = parsed;
        problem = null;

        return true;
    }

    /// <summary>Every StemForge-cut file in the library of a matched song, and what applying would do to it.</summary>
    public static IReadOnlyList<RetagFile> Plan(
        string libraryFolder,
        IReadOnlyList<SourceMatch> matches
    )
    {
        if (!Directory.Exists(libraryFolder))
            return [];

        var bySong = matches.ToDictionary(match => match.Song, StringComparer.Ordinal);

        return
        [
            .. new DirectoryInfo(libraryFolder)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(file => SampleFiles.IsAudio(file.Name))
                .Select(file => (file.FullName, StemTagReader.Read(file)?.Provenance))
                .Where(file =>
                    file.Provenance is { IsAdopted: false, Label: null }
                    && (file.Provenance.Preset ?? file.Provenance.Model) is not null
                )
                .Select(file =>
                    (
                        file.FullName,
                        file.Provenance,
                        Match: bySong.GetValueOrDefault(StemFileName.WithoutPreset(file.FullName))
                    )
                )
                .Where(file => file.Match is not null)
                .Select(file => new RetagFile(
                    file.FullName,
                    file.Match!,
                    file.Provenance switch
                    {
                        { SourceUrl: null } => RetagFate.Retag,
                        { SourceTrackKey: { } existing } when existing == file.Match!.VideoId =>
                            RetagFate.AlreadyTagged,
                        _ => RetagFate.HasOtherSource,
                    }
                ))
                .OrderBy(file => file.Path, StringComparer.Ordinal),
        ];
    }

    /// <summary>Writes the source onto a file that still names none; the comment is read again so nothing is appended twice.</summary>
    public static bool TryWrite(RetagFile file, [NotNullWhen(false)] out string? problem)
    {
        try
        {
            using var tagged = TagLib.File.Create(file.Path);

            if (StemProvenance.Parse(tagged.Tag.Comment) is not { SourceUrl: null })
            {
                problem = "The comment already names a source, or is not a StemForge one.";

                return false;
            }

            tagged.Tag.Comment = $"{tagged.Tag.Comment} | source: {file.Match.Source}";
            tagged.Save();
            problem = null;

            return true;
        }
        catch (Exception ex)
            when (ex
                    is TagLib.CorruptFileException
                        or TagLib.UnsupportedFormatException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            problem = ex.Message;

            return false;
        }
    }
}
