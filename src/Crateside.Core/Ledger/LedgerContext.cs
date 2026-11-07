using System.Globalization;
using System.Linq.Expressions;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Verdict;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crateside.Core.Ledger;

/// <summary>The ledger's SQLite schema (docs/ledger.md). Changed only by a migration.</summary>
internal sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
{
    public DbSet<RunRow> Runs => Set<RunRow>();

    public DbSet<RestatementRow> Restatements => Set<RestatementRow>();

    public DbSet<PageChoiceRow> PageChoices => Set<PageChoiceRow>();

    public DbSet<SourceRemovalRow> SourceRemovals => Set<SourceRemovalRow>();

    public DbSet<TrackMarkRow> TrackMarks => Set<TrackMarkRow>();

    public DbSet<GenreRow> Genres => Set<GenreRow>();

    public DbSet<FamiliarityRow> Familiarities => Set<FamiliarityRow>();

    /// <summary>Updates the row <paramref name="match"/> finds, or adds <paramref name="row"/> when there is none.</summary>
    public void Upsert<TRow>(DbSet<TRow> rows, Expression<Func<TRow, bool>> match, TRow row)
        where TRow : class
    {
        if (rows.FirstOrDefault(match) is { } existing)
            Entry(existing).CurrentValues.SetValues(row);
        else
            rows.Add(row);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder conventions)
    {
        conventions.Properties<KeyConfidence>().HaveConversion<string>();
        conventions.Properties<DiatonicMode>().HaveConversion<string>();
        conventions.Properties<SourceKind>().HaveConversion<string>();
        conventions.Properties<WindowAsker>().HaveConversion<string>();
        conventions.Properties<DecidedBy>().HaveConversion<string>();
        conventions.Properties<PageChoiceKind>().HaveConversion<string>();
        conventions.Properties<TrackMark>().HaveConversion<string>();
        // Round-trip text in UTC, so that ordering by the column is ordering in time.
        conventions.Properties<DateTimeOffset>().HaveConversion<UtcTextConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<RunRow>(run =>
        {
            run.ToTable(
                "Runs",
                table =>
                {
                    table.HasCheckConstraint("CK_Runs_Center", "(Tonic IS NULL) = (Mode IS NULL)");
                    table.HasCheckConstraint(
                        "CK_Runs_Source",
                        "SourceUrl IS NOT NULL OR LocalSource = 1"
                    );
                }
            );
            run.Ignore(row => row.Center);
            run.Property(row => row.Song).UseCollation("NOCASE");
            run.HasIndex(row => new { row.Song, row.RecordedAt });
            run.OwnsMany(
                row => row.Sections,
                section =>
                {
                    section.ToTable("Sections");
                    section.WithOwner().HasForeignKey("RunId");
                    section.HasKey("RunId", nameof(SectionRow.Position));
                    section.Property(row => row.Position).ValueGeneratedNever();
                }
            );
            run.OwnsMany(
                row => row.Sources,
                source =>
                {
                    source.ToTable("Sources");
                    source.WithOwner().HasForeignKey("RunId");
                    source.HasKey("RunId", nameof(SourceRow.Position));
                    source.Property(row => row.Position).ValueGeneratedNever();
                }
            );
            run.OwnsMany(
                row => row.Candidates,
                candidate =>
                {
                    candidate.ToTable("Candidates");
                    candidate.WithOwner().HasForeignKey("RunId");
                    candidate.HasKey("RunId", nameof(CandidateRow.Position));
                    candidate.Property(row => row.Position).ValueGeneratedNever();
                }
            );
            run.OwnsMany(
                row => row.Windows,
                window =>
                {
                    window.ToTable("Windows");
                    window.WithOwner().HasForeignKey("RunId");
                    window.HasKey("RunId", nameof(WindowRow.Position));
                    window.Property(row => row.Position).ValueGeneratedNever();
                }
            );
        });

        model.Entity<RestatementRow>(restatement =>
        {
            restatement.ToTable("Restatements");
            restatement.HasOne<RunRow>().WithMany().HasForeignKey(row => row.RunId);
        });

        // The person's decisions sit beside the runs, never inside them (ADR 0082).
        model.Entity<PageChoiceRow>(choice =>
        {
            choice.ToTable("PageChoices");
            choice.HasIndex(row => row.TrackKey);
        });

        model.Entity<SourceRemovalRow>(removal =>
        {
            removal.ToTable("SourceRemovals");
            removal.HasIndex(row => row.TrackKey);
        });

        model.Entity<TrackMarkRow>(mark =>
        {
            mark.ToTable("TrackMarks");
            mark.HasKey(row => new { row.Song, row.Mark });
            mark.Property(row => row.Song).UseCollation("NOCASE");
        });

        model.Entity<GenreRow>(genre =>
        {
            genre.ToTable("Genres");
            genre.HasKey(row => row.Song);
            genre.Property(row => row.Song).UseCollation("NOCASE");
        });

        model.Entity<FamiliarityRow>(familiarity =>
        {
            familiarity.ToTable("Familiarities");
            familiarity.HasKey(row => row.Song);
            familiarity.Property(row => row.Song).UseCollation("NOCASE");
        });
    }

    private sealed class UtcTextConverter()
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
            DateTimeOffset,
            string
        >(
            at => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            text =>
                DateTimeOffset.Parse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                )
        );
}

