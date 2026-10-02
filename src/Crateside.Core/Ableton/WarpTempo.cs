using AbleKit.Analysis;

namespace Crateside.Core.Ableton;

/// <summary>The tempo a finished warp holds, read bar by bar across its default clip (ADR 0038).</summary>
/// <param name="Bpm">The stable tempo that holds for the most bars, or the median bar when none does.</param>
/// <param name="Low">The slowest audible bar.</param>
/// <param name="High">The fastest audible bar.</param>
/// <param name="Stable">Each stable tempo and its audible bars, most bars first.</param>
public sealed record WarpTempo(
    double Bpm,
    TempoShape Shape,
    double Low,
    double High,
    IReadOnlyList<StableTempo> Stable
)
{
    private const int BeatsPerBar = 4;
    private const int MinStableBars = 8;
    private const double FitTolerance = 0.003;
    private const double SameTempo = 0.05;
    private const int BarsPerWindow = 4;

    /// <summary>−50 dBFS.</summary>
    private const float SilencePeak = 0.00316f;

    /// <summary>The bars <see cref="StableTempo.Bars"/> counts against, so a main tempo's share of the track.</summary>
    public int AudibleBars { get; init; }

    /// <summary>Each audible run of four bars and its tempo, where a drifting warp's range is read.</summary>
    public IReadOnlyList<TempoWindow> Windows { get; init; } = [];

    /// <summary>The tempo of a saved warp; null when the default clip was never saved.</summary>
    public static WarpTempo? Of(AnalysisFile analysis, int sampleRate) =>
        analysis.DefaultClip is { } clip
            ? Of(clip, analysis.Warp, analysis.Audio.Overview, sampleRate)
            : null;

    /// <summary>The tempo across <paramref name="clip"/>; bars <paramref name="overview"/> shows silent are set aside.</summary>
    public static WarpTempo? Of(
        DefaultClip clip,
        Warp warp,
        SampleOverview? overview,
        int sampleRate
    )
    {
        if (warp is not { Markers.Count: > 1 })
            return null;

        var first = Math.Floor(clip.Start / BeatsPerBar) * BeatsPerBar;
        var lines = Enumerable
            .Range(0, (int)((clip.End - first) / BeatsPerBar) + 1)
            .Select(bar => first + bar * BeatsPerBar)
            .Select(beat => (Beat: beat, Seconds: warp.SecondsAt(beat)!.Value))
            .ToArray();

        if (lines.Length < 2)
            return null;

        var bars = lines
            .Zip(lines.Skip(1))
            .Select(pair => new Bar(
                BeatsPerBar * 60 / (pair.Second.Seconds - pair.First.Seconds),
                IsSilent(overview, pair.First.Seconds, pair.Second.Seconds, sampleRate)
            ))
            .ToArray();

        var audible = Audible(bars);
        var runs = Runs(lines).ToList();
        var stable = runs.Select(run => bars[run])
            .Where(run => run.Count(bar => !bar.IsSilent) >= MinStableBars)
            .Select(run => new StableTempo(Median(Audible(run)), run.Count(bar => !bar.IsSilent)))
            .Aggregate(new List<StableTempo>(), Group)
            .OrderByDescending(tempo => tempo.Bars)
            .ToList();

        WarpTempo tempo = stable switch
        {
            [] => new(Median(audible), TempoShape.Drifting, audible.Min(), audible.Max(), []),
            [var main, ..] => new(
                main.Bpm,
                runs.Count is 1 ? TempoShape.Steady : TempoShape.MostlySteady,
                audible.Min(),
                audible.Max(),
                stable
            ),
        };

        return tempo with
        {
            AudibleBars = audible.Length,
            Windows = WindowsOf(lines, overview, sampleRate),
        };
    }

    /// <summary>
    /// The tempo of each audible run of four bars, from its bar lines: a downbeat marker 25 ms off
    /// moves four bars' tempo about 0.3%, where one bar's would move 1.3%.
    /// </summary>
    private static TempoWindow[] WindowsOf(
        (double Beat, double Seconds)[] lines,
        SampleOverview? overview,
        int sampleRate
    ) =>
        [
            .. Enumerable
                .Range(0, (lines.Length - 1) / BarsPerWindow)
                .Select(i =>
                    (
                        From: lines[i * BarsPerWindow].Seconds,
                        To: lines[(i + 1) * BarsPerWindow].Seconds
                    )
                )
                .Where(window => !IsSilent(overview, window.From, window.To, sampleRate))
                .Select(window => new TempoWindow(
                    window.From,
                    window.To,
                    BarsPerWindow * BeatsPerBar * 60 / (window.To - window.From)
                )),
        ];

    private static bool IsSilent(
        SampleOverview? overview,
        double from,
        double to,
        int sampleRate
    ) =>
        overview is not null
        && overview.PeakBetween((long)(from * sampleRate), (long)(to * sampleRate)) < SilencePeak;

    /// <summary>The bar tempos, or all of them when every bar is silent.</summary>
    private static double[] Audible(IReadOnlyList<Bar> bars) =>
        bars.Where(bar => !bar.IsSilent).Select(bar => bar.Bpm).ToArray() is { Length: > 0 } heard
            ? heard
            : [.. bars.Select(bar => bar.Bpm)];

    /// <summary>
    /// Consecutive bars one tempo fits, taken greedily from the start: each run grows while a
    /// straight line through its bar lines stays within the tolerance.
    /// </summary>
    private static IEnumerable<Range> Runs((double Beat, double Seconds)[] lines)
    {
        var start = 0;

        while (start < lines.Length - 1)
        {
            var end = start + 1;

            while (end < lines.Length - 1 && Fits(lines.AsSpan(start..(end + 2))))
                end++;

            yield return start..end;

            start = end;
        }
    }

    private static bool Fits(ReadOnlySpan<(double Beat, double Seconds)> lines)
    {
        double beats = 0,
            seconds = 0;

        foreach (var (beat, second) in lines)
        {
            beats += beat;
            seconds += second;
        }

        var (meanBeat, meanSeconds) = (beats / lines.Length, seconds / lines.Length);
        double covariance = 0,
            variance = 0;

        foreach (var (beat, second) in lines)
        {
            covariance += (beat - meanBeat) * (second - meanSeconds);
            variance += (beat - meanBeat) * (beat - meanBeat);
        }

        var slope = covariance / variance;

        foreach (var (beat, second) in lines)
            if (Math.Abs(second - (meanSeconds + slope * (beat - meanBeat))) > FitTolerance)
                return false;

        return true;
    }

    private static List<StableTempo> Group(List<StableTempo> groups, StableTempo run)
    {
        var index = groups.FindIndex(group => Math.Abs(group.Bpm - run.Bpm) <= SameTempo);

        if (index < 0)
            groups.Add(run);
        else
            groups[index] = groups[index] with { Bars = groups[index].Bars + run.Bars };

        return groups;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.Order().ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 is 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private readonly record struct Bar(double Bpm, bool IsSilent);
}

/// <summary>A stretch of the audio and its tempo; times in seconds from the file's start.</summary>
public readonly record struct TempoWindow(double Start, double End, double Bpm);

/// <summary>One stable tempo in a warp and how many audible bars hold it.</summary>
public readonly record struct StableTempo(double Bpm, int Bars);

public enum TempoShape
{
    /// <summary>One tempo across the whole default clip.</summary>
    Steady,

    /// <summary>A main tempo, and a ramp, a drifting stretch or another stable tempo besides.</summary>
    MostlySteady,

    /// <summary>No tempo holds for eight audible bars: nearly always a drifting track.</summary>
    Drifting,
}
