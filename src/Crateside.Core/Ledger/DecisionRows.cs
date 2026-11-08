using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Imports;
using Crateside.Core.Marks;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;

namespace Crateside.Core.Ledger;

internal sealed class PageChoiceRow
{
    public long Id { get; set; }
    public string TrackKey { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public PageChoiceKind Kind { get; set; }

    /// <summary>The page as the person saw it, as JSON.</summary>
    public string Page { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }

    public static PageChoiceRow From(string trackKey, PageChoice choice) =>
        new()
        {
            TrackKey = trackKey,
            Url = choice.Url,
            Kind = choice.Kind,
            Page = JsonSerializer.Serialize(choice.Page, DecisionJsonContext.Default.FetchedPage),
            At = choice.At,
            UndoneAt = choice.UndoneAt,
        };

    public PageChoice ToChoice() =>
        new()
        {
            Url = Url,
            Kind = Kind,
            Page = JsonSerializer.Deserialize(Page, DecisionJsonContext.Default.FetchedPage)!,
            At = At,
            UndoneAt = UndoneAt,
        };
}

internal sealed class SourceRemovalRow
{
    public long Id { get; set; }
    public string TrackKey { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }

    public static SourceRemovalRow From(string trackKey, SourceRemoval removal) =>
        new()
        {
            TrackKey = trackKey,
            Source = removal.Source,
            At = removal.At,
            UndoneAt = removal.UndoneAt,
        };

    public SourceRemoval ToRemoval() => new(Source, At) { UndoneAt = UndoneAt };
}

internal sealed class TrackMarkRow
{
    public string Song { get; set; } = string.Empty;
    public TrackMark Mark { get; set; }
}

internal sealed class GenreRow
{
    public string Song { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public bool IsSettled { get; set; }
    public string? LearnedFrom { get; set; }
    public string? Suggested { get; set; }

    /// <summary>Everything the sources said, as a JSON list.</summary>
    public string Heard { get; set; } = "[]";

    public DateTimeOffset? LookedUpAt { get; set; }

    public static GenreRow From(GenreRecord record) =>
        new()
        {
            Song = record.Song,
            Genre = record.Genre,
            IsSettled = record.IsSettled,
            LearnedFrom = record.LearnedFrom,
            Suggested = record.Suggested,
            Heard = JsonSerializer.Serialize(
                [.. record.Heard],
                DecisionJsonContext.Default.ListString
            ),
            LookedUpAt = record.LookedUpAt,
        };

    public GenreRecord ToRecord() =>
        new()
        {
            Song = Song,
            Genre = Genre,
            IsSettled = IsSettled,
            LearnedFrom = LearnedFrom,
            Suggested = Suggested,
            Heard = JsonSerializer.Deserialize(Heard, DecisionJsonContext.Default.ListString) ?? [],
            LookedUpAt = LookedUpAt,
        };
}

internal sealed class FamiliarityRow
{
    public string Song { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public long? ArtistFans { get; set; }
    public int? SongRank { get; set; }
    public DateTimeOffset? LookedUpAt { get; set; }
    public int LookupVersion { get; set; }
    public string? Settled { get; set; }

    public static FamiliarityRow From(FamiliarityRecord record) =>
        new()
        {
            Song = record.Song,
            Artist = record.Artist,
            ArtistFans = record.ArtistFans,
            SongRank = record.SongRank,
            LookedUpAt = record.LookedUpAt,
            LookupVersion = record.LookupVersion,
            Settled = record.Settled,
        };

    public FamiliarityRecord ToRecord() =>
        new()
        {
            Song = Song,
            Artist = Artist,
            ArtistFans = ArtistFans,
            SongRank = SongRank,
            LookedUpAt = LookedUpAt,
            LookupVersion = LookupVersion,
            Settled = Settled,
        };
}

internal sealed class TrackInstructionRow
{
    public string TrackKey { get; set; } = string.Empty;

    /// <summary>The <see cref="StemPlan"/> as JSON; null when the person never chose.</summary>
    public string? Plan { get; set; }

    public string? Name { get; set; }
    public string? SourceUrl { get; set; }
    public bool AddedByUrl { get; set; }
    public string? DifferentFrom { get; set; }
    public string? AcceptedUpload { get; set; }
    public DateTimeOffset? CutRequestedAt { get; set; }
    public bool Backfill { get; set; }

