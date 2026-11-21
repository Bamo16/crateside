using AbleKit.Analysis;

namespace Crateside.Core.Warps;

/// <summary>
/// A stem's whole envelope as bytes: the one table the server's lanes and the browser's
/// (playback/rows.js) are both drawn from, with the same arithmetic, so the two match (ADR 0094).
/// </summary>
public sealed class WavePeaks
{
    private WavePeaks(int columns, int channels, double secondsPerColumn, byte[] bytes) =>
        (Columns, Channels, SecondsPerColumn, Bytes) = (columns, channels, secondsPerColumn, bytes);

    public int Columns { get; }

    public int Channels { get; }

    /// <summary>A whole number of Live's bins, so a column never straddles one.</summary>
    public double SecondsPerColumn { get; }

    /// <summary>A column's lowest then highest sample as magnitudes out of 255, channels end to end.</summary>
    public byte[] Bytes { get; }

    /// <summary>The finest table of at most <paramref name="cap"/> columns; empty without an overview.</summary>
    public static WavePeaks Of(SampleOverview? overview, int sampleRate, int cap)
    {
        if (overview is not { BinCount: > 0, ChannelCount: var channels })
            return new(0, 0, 0, []);

        var finest = overview.Levels[0].Span;
        var binsPerColumn = (overview.BinCount + cap - 1) / cap;
        var columns = (overview.BinCount + binsPerColumn - 1) / binsPerColumn;
        var bytes = new byte[channels * columns * 2];

        for (var channel = 0; channel < channels; channel++)
        {
            for (var bin = 0; bin < overview.BinCount; bin++)
            {
                var from = (bin * channels + channel) * 2;
                var to = (channel * columns + bin / binsPerColumn) * 2;

                bytes[to] = Math.Max(bytes[to], Magnitude(-(float)finest[from]));
                bytes[to + 1] = Math.Max(bytes[to + 1], Magnitude((float)finest[from + 1]));
            }
        }

        return new(
            columns,
            channels,
            (double)binsPerColumn * overview.SamplesPerBin / sampleRate,
            bytes
        );
    }

    /// <summary>
    /// Each channel's lowest and highest sample in each of <paramref name="across"/> equal slices
    /// between two times, as rows.js's wavePath takes them.
    /// </summary>
    public IReadOnlyList<(double Min, double Max)[]> Envelope(double from, double to, int across)
    {
        if (Columns is 0 || to <= from)
            return [];

        var drawn = new (double Min, double Max)[Channels][];

        for (var channel = 0; channel < Channels; channel++)
        {
            drawn[channel] = new (double, double)[across];

            for (var x = 0; x < across; x++)
            {
                var first = (int)Math.Floor((from + (to - from) * x / across) / SecondsPerColumn);
                var last = Math.Max(
                    first,
                    (int)Math.Floor((from + (to - from) * (x + 1) / across) / SecondsPerColumn) - 1
                );
                var (low, high) = ((byte)0, (byte)0);

                for (
                    var column = Math.Max(first, 0);
                    column <= Math.Min(last, Columns - 1);
                    column++
                )
                {
                    var at = (channel * Columns + column) * 2;

                    (low, high) = (Math.Max(low, Bytes[at]), Math.Max(high, Bytes[at + 1]));
                }

                drawn[channel][x] = (-low / 255.0, high / 255.0);
            }
        }

        return drawn;
    }

    private static byte Magnitude(float sample) => (byte)Math.Round(Math.Clamp(sample, 0, 1) * 255);
}
