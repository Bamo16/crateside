namespace Crateside.Core.SourceOffsets;

internal enum RowKind
{
    Weak,
    Near,
    Edit,
    Silent,
}

/// <summary>One window of the added source as measured: its best peak near the file's offset and anywhere in the mix.</summary>
internal sealed class OffsetRow(double start)
{
    public double Start { get; } = start;

    public RowKind Kind { get; set; } = RowKind.Weak;

    public (int Offset, double Psr) Near { get; set; }

    public (int Offset, double Psr) Far { get; set; }

    public bool Confident => Kind is RowKind.Near or RowKind.Edit;

    public bool IsSilent => Kind is RowKind.Silent;

    public int Offset => Kind is RowKind.Edit ? Far.Offset : Near.Offset;
}

/// <summary>The verdict rules of ADR 0111 over measured windows, with the tolerances in time.</summary>
internal static class OffsetVerdicts
{
    public const double MinPsr = 12;
    public const int MinConfident = 3;
    public const double MinShare = 0.25;
    public const double AgreeMs = 0.5;
    public const double SteadyShare = 0.9;
    public const int Run = 2;
    public const double DriftMs = 1;
    public const int DriftWindows = 4;
    public const double JoinMs = 45;
    public const double SilentRms = 1e-4;
    public const double GapSeconds = 0.5;

    public static double Samples(double ms, int rate) => ms * rate / 1000;

    public static void MarkEdits(IReadOnlyList<OffsetRow> rows, int rate)
    {
        var agree = Samples(AgreeMs, rate);
        List<OffsetRow> awake = [.. rows.Where(row => !row.IsSilent)];
        var i = 0;

        while (i < awake.Count)
        {
            if (awake[i] is not { Kind: RowKind.Weak, Far.Psr: >= MinPsr })
            {
                i++;

                continue;
            }

            var j = i;

            while (
                j + 1 < awake.Count
                && awake[j + 1] is { Kind: RowKind.Weak, Far.Psr: >= MinPsr }
                && Math.Abs(awake[j + 1].Far.Offset - awake[j].Far.Offset) <= agree
            )
                j++;

            var bracketed =
                i > 0
                && awake[i - 1].Kind is RowKind.Near
                && j + 1 < awake.Count
                && awake[j + 1].Kind is RowKind.Near;

            if (j - i + 1 >= Run && !bracketed)
            {
                for (var k = i; k <= j; k++)
                    awake[k].Kind = RowKind.Edit;
            }

            i = j + 1;
        }
    }

    public static OffsetReading Decide(
        IReadOnlyList<OffsetRow> rows,
        int rate,
        double windowSeconds,
        double sourceSeconds,
        double mixSeconds
    )
    {
        List<OffsetRow> live = [.. rows.Where(row => row.Confident)];
        var awake = rows.Count(row => !row.IsSilent);

        OffsetReading Reading(
            OffsetVerdict verdict,
            IReadOnlyList<OffsetRow> voters,
            int? offset = null,
            double? ppm = null,
            IReadOnlyList<OffsetRun>? runs = null
        )
        {
            var aligned = verdict is OffsetVerdict.Aligned;
            var showsMix = verdict is OffsetVerdict.Aligned or OffsetVerdict.Edit;

            return new OffsetReading
            {
                Verdict = verdict,
                SampleRate = rate,
                WindowSeconds = windowSeconds,
                Offset = offset,
                DriftPpm = ppm,
                Runs = runs ?? [],
                Windows =
                [
                    .. rows.Select(row =>
                        row.Kind switch
                        {
                            RowKind.Silent => new OffsetWindow(row.Start, 0, 0, WindowState.Silent),
                            RowKind.Weak => new OffsetWindow(
                                row.Start,
                                row.Near.Offset,
                                row.Near.Psr,
                                WindowState.Unmatched
                            ),
                            _ => new OffsetWindow(
                                row.Start,
                                row.Offset,
                                row.Kind is RowKind.Edit ? row.Far.Psr : row.Near.Psr,
                                !aligned || voters.Contains(row)
                                    ? WindowState.Voting
                                    : WindowState.Elsewhere
                            ),
                        }
                    ),
                ],
                OnlyInSource = showsMix
                    ? SpansOf(rows, windowSeconds, sourceSeconds, row => row.Kind is RowKind.Weak)
                    : [],
                Repeated = aligned
                    ? SpansOf(
                        rows,
                        windowSeconds,
                        sourceSeconds,
                        row => row.Confident && !voters.Contains(row)
                    )
                    : [],
                OnlyInMix = showsMix
                    ? MixGaps(voters, windowSeconds, rate, sourceSeconds, mixSeconds)
                    : [],
            };
        }

        if (live.Count < MinConfident || (double)live.Count / Math.Max(1, awake) < MinShare)
            return Reading(OffsetVerdict.NotTheSameRecording, live);

        if (DriftOf(live, rate) is { } ppm)
            return Reading(OffsetVerdict.Drift, live, ppm: ppm);

        var groups = Clusters(live, rate);
        var (top, members) = groups[0];

        if ((double)members.Count / live.Count >= SteadyShare)
            return Reading(OffsetVerdict.Aligned, members, top);

        var runs = RunsOf(live, groups, windowSeconds);

        return runs.Select(run => run.Offset).Distinct().Count() >= 2
            ? Reading(OffsetVerdict.Edit, live, runs: runs)
            : Reading(OffsetVerdict.NotTheSameRecording, live);
    }

