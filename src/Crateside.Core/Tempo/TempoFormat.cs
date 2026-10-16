using System.Globalization;
using Crateside.Core.Ableton;
using Crateside.Core.Library;
using Humanizer;

namespace Crateside.Core.Tempo;

/// <summary>The one place a BPM becomes text; a test fails if a page formats one itself (ADR 0049, handoff 13).</summary>
public static class TempoFormat
{
    private const string FromWarp = "from your warp";
    private const string FromReading = "crateside's reading";
    private const string FromEstimate = "ledger's estimate";
    private const double RelatedWithin = 2;

    /// <summary>From this many ms by the end an off-tempo warp is worth truing up, and its mark is filled.</summary>
    public const double FilledAtMs = 10;

    private static readonly (double Multiple, string Words)[] Relations =
    [
        (1, "agrees"),
        (0.5, "half of it"),
        (2, "double it"),
        (2.0 / 3, "two-thirds of it"),
        (3.0 / 4, "three-quarters of it"),
        (4.0 / 3, "four-thirds of it"),
        (3.0 / 2, "one and a half times it"),
    ];

    public static FormattedTempo Format(TrackTempo tempo) =>
        new(
            Text(tempo),
            !tempo.IsVouched,
            tempo.Source is TempoSource.Estimate,
            Note(tempo),
            Flag(tempo),
            Brief(tempo),
            Mark(tempo),
            Explanation(tempo)
        );

    /// <summary>A bin of the tempo chart: whole numbers, no marks.</summary>
    public static string Bin(int low, int high) => $"{low}–{high}";

    /// <summary>A tempo shared by enough songs to be called out, and how many share it.</summary>
    public static string Peak(int bpm, int songs) => $"{bpm} ×{songs}";

    /// <summary>The slowest to the fastest of several tempos, rounded outward; one tempo alone is written as itself.</summary>
    public static string? Range(IEnumerable<TrackTempo> tempos) =>
        tempos.ToList() switch
        {
            [] => null,
            var all
                when all.MinBy(tempo => tempo.Value) is { } slowest
                    && all.MaxBy(tempo => tempo.Value) is { } fastest
                    && slowest.Value == fastest.Value => Text(slowest),
            var all => Span(all.Min(tempo => tempo.Value), all.Max(tempo => tempo.Value)),
        };

    /// <summary>How a window of the tempo strip reads on hover: <c>2:30–2:38 · ~112</c>.</summary>
    public static string Window(TempoWindow window) =>
        $"{Clock(window.Start)}–{Clock(window.End)} · ~{Whole(window.Bpm)}";

    public static string Clock(double seconds)
    {
        var whole = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);

