using System.Globalization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Pairing;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>A key the track's evidence offers the person, and who named it.</summary>
public sealed record KeyChoice(MusicKey Key, IReadOnlyList<string> NamedBy);

/// <summary>The person's own verdict on a key: what the song page offers, and what it records (ADR 0076).</summary>
public sealed class PersonsKey(
    LedgerStore store,
    WorkingSet workingSet,
    SourceIndex sources,
    PageLookups lookups,
    JobStore jobs,
    TimeProvider time
)
{
    private static readonly string[] CountedSites = ["Hooktheory", "Musicnotes"];

    /// <summary>Each distinct key the detectors, keydetect's runner-up and the Hooktheory and Musicnotes pages name, most-named first.</summary>
    public IReadOnlyList<KeyChoice> ChoicesFor(WorkOrder order, RunEvidence? run)
    {
        if ((run ?? Pending(order)) is not { } evidence)
            return [];

        List<(MusicKey Key, string By)> named = [];

        foreach (var algorithm in evidence.Algorithms)
        {
            if (RecordTimeCheck.KeyOf(algorithm.Key) is { } key)
                named.Add((key, algorithm.Label));

            if (
                algorithm.Name == "ffmpeg keydetect"
                && RecordTimeCheck.KeyOf(algorithm.RunnerUpKey) is { } runnerUp
            )
                named.Add((runnerUp, "keydetect's runner-up"));
        }

        foreach (
            var page in evidence
                .Lookups.Where(lookup => CountedSites.Contains(lookup.Site))
                .SelectMany(lookup => lookup.Pages)
        )
        {
            foreach (var pageKey in page.Keys)
            {
                if (RecordTimeCheck.KeyOf(pageKey.Key) is { } key)
                    named.Add((key, $"{page.Site} page"));
            }
        }

        return
        [
            .. named
                .GroupBy(entry => entry.Key)
                .Select(group => new KeyChoice(group.Key, [.. group.Select(e => e.By).Distinct()]))
                .OrderByDescending(choice => choice.NamedBy.Count),
        ];
    }

    /// <summary>Records the key as the person's call, HIGH, and lets the track go on if it was waiting.</summary>
    public RecordOutcome Choose(WorkOrder order, MusicKey key)
    {
        var held = lookups.HeldRun(order);
        var basis = held is null ? null : store.LatestRecord(held.Song);
        var names = basis is { Artist: { } artist, Title: { } title }
            ? (artist, title)
            : lookups.NamesOf(order);
        var audio = basis?.InputPath ?? sources.PathFor(order);
        var center = MusicKey.CenterOf(key);

        var outcome = store.Record(
            new RunRecord
            {
                RecordedAt = time.GetUtcNow(),
                Song =
                    basis?.Song
                    ?? (
                        names is var (named, called)
                            ? LedgerVocabulary.SongIdentity(named, called)
                            : order.Title.Trim().ToLowerInvariant()
                    ),
                Artist = names?.Artist,
                Title = names is { } pair
                    ? LedgerVocabulary.WithoutStemSuffix(pair.Title)
                    : order.Title,
                SourceUrl = basis?.SourceUrl ?? order.SourceUrl,
                LocalSource = basis?.LocalSource ?? false,
                InputPath = audio,
                InputKind = basis?.InputKind ?? (audio is null ? null : "full_mix"),
                HasVocals = basis?.HasVocals,
                Confidence = KeyConfidence.High,
                DecidedBy = DecidedBy.Person,
                Center = center,
                Windows = basis?.Windows ?? [],
                Sources =
                [
                    new RunSource
                    {
                        Via = "your call on the song page",
                        Kind = SourceKind.Ear,
                        States = center.Name,
                        StatesKey = center.Collapsed,
                        StatesMode = center.Mode,
                    },
                ],
                Reasoning = string.Create(
                    CultureInfo.InvariantCulture,
                    $"You chose {center.Name} on the song page."
                ),
                Evidence =
                    basis?.Evidence
                    ?? (
                        workingSet.TryReadEvidence(order.VideoId, out var evidence)
                            ? LateReading.Unstamped(evidence)
                            : null
                    ),
                Lookups =
                    basis?.Lookups ?? VerdictRecording.Serialized(lookups.CachedFor(order) ?? []),
            }
        );

        if (outcome is not RecordOutcome.Recorded)
            return outcome;

        VerdictRecording.LinkSwappedSource(store, order);

        if (
            jobs.TryGet(order.VideoId, JobStage.Verdict, out var job)
            && job.Status is JobStatus.AwaitingPerson
        )
            jobs.Record(
                order.VideoId,
                JobStage.Verdict,
                JobStatus.Succeeded,
                summary: $"{center.Name}, your call"
            );

        return outcome;
    }

    /// <summary>The evidence and pages kept for a track that has no run to read them from.</summary>
    private RunEvidence? Pending(WorkOrder order) =>
        workingSet.TryReadEvidence(order.VideoId, out var evidence)
            ? RunEvidence.From(
                new RunRecord
                {
                    RecordedAt = time.GetUtcNow(),
                    Song = order.VideoId,
                    Confidence = KeyConfidence.Withheld,
                    Evidence = evidence,
                    Lookups = VerdictRecording.Serialized(lookups.CachedFor(order) ?? []),
                }
            )
            : null;
}