    public static TrackInstructionRow From(TrackInstruction instruction) =>
        new()
        {
            TrackKey = instruction.TrackKey,
            Plan = instruction.Plan is { } plan
                ? JsonSerializer.Serialize(plan, DecisionJsonContext.Default.StemPlan)
                : null,
            Name = instruction.Name,
            SourceUrl = instruction.SourceUrl,
            AddedByUrl = instruction.AddedByUrl,
            DifferentFrom = instruction.DifferentFrom,
            AcceptedUpload = instruction.AcceptedUpload,
            CutRequestedAt = instruction.CutRequestedAt,
            Backfill = instruction.Backfill,
        };

    public TrackInstruction ToInstruction() =>
        new()
        {
            TrackKey = TrackKey,
            Plan = Plan is null
                ? null
                : JsonSerializer.Deserialize(Plan, DecisionJsonContext.Default.StemPlan),
            Name = Name,
            SourceUrl = SourceUrl,
            AddedByUrl = AddedByUrl,
            DifferentFrom = DifferentFrom,
            AcceptedUpload = AcceptedUpload,
            CutRequestedAt = CutRequestedAt,
            Backfill = Backfill,
        };
}

internal sealed class IntakeRow
{
    /// <summary>Arrival order, which is the order the records read back in.</summary>
    public long Id { get; set; }

    public string TrackKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ChannelTitle { get; set; }
    public DateTimeOffset? AddedAt { get; set; }
    public PremiumOutlook PremiumOutlook { get; set; }
    public bool IsUnavailable { get; set; }
    public string? PageUrl { get; set; }
    public string? Artist { get; set; }
    public string? SongTitle { get; set; }

    /// <summary>The <see cref="Imports.Release"/> as JSON; null for a track from the playlist.</summary>
    public string? Release { get; set; }

    public DateTimeOffset? RemovalAttemptedAt { get; set; }
    public string? RemovalFailureReason { get; set; }
    public bool RemovalFailureCanSucceedLater { get; set; }
    public string? RemovedEntryId { get; set; }

    public static IntakeRow From(IntakeRecord record) =>
        new()
        {
            TrackKey = record.TrackKey,
            Title = record.Title,
            ChannelTitle = record.ChannelTitle,
            AddedAt = record.AddedAt,
            PremiumOutlook = record.PremiumOutlook,
            IsUnavailable = record.IsUnavailable,
            PageUrl = record.PageUrl,
            Artist = record.Artist,
            SongTitle = record.SongTitle,
            Release = record.Release is { } release
                ? JsonSerializer.Serialize(release, DecisionJsonContext.Default.Release)
                : null,
            RemovalAttemptedAt = record.RemovalAttemptedAt,
            RemovalFailureReason = record.RemovalFailure?.Reason,
            RemovalFailureCanSucceedLater = record.RemovalFailure is { CanSucceedLater: true },
            RemovedEntryId = record.RemovedEntryId,
        };

    public IntakeRecord ToRecord() =>
        new()
        {
            TrackKey = TrackKey,
            Title = Title,
            ChannelTitle = ChannelTitle,
            AddedAt = AddedAt,
            PremiumOutlook = PremiumOutlook,
            IsUnavailable = IsUnavailable,
            PageUrl = PageUrl,
            Artist = Artist,
            SongTitle = SongTitle,
            Release = Release is null
                ? null
                : JsonSerializer.Deserialize(Release, DecisionJsonContext.Default.Release),
            RemovalAttemptedAt = RemovalAttemptedAt,
            RemovalFailure = RemovalFailureReason is { } reason
                ? new RemovalFailure
                {
                    Reason = reason,
                    CanSucceedLater = RemovalFailureCanSucceedLater,
                }
                : null,
            RemovedEntryId = RemovedEntryId,
        };
}

/// <summary>The one row of playlist settings, <see cref="Only"/>.</summary>
internal sealed class PlaylistSettingsRow
{
    public const int Only = 1;

    public int Id { get; set; } = Only;
    public string? PlaylistId { get; set; }
    public int? RefreshMinutes { get; set; }
    public bool ConsumeOnArrival { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(FetchedPage))]
[JsonSerializable(typeof(StemPlan))]
[JsonSerializable(typeof(Release))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class DecisionJsonContext : JsonSerializerContext { }