/// <summary>What <c>dotnet ef migrations add</c> builds the context with.</summary>
internal sealed class LedgerContextFactory : IDesignTimeDbContextFactory<LedgerContext>
{
    public LedgerContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<LedgerContext>().UseSqlite("Data Source=design.db").Options
        );
}

internal sealed class RunRow
{
    public long Id { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string Song { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? Title { get; set; }
    public string? SourceUrl { get; set; }
    public bool LocalSource { get; set; }
    public string? InputPath { get; set; }
    public string? InputKind { get; set; }
    public bool? HasVocals { get; set; }
    public KeyConfidence Confidence { get; set; }
    public DecidedBy DecidedBy { get; set; }
    public string? Tonic { get; set; }
    public DiatonicMode? Mode { get; set; }
    public bool Mixed { get; set; }
    public string? Direction { get; set; }
    public string? TonicCheck { get; set; }
    public string? Reasoning { get; set; }
    public string? WithheldReasoning { get; set; }

    /// <summary>The detectors' report, as JSON.</summary>
    public string? Evidence { get; set; }

    /// <summary>What the lookup found on each site, cited or not, as JSON.</summary>
    public string? Lookups { get; set; }

    public double? OldBpm { get; set; }
    public string? OldBpmSource { get; set; }
    public double? OldBpmBeatdetect { get; set; }

    public List<SectionRow> Sections { get; set; } = [];
    public List<SourceRow> Sources { get; set; } = [];
    public List<CandidateRow> Candidates { get; set; } = [];
    public List<WindowRow> Windows { get; set; } = [];

    public TonalCenter? Center => Tonic is not null && Mode is { } mode ? new(Tonic, mode) : null;

    public static RunRow From(RunRecord run) =>
        new()
        {
            RecordedAt = run.RecordedAt,
            Song = run.Song,
            Artist = run.Artist,
            Title = run.Title,
            SourceUrl = run.SourceUrl,
            LocalSource = run.SourceUrl is null,
            InputPath = run.InputPath,
            InputKind = run.InputKind,
            HasVocals = run.HasVocals,
            Confidence = run.Confidence,
            DecidedBy = run.DecidedBy,
            Tonic = run.Center?.Tonic,
            Mode = run.Center?.Mode,
            Mixed = run.Mixed,
            Direction = run.Direction,
            TonicCheck = run.TonicCheck,
            Reasoning = run.Reasoning,
            WithheldReasoning = run.WithheldReasoning,
            Evidence = run.Evidence,
            Lookups = run.Lookups,
            OldBpm = run.OldBpm?.Value,
            OldBpmSource = run.OldBpm?.Source,
            OldBpmBeatdetect = run.OldBpm?.Beatdetect,
            Sections =
            [
                .. run.Sections.Select(
                    (section, index) =>
                        new SectionRow
                        {
                            Position = index,
                            Label = section.Label,
                            Tonic = section.Center.Tonic,
                            Mode = section.Center.Mode,
                            Start = section.Start,
                        }
                ),
            ],
            Sources =
            [
                .. run.Sources.Select(
                    (source, index) =>
                        new SourceRow
                        {
                            Position = index,
                            Url = source.Url,
                            Via = source.Via,
                            Kind = source.Kind,
                            States = source.States,
                            StatesKey = source.StatesKey,
                            StatesMode = source.StatesMode,
                            Note = source.Note,
                            Sections = source.Sections is { } labels ? [.. labels] : null,
                        }
                ),
            ],
            Candidates =
            [
                .. run.Candidates.Select(
                    (candidate, index) =>
                        new CandidateRow
                        {
                            Position = index,
                            Key = candidate.Key,
                            Why = candidate.Why,
                        }
                ),
            ],
            Windows =
            [
                .. run.Windows.Select(
                    (window, index) =>
                        new WindowRow
                        {
                            Position = index,
                            Start = window.Start,
                            End = window.End,
                            Label = window.Label,
                            AskedBy = window.AskedBy,
                            FirstBar = window.Bars?.First,
                            LastBar = window.Bars?.Last,
                            Why = window.Why,
                        }
                ),
            ],
        };

    public RunRecord ToRecord() =>
        new()
        {
            RecordedAt = RecordedAt,
            Song = Song,
            Artist = Artist,
            Title = Title,
            SourceUrl = SourceUrl,
            LocalSource = LocalSource,
            InputPath = InputPath,
            InputKind = InputKind,
            HasVocals = HasVocals,
            Confidence = Confidence,
            DecidedBy = DecidedBy,
            Center = Center,
            Mixed = Mixed,
            Direction = Direction,
            TonicCheck = TonicCheck,
            Reasoning = Reasoning,
            WithheldReasoning = WithheldReasoning,
            Candidates =
            [
                .. Candidates
                    .OrderBy(candidate => candidate.Position)
                    .Select(candidate => new RunCandidate(candidate.Key, candidate.Why)),
            ],
            Evidence = Evidence,
            Lookups = Lookups,
            OldBpm = (OldBpm, OldBpmSource, OldBpmBeatdetect) is (null, null, null)
                ? null
                : new(OldBpm, OldBpmSource, OldBpmBeatdetect),
            Sections =
            [
                .. Sections
                    .OrderBy(section => section.Position)
                    .Select(section => new Section(
                        section.Label,
                        new(section.Tonic, section.Mode),
                        section.Start
                    )),
            ],
            Sources =
            [
                .. Sources
                    .OrderBy(source => source.Position)
                    .Select(source => new RunSource
                    {
                        Url = source.Url,
                        Via = source.Via,
                        Kind = source.Kind,
                        States = source.States,
                        StatesKey = source.StatesKey,
                        StatesMode = source.StatesMode,
                        Note = source.Note,
                        Sections = source.Sections,
                    }),
            ],
            Windows =
            [
                .. Windows
                    .OrderBy(window => window.Position)
                    .Select(window => new RunWindow(
                        window.Start,
                        window.End,
                        window.Label,
                        window.AskedBy
                    )
                    {
                        Bars = window is { FirstBar: { } first, LastBar: { } last }
                            ? new(first, last)
                            : null,
                        Why = window.Why,
                    }),
            ],
        };
}

internal sealed class SectionRow
{
    public int Position { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Tonic { get; set; } = string.Empty;
    public DiatonicMode Mode { get; set; }
    public double? Start { get; set; }
}

internal sealed class SourceRow
{
    public int Position { get; set; }
    public string? Url { get; set; }
    public string? Via { get; set; }
    public SourceKind Kind { get; set; }
    public string? States { get; set; }
    public string? StatesKey { get; set; }
    public DiatonicMode? StatesMode { get; set; }
    public string? Note { get; set; }

    /// <summary>The labels of the sections the source covered; null when it did not say.</summary>
    public List<string>? Sections { get; set; }
}

internal sealed class CandidateRow
{
    public int Position { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Why { get; set; } = string.Empty;
}

internal sealed class WindowRow
{
    public int Position { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public string? Label { get; set; }
    public WindowAsker? AskedBy { get; set; }
    public int? FirstBar { get; set; }
    public int? LastBar { get; set; }
    public string? Why { get; set; }
}

internal sealed class RestatementRow
{
    public long Id { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long RunId { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
}