    public static IReadOnlyList<OffsetRun> RunsOf(
        IReadOnlyList<OffsetRow> live,
        IReadOnlyList<(int Offset, List<OffsetRow> Rows)> groups,
        double windowSeconds
    )
    {
        List<(double From, double To, int Key, int Count)> spans = [];

        foreach (var row in live)
        {
            var key = groups.First(group => group.Rows.Contains(row)).Offset;

            if (spans is [.., var last] && last.Key == key)
                spans[^1] = last with { To = row.Start, Count = last.Count + 1 };
            else
                spans.Add((row.Start, row.Start, key, 1));
        }

        return
        [
            .. spans
                .Where(span => span.Count >= Run)
                .Select(span => new OffsetRun(span.From, span.To + windowSeconds, span.Key)),
        ];
    }

    /// <summary>Consecutive confident windows whose offsets stay within <see cref="JoinMs"/> of the previous one.</summary>
    private static List<List<OffsetRow>> Segments(IReadOnlyList<OffsetRow> live, int rate)
    {
        var join = Samples(JoinMs, rate);
        List<List<OffsetRow>> found = [];

        foreach (var row in live)
        {
            if (found is [.., var last] && Math.Abs(row.Offset - last[^1].Offset) <= join)
                last.Add(row);
            else
                found.Add([row]);
        }

        return found;
    }

    /// <summary>ppm of the first segment lying on a line that moves; null when none does.</summary>
    private static double? DriftOf(IReadOnlyList<OffsetRow> live, int rate)
    {
        var agree = Samples(AgreeMs, rate);

        foreach (var segment in Segments(live, rate))
        {
            if (segment.Count < DriftWindows)
                continue;

            var (slope, intercept) = Fit(
                [.. segment.Select(row => row.Start)],
                [.. segment.Select(row => (double)row.Offset)]
            );
            var onto = segment.Count(row =>
                Math.Abs(row.Offset - ((slope * row.Start) + intercept)) <= agree
            );
            var span = segment[^1].Start - segment[0].Start;

            if (
                (double)onto / segment.Count >= SteadyShare
                && Math.Abs(slope) * span >= Samples(DriftMs, rate)
            )
                return slope / rate * 1e6;
        }

        return null;
    }

    private static (double Slope, double Intercept) Fit(double[] x, double[] y)
    {
        var (meanX, meanY) = (x.Average(), y.Average());
        var variance = x.Sum(value => (value - meanX) * (value - meanX));
        var slope = variance is 0
            ? 0
            : x.Zip(y, (px, py) => (px - meanX) * (py - meanY)).Sum() / variance;

        return (slope, meanY - (slope * meanX));
    }

    /// <summary>Confident windows sharing one offset, biggest first.</summary>
    public static List<(int Offset, List<OffsetRow> Rows)> Clusters(
        IReadOnlyList<OffsetRow> live,
        int rate
    )
    {
        var agree = Samples(AgreeMs, rate);
        List<List<OffsetRow>> found = [];

        foreach (var row in live.OrderBy(row => row.Offset))
        {
            if (found is [.., var last] && row.Offset - last[^1].Offset <= agree)
                last.Add(row);
            else
                found.Add([row]);
        }

        return
        [
            .. found
                .Select(cluster =>
                    (
                        Median(cluster.Select(row => (double)row.Offset)),
                        cluster.OrderBy(row => row.Start).ToList()
                    )
                )
                .Select(cluster => ((int)cluster.Item1, cluster.Item2))
                .OrderByDescending(cluster => cluster.Item2.Count),
        ];
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        var middle = sorted.Length / 2;

        return sorted.Length % 2 is 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static List<OffsetSpan> SpansOf(
        IReadOnlyList<OffsetRow> rows,
        double windowSeconds,
        double total,
        Func<OffsetRow, bool> wanted
    )
    {
        List<OffsetSpan> spans = [];

        foreach (var row in rows.Where(wanted))
        {
            var end = Math.Min(row.Start + windowSeconds, total);

            if (spans is [.., var last] && row.Start <= last.To + 1e-6)
                spans[^1] = last with { To = Math.Max(last.To, end) };
            else
                spans.Add(new OffsetSpan(row.Start, end));
        }

        return spans;
    }

    private static List<OffsetSpan> MixGaps(
        IReadOnlyList<OffsetRow> voters,
        double windowSeconds,
        int rate,
        double sourceSeconds,
        double mixSeconds
    )
    {
        List<OffsetSpan> spans = [];
        var at = 0.0;

        foreach (
            var (from, to) in voters
                .Select(row =>
                    (
                        row.Start + ((double)row.Offset / rate),
                        Math.Min(row.Start + windowSeconds, sourceSeconds)
                            + ((double)row.Offset / rate)
                    )
                )
                .Order()
        )
        {
            if (from - at > GapSeconds)
                spans.Add(new OffsetSpan(at, from));

            at = Math.Max(at, to);
        }

        if (mixSeconds - at > GapSeconds)
            spans.Add(new OffsetSpan(at, mixSeconds));

        return spans;
    }
}
