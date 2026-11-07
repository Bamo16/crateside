using System.Globalization;
using AbleKit.Analysis;

namespace Crateside.Core.Warps;

/// <summary>Bars as Live's ruler numbers a warped clip's: beat 0 is bar 1, and the bar before it is -1 (ADR 0072).</summary>
public readonly record struct BarRuler(int Numerator, int Denominator)
{
    // A beat read back from seconds lands a hair either side of a bar line.
    private const double Snap = 1e-6;

    public static BarRuler Of(TimeSignature? signature) =>
        signature is { Numerator: > 0 and var numerator, Denominator: > 0 and var denominator }
            ? new(numerator, denominator)
            : new(4, 4);

    /// <summary>A bar's length in the warp's beats, which are quarter notes.</summary>
    public double BeatsPerBar => Numerator * 4.0 / Denominator;

    public int BarAt(double beat) => Number(IndexAt(beat));

    public double StartOf(int bar) => Index(bar) * BeatsPerBar;

    public double EndOf(int bar) => (Index(bar) + 1) * BeatsPerBar;

    /// <summary>Where a beat falls as Live's Start field writes it: <c>34</c> on a bar line, else <c>22.3.4</c>.</summary>
    public string Position(double beat)
    {
        var index = IndexAt(beat);
        var within = beat - index * BeatsPerBar;

        if (within < Snap)
            return Number(index).ToString(CultureInfo.InvariantCulture);

        var unit = 4.0 / Denominator;
        var inBar = (int)Math.Floor(within / unit + Snap);
        var sixteenth = (int)Math.Floor((within - inBar * unit) / 0.25 + Snap);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Number(index)}.{inBar + 1}.{sixteenth + 1}"
        );
    }

    /// <summary>Bars counted from beat 0, which is index 0, with no gap where Live skips bar 0.</summary>
    public int IndexAt(double beat) => (int)Math.Floor(beat / BeatsPerBar + Snap);

    public static int Number(int index) => index >= 0 ? index + 1 : index;

    public static int Index(int bar) => bar > 0 ? bar - 1 : bar;
}