        return $"{whole / 60}:{whole % 60:00}";
    }

    /// <summary>A whole number for the edge of a strip or a range, rounded <paramref name="up"/> or down.</summary>
    public static int Edge(double bpm, bool up) => (int)(up ? Math.Ceiling(bpm) : Math.Floor(bpm));

    /// <summary>The headline's other sources, each as it would read alone and how it relates to the headline.</summary>
    public static IReadOnlyList<SourceLine> Others(TrackTempo headline) =>
        headline.Source is TempoSource.Typed
            ? []
            :
            [
                .. new[] { TempoSource.Warp, TempoSource.Reading, TempoSource.Estimate }
                    .Where(source => source != headline.Source)
                    .Select(source => Line(headline, source))
                    .OfType<SourceLine>(),
            ];

    private static SourceLine? Line(TrackTempo headline, TempoSource source) =>
        (source, headline.From(source)) switch
        {
            (TempoSource.Warp, null) => new("Your warp", null, "not warped yet", false),
            (TempoSource.Reading, null) => new(FromReading, null, "not read yet", false),
            (_, null) => null,
            (_, { } other) => Relation(other.Measured, headline.Measured) is { } words
                ? new(Label(source), Format(other), words, false)
                : new(Label(source), Format(other), "conflict", true),
        };

    private static string? Relation(double other, double headline) =>
        Relations
            .Where(relation => Math.Abs(other - headline * relation.Multiple) <= RelatedWithin)
            .Select(relation => relation.Words)
            .FirstOrDefault();

    private static string Label(TempoSource source) =>
        source switch
        {
            TempoSource.Warp => "Your warp",
            TempoSource.Reading => FromReading,
            _ => FromEstimate,
        };

    private static string Text(TrackTempo tempo) =>
        tempo.IsVouched ? Hundredths(tempo.Value) : $"~{Whole(tempo.Value)}";

    /// <summary>Two decimals with <c>.00</c> dropped, never one: <c>128</c>, <c>128.02</c>, <c>119.50</c>.</summary>
    private static string Hundredths(double bpm) =>
        Math.Round(bpm, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture)
            is var text
        && text.EndsWith(".00", StringComparison.Ordinal)
            ? text[..^3]
            : text;

    private static int Whole(double bpm) => TempoCounts.Whole(bpm);

    private static string Note(TrackTempo tempo) =>
        tempo switch
        {
            { Source: TempoSource.Typed } => string.Empty,
            { Drifts: true } => Join(Drift(tempo), Source(tempo)),
            { ChangesTempo: true, Warp.Stable: [_, var second, ..] and var stable } => Join(
                "changes tempo",
                $"{"bar".ToQuantity(second.Bars)} at {Whole(second.Bpm)}"
                    + (stable.Count > 2 ? " · and others" : string.Empty),
                Source(tempo)
            ),
            { PartChanges: true, Warp: { AudibleBars: > 0 and var bars, Stable: [var main] } } =>
                Join(
                    $"steady for {Percent(main.Bars / (double)bars * 100)}",
                    "one part changes tempo",
                    Source(tempo)
                ),
            { PartChanges: true } => Join("one part changes tempo", Source(tempo)),
            { Off: { } off } => Join("off its tempo", OffBy(tempo, off), Source(tempo)),
            { Unconfirmed.Reason: TrueUpReason.Steps } => Join("steps in its grid", Source(tempo)),
            { Unconfirmed: not null } => Join("grid not confirmed", Source(tempo)),
            { Source: TempoSource.Reading, IsVouched: true } => Join(
                FromReading,
                "steady to the hundredth"
            ),
            { Source: TempoSource.Reading } => Join(
                FromReading,
                "steady, but not to the hundredth"
            ),
            { Source: TempoSource.Estimate } => Join(FromEstimate, "not read yet"),
            _ => Source(tempo),
        };

    /// <summary>The Tools card's short form of the note.</summary>
    private static string Brief(TrackTempo tempo) =>
        tempo switch
        {
            { Drifts: true } => Drift(tempo),
            { ChangesTempo: true } => "changes tempo",
            { PartChanges: true } => "part changes tempo",
            { Off: not null } => "off its tempo",
            _ => Source(tempo),
        };

    private static TempoFlag? Flag(TrackTempo tempo) =>
        tempo switch
        {
            { Drifts: true, Range: var (low, high) } => new(
                "drifts",
                Spread(low, high, tempo.Value)
            ),
            { Drifts: true } => new("tempo drifts"),
            { ChangesTempo: true } => new("changes tempo"),
            { PartChanges: true } => new("part changes tempo"),
            _ => null,
        };

    private static OffMark Mark(TrackTempo tempo) =>
        tempo.Off switch
        {
            { DriftMs: { } ms } when Math.Abs(ms) >= FilledAtMs => OffMark.Filled,
            { DriftMs: not null } => OffMark.Hollow,
            _ => OffMark.None,
        };

    /// <summary>How fast or slow the warp runs against the audio, for the Off its tempo list: <c>0.017 slow</c>.</summary>
    public static string? Runs(TrackTempo tempo) =>
        tempo.Off is { DriftMs: not null } off ? Offset(tempo.Measured, off.Bpm) : null;

    /// <summary>How far the grid has fallen by the end, for the Off its tempo list: <c>27 ms</c>.</summary>
    public static string? ByTheEnd(TrackTempo tempo) =>
        tempo.Off is { DriftMs: { } ms } ? $"{Math.Abs(ms):0} ms" : null;

    /// <summary>The song page's sentence naming the user's figure and what the audio holds; null where the warp is not the one questioned.</summary>
    private static string? Explanation(TrackTempo tempo) =>
        tempo switch
        {
            { Off: { DriftMs: { } ms } off } when Math.Abs(ms) >= FilledAtMs =>
                $"Your warp is typed at {Hundredths(tempo.Measured)}. The audio holds {Thousandths(off.Bpm)}, so its grid falls {Math.Abs(ms):0} ms {(ms < 0 ? "behind" : "ahead")} by the end.",
            { Off: { DriftMs: { } ms } off } =>
                $"Your warp is typed at {Hundredths(tempo.Measured)}. The audio holds {Thousandths(off.Bpm)}: {Math.Abs(ms):0} ms by the end, too little to hear.",
            { Off: { } off } =>
                $"Your warp is typed at {Hundredths(tempo.Measured)}. crateside reads {Hundredths(off.Bpm)}.",
            { Unconfirmed.Reason: TrueUpReason.Steps } =>
                "Your warp holds one tempo, but the beat jumps partway through. That takes a marker in Live where it jumps, not a retype.",
            { Unconfirmed: not null } =>
                "Too few transients to check your warp against. The figure is the one you typed; crateside can't vouch for it.",
            _ => null,
        };

    /// <summary>Up to three decimals with trailing zeros dropped: <c>125.017</c>, <c>98.002</c>, <c>125</c>.</summary>
    private static string Thousandths(double bpm) =>
        Math.Round(bpm, 3, MidpointRounding.AwayFromZero)
            .ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary><c>runs 0.012 fast · drifts 23 ms</c>, or what the reading says where only it disagrees.</summary>
    private static string OffBy(TrackTempo tempo, TempoOff off) =>
        off.DriftMs is { } ms
            ? Join($"runs {Offset(tempo.Measured, off.Bpm)}", $"drifts {Math.Abs(ms):0} ms")
            : $"crateside reads {Hundredths(off.Bpm)}";

    /// <summary>How far a warp sits from the tempo it should be at, to the thousandth, and which way: <c>0.012 fast</c>.</summary>
    public static string Offset(double warp, double should) =>
        Math.Abs(warp - should) switch
        {
            < 0.0005 => "<0.001",
            var off =>
                $"{off.ToString("0.000", CultureInfo.InvariantCulture)} {(warp > should ? "fast" : "slow")}",
        };

    /// <summary><c>drifts 110–112 · 2%</c>, or just <c>drifts</c> when there is no range to give.</summary>
    private static string Drift(TrackTempo tempo) =>
        tempo.Range is var (low, high)
            ? Join($"drifts {Span(low, high)}", Spread(low, high, tempo.Value))
            : "drifts";

    private static string Span(double low, double high) =>
        Bin(Edge(low, up: false), Edge(high, up: true));

    /// <summary>The range as a whole percentage of the figure shown: tenths would claim a precision the windows do not have.</summary>
    private static string Spread(double low, double high, double shown) =>
        Percent((high - low) / shown * 100);

    private static string Percent(double percent) =>
        percent < 1 ? "<1%" : $"{Math.Round(percent, MidpointRounding.AwayFromZero):0}%";

    private static string Source(TrackTempo tempo) =>
        tempo.Source switch
        {
            TempoSource.Warp => FromWarp,
            TempoSource.Reading => FromReading,
            TempoSource.Estimate => FromEstimate,
            _ => string.Empty,
        };

    private static string Join(params ReadOnlySpan<string> parts) => string.Join(" · ", parts);
}

