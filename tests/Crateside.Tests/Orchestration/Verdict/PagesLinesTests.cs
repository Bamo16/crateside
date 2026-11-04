using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class PagesLinesTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SourcePages", name));

    private static List<string> MoneyLines(Dictionary<string, string> scores) =>
        [
            .. VerdictRunner.PagesLines([
                new(
                    "Hooktheory",
                    LookupStatus.Found,
                    [
                        SourcePages.HooktheoryPage(
                            "https://www.hooktheory.com/theorytab/view/pink-floyd/money",
                            Fixture("hooktheory-money.html"),
                            scores
                        ),
                    ]
                ),
            ]),
        ];

    private static Dictionary<string, string> MoneyScores =>
        new()
        {
            ["jDgX__kw_mK"] = Fixture("hooktheory-api-jDgX__kw_mK.json"),
            ["VMgJXXWOKoq"] = Fixture("hooktheory-api-VMgJXXWOKoq.json"),
        };

    [Fact]
    public void A_tab_lists_its_contributors_sections_and_the_share_of_the_video_it_maps()
    {
        var lines = MoneyLines(MoneyScores);

        Assert.Contains(
            "  - 3 contributors (Artman, JerichoScott, Baraniscool); 2 sections (Verse, Chorus); "
                + "the site lists the whole song's keys as B dorian, B minor",
            lines
        );
        Assert.Contains(
            "  - synced to YouTube 2aW7HweAf3o; its sections span 6.3% of that video",
            lines
        );
    }

    [Fact]
    public void A_key_change_inside_a_section_is_shown_with_its_beats()
    {
        var lines = MoneyLines(MoneyScores);

        Assert.Contains(
            "  - Chorus changes key inside the section: B minor from beat 1, then B dorian from beat 15",
            lines
        );
        Assert.DoesNotContain(lines, line => line.StartsWith("  - Verse changes key"));
    }

    [Fact]
    public void A_sections_progression_is_passed_through_as_roman_numerals_with_how_much_is_filled_in()
    {
        var lines = MoneyLines(MoneyScores);

        Assert.Contains(
            lines,
            line =>
                line.StartsWith("  - Verse: i7 i43 i7 i64")
                && line.EndsWith("(chords 85% filled in, melody 46%)")
        );
    }

    [Fact]
    public void Moneys_chorus_names_its_dorian_iv_as_major_where_the_page_writes_it_minor()
    {
        var lines = MoneyLines(MoneyScores);

        Assert.Contains(
            lines,
            line =>
                line.StartsWith("  - Chorus, its chords by its score", StringComparison.Ordinal)
                && line.Contains(
                    "B minor from beat 1: F#:min F:min E:min G:maj/3 G:maj/5 | "
                        + "B dorian from beat 15: B:min B:min/5 B:min E:maj/5 B:min/5 B:min7/b7 B:min E:7/b7",
                    StringComparison.Ordinal
                )
        );
        Assert.DoesNotContain(
            lines,
            line => line.StartsWith("  - Chorus: v vii", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void A_tab_whose_scores_could_not_be_read_says_its_sync_is_unknown()
    {
        var lines = MoneyLines([]);

        Assert.Contains(
            "  - synced to YouTube 2aW7HweAf3o; the sections' own scores could not be read, so their sync is unknown",
            lines
        );
    }

    [Fact]
    public void A_candidate_is_shown_to_the_judge_as_unconfirmed_and_not_evidence()
    {
        var lines = VerdictRunner.PagesLines([
            new SiteLookup("Hooktheory", LookupStatus.NotFound, [])
            {
                Candidates =
                [
                    new FetchedPage
                    {
                        Site = "Hooktheory",
                        Url = "https://www.hooktheory.com/theorytab/view/sega/chemical-plant-zone",
                        Heading = "Chemical Plant Zone",
                        Note = "unconfirmed: filed under Sega",
                    },
                ],
            },
        ]);

        Assert.Contains(
            lines,
            line =>
                line.Contains("unconfirmed, not evidence")
                && line.Contains("sega/chemical-plant-zone")
        );
    }
}
