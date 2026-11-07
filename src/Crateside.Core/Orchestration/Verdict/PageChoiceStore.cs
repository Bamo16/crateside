using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.Watching;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Orchestration.Verdict;

public enum PageChoiceKind
{
    /// <summary>A page the lookup found, said not to be this song.</summary>
    Rejected,

    /// <summary>A title-only candidate, said to be this song.</summary>
    Promoted,

    /// <summary>A page the person gave by address.</summary>
    Added,
}

/// <summary>One thing the person said about a page for a track (ADR 0078).</summary>
public sealed record PageChoice
{
    public required string Url { get; init; }
    public required PageChoiceKind Kind { get; init; }

    /// <summary>The page as the person saw it, so it counts or stays visible without another fetch.</summary>
    public required FetchedPage Page { get; init; }

    public required DateTimeOffset At { get; init; }
    public DateTimeOffset? UndoneAt { get; init; }

    [JsonIgnore]
    public bool IsActive => UndoneAt is null;
}

/// <summary>A recorded human source the person said does not count for a track (ADR 0081).</summary>
public sealed record SourceRemoval(string Source, DateTimeOffset At)
{
    public DateTimeOffset? UndoneAt { get; init; }

    [JsonIgnore]
    public bool IsActive => UndoneAt is null;
}

/// <summary>The pages the person has rejected, counted or added, and the recorded sources removed, kept in the ledger by track key (ADR 0078, 0081, 0082).</summary>
public sealed class PageChoiceStore
{
    private readonly LedgerStore _ledger;
    private readonly Lock _gate = new();

    private Dictionary<string, List<PageChoice>>? _cached;
    private Dictionary<string, List<SourceRemoval>> _removals = [];

    /// <summary>Raised after a choice is made or undone, with the track it was made on.</summary>
    public event Action<string>? Changed;

    public PageChoiceStore(LedgerStore ledger, FileChanges changes)
        : this(ledger) => Changed += _ => changes.NotifyNow();

    internal PageChoiceStore(LedgerStore ledger) => _ledger = ledger;

    /// <summary>The choices in force for the track.</summary>
    public IReadOnlyList<PageChoice> ActiveFor(string trackKey)
    {
        lock (_gate)
            return
            [
                .. Records().GetValueOrDefault(trackKey)?.Where(choice => choice.IsActive) ?? [],
            ];
    }

    /// <summary>The latest time a choice for the track was made or undone, or null for a track never corrected.</summary>
    public DateTimeOffset? LastChange(string trackKey)
    {
        lock (_gate)
            return (Records().GetValueOrDefault(trackKey) ?? [])
                .SelectMany(choice => new[] { choice.At, choice.UndoneAt ?? choice.At })
                .Concat(
                    Removals(trackKey)
                        .SelectMany(removal => new[] { removal.At, removal.UndoneAt ?? removal.At })
                )
                .Order()
                .Select(time => (DateTimeOffset?)time)
                .LastOrDefault();
    }

    /// <summary>Records the choice, ending any choice still in force for the same page.</summary>
    public void Choose(string trackKey, PageChoice choice)
    {
        lock (_gate)
        {
            var records = Records();
            var held = records.GetValueOrDefault(trackKey) ?? [];

            records[trackKey] =
            [
                .. held.Select(existing =>
                    existing.IsActive && SameUrl(existing.Url, choice.Url)
                        ? existing with
                        {
                            UndoneAt = choice.At,
                        }
                        : existing
                ),
                choice,
            ];

            Persist(trackKey);
        }

        Changed?.Invoke(trackKey);
    }

    /// <summary>Ends the choice in force for the page; false when there was none.</summary>
    public bool Undo(string trackKey, string url, DateTimeOffset at)
    {
        lock (_gate)
        {
            var records = Records();

            if (
                records.GetValueOrDefault(trackKey) is not { } held
                || !held.Any(choice => choice.IsActive && SameUrl(choice.Url, url))
            )
                return false;

            records[trackKey] =
            [
                .. held.Select(choice =>
                    choice.IsActive && SameUrl(choice.Url, url)
                        ? choice with
                        {
                            UndoneAt = at,
                        }
                        : choice
                ),
            ];

            Persist(trackKey);
        }

        Changed?.Invoke(trackKey);

        return true;
    }

