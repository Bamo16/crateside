using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Verdict;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Ledger;

/// <summary>
/// Startup step: a person's decision still in its JSON file moves into the ledger's table when the
/// table is empty, and the file is renamed <c>.imported-&lt;yyyyMMdd&gt;</c>, not deleted (ADR 0082).
/// </summary>
public sealed class DecisionsImport(CratesidePaths paths, LedgerStore ledger, TimeProvider time)
{
    /// <summary>Imports every pending file; returns what happened to each file that was there.</summary>
    public IReadOnlyList<DecisionFileImport> Run() =>
        [
            .. new[]
            {
                ImportFile(
                    paths.PageChoicesFile,
                    LegacyDecisionJsonContext.Default.ListPageChoicesRecord,
                    context => !context.PageChoices.Any() && !context.SourceRemovals.Any(),
                    AddPageChoices
                ),
                ImportFile(
                    paths.TrackMarksFile,
                    TrackMarkJsonContext.Default.ListTrackMarkRecord,
                    context => !context.TrackMarks.Any(),
                    AddTrackMarks
                ),
                ImportFile(
                    paths.GenresFile,
                    GenreJsonContext.Default.ListGenreRecord,
                    context => !context.Genres.Any(),
                    AddGenres
                ),
                ImportFile(
                    paths.FamiliarityFile,
                    FamiliarityJsonContext.Default.ListFamiliarityRecord,
                    context => !context.Familiarities.Any(),
                    AddFamiliarities
                ),
            }.OfType<DecisionFileImport>(),
        ];

    private DecisionFileImport? ImportFile<TFile>(
        string path,
        JsonTypeInfo<TFile> format,
        Func<LedgerContext, bool> isEmpty,
        Func<LedgerContext, TFile, int> add
    )
    {
        var name = Path.GetFileName(path);

        if (!File.Exists(path))
            return null;

        if (format.ReadFileOrDefault(path) is not { } file)
            return new DecisionFileImport(name, 0, DecisionFileOutcome.Unreadable);

        int? count = null;

        ledger.Save(context => count = isEmpty(context) ? add(context, file) : null);

        if (count is not { } rows)
            return new DecisionFileImport(name, 0, DecisionFileOutcome.TableNotEmpty);

        return new DecisionFileImport(
            name,
            rows,
            RenameImported(path)
                ? DecisionFileOutcome.Imported
                : DecisionFileOutcome.ImportedNotRenamed
        );
    }

    private bool RenameImported(string path)
    {
        var target = $"{path}.imported-{time.GetLocalNow():yyyyMMdd}";

        for (var i = 2; File.Exists(target); i++)
            target = $"{path}.imported-{time.GetLocalNow():yyyyMMdd}-{i}";

        try
        {
            File.Move(path, target);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int AddPageChoices(LedgerContext context, List<PageChoicesRecord> file)
    {
        foreach (var record in file)
        {
            context.PageChoices.AddRange(
                record.Choices.Select(choice => PageChoiceRow.From(record.TrackKey, choice))
            );
            context.SourceRemovals.AddRange(
                (record.Removed ?? []).Select(removal =>
                    SourceRemovalRow.From(record.TrackKey, removal)
                )
            );
        }

        return file.Sum(record => record.Choices.Count + (record.Removed?.Count ?? 0));
    }

    private static int AddTrackMarks(LedgerContext context, List<TrackMarkRecord> file)
    {
        var rows = file.SelectMany(record =>
                record.Marks.Select(mark => new TrackMarkRow { Song = record.Song, Mark = mark })
            )
            .DistinctBy(row => (row.Song.ToUpperInvariant(), row.Mark))
            .ToList();

        context.TrackMarks.AddRange(rows);

        return rows.Count;
    }

    private static int AddGenres(LedgerContext context, List<GenreRecord> file)
    {
        var rows = file.Select(GenreRow.From)
            .DistinctBy(row => row.Song.ToUpperInvariant())
            .ToList();

        context.Genres.AddRange(rows);

        return rows.Count;
    }

    private static int AddFamiliarities(LedgerContext context, List<FamiliarityRecord> file)
    {
        var rows = file.Select(FamiliarityRow.From)
            .DistinctBy(row => row.Song.ToUpperInvariant())
            .ToList();

        context.Familiarities.AddRange(rows);

        return rows.Count;
    }
}

public enum DecisionFileOutcome
{
    /// <summary>Its rows are in the ledger and the file was renamed.</summary>
    Imported,

    /// <summary>Its rows are in the ledger but the file could not be renamed.</summary>
    ImportedNotRenamed,

    /// <summary>The table already held rows, so the file was left alone.</summary>
    TableNotEmpty,

    /// <summary>The file could not be read, so it was left alone.</summary>
    Unreadable,
}

public sealed record DecisionFileImport(string File, int Rows, DecisionFileOutcome Outcome);

/// <summary>One track's choices, as <c>page-choices.json</c> kept them.</summary>
internal sealed record PageChoicesRecord(string TrackKey, List<PageChoice> Choices)
{
    public List<SourceRemoval>? Removed { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(List<PageChoicesRecord>))]
internal sealed partial class LegacyDecisionJsonContext : JsonSerializerContext { }
