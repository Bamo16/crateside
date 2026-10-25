namespace Crateside.Core.Ledger;

/// <summary>One band of the key strip: a centre over a stretch of the track, in seconds.</summary>
public sealed record KeyBand(
    double Start,
    double End,
    TonalCentre Centre,
    string? Section,
    bool FromWindow
)
{
    /// <summary><c>B min</c>, or <c>A Dor</c> for a modal centre.</summary>
    public string Key => KeyStrip.Short(Centre);

    /// <summary><c>verse · A Dor</c>, or just the key where the song has one centre.</summary>
    public string Label => Section is { Length: > 0 } section ? $"{section} · {Key}" : Key;
}

/// <summary>The bands the verdict's centres take on the track, and the sections that have no time to be drawn at.</summary>
public sealed record KeyStripPlan(IReadOnlyList<KeyBand> Bands, IReadOnlyList<Section> Unplaced)
{
    public bool IsEmpty => Bands is [] && Unplaced is [];
}

/// <summary>Places a verdict's centres on the track's time axis (handoff 20).</summary>
public static class KeyStrip
{
    /// <summary><c>B min</c>, or <c>A Dor</c> for a modal centre.</summary>
    public static string Short(TonalCentre centre) =>
        ModeText.Abbreviation(centre, false) is { } mode
            ? $"{centre.Tonic.Replace('#', '♯')} {mode}"
            : ModeText.Tag(centre);

    /// <summary>
    /// A section that came from a window takes the window's span, one with a start runs to the next
    /// boundary, the primary centre fills everything else, and a section with neither is named,
    /// never drawn.
    /// </summary>
    public static KeyStripPlan Plan(RunEvidence run, double seconds)
    {
        if (run.Centre is not { } primary || seconds <= 0)
            return new([], []);

        var primaryLabel = run.Sections.FirstOrDefault(section => section.Centre == primary)?.Label;
        var others = run.Sections.Where(section => section.Centre != primary).ToList();

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
                            .DefaultIfEmpty(seconds)
                            .Min(),
                    seconds
                ),
                placed.Section.Centre,
                placed.Section.Label,
                placed.FromWindow
            ))
            .Where(band => band.End > band.Start)
            .OrderBy(band => band.Start)
            .ToList();

        List<KeyBand> bands = [];
        var cursor = 0.0;

        foreach (var band in drawn)
        {
            if (band.Start > cursor)
                bands.Add(new(cursor, band.Start, primary, primaryLabel, false));

            bands.Add(band);
            cursor = Math.Max(cursor, band.End);
        }

        if (cursor < seconds)
            bands.Add(new(cursor, seconds, primary, primaryLabel, false));

        return new(bands, unplaced);
    }
}
