using System.Diagnostics;
using System.Globalization;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Pairing;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>A key the track's evidence offers the person, and who named it; a <paramref name="Mode"/> is one a page stated, tagged by <paramref name="Key"/>.</summary>
public sealed record KeyChoice(
    MusicKey Key,
    IReadOnlyList<string> NamedBy,
    DiatonicMode? Mode = null
)
{
    public TonalCenter Center =>
        Mode is { } mode ? MusicKey.CenterOf(Key) with { Mode = mode } : MusicKey.CenterOf(Key);
}

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
        if ((run ?? PendingFor(order)) is not { } evidence)
            return [];

        List<(MusicKey Key, DiatonicMode? Mode, string By)> named = [];

        foreach (var algorithm in evidence.Algorithms)
        {
            if (RecordTimeCheck.KeyOf(algorithm.Key) is { } key)
                named.Add((key, null, algorithm.Label));

            if (
                algorithm.Name == "ffmpeg keydetect"
                && RecordTimeCheck.KeyOf(algorithm.RunnerUpKey) is { } runnerUp
            )
                named.Add((runnerUp, null, "keydetect's runner-up"));
        }

        foreach (
            var page in lookups
                .Effective(order.TrackKey, evidence.Lookups)
                .Where(lookup => CountedSites.Contains(lookup.Site))
                .SelectMany(lookup => lookup.Pages)
        )
        {
            foreach (var pageKey in page.Keys)
            {
                if (RecordTimeCheck.KeyOf(pageKey.Key) is { } key)
                    named.Add((key, null, $"{page.Site} page"));

                if (
                    LedgerVocabulary.ModalCenterOf(pageKey.Key) is { Mode: var mode } modal
                    && MusicKey.FromString(modal.Collapsed) is { } tag
                )
                    named.Add((tag, mode, $"{page.Site} page"));
            }
        }

        return
        [
            .. named
                .GroupBy(entry => (entry.Key, entry.Mode))
                .Select(group => new KeyChoice(
                    group.Key.Key,
                    [.. group.Select(entry => entry.By).Distinct()],
                    group.Key.Mode
                ))
                .OrderBy(choice => choice.Mode is not null)
                .ThenByDescending(choice => choice.NamedBy.Count),
        ];
    }

    /// <summary>Records the key as the person's call, HIGH, and lets the track go on if it was waiting; a <paramref name="mode"/> is kept on the center and the tag follows its major or minor.</summary>
    public RecordOutcome Choose(WorkOrder order, MusicKey key, DiatonicMode? mode = null)
    {
        var held = lookups.HeldRun(order);
        var basis = held is null ? null : store.LatestRecord(held.Song);
        var names = basis is { Artist: { } artist, Title: { } title }
            ? (artist, title)
            : lookups.NamesOf(order);
        var audio = basis?.InputPath ?? sources.PathFor(order);
        var center = MusicKey.CenterOf(key) with { Mode = mode ?? MusicKey.CenterOf(key).Mode };

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
                OldBpm = basis?.OldBpm,
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
                        workingSet.TryReadEvidence(order.TrackKey, out var evidence)
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
            jobs.TryGet(order.TrackKey, JobStage.Verdict, out var job)
            && job.Status is JobStatus.AwaitingPerson
        )
            jobs.Record(
                order.TrackKey,
                JobStage.Verdict,
                JobStatus.Succeeded,
                summary: $"{center.Name}, your call"
            );

        return outcome;
    }

    /// <summary>What the song page shows for a track with no run yet: the kept evidence and pages, and why the rules gave no ruling. Null when no evidence is kept.</summary>
    public RunEvidence? PendingFor(WorkOrder order)
    {
        if (!workingSet.TryReadEvidence(order.TrackKey, out var evidence))
            return null;

        var cached = lookups.CachedFor(order);
        var found = cached is null ? null : lookups.Effective(order.TrackKey, cached);

        return RunEvidence.From(
            new RunRecord
            {
                RecordedAt = time.GetUtcNow(),
                Song = order.TrackKey,
                Confidence = KeyConfidence.Withheld,
                Evidence = evidence,
                Lookups = VerdictRecording.Serialized(cached ?? []),
                Sources = [.. (found ?? []).SelectMany(PageSources)],
                Reasoning = found is null
                    ? "The Hooktheory, Musicnotes and Ultimate Guitar pages have not been looked up yet."
                    : VerdictRules.Decide(evidence, found) switch
                    {
                        RulesOutcome.Ruled(var ruling) =>
                            $"The rules would rule {ruling.Center.Name}, {ruling.Confidence.ToString().ToUpperInvariant()}: {ruling.Reasoning}",
                        RulesOutcome.Open(var reason) => reason,
                        _ => throw new UnreachableException(),
                    },
            }
        );
    }

    private static IEnumerable<RunSource> PageSources(SiteLookup lookup) =>
        lookup.Pages.Select(page => new RunSource
        {
            Url = page.Url,
            Via = VerdictRules.ViaOf(page),
            Kind =
                CountedSites.Contains(lookup.Site) && !VerdictRules.IsUserArrangement(page)
                    ? SourceKind.Authoritative
                    : SourceKind.Corroborating,
            States = string.Join("; ", page.Keys.Select(key => key.Key).Distinct()),
            StatesKey = page
                .Keys.Select(key => RecordTimeCheck.KeyOf(key.Key)?.LedgerName)
                .FirstOrDefault(name => name is not null),
            Note = CountedSites.Contains(lookup.Site)
                ? page.Note
                : "Shown, not counted: the rules leave Ultimate Guitar out (ADR 0076).",
        });
}