    /// <summary>The sources the person removed from the track, by <see cref="EvidenceSource.Id"/>.</summary>
    public IReadOnlyList<string> RemovedFor(string trackKey)
    {
        lock (_gate)
        {
            Records();

            return
            [
                .. Removals(trackKey)
                    .Where(removal => removal.IsActive)
                    .Select(removal => removal.Source),
            ];
        }
    }

    /// <summary>Stops counting a recorded source for the track; false when it was already removed.</summary>
    public bool RemoveSource(string trackKey, string source, DateTimeOffset at)
    {
        lock (_gate)
        {
            Records();

            if (
                Removals(trackKey)
                    .Any(removal => removal.IsActive && SameUrl(removal.Source, source))
            )
                return false;

            _removals[trackKey] = [.. Removals(trackKey), new SourceRemoval(source, at)];
            Persist(trackKey);
        }

        Changed?.Invoke(trackKey);

        return true;
    }

    /// <summary>Counts a removed source again; false when it was not removed.</summary>
    public bool RestoreSource(string trackKey, string source, DateTimeOffset at)
    {
        lock (_gate)
        {
            Records();

            if (
                !Removals(trackKey)
                    .Any(removal => removal.IsActive && SameUrl(removal.Source, source))
            )
                return false;

            _removals[trackKey] =
            [
                .. Removals(trackKey)
                    .Select(removal =>
                        removal.IsActive && SameUrl(removal.Source, source)
                            ? removal with
                            {
                                UndoneAt = at,
                            }
                            : removal
                    ),
            ];
            Persist(trackKey);
        }

        Changed?.Invoke(trackKey);

        return true;
    }

    /// <summary>The run's sources, each marked removed where the person removed it.</summary>
    public RunEvidence Marked(string trackKey, RunEvidence run) =>
        RemovedFor(trackKey) is [_, ..] removed
            ? run with
            {
                Sources =
                [
                    .. run.Sources.Select(source =>
                        source.Id is { } id && removed.Any(held => SameUrl(held, id))
                            ? source with
                            {
                                IsRemoved = true,
                            }
                            : source
                    ),
                ],
            }
            : run;

    private List<SourceRemoval> Removals(string trackKey) =>
        _removals.GetValueOrDefault(trackKey) ?? [];

    internal static bool SameUrl(string a, string b) =>
        a.TrimEnd('/').Equals(b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private Dictionary<string, List<PageChoice>> Records() => _cached ??= Load();

    private void Persist(string trackKey)
    {
        var choices = _cached!.GetValueOrDefault(trackKey) ?? [];
        var removals = Removals(trackKey);

        _ledger.Save(context =>
        {
            context.PageChoices.RemoveRange(
                context.PageChoices.Where(row => row.TrackKey == trackKey)
            );
            context.SourceRemovals.RemoveRange(
                context.SourceRemovals.Where(row => row.TrackKey == trackKey)
            );
            context.PageChoices.AddRange(
                choices.Select(choice => PageChoiceRow.From(trackKey, choice))
            );
            context.SourceRemovals.AddRange(
                removals.Select(removal => SourceRemovalRow.From(trackKey, removal))
            );
        });
    }

    private Dictionary<string, List<PageChoice>> Load()
    {
        var (choices, removals) = _ledger.Query(context =>
            (
                context.PageChoices.AsNoTracking().OrderBy(row => row.Id).ToList(),
                context.SourceRemovals.AsNoTracking().OrderBy(row => row.Id).ToList()
            )
        );

        _removals = removals
            .GroupBy(row => row.TrackKey)
            .ToDictionary(
                track => track.Key,
                track => track.Select(row => row.ToRemoval()).ToList()
            );

        return choices
            .GroupBy(row => row.TrackKey)
            .ToDictionary(
                track => track.Key,
                track => track.Select(row => row.ToChoice()).ToList()
            );
    }
}
