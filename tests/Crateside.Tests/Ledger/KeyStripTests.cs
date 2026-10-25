using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class KeyStripTests
{
    private const double Seconds = 200;

    private static readonly TonalCenter BMinor = new("B", DiatonicMode.Aeolian);
    private static readonly TonalCenter ADorian = new("A", DiatonicMode.Dorian);
    private static readonly TonalCenter EMinor = new("E", DiatonicMode.Aeolian);
    private static readonly TonalCenter CLydian = new("C", DiatonicMode.Lydian);

    private static RunEvidence Run(
        TonalCenter? center,
        Section[] sections,
        RunWindow[]? windows = null,
        WindowReading[]? readings = null
    ) =>
        new()
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "a - b",
            Center = center,
            Sections = sections,
            Windows = windows ?? [],
            Readings = readings ?? [],
        };

    private static string Drawn(KeyStripPlan plan) =>
        string.Join(" | ", plan.Bands.Select(band => $"{band.Start:0}-{band.End:0} {band.Label}"));

    [Fact]
    public void A_song_with_one_center_is_one_band_across_the_track() =>
        Assert.Equal(
            "0-200 B min",
            Drawn(KeyStrip.Plan(Run(BMinor, [new("verse", BMinor)]), Seconds))
        );

    [Fact]
    public void A_withheld_verdict_draws_nothing() =>
        Assert.True(KeyStrip.Plan(Run(null, []), Seconds).IsEmpty);

    [Fact]
    public void A_section_from_a_window_takes_the_windows_span_and_the_primary_fills_the_rest()
    {
        Section chorus = new("chorus", EMinor);
        RunWindow first = new(60, 90, "chorus", WindowAsker.User);
        RunWindow second = new(120, 150, "chorus", WindowAsker.User);
        var run = Run(
            ADorian,
            [new("verse", ADorian), chorus],
            [first, second],
            [new(first, [], null, chorus), new(second, [], null, chorus)]
        );

        var plan = KeyStrip.Plan(run, Seconds);

        Assert.Equal(
            "0-60 verse · A Dor | 60-90 chorus · E min | 90-120 verse · A Dor | 120-150 chorus · E min | 150-200 verse · A Dor",
            Drawn(plan)
        );
        Assert.All(
            plan.Bands.Where(band => band.Center == EMinor),
            band => Assert.True(band.FromWindow)
        );
    }

    [Fact]
    public void A_section_without_a_time_is_named_and_not_drawn()
    {
        Section bridge = new("bridge", CLydian);

        var plan = KeyStrip.Plan(Run(ADorian, [new("verse", ADorian), bridge]), Seconds);

        Assert.Equal("0-200 A Dor", Drawn(plan));
        Assert.Equal([bridge], plan.Unplaced);
    }

    [Fact]
    public void A_section_with_a_start_runs_to_the_next_boundary()
    {
        var run = Run(
            ADorian,
            [new("verse", ADorian), new("bridge", CLydian, 150), new("coda", EMinor, 180)]
        );

        Assert.Equal(
            "0-150 verse · A Dor | 150-180 bridge · C Lyd | 180-200 coda · E min",
            Drawn(KeyStrip.Plan(run, Seconds))
        );
    }

    [Fact]
    public void A_start_beyond_the_track_is_not_drawn_but_is_not_lost()
    {
        Section late = new("outro", EMinor, 500);

        var plan = KeyStrip.Plan(Run(ADorian, [new("verse", ADorian), late]), Seconds);

        Assert.Equal("0-200 A Dor", Drawn(plan));
        Assert.Equal([late], plan.Unplaced);
    }

    [Fact]
    public void A_sections_center_equal_to_the_primary_adds_no_band() =>
        Assert.Equal(
            "0-200 B min",
            Drawn(
                KeyStrip.Plan(
                    Run(BMinor, [new("verse", BMinor), new("chorus", BMinor, 40)]),
                    Seconds
                )
            )
        );

    [Fact]
    public void A_primary_filling_the_track_is_labeled_with_its_key_alone()
    {
        Section bridge = new("bridge", CLydian);

        var plan = KeyStrip.Plan(Run(ADorian, [new("intro", ADorian), bridge]), Seconds);

        Assert.Equal("0-200 A Dor", Drawn(plan));
        Assert.Equal([bridge], plan.Unplaced);
    }
}
