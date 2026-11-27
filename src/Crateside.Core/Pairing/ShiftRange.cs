namespace Crateside.Core.Pairing;

/// <summary>The semitone shifts in play, from <see cref="Lowest"/> up to <see cref="Highest"/>, always holding 0 (ADR 0102).</summary>
public sealed record ShiftRange
{
    public ShiftRange(int lowest, int highest, int max)
    {
        Max = Math.Max(max, 0);
        Lowest = Math.Clamp(lowest, -Max, 0);
        Highest = Math.Clamp(highest, 0, Max);
    }

    public int Lowest { get; }

    public int Highest { get; }

    public int Max { get; }

    public int Count => Highest - Lowest + 1;

    /// <summary>A range from the query, clamped; a missing end falls back to ±<paramref name="defaultShift"/>.</summary>
    public static ShiftRange Parse(int? lowest, int? highest, int max, int defaultShift) =>
        new(lowest ?? -defaultShift, highest ?? defaultShift, max);

    /// <summary>Every shift in range, highest first.</summary>
    public IEnumerable<int> Rows() => Enumerable.Range(0, Count).Select(i => Highest - i);

    public ShiftRange WithLowest(int lowest) => new(lowest, Highest, Max);

    public ShiftRange WithHighest(int highest) => new(Lowest, highest, Max);

    public bool Includes(int shift) => shift >= Lowest && shift <= Highest;
}