/// <param name="IsApproximate">Drawn with the tilde at weight 400.</param>
/// <param name="IsEstimate">the ledger's, so also drawn at <c>--text-50</c>.</param>
/// <param name="Note">The song page's note, and the tooltip wherever the figure stands without one.</param>
/// <param name="Flag">The Tracks flag beside the title, where the tempo's shape earns one.</param>
/// <param name="Brief">The Tools card's short note.</param>
/// <param name="Mark">The pennant after the figure, for a warp crateside can true up.</param>
/// <param name="Explanation">The song page's sentence naming both figures, where the user's warp is the one questioned.</param>
public sealed record FormattedTempo(
    string Text,
    bool IsApproximate,
    bool IsEstimate,
    string Note,
    TempoFlag? Flag,
    string Brief,
    OffMark Mark = OffMark.None,
    string? Explanation = null
)
{
    /// <summary>What the clipboard gets: the figure as shown, without the tilde.</summary>
    public string Figure => Text.TrimStart('~');

    /// <summary>The invisible <c>.00</c> that aligns a whole figure on the decimal point.</summary>
    public string Padding => Text.Contains('.') ? string.Empty : ".00";

    /// <summary>Said in the row once the figure is on the clipboard.</summary>
    public string Copied =>
        IsApproximate
            ? $"Copied {Figure}. The tempo drifts, so it's approximate."
            : $"Copied {Figure}";
}

/// <summary>How far off its tempo a warp is: filled from 10 ms by the end, hollow below, none where a reading alone disagrees.</summary>
public enum OffMark
{
    None,
    Hollow,
    Filled,
}

/// <summary>The Tracks flag beside the title; the spread, where there is one, is set in mono.</summary>
public sealed record TempoFlag(string Words, string? Spread = null);

/// <summary>One of the evidence panel's lines under the headline: a source, its figure if it has one, and how it relates.</summary>
public sealed record SourceLine(
    string Label,
    FormattedTempo? Figure,
    string Comment,
    bool IsConflict
);
