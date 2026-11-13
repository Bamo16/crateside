using System.Runtime.CompilerServices;
using AbleKit.Analysis;
using Crateside.Core.Ledger;
using Crateside.Core.Warps;

namespace Crateside.Components;

/// <summary>What the browser redraws the rows from while the view's window is dragged (ADR 0094); the drawing is mirrored in playback.js.</summary>
public partial class WarpView
{
    private const int PeakColumnsPerSecond = 150;
    private const int PeakColumnsCap = 32768;

    private WarpGroup? _liveGroup;
    private string? _liveKey;

    /// <summary>The track's rows, whole, once per change to what they hold; null when the browser already has them.</summary>
    private object? LiveOf(WarpGroup group, IReadOnlyList<Band> bands)
    {
        var key =
            string.Join(
                '|',
                bands.Select(band =>
                    $"{band.Start}:{band.End}:{band.Tag}:{band.Style}:{band.Line}:{band.Window}"
                )
            )
            + $"|{_openWindow}|{SelectedSection}|{(Run is null ? 0 : RuntimeHelpers.GetHashCode(Run))}|{group.Seconds}";

        if (ReferenceEquals(_liveGroup, group) && _liveKey == key)
            return null;

        (_liveGroup, _liveKey) = (group, key);

        var (figure, low, high, _) = TempoScale(group);
        var markers = group.SharedAllMarkers;

        return new
        {
            seconds = group.Seconds,
            timeRuler = group.Lender is null,
            clip = group.Lender is { } lender
                ? new
                {
                    start = lender.Clip.Start,
                    title = $"Clip start · {WarpWording.Time(lender.Clip.Start)} · bar {StartBar(group)}",
                }
                : null,
            loop = group.Lender?.Loop is { } loop
                ? new
                {
                    start = loop.Start,
                    end = loop.End,
                    on = loop.On,
                    title = LoopTitle(loop),
                }
                : null,
            bands = bands.Select(band => new
            {
                start = band.Start,
                end = band.End,
                tag = band.Tag,
                style = band.Style,
                window = band.Window,
                open = band.Window is { } window && window == _openWindow,
                button = band.Window is not null,
            }),
            lanes = group.Stems.Select(stem => LaneOf(group, stem)),
            keys = Run is null
                ? null
                : KeyStrip
                    .Plan(Run, group.Seconds)
                    .Bands.Select(band => new
                    {
                        start = band.Start,
                        end = band.End,
                        label = band.Label,
                        title = KeyLane.Title(band),
                        fromWindow = band.FromWindow,
                        unbacked = band.StartUnbacked,
                        pickable = band.Section is { Length: > 0 },
                        picked = band.Section is { Length: > 0 } section
                            && string.Equals(
                                section,
                                SelectedSection,
                                StringComparison.OrdinalIgnoreCase
                            ),
                    }),
            tempo = markers is { Count: > 1 }
                ? new { line = Y(figure, low, high), segments = TempoSegments(markers, low, high) }
                : null,
            off = OffGrid(group) is { } off
                ? off.Report.Windows.Select(window =>
                    new[] { (window.Start + window.End) / 2, OffGridRow.Percent(window.OffsetMs) }
                )
                : null,
        };
    }

    private static object LaneOf(WarpGroup group, StemWarp stem) =>
        stem.Sidecar is null
            ? new { file = Path.GetFileName(stem.Path), covered = false }
            : new
            {
                file = Path.GetFileName(stem.Path),
                covered = true,
                peaks = PeaksOf(stem, group.Seconds),
                markers = stem.Markers.Select(marker => marker.Seconds),
                transients = stem.TransientTimes,
                clip = stem.Clip.Start,
                parts = group.PartsFrom(stem),
            };

    private static IEnumerable<double?[]> TempoSegments(
        IReadOnlyList<WarpMarker> markers,
        double low,
        double high
    )
    {
        for (var i = 0; i < markers.Count - 1; i++)
        {
            var span = markers[i + 1].Seconds - markers[i].Seconds;

            if (span <= 0)
                continue;

            var bpm = (markers[i + 1].Beat - markers[i].Beat) / span * 60;

            yield return
            [
                i is 0 ? null : markers[i].Seconds,
                i == markers.Count - 2 ? null : markers[i + 1].Seconds,
                Y(bpm, low, high),
            ];
        }
    }

    /// <summary>Each channel's whole-track envelope as bytes, a column's lowest then highest sample as magnitudes out of 255, channels end to end.</summary>
    private static object PeaksOf(StemWarp stem, double seconds)
    {
        var columns = (int)
            Math.Clamp(Math.Ceiling(seconds * PeakColumnsPerSecond), Columns, PeakColumnsCap);
        var channels = stem.Overview(0, seconds, columns);
        var bytes = new byte[channels.Count * columns * 2];

        for (var channel = 0; channel < channels.Count; channel++)
        {
            for (var x = 0; x < columns; x++)
            {
                var (min, max) = channels[channel][x];
                var at = (channel * columns + x) * 2;

                bytes[at] = (byte)Math.Round(Math.Clamp(-min, 0, 1) * 255);
                bytes[at + 1] = (byte)Math.Round(Math.Clamp(max, 0, 1) * 255);
            }
        }

        return new
        {
            columns,
            channels = channels.Count,
            data = Convert.ToBase64String(bytes),
        };
    }
}
