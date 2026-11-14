namespace Crateside.Core.Ableton;

/// <summary>One stem file's Meter keywords as Live holds them: what it carries, and what the person took off it.</summary>
public sealed record StemMeter(
    string Song,
    string Stem,
    IReadOnlySet<string> Present,
    IReadOnlySet<string> Removed
);

/// <summary>What the Meter tags of a song's stems come to once a change on one is carried to the rest (#154).</summary>
/// <param name="Applied">The Meter values every stem should carry; unchanged when the stems contradict.</param>
/// <param name="Retired">Every value crateside once applied and no longer wants, which is taken off the stems that still hold it.</param>
/// <param name="RemovalsSeen">Each removal already acted on, as the stem and the value, so a hidden keyword the store still holds does not undo a later choice.</param>
public sealed record MeterOutcome(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Retired,
    IReadOnlyList<string> RemovalsSeen,
    bool Contradicts
);

public static class MeterTags
{
    public const string Group = "Meter";

    public const string OddBars = "Odd bars";

    public const string Mixed = "Mixed";

    public const string Plain = "4/4";

    public static string Keyword(string value) => $"{Group}|{value}";

    /// <summary>The Meter value of a keyword, or null for any other keyword or a value crateside does not know.</summary>
    public static string? ValueOf(string keyword) =>
        keyword.Split('|') is [Group, var value] && CratesideKeywords.IsKnown(keyword)
            ? value
            : null;

    /// <summary>A track carries one base, <c>n/d</c> or <c>Mixed</c>; more than one is a contradiction.</summary>
    public static bool Contradicts(IEnumerable<string> values) =>
        values.Count(value => value != OddBars) > 1;

    /// <summary>Whether Live should flag the track as odd: anything but plain 4/4.</summary>
    public static bool IsOdd(IReadOnlyCollection<string> values) =>
        values.Count is not 1 || !values.Contains(Plain);

    /// <summary>
    /// Carries each stem's change since <paramref name="applied"/> to the rest: a value one stem has
    /// that was not applied is added, and a value taken off one stem that was applied is removed.
    /// </summary>
    public static MeterOutcome Settle(
        IReadOnlyList<string> applied,
        IReadOnlyList<string> retired,
        IReadOnlyList<string> removalsSeen,
        IEnumerable<StemMeter> stems
    )
    {
        var added = new HashSet<string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var acted = new List<string>();

        foreach (var stem in stems)
        {
            added.UnionWith(stem.Present.Except(applied));

            foreach (var value in stem.Removed.Intersect(applied))
            {
                var removal = $"{stem.Stem}|{value}";

                if (removalsSeen.Contains(removal))
                    continue;

                removed.Add(value);
                acted.Add(removal);
            }
        }

        var wanted = applied.Union(added).Except(removed).Order(StringComparer.Ordinal).ToList();

        if (Contradicts(wanted))
            return new MeterOutcome(applied, retired, removalsSeen, Contradicts: true);

        return new MeterOutcome(
            wanted,
            [.. retired.Union(applied).Except(wanted).Order(StringComparer.Ordinal)],
            [.. removalsSeen.Union(acted)],
            Contradicts: false
        );
    }
}
