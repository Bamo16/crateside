namespace Crateside.Core.Ledger;

/// <summary>One band of the key strip: a center over a stretch of the track, in seconds.</summary>
public sealed record KeyBand(
    double Start,
    double End,
    TonalCenter Center,
    string? Section,
    bool FromWindow,
    bool StartUnbacked = false
)
{
    /// <summary><c>B min</c>, or <c>A Dor</c> for a modal center.</summary>
    public string Key => KeyStrip.Short(Center);

    /// <summary><c>verse · A Dor</c>, or just the key where the song has one center.</summary>
    public string Label => Section is { Length: > 0 } section ? $"{section} · {Key}" : Key;
}

/// <summary>The bands the verdict's centers take on the track, and the sections that have no time to be drawn at.</summary>
public sealed record KeyStripPlan(IReadOnlyList<KeyBand> Bands, IReadOnlyList<Section> Unplaced)
{
    public bool IsEmpty => Bands is [] && Unplaced is [];
}

/// <summary>Places a verdict's centers on the track's time axis (handoff 20).</summary>
public static class KeyStrip
{
    /// <summary><c>B min</c>, or <c>A Dor</c> for a modal center.</summary>
    public static string Short(TonalCenter center) =>
        ModeText.Abbreviation(center, false) is { } mode
            ? $"{center.Tonic.Replace('#', '♯')} {mode}"
            : ModeText.Tag(center);

    /// <summary>
    /// A section that came from a window takes the window's span, one with a start runs to the next
    /// start of any section (the primary's included) or window, the primary center fills everything
    /// else under the label of the primary section starting there, and a section with neither is
    /// named, never drawn.
    /// </summary>
    public static KeyStripPlan Plan(RunEvidence run, double seconds)
    {
        if (run.Center is not { } primary || seconds <= 0)
            return new([], []);

        var primaryLabel = run.Sections.FirstOrDefault(section => section.Center == primary)?.Label;
        var primaryStarts = run
            .Sections.Where(section => section.Center == primary)
            .Where(section => section.Start is not null)
            .OrderBy(section => section.Start)
            .Select(section => (section.Start, section.Label))
            .ToList();
        var others = run.Sections.Where(section => section.Center != primary).ToList();

        if (others is [])
            return new([new(0, seconds, primary, null, false)], []);

        List<(double Start, double? End, Section Section, bool FromWindow)> found = [];
        List<Section> unplaced = [];

        foreach (var section in others)
        {
            var windows = run
                .Readings.Where(reading => reading.Section == section)
                .Select(reading => reading.Window)
                .ToList();

            if (windows is not [])
                found.AddRange(
                    windows.Select(window => (window.Start, (double?)window.End, section, true))
                );
            else if (section.Start is { } start && start < seconds)
                found.Add((Math.Max(start, 0), null, section, false));
            else
                unplaced.Add(section);
        }

        if (found is [])
            return new([new(0, seconds, primary, null, false)], unplaced);

        var drawn = found
            .Select(placed => new KeyBand(
                placed.Start,
                Math.Min(
                    placed.End
                        ?? found
                            .Where(other => other.Start > placed.Start)
                            .Select(other => other.Start)
                            .Concat(
                                primaryStarts
                                    .Where(primaryStart => primaryStart.Start > placed.Start)
                                    .Select(primaryStart => primaryStart.Start!.Value)
                            )
                            .DefaultIfEmpty(seconds)
                            .Min(),
                    seconds
                ),
                placed.Section.Center,
                placed.Section.Label,
                placed.FromWindow,
                StartUnbacked: !placed.FromWindow
            ))
            .Where(band => band.End > band.Start)
            .OrderBy(band => band.Start)
            .ToList();

        List<KeyBand> bands = [];
        var cursor = 0.0;

        foreach (var band in drawn)
        {
            if (band.Start > cursor)
                bands.Add(new(cursor, band.Start, primary, PrimaryLabelAt(cursor), false));

            bands.Add(band);
            cursor = Math.Max(cursor, band.End);
        }

        if (cursor < seconds)
            bands.Add(new(cursor, seconds, primary, PrimaryLabelAt(cursor), false));

        return new(bands, unplaced);

        string? PrimaryLabelAt(double at) =>
            primaryStarts.LastOrDefault(primaryStart => primaryStart.Start <= at).Label
            ?? primaryLabel;
    }
}
