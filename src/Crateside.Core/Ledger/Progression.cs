using System.Text.RegularExpressions;

namespace Crateside.Core.Ledger;

/// <summary>A run of the chord progression: chords played once, or a loop of them played <see cref="Repeats"/> times.</summary>
public sealed record ChordSegment(IReadOnlyList<string> Chords, int Repeats)
{
    public bool IsLoop => Repeats > 1;

    public string Arrowed => string.Join(" → ", Chords);

    public override string ToString() => IsLoop ? $"{Arrowed} ×{Repeats}" : Arrowed;
}

/// <summary>The detectors' chord stream folded for reading (ADR 0064).</summary>
public static class Progression
{
    private const int MaxLoopLength = 8;
    private const int MinRepeats = 3;

    /// <summary>The chords spelled for the screen, a repeated loop folded: <c>G♯m → E ×18 · Em · G♯m → E ×12</c>.</summary>
    public static IReadOnlyList<ChordSegment> Fold(IReadOnlyList<string> chords)
    {
        var spelled = chords.Select(JudgeProse.Chord).ToList();
        List<ChordSegment> segments = [];
        List<string> once = [];
        var i = 0;

        while (i < spelled.Count)
        {
            var (length, repeats) = Loop(spelled, i);

            if (repeats < MinRepeats)
            {
                once.Add(spelled[i++]);

                continue;
            }

            if (once is not [])
            {
                segments.Add(new([.. once], 1));
                once.Clear();
            }

            segments.Add(new(spelled[i..(i + length)], repeats));
            i += length * repeats;
        }

        if (once is not [])
            segments.Add(new([.. once], 1));

        return segments;
    }

    /// <summary>The loop at <paramref name="start"/> that covers the most chords, shortest first on a tie.</summary>
    private static (int Length, int Repeats) Loop(List<string> chords, int start)
    {
        (int Length, int Repeats) best = (0, 0);

        for (var length = 1; length <= MaxLoopLength && start + length <= chords.Count; length++)
        {
            var repeats = 1;

            while (
                start + (repeats + 1) * length <= chords.Count
                && Same(chords, start, start + repeats * length, length)
            )
                repeats++;

            if (repeats >= MinRepeats && repeats * length > best.Length * best.Repeats)
                best = (length, repeats);
        }

        return best;
    }

    private static bool Same(List<string> chords, int first, int second, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (chords[first + i] != chords[second + i])
                return false;
        }

        return true;
    }
}

/// <summary>keydetect's flag as a short chip, with the band it names in its tooltip (ADR 0064).</summary>
public sealed partial record FlagChip(string Text, string Tooltip)
{
    public static FlagChip? Of(string? flag) =>
        flag?.Trim() switch
        {
            null or "" => null,
            var text when Distrust.Match(text) is { Success: true } distrust => new(
                $"r < {distrust.Groups["Limit"].Value}",
                $"keydetect's r is below {distrust.Groups["Limit"].Value}: an unreliable band."
            ),
            var text when Gray.Match(text) is { Success: true } gray => new(
                $"r {gray.Groups["Low"].Value}–{gray.Groups["High"].Value}",
                $"keydetect's r is between {gray.Groups["Low"].Value} and {gray.Groups["High"].Value}: the gray zone."
            ),
            var text when Trust.Match(text) is { Success: true } trust => new(
                $"r > {trust.Groups["Limit"].Value}",
                $"keydetect's r is above {trust.Groups["Limit"].Value}: usually right, but watch for a relative-key swap."
            ),
            var text => new(text, text),
        };

    [GeneratedRegex(@"^DISTRUST.*?r\s*<\s*(?<Limit>\d*\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Distrust { get; }

    [GeneratedRegex(
        @"^gray zone.*?(?<Low>\d*\.\d+)\s*[-–]\s*(?<High>\d*\.\d+)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex Gray { get; }

    [GeneratedRegex(@"^trust band.*?>\s*(?<Limit>\d*\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Trust { get; }
}
