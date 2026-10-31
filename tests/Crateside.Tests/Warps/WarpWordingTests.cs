using AbleKit.Analysis;
using Crateside.Core.Ableton;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class WarpWordingTests
{
    private const string Instrumental = @"P:\Staging\Song (Instrumental - Clean).flac";
    private const string Vocal = @"P:\Staging\Song (Vocal - Full).flac";

    private static readonly DateTime Saved = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Local);

    private static readonly WarpMarker[] Shared =
    [
        new(0.5, 1),
        new(130, 300),
        new(130.01, 300.03125),
    ];

    [Fact]
    public void A_lent_warp_saved_over_in_Live_is_yours_and_says_where_it_parts()
    {
        var vocal = Stem(
            Vocal,
            new WarpOwner.Yours(
                Saved,
                [],
                new WrittenWarp(Instrumental, 1, Saved.AddDays(-9).ToUniversalTime())
            ),
            [new(0.5, 1), new(130, 300), new(140, 322), new(140.01, 322.03125)]
        );
        var wording = Wording(Lender(), vocal);

        Assert.Equal(new WarpChip("Your warp", ChipStyle.Filled), wording.Chip(vocal));
        Assert.Equal("saved over crateside's · 30 Sep · differs from 2:20", wording.Sub(vocal));
        Assert.Equal(
            "crateside lent it Instrumental - Clean's warp on 21 Sep; you saved over it in Live on 30 Sep, so it is yours now. It follows Instrumental - Clean up to 2:20, then has markers of its own.",
            wording.Sentence(vocal)
        );
        Assert.True(wording.CanReapply(vocal));
    }

    [Fact]
    public void A_warp_still_as_crateside_lent_it_has_nothing_to_reapply()
    {
        var vocal = Stem(Vocal, new WarpOwner.Borrowed(Instrumental, Saved), Shared);
        var wording = Wording(Lender(), vocal);

        Assert.Equal("Borrowed from Instrumental - Clean", wording.Chip(vocal).Label);
        Assert.False(wording.CanReapply(vocal));
        Assert.Equal(
            "Nothing to re-apply: it is still Instrumental - Clean's warp, unchanged.",
            wording.ReapplyStatus(vocal)
        );
    }

    [Fact]
    public void The_lender_says_it_is_the_warp_crateside_lends()
    {
        var lender = Lender();
        var wording = Wording(
            lender,
            Stem(Vocal, new WarpOwner.Borrowed(Instrumental, Saved), Shared)
        );

        Assert.Equal("saved 30 Sep · lent to Vocal - Full", wording.Sub(lender));
        Assert.Equal(
            "Nothing to re-apply: this is the warp crateside lends.",
            wording.ReapplyStatus(lender)
        );
    }

    [Fact]
    public void A_stem_with_no_sidecar_offers_neither_control()
    {
        var vocal = new StemWarp { Path = Vocal, Owner = new WarpOwner.None() };
        var wording = Wording(Lender(), vocal);

        Assert.False(wording.CanReapply(vocal));
        Assert.False(WarpWording.CanLetLiveAnalyze(vocal));
        Assert.Equal(
            "No warp file yet: Live has not opened this stem.",
            wording.LetLiveStatus(vocal, "Staging")
        );
    }

    [Fact]
    public void Trueing_up_is_offered_only_on_the_users_canonical_warp_while_it_is_off_its_tempo()
    {
        var off = TrackTempo.Of(
            new WarpTempo(180, TempoShape.Steady, 180, 180, []) { Grid = new(null, 179.983, 23) },
            null,
            null
        );
        var holds = TrackTempo.Of(
            new WarpTempo(180, TempoShape.Steady, 180, 180, [])
            {
                Grid = new(TrueUpReason.WithinNoise, null, 0),
            },
            null,
            null
        );

        Assert.True(Wording(Lender()).CanTrueUp(off));
        Assert.False(Wording(Lender()).CanTrueUp(holds));
        Assert.False(Wording(Lender() with { IsAuthority = false }).CanTrueUp(off));
        Assert.False(
            Wording(Lender() with { Owner = new WarpOwner.Starter(Saved) }).CanTrueUp(off)
        );
    }

    [Fact]
    public void The_true_up_status_says_how_far_off_the_warp_runs_and_what_would_change()
    {
        var off = TrackTempo.Of(
            new WarpTempo(180, TempoShape.Steady, 180, 180, []) { Grid = new(null, 179.983, 23) },
            null,
            null
        )!;
        var plan = new TrueUpPlan.Retype(
            new TrueUpVerdict.Retype(179.983, 180, false, 23.4, 0.1),
            [],
            [],
            [],
            0.0032,
            null
        );

        Assert.Equal(
            "Runs 0.017 fast, so its grid falls 23 ms ahead by the end. Retypes it to 179.983 and lines it up with Instrumental - Clean's transients. Yours is backed up first.",
            Wording(Lender()).TrueUpStatus(off, plan)
        );
    }

    [Fact]
    public void The_true_up_question_says_what_moves_and_to_where()
    {
        var plan = new TrueUpPlan.Retype(
            new TrueUpVerdict.Retype(179.983, 180, false, 23.4, 0.1),
            [],
            [],
            [Vocal],
            0.0032,
            null
        );

        Assert.Equal(
            "Retype your warp from 180 to 179.983 and line its first beat up with Instrumental - Clean's transients? The grid moves by up to 23 ms, most at the end. Vocal - Full borrows this warp and moves with it. Yours (2 markers, saved 30 Sep) is backed up first.",
            Wording(Lender(), Stem(Vocal, new WarpOwner.Borrowed(Instrumental, Saved), Shared))
                .TrueUpQuestion(plan)
        );
    }

    [Fact]
    public void A_true_up_that_does_not_happen_says_why()
    {
        Assert.Equal(
            "Not offered: the grid would not hold within 5 ms all through the track.",
            WarpWording.TrueUpRefusal(new TrueUpPlan.Left(TrueUpReason.DoesNotHold))
        );
        Assert.Equal(
            "Not retyped: the warp was saved again since crateside looked at it.",
            WarpWording.TrueUpDone(new TrueUpOutcome.Changed(), "just now")
        );
        Assert.Equal(
            "Trued up just now: 125.026, holding within 5 ms all through the track. Your previous warp is backed up.",
            WarpWording.TrueUpDone(new TrueUpOutcome.Retyped(125.0257, []), "just now")
        );
    }

    [Theory]
    [InlineData(14.2, "0:14.20")]
    [InlineData(251.66, "4:11.66")]
    public void A_time_in_the_audio_reads_to_the_hundredth(double seconds, string text) =>
        Assert.Equal(text, WarpWording.Time(seconds));

    private static StemWarp Lender() =>
        Stem(Instrumental, new WarpOwner.Yours(Saved, [Vocal], null), Shared) with
        {
            IsAuthority = true,
        };

    private static WarpWording Wording(params StemWarp[] stems) =>
        new(WarpGroup.Of(stems), path => path == Vocal ? "Vocal - Full" : "Instrumental - Clean");

    private static StemWarp Stem(string path, WarpOwner owner, WarpMarker[] markers) =>
        new()
        {
            Path = path,
            Owner = owner,
            Sidecar = new AnalysisFile
            {
                Audio = AudioAnalysis.FromSamples([0.5f, 0.5f], 2, [], 3),
                Warp = new Warp { Markers = markers },
                IsDefaultClipSaved = true,
            },
        };
}
