using System.Diagnostics;
using Humanizer;

namespace Crateside.Core.Ableton;

/// <summary>
/// The Live browser Collection, 1 to 7, each meaning is written to; none writes nothing and a
/// number mapped to none is never touched (#166).
/// </summary>
public sealed record CollectionSettings
{
    public const int Lowest = 1;
    public const int Highest = 7;

    public int? CheckBeat { get; init; }

    public int? Warped { get; init; }

    public int? WarpByHand { get; init; }

    public int? KeyCaveat { get; init; }

    public int? OddBars { get; init; }

    /// <summary>The numbers crateside used before they were a choice, which an existing install keeps.</summary>
    public static CollectionSettings Maintainers { get; } =
        new()
        {
            CheckBeat = 2,
            Warped = 4,
            WarpByHand = 5,
            KeyCaveat = 6,
            OddBars = 7,
        };

    public int? NumberOf(BrowserCollection collection) =>
        collection switch
        {
            BrowserCollection.CheckBeat => CheckBeat,
            BrowserCollection.Warped => Warped,
            BrowserCollection.WarpByHand => WarpByHand,
            BrowserCollection.KeyCaveat => KeyCaveat,
            BrowserCollection.OddBars => OddBars,
            _ => throw new UnreachableException(),
        };

    public CollectionSettings With(BrowserCollection collection, int? number) =>
        collection switch
        {
            BrowserCollection.CheckBeat => this with { CheckBeat = number },
            BrowserCollection.Warped => this with { Warped = number },
            BrowserCollection.WarpByHand => this with { WarpByHand = number },
            BrowserCollection.KeyCaveat => this with { KeyCaveat = number },
            BrowserCollection.OddBars => this with { OddBars = number },
            _ => throw new UnreachableException(),
        };

    /// <summary>Why these settings are refused, or null when they stand.</summary>
    public string? Problem()
    {
        var taken = new Dictionary<int, BrowserCollection>();

        foreach (var collection in Enum.GetValues<BrowserCollection>())
        {
            if (NumberOf(collection) is not { } number)
                continue;

            if (number is < Lowest or > Highest)
                return $"{Name(collection)} can use Collection {Lowest} to {Highest}, not {number}.";

            if (!taken.TryAdd(number, collection))
                return $"Collection {number} already marks {Name(taken[number]).ToLowerInvariant()}; two meanings cannot share one.";
        }

        return null;
    }

    /// <summary>
    /// A file's Collections with the numbers mapped to <paramref name="replacing"/> swapped for those
    /// mapped to <paramref name="wanted"/>; every other number is the user's and stays.
    /// </summary>
    public IReadOnlyList<int> Merged(
        IReadOnlyList<int> existing,
        IEnumerable<BrowserCollection> wanted,
        IReadOnlySet<BrowserCollection> replacing
    )
    {
        var removable = replacing.Select(NumberOf).OfType<int>().ToHashSet();
        var added = wanted.Select(NumberOf).OfType<int>().ToList();

        return removable.Count is 0 && added.Count is 0
            ? existing
            : [.. existing.Where(number => !removable.Contains(number)).Union(added).Order()];
    }

    public static string Name(BrowserCollection collection) => collection.Humanize();
}
