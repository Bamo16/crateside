using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class KeyStripTests
{
    private const double Seconds = 200;

    private static readonly TonalCenter BMinor = new("B", DiatonicMode.Aeolian);
    private static readonly TonalCenter DMajor = new("D", DiatonicMode.Ionian);
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
    public void A_section_a_window_was_asked_over_is_drawn_from_a_recorded_run()
    {
        TonalCenter home = new("C#", DiatonicMode.Aeolian);
        TonalCenter away = new("A#", DiatonicMode.Aeolian);
        var run = RunEvidence.From(
            new()
            {
                RecordedAt = DateTimeOffset.UnixEpoch,
                Song = "a - b",
                Confidence = KeyConfidence.Medium,
                Center = home,
                Sections =
                [
                    new("Main Section", home, 0),
                    new("Buildup and Last Drop", away, 141.84),
                ],
                Windows =
                [
                    new(0, 141.84, "Main Section", WindowAsker.User),
                    new(141.84, 219.07, "Buildup and Last Drop", WindowAsker.User),
                ],
            }
        );

        var plan = KeyStrip.Plan(run, 219.07);

        Assert.Equal(
            "0-142 Main Section · C♯ min | 142-219 Buildup and Last Drop · A♯ min",
            Drawn(plan)
        );
        Assert.Empty(plan.Unplaced);
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
    public void A_section_with_a_start_but_no_window_is_named_and_not_drawn()
    {
        Section bridge = new("bridge", CLydian, 150);
        Section coda = new("coda", EMinor, 180);

        var plan = KeyStrip.Plan(Run(ADorian, [new("verse", ADorian), bridge, coda]), Seconds);

        Assert.Equal("0-200 A Dor", Drawn(plan));
        Assert.Equal([bridge, coda], plan.Unplaced);
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

    [Fact]
    public void Sections_the_judge_gave_starts_are_named_and_the_primary_fills_the_track()
    {
        Section verse = new("Verse", BMinor, 0);
        var run = Run(DMajor, [verse, new("Chorus", DMajor, 71.55)]);

        var plan = KeyStrip.Plan(run, Seconds);

        Assert.Equal("0-200 D maj", Drawn(plan));
        Assert.Equal([verse], plan.Unplaced);
    }

    [Fact]
    public void The_primary_fill_after_a_window_takes_the_label_of_the_primary_section_there()
    {
        Section verse = new("Verse", BMinor);
        RunWindow window = new(10, 70, "Verse", WindowAsker.User);
        var run = Run(
            DMajor,
            [new("Intro", DMajor), verse, new("Chorus", DMajor, 70), new("Bridge", CLydian, 120)],
            [window],
            [new(window, [], null, verse)]
        );

        var plan = KeyStrip.Plan(run, Seconds);

        Assert.Equal(
            "0-10 Intro · D maj | 10-70 Verse · B min | 70-200 Chorus · D maj",
            Drawn(plan)
        );
        Assert.Equal(["Bridge"], plan.Unplaced.Select(section => section.Label));
    }

    [Fact]
    public void A_window_keeps_its_own_span_when_a_primary_section_starts_inside_it()
    {
        Section chorus = new("chorus", EMinor);
        RunWindow window = new(60, 90, "chorus", WindowAsker.User);
        var run = Run(
            ADorian,
            [new("verse", ADorian, 0), new("hook", ADorian, 70), chorus],
            [window],
            [new(window, [], null, chorus)]
        );

        Assert.Equal(
            "0-60 verse · A Dor | 60-90 chorus · E min | 90-200 hook · A Dor",
            Drawn(KeyStrip.Plan(run, Seconds))
        );
    }

    [Theory]
    [InlineData(WindowAsker.User)]
    [InlineData(WindowAsker.Judge)]
    public void A_band_whose_section_a_window_tags_says_only_the_key_whoever_asked(
        WindowAsker asker
    )
    {
        Section chorus = new("chorus", EMinor, 60);
        RunWindow window = new(60, 90, "chorus", asker);
        var run = Run(
            ADorian,
            [new("verse", ADorian, 0), chorus],
            [window],
            [new(window, [], null, chorus)]
        );

        var texts = KeyStrip
            .Plan(run, Seconds)
            .Bands.Select(band => KeyStrip.Text(run, band))
            .ToList();

        Assert.Equal(["verse · A Dor", "E min", "verse · A Dor"], texts);
    }
}
