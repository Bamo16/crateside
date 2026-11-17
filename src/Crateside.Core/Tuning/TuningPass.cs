using System.Diagnostics;
using System.Globalization;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Warps;

namespace Crateside.Core.Tuning;

/// <summary>
/// Reads each track's tuning once, over stretches of its instrumental stem, and gives every saved
/// default clip of the track the detune that brings it to A = 440 (ADR 0101).
/// </summary>
public sealed class TuningPass(
    IDetectorSet detectors,
    SidecarWriter writer,
    TuningStore store,
    TimeProvider clock,
    StageNotes? notes = null
)
{
    private const int Stretches = 5;
    private const double StretchSeconds = 30;

    private readonly HashSet<string> _unread = new(StringComparer.Ordinal);

    /// <summary>Decides every track not yet decided and writes what is decided; the tracks written to.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        IReadOnlyList<TuningTrack> tracks,
        CancellationToken ct
    )
    {
        List<string> written = [];

        foreach (var track in tracks)
        {
            ct.ThrowIfCancellationRequested();

            if (await SettleAsync(track, ct))
                written.Add(track.Song);
        }

        return written;
    }

    /// <summary>Writes the correction of a review-list track the user accepted; the pass lays it into the clips.</summary>
    public bool Accept(string trackKey)
    {
        if (store.Of(trackKey) is not { State: TuningState.Review, Median: { } median } entry)
            return false;

        store.Save(
            trackKey,
            entry with
            {
                State = TuningState.Written,
                Correction = Math.Clamp(-median, -50, 50),
                At = clock.GetUtcNow(),
            }
        );

        return true;
    }

    /// <summary>Takes the detune out of the clips it wrote and leaves the track alone from now on.</summary>
    public bool PutBack(string trackKey)
    {
        if (store.Of(trackKey) is not { } entry)
            return false;

        var kept = entry
            .Written.Where(sample => !writer.Detune(sample.Key, 0, writtenBefore: sample.Value))
            .ToDictionary();

        store.Save(
            trackKey,
            entry with
            {
                State = TuningState.LeftAlone,
                Written = kept,
                At = clock.GetUtcNow(),
            }
        );

        return true;
    }

    internal async Task<bool> SettleAsync(TuningTrack track, CancellationToken ct)
    {
        var entry = store.Of(track.TrackKey) ?? await DecideAsync(track, ct);

        return entry is { State: TuningState.Written } && WriteTo(track, entry);
    }

    private async Task<TuningEntry?> DecideAsync(TuningTrack track, CancellationToken ct)
    {
        if (_unread.Contains(track.TrackKey) || track.ReadFrom is not { } audio)
            return null;

        var readings = await ReadAsync(audio, track.Duration, ct);

        if (readings.Count < TuningVerdict.SteadyReadings)
        {
            _unread.Add(track.TrackKey);

            return null;
        }

        var entry = TuningVerdict.Of(readings) switch
        {
            TuningVerdict.Write(var median, var correction) => Entry(
                TuningState.Written,
                median,
                correction
            ),
            TuningVerdict.Review(var median, var why) => Entry(
                TuningState.Review,
                median,
                why: why
            ),
            TuningVerdict.InTune(var median) => Entry(TuningState.InTune, median),
            _ => throw new UnreachableException(),
        };

        store.Save(track.TrackKey, entry);

        return entry;

        TuningEntry Entry(
            TuningState state,
            double? median,
            double? correction = null,
            string? why = null
        ) =>
            new()
            {
                State = state,
                Song = track.Song,
                Median = median,
                Readings = readings,
                Correction = correction,
                Why = why,
                At = clock.GetUtcNow(),
            };
    }

    private async Task<IReadOnlyList<double>> ReadAsync(
        string audio,
        TimeSpan? duration,
        CancellationToken ct
    )
    {
        var stretches = Math.Min(Stretches, (int)((duration?.TotalSeconds ?? 0) / StretchSeconds));
        List<double> readings = [];

        for (var i = 0; i < stretches; i++)
        {
            var middle = duration!.Value.TotalSeconds * (i + 0.5) / stretches;
            var start = Math.Max(0, middle - StretchSeconds / 2);
            var window = new RunWindow(start, start + StretchSeconds, null, null);

            if (
                await detectors.ReadKeydetectAsync(audio, window, ct) is KeydetectReading.Read
                {
                    TuningCents: { } cents
                }
            )
                readings.Add(cents);
        }

        return readings;
    }

    private bool WriteTo(TuningTrack track, TuningEntry entry)
    {
        if (entry.Correction is not { } correction)
            return false;

        var written = entry.Written.ToDictionary();

        foreach (var sample in track.Samples.Where(sample => !written.ContainsKey(sample)))
            if (writer.Detune(sample, correction))
                written[sample] = correction;

        if (written.Count == entry.Written.Count)
            return false;

        store.Save(track.TrackKey, entry with { Written = written });

        if (entry.Written.Count is 0)
            notes?.Record(
                track.TrackKey,
                "Detune",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Detuned {correction:+0.#;−0.#} ¢ (readings {string.Join(", ", entry.Readings.Select(reading => reading.ToString("+0.#;−0.#", CultureInfo.InvariantCulture)))})"
                )
            );

        return true;
    }
}
